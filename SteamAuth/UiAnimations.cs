using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SyntaX
{
    internal static class UiAnimations
    {
        private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static readonly IEasingFunction EaseInOut = new QuarticEase { EasingMode = EasingMode.EaseInOut };
        private static readonly IEasingFunction ElasticOut = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 7 };
        private static readonly IEasingFunction BounceOut = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 3, Bounciness = 1.6 };
        private static readonly IEasingFunction BackOut = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

        public static void Enter(UIElement target, int durationMs = 450, double slideY = 22)
        {
            if (target == null) return;
            target.Opacity = 0;
            var slide = new TranslateTransform(0, slideY);
            target.RenderTransform = slide;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            var dur = TimeSpan.FromMilliseconds(durationMs);
            target.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = EaseOut });
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(slideY, 0, dur) { EasingFunction = EaseOut });
        }

        public static void EnterFrom(UIElement target, double fromX, double fromY, int ms = 400)
        {
            if (target == null) return;
            target.Opacity = 0;
            var t = new TranslateTransform(fromX, fromY);
            target.RenderTransform = t;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            var d = TimeSpan.FromMilliseconds(ms);
            target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = EaseOut });
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, d) { EasingFunction = BackOut });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, d) { EasingFunction = BackOut });
        }

        public static void EnterStagger(UIElement target, int index, int baseMs = 350, int stepMs = 60, double slideY = 16)
        {
            Enter(target, baseMs + Math.Max(0, index) * stepMs, slideY);
        }

        public static void SwapContent(Panel host, UIElement content, int fadeOutMs = 80, int fadeInMs = 350)
        {
            if (host == null || content == null) return;
            if (host.Children.Count == 0)
            {
                host.Children.Add(content);
                Enter(content, fadeInMs);
                return;
            }
            var old = host.Children[0];
            var fadeOut = new DoubleAnimation(old.Opacity, 0, TimeSpan.FromMilliseconds(fadeOutMs)) { EasingFunction = EaseOut };
            var slideOut = new TranslateTransform(0, 0);
            old.RenderTransform = slideOut;
            slideOut.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -12, TimeSpan.FromMilliseconds(fadeOutMs)) { EasingFunction = EaseOut });
            fadeOut.Completed += (s, e) =>
            {
                host.Children.Clear();
                host.Children.Add(content);
                Enter(content, fadeInMs, 20);
            };
            old.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        public static void PressScale(FrameworkElement target, double scale = 0.95)
        {
            if (target == null) return;
            EnsureScale(target);
            var d = TimeSpan.FromMilliseconds(70);
            var a = new DoubleAnimation(scale, d) { EasingFunction = EaseOut };
            if (target.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        public static void ReleaseScale(FrameworkElement target)
        {
            if (target == null || !(target.RenderTransform is ScaleTransform st)) return;
            var d = TimeSpan.FromMilliseconds(250);
            var a = new DoubleAnimation(1, d) { EasingFunction = ElasticOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public static void TapPulse(FrameworkElement target)
        {
            if (target == null) return;
            EnsureScale(target);
            if (!(target.RenderTransform is ScaleTransform st)) return;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(350) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero), EaseOut));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.93, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), EaseOut));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.04, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), EaseOut));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350)), EaseOut));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public static void SoftFlash(UIElement target)
        {
            if (target == null) return;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            target.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static void PulseOpacity(UIElement target, double min, double max, int ms = 2000)
        {
            if (target == null) return;
            var a = new DoubleAnimation(min, max, TimeSpan.FromMilliseconds(ms))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = EaseInOut
            };
            target.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static void FloatParticle(UIElement target, double distance, double durationSec)
        {
            if (target == null) return;
            var a = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(durationSec),
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(durationSec / 2))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(durationSec))));
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            if (!(target.RenderTransform is TranslateTransform))
                target.RenderTransform = new TranslateTransform();
            if (target.RenderTransform is TranslateTransform tt)
                tt.BeginAnimation(TranslateTransform.YProperty, a);
        }

        public static void RotateForever(FrameworkElement target, int ms = 3000)
        {
            if (target == null) return;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            if (!(target.RenderTransform is RotateTransform))
                target.RenderTransform = new RotateTransform();
            if (target.RenderTransform is RotateTransform rt)
            {
                var a = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(ms))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                rt.BeginAnimation(RotateTransform.AngleProperty, a);
            }
        }

        private static void EnsureScale(FrameworkElement target)
        {
            if (target.RenderTransform is ScaleTransform) return;
            target.RenderTransform = new ScaleTransform(1, 1);
            target.RenderTransformOrigin = new Point(0.5, 0.5);
        }
    }
}
