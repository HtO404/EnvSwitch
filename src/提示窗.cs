using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EnvSwitch
{
    /// <summary>跟主界面同一套主题的小对话框（不用系统 MessageBox，免得主题跳档）。</summary>
    public static class Dialogs
    {
        static T Res<T>(string key, T fallback) where T : class
        {
            try { return (T)Application.Current.FindResource(key); } catch { return fallback; }
        }

        static Window Shell(Window owner, string title, double width)
        {
            var w = new Window
            {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                Background = Res("Bg", Brushes.White),
                Foreground = Res("Ink", Brushes.Black),
                FontFamily = Res<FontFamily>("FontBody", new FontFamily("Microsoft YaHei UI")),
                FontSize = 13,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                Icon = owner != null ? owner.Icon : null
            };
            return w;
        }

        static Grid Body(string message, Window w, out TextBlock msg)
        {
            var grid = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 21, FontSize = 13 };
            Grid.SetRow(msg, 0);
            grid.Children.Add(msg);
            w.Content = grid;
            return grid;
        }

        public static void Info(Window owner, string title, string message)
        {
            var w = Shell(owner, title, 470);
            TextBlock msg;
            var grid = Body(message, w, out msg);
            var ok = new Button
            {
                Content = "知道了",
                Style = Res<Style>("BtnPrimary", null),
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            ok.Click += (s, e) => w.Close();
            Grid.SetRow(ok, 1);
            grid.Children.Add(ok);
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape || e.Key == Key.Enter) w.Close(); };
            w.ShowDialog();
        }

        public static bool Confirm(Window owner, string title, string message, string okText, string cancelText)
        {
            bool result = false;
            var w = Shell(owner, title, 540);
            TextBlock msg;
            var grid = Body(message, w, out msg);
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var ok = new Button { Content = okText, Style = Res<Style>("BtnPrimary", null), MinWidth = 116 };
            var cancel = new Button { Content = cancelText, Style = Res<Style>("BtnGhost", null), MinWidth = 92, Margin = new Thickness(10, 0, 0, 0) };
            ok.Click += (s, e) => { result = true; w.Close(); };
            cancel.Click += (s, e) => { result = false; w.Close(); };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);
            w.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) w.Close();
                if (e.Key == Key.Enter) { result = true; w.Close(); }
            };
            w.ShowDialog();
            return result;
        }

        public static string Input(Window owner, string title, string message, string initial)
        {
            string result = null;
            var w = Shell(owner, title, 470);
            TextBlock msg;
            var grid = Body(message, w, out msg);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var box = new TextBox { Text = initial ?? "", Height = 34, Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(box, 1);
            grid.Children.Add(box);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var ok = new Button { Content = "确定", Style = Res<Style>("BtnPrimary", null), MinWidth = 100 };
            var cancel = new Button { Content = "取消", Style = Res<Style>("BtnGhost", null), MinWidth = 88, Margin = new Thickness(10, 0, 0, 0) };
            ok.Click += (s, e) => { result = box.Text; w.Close(); };
            cancel.Click += (s, e) => { result = null; w.Close(); };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 2);
            grid.Children.Add(panel);

            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            w.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { result = null; w.Close(); }
                if (e.Key == Key.Enter) { result = box.Text; w.Close(); }
            };
            w.ShowDialog();
            return result;
        }
    }
}
