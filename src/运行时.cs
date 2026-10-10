using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EnvSwitch
{
    public sealed class ProbeResult
    {
        public string Cc = "?";
        public string Dc = "?";
        public bool Local443;
        public bool Local4567;
        public bool Online443;
        public DateTime At = DateTime.MinValue;
    }

    /// <summary>进程/服务/端口探测、启动软件、写运行记录。全部用系统自带能力，不引第三方包。</summary>
    public static class Runtime
    {
        public const string CcService = "DemoApiService";
        public const string DcService = "DemoDataService";

        // ---------- 服务状态（advapi32，免管理员，只查状态） ----------
        [StructLayout(LayoutKind.Sequential)]
        struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenService(IntPtr scm, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool QueryServiceStatus(IntPtr svc, out SERVICE_STATUS status);
        [DllImport("advapi32.dll")]
        static extern bool CloseServiceHandle(IntPtr handle);

        /// <summary>返回 running / stopped / missing / unknown</summary>
        public static string ServiceState(string name)
        {
            IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
            try
            {
                scm = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
                if (scm == IntPtr.Zero) return "unknown";
                svc = OpenService(scm, name, 0x0004);    // SERVICE_QUERY_STATUS
                if (svc == IntPtr.Zero) return "missing";
                SERVICE_STATUS st;
                if (!QueryServiceStatus(svc, out st)) return "unknown";
                return st.dwCurrentState == 0x00000004 ? "running" : "stopped";
            }
            catch { return "unknown"; }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                if (scm != IntPtr.Zero) CloseServiceHandle(scm);
            }
        }

        public static bool PortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    return c.Connected;
                }
            }
            catch { return false; }
        }

        public static ProbeResult ProbeAll(AppConfig cfg)
        {
            var p = new ProbeResult();
            p.Cc = ServiceState(CcService);
            p.Dc = ServiceState(DcService);
            var local = cfg != null ? cfg.Env("local") : null;
            var online = cfg != null ? cfg.Env("online") : null;
            if (local != null)
            {
                p.Local443 = PortOpen(local.Host, local.Port, 700);
                p.Local4567 = PortOpen(local.Host, 4567, 700);
            }
            if (online != null) p.Online443 = PortOpen(online.Host, online.Port, 900);
            p.At = DateTime.Now;
            return p;
        }

        // ---------- 进程 ----------
        public static Process[] Find(string exeName)
        {
            try { return Process.GetProcessesByName(exeName); }
            catch { return new Process[0]; }
        }

        public static List<string> RunningPaths(string exeName)
        {
            var list = new List<string>();
            foreach (var p in Find(exeName))
            {
                try { list.Add(p.MainModule != null ? p.MainModule.FileName : "(路径不可读)"); }
                catch { list.Add("(路径不可读)"); }
                finally { p.Dispose(); }
            }
            return list;
        }

        public static bool WaitAllExit(string exeName, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                var ps = Find(exeName);
                if (ps.Length == 0) return true;
                foreach (var p in ps) p.Dispose();
                Thread.Sleep(200);
            }
            var left = Find(exeName);
            var none = left.Length == 0;
            foreach (var p in left) p.Dispose();
            return none;
        }

        /// <summary>先发正常关闭消息，等软件自己保存退出。</summary>
        public static bool CloseGraceful(string exeName, int waitMs)
        {
            var ps = Find(exeName);
            if (ps.Length == 0) return true;
            foreach (var p in ps)
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); }
                catch { }
                finally { p.Dispose(); }
            }
            return WaitAllExit(exeName, waitMs);
        }

        public static void ForceKill(string exeName)
        {
            foreach (var p in Find(exeName))
            {
                try { p.Kill(); p.WaitForExit(3000); }
                catch { }
                finally { p.Dispose(); }
            }
        }

        public static void StartApp(AppSpec app)
        {
            var psi = new ProcessStartInfo
            {
                FileName = app.ExePath,
                WorkingDirectory = app.Dir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        public static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
            catch { }
        }

        // ---------- 运行记录 ----------
        public static string LogPath(string dataDir) { return Path.Combine(dataDir, "运行记录.log"); }

        public static void AppendLog(string dataDir, string line)
        {
            try
            {
                var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + line + "\r\n";
                File.AppendAllText(LogPath(dataDir), text, new UTF8Encoding(true));
            }
            catch { }
        }

        public static string LastLogLine(string dataDir)
        {
            try
            {
                var path = LogPath(dataDir);
                if (!File.Exists(path)) return "";
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0; i--)
                    if (!string.IsNullOrWhiteSpace(lines[i])) return lines[i].Trim();
                return "";
            }
            catch { return ""; }
        }

        /// <summary>扫描本机已有的软件安装目录，供界面下拉选择。</summary>
        public static List<string> DiscoverDirs(string exeName, params string[] roots)
        {
            var result = new List<string>();
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        if (File.Exists(Path.Combine(dir, exeName)))
                        {
                            result.Add(dir);
                            continue;
                        }
                        try
                        {
                            foreach (var sub in Directory.GetDirectories(dir))
                                if (File.Exists(Path.Combine(sub, exeName))) result.Add(sub);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return result;
        }
    }
}
