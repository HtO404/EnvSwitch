using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EnvSwitch
{
    /// <summary>
    /// 写入自检：
    ///   ① 副本上跑「软件 × 环境」共 4 组，校验只改该改的值、其余字节逐字不动；
    ///   ② 反例（缺键 / 重复键 / 非法版本号 / 坏 JSON）必须被拦下；
    ///   ③ 加 --含真文件往返 才动真文件：写入 → 校验 → 按原文还原 → SHA-256 比对一致。
    /// 配置里目录还空着（同事机器首次运行）时，相关项按"跳过"处理，不判失败。
    /// </summary>
    public static class SelfTest
    {
        static readonly StringBuilder R = new StringBuilder();
        static int _fails;
        static int _skips;

        const string SampleGood =
            "{\r\n  \"API_Access\": {\r\n    \"url\": \"https://10.0.0.100/api/\",\r\n    \"urlDataServer\": \"https://10.0.0.100/api/\"\r\n  },\r\n  \"TERMINAL_Access\": {\r\n    \"ProjectName\": \"proj-1\",\r\n    \"SatelliteNumbers\": [1]\r\n  }\r\n}\r\n";

        static void Line(string s) { R.AppendLine(s); }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok) _fails++;
            Line(string.Format("  {0}  {1}{2}", ok ? "PASS" : "FAIL", name, string.IsNullOrEmpty(detail) ? "" : "  —— " + detail));
        }

        static void Skip(string name, string why)
        {
            _skips++;
            Line("  跳过  " + name + "  —— " + why);
        }

        static string Sha(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static int Count(string s, string sub)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
            return n;
        }

        static AppSpec TempApp(string label, string dir, bool urlDataServer)
        {
            return new AppSpec { Label = label, Dir = dir, Exe = "x.exe", UrlDataServer = urlDataServer };
        }

        public static int Run(string dataDir, string[] args, out string report)
        {
            bool realRound = args.Any(a => a.Contains("含真文件"));
            var cfg = AppConfig.Load(Path.Combine(dataDir, "配置.json"));
            var work = Path.Combine(Path.GetTempPath(), "EnvSwitch-自检-" + DateTime.Now.ToString("HHmmss"));

            Line("EnvSwitch · 写入自检");
            Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("程序：" + System.Reflection.Assembly.GetExecutingAssembly().Location);
            Line("配置：" + Path.Combine(dataDir, "配置.json"));
            Line("临时目录：" + work);
            Line("");
            try
            {
                Directory.CreateDirectory(work);

                // ---------- ① 副本写入（真文件存在时用真文件副本，否则用内置样例） ----------
                Line("[1/3] 副本写入自检（不碰真文件）");
                foreach (var appKey in AppConfig.AppKeys)
                {
                    var app = cfg.App(appKey);
                    if (app == null) { Check(appKey, false, "配置里没有这个软件"); continue; }

                    foreach (var envKey in new[] { "online", "local" })
                    {
                        var env = cfg.Env(envKey);
                        var caseDir = Path.Combine(work, appKey + "-" + envKey);
                        Directory.CreateDirectory(caseDir);
                        var copy = Path.Combine(caseDir, "appsettings.json");
                        string source = null;
                        if (!string.IsNullOrEmpty(app.Dir) && File.Exists(app.ConfigPath)) source = app.ConfigPath;
                        if (source != null) File.Copy(source, copy, true);
                        else File.WriteAllText(copy, SampleGood, new UTF8Encoding(false));

                        var before = File.ReadAllText(copy, Encoding.UTF8);
                        var beforeCrlf = Count(before, "\r\n");
                        var plan = WritePlan.Build(appKey, TempApp(app.Label, caseDir, app.UrlDataServer), env, "star99", true);
                        var label = app.Label + " × " + (env != null ? env.Label : envKey) + " → star99"
                                    + (source == null ? "（内置样例）" : "");
                        if (!plan.Ok) { Check(label, false, "计划构建失败：" + plan.Error); continue; }

                        plan.Apply();
                        var after = File.ReadAllText(copy, Encoding.UTF8);
                        var same = WritePlan.MaskFields(before) == WritePlan.MaskFields(after);
                        var bytes = File.ReadAllBytes(copy);
                        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                        var crlfOk = Count(after, "\r\n") == beforeCrlf;
                        var deltaOk = (after.Length - before.Length) == plan.Changes.Sum(c => c.NewValue.Length - (c.OldValue ?? "").Length);
                        var keys = plan.Changes.Select(c => c.Key).ToList();
                        Check(label, same && !bom && crlfOk && deltaOk,
                            same ? ("只改了 " + keys.Count + " 个值（" + string.Join("/", keys) + "）；CRLF " + beforeCrlf + " 处不变；无 BOM；字节差吻合")
                                 : "结构被改动了");
                    }
                }

                // ---------- ② 反例（用内置样例，不依赖真文件） ----------
                Line("");
                Line("[2/3] 反例（这些情况必须被拦下）");
                var t1 = Path.Combine(work, "缺键");
                Directory.CreateDirectory(t1);
                File.WriteAllText(Path.Combine(t1, "appsettings.json"),
                    "{\r\n  \"API_Access\": {\r\n    \"url\": \"https://10.0.0.100/api/\"\r\n  }\r\n}\r\n",
                    new UTF8Encoding(false));
                var p1 = WritePlan.Build("t", TempApp("样例", t1, false), cfg.Env("local"), "proj-1", true);
                Check("缺 ProjectName 的文件被拒绝写入", !p1.Ok && p1.Error.Contains("缺少"), p1.Ok ? "竟然通过了" : p1.Error);

                var t2 = Path.Combine(work, "重复键");
                Directory.CreateDirectory(t2);
                File.WriteAllText(Path.Combine(t2, "appsettings.json"),
                    "{\r\n  \"API_Access\": {\r\n    \"url\": \"https://10.0.0.100/api/\",\r\n    \"url\": \"https://127.0.0.1/api/\"\r\n  },\r\n  \"TERMINAL_Access\": {\r\n    \"ProjectName\": \"proj-1\"\r\n  }\r\n}\r\n",
                    new UTF8Encoding(false));
                var p2 = WritePlan.Build("t", TempApp("样例", t2, false), cfg.Env("local"), "proj-1", true);
                Check("同一个 url 出现两次的文件被拒绝写入", !p2.Ok && p2.Error.Contains("次"), p2.Ok ? "竟然通过了" : p2.Error);

                var p3 = WritePlan.Build("t", TempApp("样例", t2, false), cfg.Env("local"), "star7\"7", true);
                Check("非法版本号被拦下", !p3.Ok, p3.Ok ? "竟然通过了" : p3.Error);

                var t3 = Path.Combine(work, "坏JSON");
                Directory.CreateDirectory(t3);
                File.WriteAllText(Path.Combine(t3, "appsettings.json"), "{ \"API_Access\": { \"url\": ", new UTF8Encoding(false));
                var p4 = WritePlan.Build("t", TempApp("样例", t3, false), cfg.Env("local"), "proj-1", true);
                Check("坏 JSON 被拒绝写入", !p4.Ok, p4.Ok ? "竟然通过了" : p4.Error);

                // ---------- ③ 真文件往返（可选） ----------
                Line("");
                if (!realRound)
                {
                    Line("[3/3] 真文件往返：本次未执行（要跑就加参数 --含真文件往返）");
                }
                else
                {
                    Line("[3/3] 真文件往返（写入 → 校验 → 按原文还原 → SHA-256 比对）");
                    foreach (var appKey in AppConfig.AppKeys)
                    {
                        var app = cfg.App(appKey);
                        if (app == null) { Check(appKey, false, "配置里没有这个软件"); continue; }
                        if (string.IsNullOrEmpty(app.Dir) || !File.Exists(app.ConfigPath))
                        {
                            Skip(app.Label + " 真文件往返", "配置里还没有可用目录（首次运行会自动识别）");
                            continue;
                        }
                        var path = app.ConfigPath;
                        var hash0 = Sha(path);
                        var original = File.ReadAllText(path, Encoding.UTF8);
                        var curUrl = WritePlan.ReadField(original, "url");
                        var envKey = curUrl == cfg.Env("local").Url ? "online" : "local";
                        var plan = WritePlan.Build(appKey, app, cfg.Env(envKey), "selftest00", true);
                        if (!plan.Ok) { Check(app.Label + " 真文件往返", false, plan.Error); continue; }
                        plan.Apply();
                        var written = File.ReadAllText(path, Encoding.UTF8);
                        var okWrite = WritePlan.ReadField(written, "url") == cfg.Env(envKey).Url
                                      && WritePlan.ReadField(written, "ProjectName") == "selftest00"
                                      && WritePlan.MaskFields(written) == WritePlan.MaskFields(original);
                        File.WriteAllText(path, original, new UTF8Encoding(false));
                        var hash1 = Sha(path);
                        Check(app.Label + " 真文件往返", okWrite && hash0 == hash1,
                            okWrite ? ("换成 " + cfg.Env(envKey).Label + " + selftest00 后校验通过，还原后哈希一致 " + hash1.Substring(0, 16) + "…")
                                    : "写入或还原校验不通过");
                    }
                }

                Line("");
                Line(_fails == 0 ? ("结论：全部通过" + (_skips > 0 ? "（另有 " + _skips + " 项按跳过处理）" : ""))
                                 : ("结论：有 " + _fails + " 项未通过，请把这份报告发我"));
            }
            catch (Exception ex)
            {
                _fails++;
                Line("自检过程中出错：" + ex);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
            report = R.ToString();
            return _fails == 0 ? 0 : 1;
        }
    }
}
