using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SyntaX
{
    internal enum NotificationKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    internal sealed class NotificationHost
    {
        private readonly StackPanel _stack;
        private readonly Window _window;

        public NotificationHost(Window window, StackPanel stack)
        {
            _window = window;
            _stack = stack;
        }

        public void Show(string message, NotificationKind kind = NotificationKind.Info, int durationMs = 4200)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _window.Dispatcher.Invoke(() => PushToast(message, kind, durationMs));
        }

        private void PushToast(string message, NotificationKind kind, int durationMs)
        {
            var accent = GetAccent(kind);
            var toast = new Border
            {
                MaxWidth = 340,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0,
                RenderTransform = new TranslateTransform(30, 0),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    Opacity = 0.4,
                    ShadowDepth = 8,
                    Direction = 315
                }
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconText = GetIcon(kind);
            var icon = new TextBlock
            {
                Text = iconText,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 10, 0),
                Foreground = new SolidColorBrush(accent)
            };
            Grid.SetColumn(icon, 0);
            inner.Children.Add(icon);

            var text = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 245)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);
            inner.Children.Add(text);

            toast.Child = inner;
            ApplyKindStyle(toast, kind, accent);
            _stack.Children.Insert(0, toast);

            var slide = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slide);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (_, __) =>
                {
                    try { _stack.Children.Remove(toast); } catch { }
                };
                toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            timer.Start();
        }

        private static void ApplyKindStyle(Border toast, NotificationKind kind, Color accent)
        {
            toast.Background = new SolidColorBrush(Color.FromArgb(230, 14, 14, 20));
            toast.BorderBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
        }

        private static Color GetAccent(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.Success: return Color.FromRgb(52, 211, 153);
                case NotificationKind.Warning: return Color.FromRgb(251, 191, 36);
                case NotificationKind.Error: return Color.FromRgb(248, 113, 113);
                default: return Color.FromRgb(96, 165, 250);
            }
        }

        private static string GetIcon(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.Success: return "\u2713";
                case NotificationKind.Warning: return "\u26A0";
                case NotificationKind.Error: return "\u2717";
                default: return "\u24D8";
            }
        }
    }
}
