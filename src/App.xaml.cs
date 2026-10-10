using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EnvSwitch
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var dataDir = ResolveDataDir();
            EnvSwitch.MainWindow.DataDir = dataDir;

            if (e.Args.Length > 0 && e.Args[0] == "--截图")
            {
                Screenshot(dataDir, e.Args);
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--自检")
            {
                SelfTestMode(dataDir, e.Args);
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--帧率")
            {
                FpsMode(e.Args);
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--找一找")
            {
                FindMode(dataDir, e.Args);
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--写默认配置")
            {
                var dir = e.Args.Length > 1 ? e.Args[1] : dataDir;
                try
                {
                    Directory.CreateDirectory(dir);
                    AppConfig.Default().Save(Path.Combine(dir, "配置.json"));
                }
                catch { }
                AttachConsole(-1);
                try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); } catch { }
                Console.WriteLine("已写出默认配置：" + Path.Combine(dir, "配置.json"));
                Shutdown(0);
                return;
            }

            var win = new MainWindow();
            win.Show();
        }

        /// <summary>数据目录：exe 同目录或上一级，哪个有 配置.json 用哪个（默认上一级）；
        /// 两处都不可写（例如解压到 Program Files）就回落到 %APPDATA%\EnvSwitch\。</summary>
        static string ResolveDataDir()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var parent = Directory.GetParent(exeDir);
            string candidate = File.Exists(Path.Combine(exeDir, "配置.json"))
                ? exeDir
                : (parent != null ? parent.FullName : exeDir);
            if (Writable(candidate)) return candidate;

            try
            {
                var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EnvSwitch");
                Directory.CreateDirectory(roaming);
                if (Writable(roaming)) return roaming;
            }
            catch { }
            return candidate;
        }

        static bool Writable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".写权限探测");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        void Screenshot(string dataDir, string[] args)
        {
            var theme = args.Length > 1 ? args[1] : "白天";
            var outPath = args.Length > 2
                ? args[2]
                : Path.Combine(dataDir, "效果图", theme + ".png");
            try
            {
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }

            AttachConsole(-1);
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); } catch { }

            var win = new MainWindow();
            win.ConfigureForScreenshot(theme,
                args.Length > 3 ? args[3] : null,
                args.Length > 4 ? args[4] : null,
                args.Length > 5 ? args[5] : null);
            win.Left = -30000;
            win.Top = -30000;
            win.ShowInTaskbar = false;
            win.ContentRendered += (s, e2) =>
            {
                try
                {
                    Render(win, outPath);
                    Console.WriteLine("已保存：" + outPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("截图失败：" + ex.Message);
                }
                Shutdown();
            };
            win.Show();
        }

        static void Render(Window win, string path)
        {
            var content = (FrameworkElement)win.Content;
            var dpi = VisualTreeHelper.GetDpi(win);
            double w = content.ActualWidth, h = content.ActualHeight;
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(w * dpi.DpiScaleX),
                (int)Math.Ceiling(h * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(content);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>--找一找 [目录]：只做识别并把结果打到控制台（自检/排障用，不开窗口）。</summary>
        void FindMode(string dataDir, string[] args)
        {
            AttachConsole(-1);
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); } catch { }
            var root = args.Length > 1 ? args[1] : dataDir;
            Console.WriteLine("扫描起点：" + root);
            var found = Discovery.ScanAround(root);
            Console.WriteLine("共识别到 " + found.Count + " 个目标：");
            foreach (var f in found) Console.WriteLine("  " + f + "\r\n      " + f.Dir);
            foreach (var key in AppConfig.AppKeys)
            {
                var best = Discovery.PickBest(found, key, null);
                Console.WriteLine("  → " + Discovery.Label(key) + " 默认选中：" +
                    (best != null ? best.Dir + "（appsettings 里版本 " + (best.ProjectName ?? "?") + "）" : "没找到可用的"));
            }
            Shutdown(found.Exists(f => f.Kind == FoundKind.Usable) ? 0 : 1);
        }

        /// <summary>--帧率 [毫秒]：离屏起窗，让滑块拇指连续滑动，打印实测帧率。</summary>
        void FpsMode(string[] args)
        {
            int ms = 2000;
            if (args.Length > 1) int.TryParse(args[1], out ms);
            if (ms < 300) ms = 300;

            AttachConsole(-1);
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); } catch { }

            var win = new MainWindow();
            win.ConfigureForScreenshot("白天");
            // 必须在屏幕上量：离屏窗口 DWM 不做合成，帧率会被严重低估
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            win.ShowInTaskbar = false;
            win.ShowActivated = false;
            win.ContentRendered += async (s, e) =>
            {
                double baseFps = await win.MeasureFpsAsync(1200, true);
                double animFps = await win.MeasureFpsAsync(ms, false);
                Console.WriteLine(string.Format("基准（极简动画，等于本会话可渲染上限）：{0:N1} 帧/秒", baseFps));
                Console.WriteLine(string.Format("滑块滑动动画实测：{0:N1} 帧/秒（采样 {1} ms）", animFps, ms));
                Shutdown();
            };
            win.Show();
        }

        void SelfTestMode(string dataDir, string[] args)        {
            AttachConsole(-1);
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); } catch { }
            string report;
            int code = SelfTest.Run(dataDir, args, out report);
            Console.WriteLine();
            Console.WriteLine(report);
            try { File.WriteAllText(Path.Combine(dataDir, "自检报告.txt"), report, new UTF8Encoding(true)); } catch { }
            Shutdown(code);
        }
    }
}
