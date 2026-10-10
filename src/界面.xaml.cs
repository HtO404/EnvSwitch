using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EnvSwitch
{
    public partial class MainWindow : Window
    {
        public static string DataDir = "";
        public static string ConfigPath { get { return Path.Combine(DataDir, "配置.json"); } }

        AppConfig _cfg;
        string _app = "studio";
        string _env = "local";
        string _version = "";
        string _studioDir = "";
        bool _loading = true;
        bool _refreshing;
        bool _launching;
        bool _suppressSave;
        bool _scanning;
        List<FoundApp> _lastFound = new List<FoundApp>();
        bool _screenshot;
        string _forceTheme;
        string _forceApp;
        string _forceEnv;
        string _forceVersion;
        ProbeResult _probe = new ProbeResult();
        List<string> _running = new List<string>();
        string _flash = "";
        DispatcherTimer _tick;

        public MainWindow()
        {
            InitializeComponent();
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/图标.ico")); } catch { }
            Root.Opacity = 0;
            Loaded += OnLoaded;
            Closed += (s, e) => Application.Current.Shutdown();
        }

        // ================= 启动装配 =================
        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cfg = AppConfig.Load(ConfigPath);
            VersionText.Text = "v" + VersionOf();

            if (_screenshot)
            {
                _app = !string.IsNullOrEmpty(_forceApp) ? _forceApp : "studio";
                _env = !string.IsNullOrEmpty(_forceEnv) ? _forceEnv : "local";
                _version = !string.IsNullOrEmpty(_forceVersion) ? _forceVersion : FirstVersion();
                _studioDir = StudioConfigDir();
                // 出图也要把选中状态摆出来（否则滑块拇指不会就位、截图与真实界面不一致）
                SelectRadio(_app, _env);
                (_forceTheme == "夜晚" ? ThemeNight : ThemeDay).IsChecked = true;
                DensityComfort.IsChecked = true;
            }
            else
            {
                _app = string.Equals(_cfg.Last.App, "terminal", StringComparison.OrdinalIgnoreCase) ? "terminal" : "studio";
                _env = string.Equals(_cfg.Last.Env, "online", StringComparison.OrdinalIgnoreCase) ? "online" : "local";
                _version = string.IsNullOrWhiteSpace(_cfg.Last.Version) ? CurrentVersionFromFile() : _cfg.Last.Version;
                _studioDir = string.IsNullOrWhiteSpace(_cfg.Last.StudioDir) ? StudioConfigDir() : _cfg.Last.StudioDir;
                SelectRadio(_app, _env);
                (string.Equals(_cfg.Theme, "day", StringComparison.OrdinalIgnoreCase) ? ThemeDay
                    : string.Equals(_cfg.Theme, "night", StringComparison.OrdinalIgnoreCase) ? ThemeNight
                    : ThemeAuto).IsChecked = true;
                (string.Equals(_cfg.Density, "compact", StringComparison.OrdinalIgnoreCase) ? DensityCompact : DensityComfort).IsChecked = true;

                // 同事笔记本可能只有 1366×768：把窗口收敛到工作区，别超出屏幕
                var wa = SystemParameters.WorkArea;
                MinHeight = Math.Min(700, Math.Max(520, wa.Height - 20));
                MinWidth = Math.Min(960, Math.Max(820, wa.Width - 20));
                if (Height > wa.Height - 20) Height = Math.Max(MinHeight, wa.Height - 20);
                if (Width > wa.Width - 20) Width = Math.Max(MinWidth, wa.Width - 20);
            }
            _loading = false;

            // 三个分段滑块的滑动拇指（颜色跟主题，位置跟选中项）
            Segments.Bind(EnvSlider, EnvThumb, EnvThumbShift, EnvOnline, EnvLocal);
            Segments.Bind(ThemeSlider, ThemeThumb, ThemeThumbShift, ThemeAuto, ThemeDay, ThemeNight);
            Segments.Bind(DensitySlider, DensityThumb, DensityThumbShift, DensityComfort, DensityCompact);

            ApplyTheme();
            ApplyDensity();
            Rebuild();
            _flash = _cfg.LoadWarning ?? "";

            if (_screenshot)
            {
                Root.Opacity = 1;
            }
            else
            {
                AnimateIn();
                _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _tick.Tick += async (s2, e2) => await RefreshAsync();
                _tick.Start();
            }
            await RefreshAsync();
            if (!_screenshot) await AutoDiscoverAsync(true);
        }

        static string VersionOf()
        {
            try
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "?"; }
        }

        void AnimateIn()
        {
            Root.RenderTransform = new TranslateTransform(0, 8);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sb = new Storyboard();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
            Storyboard.SetTarget(fade, Root);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            var move = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
            Storyboard.SetTarget(move, Root);
            Storyboard.SetTargetProperty(move, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            sb.Children.Add(fade);
            sb.Children.Add(move);
            sb.Begin();
        }

        public void ConfigureForScreenshot(string theme, string app = null, string env = null, string version = null)
        {
            _screenshot = true;
            _forceTheme = theme;
            _forceApp = app;
            _forceEnv = env;
            _forceVersion = version;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 1080;
            Height = 800;
        }

        // ================= 资源与主题 =================
        Brush Br(string key)
        {
            try { return (Brush)FindResource(key); } catch { return Brushes.Transparent; }
        }

        FontFamily Ff(string key)
        {
            try { return (FontFamily)FindResource(key); } catch { return new FontFamily("Microsoft YaHei UI"); }
        }

        string ActualTheme()
        {
            if (_screenshot) return _forceTheme == "夜晚" ? "night" : "day";
            var t = _cfg != null ? _cfg.Theme : "auto";
            if (string.Equals(t, "day", StringComparison.OrdinalIgnoreCase)) return "day";
            if (string.Equals(t, "night", StringComparison.OrdinalIgnoreCase)) return "night";
            var h = DateTime.Now.Hour;
            return (h >= 6 && h < 18) ? "day" : "night";
        }

        void ApplyTheme()
        {
            var uri = new Uri(ActualTheme() == "day" ? "主题-白天.xaml" : "主题-夜晚.xaml", UriKind.Relative);
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
            // 离屏截图时窗口背景不在 Content 里，这里给根面板补上底色
            Root.Background = Br("Bg");
            // 关键修复：Window 级 DynamicResource 在替换合并字典后不会重解析，
            // 导致「靠继承取色」的文字（卡片标题、步骤标题…）停留在旧主题。这里显式重设一次。
            Foreground = Br("Ink");
            Segments.Resnap();
        }

        void ApplyDensity()
        {
            bool compact = _cfg != null && string.Equals(_cfg.Density, "compact", StringComparison.OrdinalIgnoreCase);
            RowHeader.Height = new GridLength(compact ? 46 : 56);
            RowStatus.Height = new GridLength(compact ? 32 : 40);
            BodyGrid.Margin = compact ? new Thickness(14, 10, 14, 0) : new Thickness(18, 16, 18, 0);
            WorkflowBar.Padding = compact ? new Thickness(14, 8, 14, 8) : new Thickness(18, 13, 18, 13);
        }

        // ================= 数据 =================
        string FirstVersion()
        {
            return _cfg.Versions != null && _cfg.Versions.Count > 0 ? _cfg.Versions[0] : "proj-4";
        }

        string StudioConfigDir()
        {
            var a = _cfg.App("studio");
            return a != null ? a.Dir : "";
        }

        string CurrentVersionFromFile()
        {
            try
            {
                var a = _cfg.App("terminal");
                if (a != null && a.ConfigPath != null && File.Exists(a.ConfigPath))
                {
                    var v = WritePlan.ReadField(File.ReadAllText(a.ConfigPath, Encoding.UTF8), "ProjectName");
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }
            return FirstVersion();
        }

        static AppSpec WithDir(AppSpec a, string dir)
        {
            return new AppSpec { Label = a.Label, Dir = dir, Exe = a.Exe, UrlDataServer = a.UrlDataServer, AltDirs = a.AltDirs };
        }

        AppSpec Effective(string key)
        {
            var a = _cfg.App(key);
            if (a == null) return null;
            if (key == "studio" && !string.IsNullOrWhiteSpace(_studioDir)) return WithDir(a, _studioDir);
            return a;
        }

        WritePlan PlanFor(string key)
        {
            return WritePlan.Build(key, Effective(key), _cfg.Env(_env), _version, key == _app);
        }

        string EnvLabel()
        {
            var e = _cfg.Env(_env);
            return e != null ? e.Label : _env;
        }

        void SaveLast()
        {
            if (_screenshot || _suppressSave) return;   // 截图/测帧率的实例绝不写配置
            _cfg.Last.App = _app;
            _cfg.Last.Env = _env;
            _cfg.Last.Version = _version;
            _cfg.Last.StudioDir = _studioDir;
            _cfg.Save(ConfigPath);
        }

        // ================= 刷新界面 =================
        void Rebuild()
        {
            _refreshing = true;
            try
            {
                RebuildCards();
                RebuildEnvBadge();
                // 环境滑块里的文字（内容来自配置.json；字色由界面里的 Foreground=Ink 定：白天黑字、夜晚白字）
                var envOn = _cfg.Env("online");
                var envLo = _cfg.Env("local");
                EnvOnline.Content = envOn != null ? envOn.Label + " · " + envOn.Host : "线上";
                EnvLocal.Content = envLo != null ? envLo.Label + " · " + envLo.Host : "本地";
                Step1.IsChecked = _app == "terminal" && _env == "online";
                Step2.IsChecked = _app == "terminal" && _env == "local";
                Step3.IsChecked = _app == "studio" && _env == "local";
                Step4.IsChecked = _app == "studio" && _env == "online";
                PaintStepNumbers();
                VersionBtnText.Text = _version;
                var studioDir = Effective("studio") != null ? Effective("studio").Dir : "";
                DirBtnText.Text = Shorten(studioDir, 56);
                KillBtn.IsEnabled = _running.Count > 0;
            }
            finally { _refreshing = false; }
            RenderStatus();
            RebuildDiff();
        }

        void RebuildCards()
        {
            var term = _cfg.App("terminal");
            var studio = Effective("studio");
            TerminalTitle.Text = term != null ? term.Label : "DemoClient";
            TerminalVer.Text = FileVer(term) ?? "未找到程序";
            TerminalDir.Text = term != null ? term.Dir : "";
            StudioTitle.Text = _cfg.App("studio") != null ? _cfg.App("studio").Label : "DemoConsole";
            StudioVer.Text = FileVer(studio) ?? "未找到程序";
            StudioDir.Text = studio != null ? studio.Dir : "";
            SetCard("terminal", TerminalCfg, TerminalMark, AppTerminal);
            SetCard("studio", StudioCfg, StudioMark, AppStudio);
        }

        void SetCard(string key, TextBlock block, Border mark, RadioButton radio)
        {
            var plan = PlanFor(key);
            if (plan.Ok)
            {
                block.Text = "配置：url " + HostOf(plan.CurrentUrl) + " · 版本 " + plan.CurrentVersion;
                block.Foreground = Br("Ink2");
            }
            else
            {
                block.Text = "配置：" + plan.Error;
                block.Foreground = Br("DangerInk");
            }
            bool selected = key == _app;
            block.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            mark.Background = selected ? Br("Brand") : Brushes.Transparent;
            mark.BorderBrush = selected ? Br("Brand") : Br("Ink3");
            radio.IsChecked = selected;
        }

        static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(空)";
            try { return new Uri(url).Host; } catch { return url; }
        }

        string FileVer(AppSpec a)
        {
            try
            {
                if (a == null || a.ExePath == null || !File.Exists(a.ExePath)) return null;
                var v = FileVersionInfo.GetVersionInfo(a.ExePath);
                return (a.Exe ?? "") + "  " + (v.FileVersion ?? "");
            }
            catch { return a.Exe; }
        }

        static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : "…" + s.Substring(s.Length - max + 1);
        }

        void RebuildEnvBadge()
        {
            var e = _cfg.Env(_env);
            if (e == null) return;
            if (_env == "online")
            {
                EnvBadge.Background = Br("DangerFill");
                EnvBadgeText.Foreground = Br("OnDanger");
                EnvBadgeText.Text = "线上 · 生产数据 " + e.Host;
            }
            else
            {
                EnvBadge.Background = Br("BrandSoft");
                EnvBadgeText.Foreground = Br("Brand");
                EnvBadgeText.Text = "本地 · 127.0.0.1";
            }
        }

        void RebuildDiff()
        {
            DiffHost.Children.Clear();

            // 同事机器可能没装本地服务：先把话说清楚
            if (_env == "local" && _probe.At != DateTime.MinValue && _probe.Cc == "missing" && _probe.Dc == "missing")
            {
                DiffHost.Children.Add(new TextBlock
                {
                    Text = "注意：本机没装本地服务（DemoApiService / DemoDataService），「本地」环境用不了；"
                         + "改用「线上」，或找系统管理员装本地服务。",
                    FontSize = 11.5,
                    Foreground = Br("DangerInk"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 8, 10, 2)
                });
            }

            var order = new List<WritePlan> { PlanFor(_app) };
            if (_cfg.WriteBoth)
                foreach (var k in AppConfig.AppKeys)
                    if (k != _app) order.Add(PlanFor(k));

            int files = order.Count(p => p.Ok);
            int changed = order.Where(p => p.Ok).Sum(p => p.Changes.Count(c => c.Changed));
            DiffHint.Text = files + " 个文件 · 改 " + changed + " 个值";
            foreach (var p in order) AddFileBlock(p);
        }

        void AddFileBlock(WritePlan p)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, p.IsTarget ? 7 : 13, 0, 3) };
            head.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(1),
                Background = p.IsTarget ? Br("Brand") : Br("Ink3"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0)
            });
            head.Children.Add(new TextBlock
            {
                Text = (p.AppLabel ?? p.AppKey) + (p.IsTarget ? "（本次启动）" : "（一起改成同环境同版本）"),
                FontSize = 12,
                FontWeight = p.IsTarget ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = p.IsTarget ? Br("Ink") : Br("Ink2"),
                VerticalAlignment = VerticalAlignment.Center
            });
            DiffHost.Children.Add(head);

            DiffHost.Children.Add(new TextBlock
            {
                Text = p.Path ?? "(未配置)",
                FontFamily = Ff("FontMono"),
                FontSize = 11,
                Foreground = Br("Ink2"),
                Margin = new Thickness(26, 0, 10, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!p.Ok)
            {
                DiffHost.Children.Add(new TextBlock
                {
                    Text = "  不能写入：" + p.Error,
                    FontSize = 11.5,
                    Foreground = Br("DangerInk"),
                    Margin = new Thickness(26, 2, 10, 2),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var c in p.Changes)
            {
                var row = new Grid { Margin = new Thickness(26, 1, 10, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var key = new TextBlock
                {
                    Text = c.Key,
                    FontFamily = Ff("FontMono"),
                    FontSize = 11,
                    Foreground = Br(c.Changed ? "Ink2" : "Ink3"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(key, 0);
                row.Children.Add(key);

                var oldTxt = new TextBlock
                {
                    Text = Pretty(c.OldValue),
                    FontFamily = Ff("FontMono"),
                    FontSize = 11.5,
                    Foreground = Br(c.Changed ? "Ink2" : "Ink3"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(oldTxt, 2);
                row.Children.Add(oldTxt);

                if (c.Changed)
                {
                    var arrow = new TextBlock
                    {
                        Text = "→",
                        FontSize = 11.5,
                        Foreground = Br("Ink3"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(arrow, 3);
                    row.Children.Add(arrow);

                    var nb = new Border
                    {
                        Background = Br("BrandSoft"),
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(6, 1, 6, 1),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    nb.Child = new TextBlock
                    {
                        Text = Pretty(c.NewValue),
                        FontFamily = Ff("FontMono"),
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Br("Brand")
                    };
                    Grid.SetColumn(nb, 4);
                    row.Children.Add(nb);
                }
                else
                {
                    var same = new TextBlock
                    {
                        Text = "不变",
                        FontSize = 11,
                        Foreground = Br("Ink3"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(same, 3);
                    Grid.SetColumnSpan(same, 2);
                    row.Children.Add(same);
                }
                DiffHost.Children.Add(row);
            }
        }

        static string Pretty(string v)
        {
            return string.IsNullOrEmpty(v) ? "(空)" : v;
        }

        // 步骤按钮的序号块：选中时用主题色实心块（OnBrand 字），未选中是描边中性块 —— 与卡片右上角方块同一套语言
        void PaintStepNumbers()
        {
            PaintStepNumber(Step1Num, Step1NumText, _app == "terminal" && _env == "online");
            PaintStepNumber(Step2Num, Step2NumText, _app == "terminal" && _env == "local");
            PaintStepNumber(Step3Num, Step3NumText, _app == "studio" && _env == "local");
            PaintStepNumber(Step4Num, Step4NumText, _app == "studio" && _env == "online");
        }

        void PaintStepNumber(Border box, TextBlock num, bool selected)
        {
            box.Background = selected ? Br("Brand") : Brushes.Transparent;
            box.BorderBrush = selected ? Br("Brand") : Br("Line");
            num.Foreground = selected ? Br("OnBrand") : Br("Ink");
            num.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        /// <summary>测帧率：统计 CompositionTarget.Rendering 的真实帧数。
        /// baselineOnly=true 时只跑一个极简的透明度动画，用来量出「这台机器/这个会话的可渲染上限」。</summary>
        public async Task<double> MeasureFpsAsync(int ms, bool baselineOnly)
        {
            _suppressSave = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            FrameworkElement probe = null;
            if (baselineOnly)
            {
                probe = new Border
                {
                    Width = 1,
                    Height = 1,
                    Opacity = 0.5,
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };
                Root.Children.Add(probe);
                probe.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 0.6, new Duration(TimeSpan.FromMilliseconds(400)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                });
            }

            int frames = 0;
            EventHandler onFrame = (s, e) => frames++;
            CompositionTarget.Rendering += onFrame;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (!baselineOnly) Segments.Wiggle(4);
                await Task.Delay(baselineOnly ? 200 : 880);
            }
            CompositionTarget.Rendering -= onFrame;
            if (probe != null)
            {
                probe.BeginAnimation(OpacityProperty, null);
                Root.Children.Remove(probe);
            }
            _suppressSave = false;
            return frames / Math.Max(0.001, sw.Elapsed.TotalSeconds);
        }

        // ================= 状态条 =================
        void RenderStatus()
        {
            StatusHost.Children.Clear();
            bool probed = _probe.At != DateTime.MinValue;
            AddItem("CC", SvcState(_probe.Cc, probed));
            AddItem("DC", SvcState(_probe.Dc, probed));
            AddItem("443", probed ? (bool?)_probe.Local443 : null);
            AddItem("4567", probed ? (bool?)_probe.Local4567 : null);
            AddSep();
            AddItem("线上", probed ? (bool?)_probe.Online443 : null);
            AddSep();
            AddText("实例", Br("Ink2"));
            if (_running.Count > 0) AddText(string.Join("、", _running), Br("DangerInk"), true);
            else AddText("未运行", Br("Ink3"));
            AddSep();
            AddText("记录", Br("Ink2"));
            var last = LastLogSummary();
            AddText(string.IsNullOrEmpty(last) ? "还没有记录" : last, Br("Ink3"));
            if (!string.IsNullOrEmpty(_flash))
            {
                AddSep();
                AddText(_flash, _running.Count > 0 ? Br("DangerInk") : Br("Brand"), true);
            }
        }

        static bool? SvcState(string s, bool probed)
        {
            if (!probed) return null;
            if (s == "running") return true;
            if (s == "missing") return false;
            return null;
        }

        void AddItem(string label, bool? state)
        {
            StatusHost.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Background = state == null ? Br("Ink3") : (state.Value ? Br("Ok") : Br("Danger"))
            });
            StatusHost.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = Ff("FontMono"),
                FontSize = 11,
                Foreground = Br("Ink2"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            });
        }

        void AddText(string text, Brush fg, bool warn = false)
        {
            StatusHost.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = fg,
                FontWeight = warn ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        void AddSep()
        {
            StatusHost.Children.Add(new Border
            {
                Width = 1,
                Height = 14,
                Background = Br("Line"),
                Margin = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        static string LastLogSummary()
        {
            var line = Runtime.LastLogLine(DataDir);
            if (string.IsNullOrEmpty(line)) return "";
            var parts = line.Split('|');
            if (parts.Length >= 4)
            {
                var t = parts[0].Trim();
                if (t.Length >= 16) t = t.Substring(11, 5);
                return t + " " + parts[1].Trim() + " · " + parts[2].Trim() + " · " + parts[3].Trim();
            }
            return line;
        }

        async Task RefreshAsync()
        {
            if (_screenshot) return;
            var cfg = _cfg;
            ProbeResult probe = null;
            List<string> running = null;
            await Task.Run(() =>
            {
                probe = Runtime.ProbeAll(cfg);
                running = new List<string>();
                foreach (var key in AppConfig.AppKeys)
                {
                    var a = Effective(key);
                    if (a == null) continue;
                    if (Runtime.Find(a.ExeName).Length > 0) running.Add(a.Label);
                }
            });
            _probe = probe;
            _running = running;
            KillBtn.IsEnabled = _running.Count > 0;
            RenderStatus();
        }

        // ================= 交互 =================
        void SelectRadio(string app, string env)
        {
            AppTerminal.IsChecked = app == "terminal";
            AppStudio.IsChecked = app == "studio";
            EnvOnline.IsChecked = env == "online";
            EnvLocal.IsChecked = env == "local";
        }

        void App_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _refreshing) return;
            var tag = (string)(sender as FrameworkElement).Tag;
            if (tag == null) return;
            _app = tag;
            Rebuild();
            SaveLast();
        }

        void Env_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _refreshing) return;
            var tag = (string)(sender as FrameworkElement).Tag;
            if (tag == null) return;
            _env = tag;
            Rebuild();
            SaveLast();
        }

        void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _refreshing) return;
            _cfg.Theme = (string)(sender as FrameworkElement).Tag;
            ApplyTheme();
            Rebuild();
            SaveLast();
        }

        void Density_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _refreshing) return;
            _cfg.Density = (string)(sender as FrameworkElement).Tag;
            ApplyDensity();
            Rebuild();
            SaveLast();
        }

        void VersionBtn_Click(object sender, RoutedEventArgs e)
        {
            BuildVersionList();
            VersionPopup.IsOpen = true;
        }

        void BuildVersionList()
        {
            VersionList.Children.Clear();
            foreach (var v in _cfg.Versions)
            {
                var b = new Button { Content = v, Style = (Style)FindResource("DropItem") };
                if (v == _version) b.Foreground = Br("Brand");
                var captured = v;
                b.Click += (s, e2) => { _version = captured; VersionPopup.IsOpen = false; Rebuild(); SaveLast(); };
                VersionList.Children.Add(b);
            }
        }

        void AddVersion_Click(object sender, RoutedEventArgs e)
        {
            var v = Dialogs.Input(this, "新增版本号", "填需求文档里指定的 star 版本（例如 star77）：", "star");
            if (string.IsNullOrWhiteSpace(v)) return;
            v = v.Trim();
            if (!WritePlan.VersionRule.IsMatch(v))
            {
                Dialogs.Info(this, "版本号不合法", "只允许字母、数字、下划线、点、横线，长度 1–32。");
                return;
            }
            if (!_cfg.Versions.Contains(v)) _cfg.Versions.Add(v);
            _version = v;
            _cfg.Save(ConfigPath);
            Rebuild();
        }

        void DirBtn_Click(object sender, RoutedEventArgs e)
        {
            BuildDirList();
            DirPopup.IsOpen = true;
        }

        void BuildDirList()
        {
            DirList.Children.Clear();
            var a = _cfg.App("studio");
            var all = new List<string>();
            if (a != null && !string.IsNullOrWhiteSpace(a.Dir)) all.Add(a.Dir);
            if (a != null && a.AltDirs != null) all.AddRange(a.AltDirs);
            var cur = Effective("studio") != null ? Effective("studio").Dir : "";
            foreach (var d in Runtime.DiscoverDirs(a != null ? a.Exe : "DemoConsole.exe", @"D:\", @"C:\Program Files"))
            {
                if (all.Count < 12) all.Add(d);
            }
            foreach (var d in all.Distinct())
            {
                var b = new Button { Content = d, Style = (Style)FindResource("DropItem"), FontFamily = Ff("FontMono"), FontSize = 11.5 };
                if (string.Equals(d, cur, StringComparison.OrdinalIgnoreCase)) b.Foreground = Br("Brand");
                var captured = d;
                b.Click += (s, e2) => { _studioDir = captured; DirPopup.IsOpen = false; Rebuild(); SaveLast(); };
                DirList.Children.Add(b);
            }
        }

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            var cur = Effective("studio") != null ? Effective("studio").Dir : "";
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择包含 DemoConsole.exe 的目录";
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(cur) && Directory.Exists(cur)) dlg.SelectedPath = cur;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var a = _cfg.App("studio");
                if (a != null && !File.Exists(Path.Combine(dlg.SelectedPath, a.Exe)))
                {
                    if (!Dialogs.Confirm(this, "这个目录里没有程序",
                        "在 " + dlg.SelectedPath + " 里没找到 " + a.Exe + "。\n\n仍然使用这个目录吗？", "仍然使用", "重新选")) return;
                }
                _studioDir = dlg.SelectedPath;
                Rebuild();
                SaveLast();
            }
        }

        // 单击步骤按钮 = 只选中（同步到界面与配置），不启动
        void Step_Click(object sender, RoutedEventArgs e)
        {
            SelectStep((string)(sender as FrameworkElement).Tag);
        }

        // 双击步骤按钮 = 按这一步真正启动
        async void Step_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectStep((string)(sender as FrameworkElement).Tag);
            await DoLaunch();
        }

        void SelectStep(string tag)
        {
            var parts = tag.Split('|');
            _loading = true;
            SelectRadio(parts[0], parts[1]);
            _loading = false;
            _app = parts[0];
            _env = parts[1];
            Rebuild();
            SaveLast();
        }

        async void Launch_Click(object sender, RoutedEventArgs e)
        {
            await DoLaunch();
        }

        // ================= 自动识别（同事机器上不用手填路径） =================
        bool NeedsDiscovery(string key)
        {
            var a = _cfg.App(key);
            if (a == null) return false;
            if (string.IsNullOrWhiteSpace(a.Dir)) return true;
            return !File.Exists(a.ExePath);          // 换机器/挪目录后路径失效
        }

        async Task AutoDiscoverAsync(bool initial)
        {
            if (_scanning) return;
            if (!NeedsDiscovery("terminal") && !NeedsDiscovery("studio")) return;
            await RunDiscoveryAsync(initial ? "首次启动：正在找DemoClient / DemoConsole…" : "正在重新扫描…", initial ? null : this);
        }

        async Task RunDiscoveryAsync(string note, Window owner)
        {
            if (_scanning) return;
            _scanning = true;
            _flash = note;
            RenderStatus();
            var launcherDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var found = new List<FoundApp>();
            await Task.Run(() => { found = Discovery.ScanAround(launcherDir); });
            _lastFound = found;
            _scanning = false;

            var notes = ApplyFound(found);
            _cfg.Save(ConfigPath);      // 目录/版本候选一并落盘，下次直接可用
            Rebuild();
            _flash = notes.Count > 0 ? ("已自动找到：" + string.Join("；", notes)) : FlashForScan(found);
            RenderStatus();

            if (owner != null)
            {
                var sb = new StringBuilder();
                if (found.Count == 0)
                    sb.Append("没找到DemoClient / DemoConsole。\n\n把本程序放到它们的旁边（同一个文件夹或同一个盘），再点「重扫」；也可以点「浏览」手动指定DemoConsole目录。");
                else
                    foreach (var f in found)
                        sb.Append("· " + f + "\n   " + f.Dir + "\n");
                Dialogs.Info(owner, "扫描结果", sb.ToString());
            }
        }

        List<string> ApplyFound(List<FoundApp> found)
        {
            var notes = new List<string>();
            var term = Discovery.PickBest(found, "terminal", null);
            var studio = Discovery.PickBest(found, "studio", term != null ? term.ProjectName : null);

            if (term != null && NeedsDiscovery("terminal"))
            {
                _cfg.Apps["terminal"].Dir = term.Dir;
                notes.Add("DemoClient → " + term.Dir);
            }
            if (studio != null && (string.IsNullOrWhiteSpace(_studioDir) || !File.Exists(Path.Combine(_studioDir, "DemoConsole.exe"))))
            {
                _studioDir = studio.Dir;
                _cfg.Apps["studio"].Dir = studio.Dir;
                notes.Add("DemoConsole → " + studio.Dir);
            }

            // 其它副本进备选、发现的 star 版本进候选
            foreach (var f in found)
            {
                if (f.Kind != FoundKind.Usable) continue;
                var a = _cfg.App(f.AppKey);
                if (a == null) continue;
                if (!string.Equals(a.Dir, f.Dir, StringComparison.OrdinalIgnoreCase) && !a.AltDirs.Contains(f.Dir))
                    a.AltDirs.Add(f.Dir);
                if (!string.IsNullOrEmpty(f.ProjectName) && !_cfg.Versions.Contains(f.ProjectName))
                    _cfg.Versions.Add(f.ProjectName);
            }

            // 用户还没选过版本时，跟随识别到的终端 appsettings.json 里的 ProjectName，并落盘记住
            if (string.IsNullOrWhiteSpace(_cfg.Last.Version))
            {
                _version = CurrentVersionFromFile();
                _cfg.Last.Version = _version;
            }
            return notes;
        }

        string FlashForScan(List<FoundApp> found)
        {
            if (found.Count == 0)
                return "没找到DemoClient / DemoConsole：把本程序放到它们旁边，或点「浏览」手动选";
            var suspect = found.FindAll(f => f.Kind == FoundKind.Suspect);
            if (suspect.Count > 0)
                return "找到一个像目标程序但缺 appsettings.json 的目录：" + suspect[0].Dir;
            return "扫描完成";
        }

        async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            await RunDiscoveryAsync("正在重新扫描…", this);
        }

        // ================= 导出诊断（同事出问题时一键打包） =================
        void Diag_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("EnvSwitch 诊断信息");
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("程序版本：v" + VersionOf() + "    程序路径：" + System.Reflection.Assembly.GetExecutingAssembly().Location);
                sb.AppendLine("系统：" + Environment.OSVersion + "   64位=" + Environment.Is64BitOperatingSystem);
                sb.AppendLine("数据目录：" + DataDir);
                sb.AppendLine();
                sb.AppendLine("--- 服务与端口 ---");
                sb.AppendLine("DemoApiService=" + Runtime.ServiceState(Runtime.CcService) + "  DemoDataService=" + Runtime.ServiceState(Runtime.DcService));
                sb.AppendLine("127.0.0.1:443=" + Runtime.PortOpen("127.0.0.1", 443, 700) + "  127.0.0.1:4567=" + Runtime.PortOpen("127.0.0.1", 4567, 700));
                var online = _cfg.Env("online");
                if (online != null) sb.AppendLine(online.Host + ":" + online.Port + "=" + Runtime.PortOpen(online.Host, online.Port, 900));
                sb.AppendLine();
                sb.AppendLine("--- 本轮识别结果 ---");
                if (_lastFound.Count == 0) sb.AppendLine("(没有扫描记录)");
                foreach (var f in _lastFound) sb.AppendLine("· " + f + "\r\n    " + f.Dir);
                sb.AppendLine();
                sb.AppendLine("--- 配置.json ---");
                sb.AppendLine(File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "(不存在)");
                foreach (var key in AppConfig.AppKeys)
                {
                    var a = Effective(key);
                    sb.AppendLine();
                    sb.AppendLine("--- " + (a != null ? a.Label : key) + " 的 appsettings.json ---");
                    sb.AppendLine(a != null ? ("路径：" + (a.ConfigPath ?? "(未配置)")) : "(未配置)");
                    try
                    {
                        if (a != null && a.ConfigPath != null && File.Exists(a.ConfigPath))
                            sb.AppendLine(File.ReadAllText(a.ConfigPath, Encoding.UTF8));
                    }
                    catch (Exception ex) { sb.AppendLine("(读取失败：" + ex.Message + ")"); }
                }
                sb.AppendLine();
                sb.AppendLine("--- 最近运行记录（末尾 50 行）---");
                try
                {
                    var log = Runtime.LogPath(DataDir);
                    if (File.Exists(log))
                    {
                        var lines = File.ReadAllLines(log, Encoding.UTF8);
                        int start = Math.Max(0, lines.Length - 50);
                        for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                    }
                }
                catch { }

                var outPath = Path.Combine(DataDir, "诊断信息.txt");
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
                Runtime.OpenPath(outPath);
                Dialogs.Info(this, "已导出诊断信息",
                    "文件：" + outPath + "\n\n把「诊断信息.txt」+「运行记录.log」发给维护者，就能定位问题。");
            }
            catch (Exception ex)
            {
                Dialogs.Info(this, "导出失败", ex.Message);
            }
        }

        // ================= 启动主流程 =================
        async Task DoLaunch()
        {
            if (_launching) return;
            _launching = true;
            LaunchBtn.IsEnabled = false;
            _flash = "";
            try
            {
                var app = Effective(_app);
                if (app == null) { Dialogs.Info(this, "启动失败", "配置.json 里没有这个软件。"); return; }
                var plan = PlanFor(_app);
                if (!plan.Ok) { Dialogs.Info(this, "不能启动", plan.Error); return; }
                var exe = app.ExePath;
                if (exe == null || !File.Exists(exe))
                {
                    Dialogs.Info(this, "不能启动", "找不到程序：\n" + (exe ?? "(目录未配置)"));
                    return;
                }
                var exeName = Path.GetFileNameWithoutExtension(exe);

                if (Runtime.Find(exeName).Length > 0)
                {
                    var paths = Runtime.RunningPaths(exeName);
                    if (!Dialogs.Confirm(this, "需要先关闭",
                        app.Label + " 正在运行" + (paths.Count > 0 ? "：\n" + string.Join("\n", paths) : "") +
                        "\n\n切换环境必须先关闭它（软件只在启动时读 appsettings.json）。\n现在关闭并继续？",
                        "关闭并继续", "取消")) return;
                    if (!Runtime.CloseGraceful(exeName, 6000))
                    {
                        if (!Dialogs.Confirm(this, "还没退出",
                            app.Label + " 没有响应关闭消息（可能正在弹窗或保存中）。\n\n强制结束？未保存的内容会丢。",
                            "强制结束", "取消")) return;
                        Runtime.ForceKill(exeName);
                        if (!Runtime.WaitAllExit(exeName, 3000))
                        {
                            Dialogs.Info(this, "关闭失败", "进程仍在运行，请手动关闭后重试。");
                            return;
                        }
                    }
                }

                var plans = new List<WritePlan> { plan };
                if (_cfg.WriteBoth)
                    foreach (var k in AppConfig.AppKeys)
                        if (k != _app) plans.Add(PlanFor(k));

                int written = 0;
                var skipped = new List<string>();
                foreach (var p in plans)
                {
                    if (!p.Ok)
                    {
                        if (p.IsTarget) { Dialogs.Info(this, "不能启动", p.Error); return; }
                        skipped.Add(p.AppLabel);
                        continue;
                    }
                    p.Apply();
                    written++;
                }

                Runtime.StartApp(app);
                Runtime.AppendLog(DataDir, string.Format("{0} | {1} | {2} | 写{3}份 | {4}",
                    app.Label, EnvLabel(), _version, written, exe));
                _flash = "已启动 " + app.Label + " · " + EnvLabel() + " · " + _version +
                         (skipped.Count > 0 ? "（另一份没写成：" + string.Join("、", skipped) + "）" : "");
                SaveLast();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Dialogs.Info(this, "启动失败", ex.Message);
                Runtime.AppendLog(DataDir, "失败 | " + ex.Message);
            }
            finally
            {
                _launching = false;
                LaunchBtn.IsEnabled = true;
                RenderStatus();
            }
        }

        async void Kill_Click(object sender, RoutedEventArgs e)
        {
            var targets = new List<AppSpec>();
            foreach (var key in AppConfig.AppKeys)
            {
                var a = Effective(key);
                if (a != null && Runtime.Find(a.ExeName).Length > 0) targets.Add(a);
            }
            if (targets.Count == 0) { _flash = "没有在运行的实例"; RenderStatus(); return; }
            if (!Dialogs.Confirm(this, "关闭实例",
                "将要关闭：\n" + string.Join("\n", targets.Select(t => t.Label + "  " + t.ExePath)) + "\n\n继续？",
                "关闭", "取消")) return;
            foreach (var t in targets)
            {
                var name = t.ExeName;
                if (!Runtime.CloseGraceful(name, 6000))
                {
                    if (!Dialogs.Confirm(this, "还没退出", t.Label + " 没有响应关闭消息。强制结束？", "强制结束", "取消")) continue;
                    Runtime.ForceKill(name);
                }
                Runtime.AppendLog(DataDir, "关闭实例 | " + t.Label);
            }
            _flash = "已关闭在跑的实例";
            await RefreshAsync();
        }

        void OpenDir_Click(object sender, RoutedEventArgs e)
        {
            var a = Effective(_app);
            if (a != null && !string.IsNullOrEmpty(a.Dir)) Runtime.OpenPath(a.Dir);
        }

        void Help_Click(object sender, RoutedEventArgs e)
        {
            var p = Path.Combine(DataDir, "说明.md");
            Runtime.OpenPath(File.Exists(p) ? p : DataDir);
        }
    }
}
