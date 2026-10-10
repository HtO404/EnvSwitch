using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EnvSwitch
{
    /// <summary>
    /// 分段滑块的「滑动拇指」：选中项变化时，拇指平滑滑到那一段（宽度+位移两条动画，200ms）。
    /// 动画只走渲染层（TranslateTransform + 一个 12px 高元素的 Width），因此跟随显示器刷新率（60fps 级别）。
    /// </summary>
    public static class Segments
    {
        static readonly Duration Dur = new Duration(TimeSpan.FromMilliseconds(200));
        static readonly List<Entry> All = new List<Entry>();

        sealed class Entry
        {
            public Grid Host;
            public Border Thumb;
            public TranslateTransform Shift;
            public RadioButton[] Items;
        }

        public static void Bind(Grid host, Border thumb, TranslateTransform shift, params RadioButton[] items)
        {
            var e = new Entry { Host = host, Thumb = thumb, Shift = shift, Items = items };
            All.Add(e);
            foreach (var it in items)
            {
                it.Checked += (s, a) => Move(e, true);
                it.SizeChanged += (s, a) => Move(e, false);
            }
            host.SizeChanged += (s, a) => Move(e, false);
            host.Loaded += (s, a) => Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => Move(e, false)));
        }

        /// <summary>主题切换后重新贴合一次（颜色是 DynamicResource，会自动跟；这里只修尺寸/位置）。</summary>
        public static void Resnap()
        {
            foreach (var e in All) Move(e, false);
        }

        static void Move(Entry e, bool animate)
        {
            if (e.Host == null || e.Items == null) return;
            RadioButton sel = null;
            foreach (var it in e.Items) if (it.IsChecked == true) { sel = it; break; }
            if (sel == null || sel.ActualWidth <= 0) return;

            // 同一组选项强制等宽 ⇒ 拇指宽度恒定，动画期间只动 TranslateTransform（不触发任何布局）
            double maxW = 0;
            foreach (var it in e.Items) maxW = Math.Max(maxW, it.ActualWidth);
            if (maxW > 0)
            {
                foreach (var it in e.Items)
                    if (Math.Abs(it.Width - maxW) > 0.5) it.Width = maxW;
                if (Math.Abs(e.Thumb.Width - maxW) > 0.5 && !animate) e.Thumb.Width = maxW;
            }

            double x;
            try
            {
                var r = sel.TransformToAncestor(e.Host).TransformBounds(new Rect(0, 0, sel.ActualWidth, sel.ActualHeight));
                x = r.X;
            }
            catch { return; }

            if (!animate)
            {
                e.Shift.BeginAnimation(TranslateTransform.XProperty, null);
                e.Shift.X = x;
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            e.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, Dur) { EasingFunction = ease });
        }

        /// <summary>测帧率用：不改变选中状态，只让拇指来回滑。</summary>
        public static void Wiggle(int times)
        {
            foreach (var e in All)
            {
                if (e.Thumb == null || e.Host == null) continue;
                double span = Math.Max(40, e.Host.ActualWidth - e.Thumb.ActualWidth);
                var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                for (int i = 0; i < times; i++)
                {
                    var a = new DoubleAnimation(i % 2 == 0 ? span : 0, Dur)
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 220),
                        EasingFunction = ease
                    };
                    e.Shift.BeginAnimation(TranslateTransform.XProperty, a);
                }
            }        }
    }
}
