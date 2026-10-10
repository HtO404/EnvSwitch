using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace EnvSwitch
{
    /// <summary>识别结果的可信度。</summary>
    public enum FoundKind
    {
        /// <summary>三重签名全过：可以用。</summary>
        Usable,
        /// <summary>像是目标程序，但目录里缺 appsettings.json —— 只提示，不让启动。</summary>
        Suspect
    }

    public sealed class FoundApp
    {
        public string AppKey;          // terminal / studio
        public string Label;           // DemoClient / DemoConsole
        public string Dir;
        public string ExePath;
        public string ProductName;     // PE 版本信息里的产品名（第二重签名）
        public string FileVersion;
        public string ProjectName;     // 从 appsettings.json 读到的 star 版本
        public string Url;             // 从 appsettings.json 读到的接口地址
        public bool HasConfig;         // 第三重签名：appsettings.json 且含两个必需键
        public FoundKind Kind;

        public override string ToString()
        {
            return string.Format("{0}  {1}  版本={2}  产品={3}  项目={4}  appsettings={5}",
                Kind == FoundKind.Usable ? "可用" : "疑似", Label,
                FileVersion ?? "(无)", ProductName ?? "(无)", ProjectName ?? "(无)", HasConfig ? "有" : "缺");
        }
    }

    /// <summary>
    /// 自动识别DemoClient / DemoConsole。识别依据（2026-10-09 实测）：
    ///   ① 文件名必须是 DemoClient.exe / DemoConsole.exe
    ///   ② PE 版本信息的 ProductName 必须是 DemoClient / DemoConsole（挡住改名的假文件，与版本无关）
    ///   ③ 同目录必须有 appsettings.json，且含 API_Access.url + TERMINAL_Access.ProjectName（正是本工具要改的键）
    /// 三条全过 = 可用；只过 ①+② = 疑似；否则忽略。**不看目录名**，所以同事那边怎么摆都认得出来。
    /// </summary>
    public static class Discovery
    {
        static readonly string[] SkipDirs =
        {
            "windows", "program files", "program files (x86)", "programdata", "appdata",
            "$recycle.bin", "system volume information", "node_modules", ".git", ".vs",
            "temp", "tmp", "packages", "installer", "windowsapps"
        };

        public static string ExpectedExe(string appKey) { return appKey == "terminal" ? "DemoClient.exe" : "DemoConsole.exe"; }
        public static string ExpectedProduct(string appKey) { return appKey == "terminal" ? "DemoClient" : "DemoConsole"; }
        public static string Label(string appKey) { return appKey == "terminal" ? "DemoClient" : "DemoConsole"; }

        /// <summary>识别单个目录（不递归）。不像目标程序时返回 null。</summary>
        public static FoundApp Identify(string dir, string appKey)
        {
            try
            {
                var exe = Path.Combine(dir, ExpectedExe(appKey));
                if (!File.Exists(exe)) return null;

                var f = new FoundApp { AppKey = appKey, Label = Label(appKey), Dir = dir, ExePath = exe };

                // 第二重：PE 版本信息（与版本号无关，改名换目录都认得出）
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(exe);
                    f.ProductName = (vi.ProductName ?? "").Trim();
                    f.FileVersion = vi.FileVersion;
                }
                catch { }
                bool productOk = string.Equals(f.ProductName, ExpectedProduct(appKey), StringComparison.OrdinalIgnoreCase);
                if (!productOk) return null;

                // 第三重：同目录 appsettings.json 且含两个必需键
                var cfg = Path.Combine(dir, "appsettings.json");
                if (File.Exists(cfg))
                {
                    try
                    {
                        object root; string err;
                        if (Json.TryParse(File.ReadAllText(cfg, Encoding.UTF8), out root, out err))
                        {
                            f.Url = Json.Str(root, "API_Access", "url");
                            f.ProjectName = Json.Str(root, "TERMINAL_Access", "ProjectName");
                            f.HasConfig = f.Url != null && f.ProjectName != null;
                        }
                    }
                    catch { }
                }

                f.Kind = f.HasConfig ? FoundKind.Usable : FoundKind.Suspect;
                return f;
            }
            catch { return null; }
        }

        /// <summary>在 root 的子树里找（广度优先、限深度、跳过系统目录、带目录数/时间预算）。
        /// progress 可为 null；预算用完就停，返回已找到的。</summary>
        public static List<FoundApp> Scan(string root, int maxDepth, Action<string> progress = null, Func<bool> cancelled = null,
                                          int maxDirs = 4000, int maxMs = 20000)
        {
            var result = new List<FoundApp>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return result;

            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sw = Stopwatch.StartNew();
            int visited = 0;
            bool truncated = false;

            while (queue.Count > 0)
            {
                if (cancelled != null && cancelled()) break;
                if (visited >= maxDirs || sw.ElapsedMilliseconds > maxMs) { truncated = true; break; }
                var item = queue.Dequeue();
                var dir = item.Key;
                int depth = item.Value;
                if (!seen.Add(dir)) continue;
                visited++;
                if (progress != null && visited % 100 == 0) progress("已查 " + visited + " 个目录…");

                foreach (var appKey in AppConfig.AppKeys)
                {
                    var hit = Identify(dir, appKey);
                    if (hit != null && !result.Exists(x => string.Equals(x.Dir, hit.Dir, StringComparison.OrdinalIgnoreCase) && x.AppKey == hit.AppKey))
                        result.Add(hit);
                }

                if (depth >= maxDepth) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch { continue; }
                foreach (var sub in subs)
                {
                    var name = Path.GetFileName(sub);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (IsJunk(name)) continue;
                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;   // 不跟符号链接，防绕圈
                    }
                    catch { continue; }
                    queue.Enqueue(new KeyValuePair<string, int>(sub, depth + 1));
                }
            }
            if (truncated && progress != null)
                progress("目录较多，已扫 " + visited + " 个就先停下（用时 " + (int)(sw.ElapsedMilliseconds / 1000) + " 秒）");
            return result;
        }

        static bool IsJunk(string name)
        {
            foreach (var junk in SkipDirs)
                if (string.Equals(name, junk, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>整条路径就属于"明显不该扫"的地方（临时目录、系统目录等）。</summary>
        static bool JunkRoot(string path)
        {
            var l = "\\" + path.ToLowerInvariant().Trim('\\') + "\\";
            return l.Contains("\\appdata\\") || l.Contains("\\windows\\") || l.Contains("\\temp\\")
                   || l.Contains("\\program files") || l.Contains("\\programdata\\")
                   || l.Contains("\\$recycle.bin") || l.Contains("\\system volume information");
        }

        /// <summary>按"常见摆法"分层扫描：① 启动器自身目录子树（深度 5）② 上一级目录子树（深度 3）。
        /// 上一级若是系统/临时目录就跳过（否则在 downloads/temp 这类地方会白扫很久）。</summary>
        public static List<FoundApp> ScanAround(string launcherDir, Action<string> progress = null, Func<bool> cancelled = null)
        {
            var all = new List<FoundApp>();
            all.AddRange(Scan(launcherDir, 5, progress, cancelled));

            try
            {
                var parent = Directory.GetParent(launcherDir.TrimEnd(Path.DirectorySeparatorChar));
                if (parent != null && !JunkRoot(parent.FullName))
                {
                    foreach (var hit in Scan(parent.FullName, 3, progress, cancelled))
                    {
                        if (!all.Exists(x => string.Equals(x.Dir, hit.Dir, StringComparison.OrdinalIgnoreCase) && x.AppKey == hit.AppKey))
                            all.Add(hit);
                    }
                }
            }
            catch { }
            return all;
        }

        /// <summary>从候选里挑默认用哪一个：能对上版本 &gt; 文件版本更新 &gt; 路径短。</summary>
        public static FoundApp PickBest(List<FoundApp> candidates, string appKey, string preferVersion)
        {
            FoundApp best = null;
            foreach (var c in candidates)
            {
                if (c.AppKey != appKey || c.Kind != FoundKind.Usable) continue;
                if (best == null) { best = c; continue; }
                best = Better(c, best, preferVersion);
            }
            return best;
        }

        static FoundApp Better(FoundApp a, FoundApp b, string preferVersion)
        {
            if (!string.IsNullOrEmpty(preferVersion))
            {
                bool am = string.Equals(a.ProjectName, preferVersion, StringComparison.OrdinalIgnoreCase);
                bool bm = string.Equals(b.ProjectName, preferVersion, StringComparison.OrdinalIgnoreCase);
                if (am != bm) return am ? a : b;
            }
            var av = ParseVer(a.FileVersion);
            var bv = ParseVer(b.FileVersion);
            if (av != bv) return av > bv ? a : b;
            return a.Dir.Length <= b.Dir.Length ? a : b;
        }

        static Version ParseVer(string s)
        {
            Version v;
            return Version.TryParse(s, out v) ? v : new Version(0, 0);
        }
    }
}
