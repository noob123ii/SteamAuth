using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SyntaX
{
    public class AuthenticationService
    {
        private readonly MainWindow _mainWindow;
        private readonly AppTheme _theme;
        private string _cachedHWID;
        private Border _loadingOverlay;

        private static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily BodyFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Code, Consolas");

        public AuthenticationService(MainWindow mainWindow, AppTheme theme)
        {
            _mainWindow = mainWindow;
            _theme = theme;
        }

        public UIElement BuildLoginScreen(Action<string> onKeySubmit, Action onDiscordLogin)
        {
            var root = new Grid { ClipToBounds = true };

            // ---- Aurora background blobs ----
            for (int i = 0; i < 3; i++)
            {
                var blob = new Border
                {
                    Width = 300 + i * 180,
                    Height = 300 + i * 180,
                    CornerRadius = new CornerRadius(200 + i * 100),
                    Opacity = 0.08 + i * 0.03,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                var colors = i == 0 ? _theme.Accent :
                             i == 1 ? Color.FromRgb(180, 130, 255) :
                                      Color.FromRgb(100, 200, 255);
                blob.Background = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(100, colors.R, colors.G, colors.B), 0),
                        new GradientStop(Color.FromArgb(0, colors.R, colors.G, colors.B), 1)
                    }
                };
                blob.HorizontalAlignment = HorizontalAlignment.Left;
                blob.VerticalAlignment = VerticalAlignment.Top;
                blob.Margin = new Thickness(-100 + i * 60, -80 + i * 40, 0, 0);
                root.Children.Add(blob);

                var floatY = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(6 + i * 2),
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                };
                floatY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                floatY.KeyFrames.Add(new LinearDoubleKeyFrame(30 + i * 10, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3 + i))));
                floatY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6 + i * 2))));
                blob.BeginAnimation(FrameworkElement.MarginProperty, floatY);

                var floatX = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(8 + i * 2),
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                };
                floatX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                floatX.KeyFrames.Add(new LinearDoubleKeyFrame(25 + i * 8, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + i))));
                floatX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(8 + i * 2))));
            }

            // ---- Grid overlay pattern ----
            var pattern = new Border
            {
                Opacity = 0.04,
                IsHitTestVisible = false
            };
            var patternCanvas = new Grid();
            for (int x = 0; x < 60; x++)
            {
                var line = new Border
                {
                    Width = 1,
                    Height = 680,
                    Background = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(x * 25, 0, 0, 0)
                };
                patternCanvas.Children.Add(line);
            }
            for (int y = 0; y < 40; y++)
            {
                var line = new Border
                {
                    Width = 960,
                    Height = 1,
                    Background = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, y * 25, 0, 0)
                };
                patternCanvas.Children.Add(line);
            }
            pattern.Child = patternCanvas;
            root.Children.Add(pattern);

            // ---- Floating particles ----
            var particles = new Grid { IsHitTestVisible = false };
            var rng = new Random(42);
            for (int i = 0; i < 15; i++)
            {
                var size = 2 + rng.Next(4);
                var particle = new Border
                {
                    Width = size,
                    Height = size,
                    CornerRadius = new CornerRadius(size / 2),
                    Background = new SolidColorBrush(
                        i % 3 == 0 ? _theme.Accent :
                        i % 3 == 1 ? Color.FromRgb(180, 130, 255) :
                                     Color.FromRgb(100, 200, 255)),
                    Opacity = 0.2 + rng.NextDouble() * 0.3,
                    HorizontalAlignment = (HorizontalAlignment)rng.Next(3),
                    VerticalAlignment = (VerticalAlignment)rng.Next(3),
                    Margin = new Thickness(rng.Next(50, 800), rng.Next(30, 500), 0, 0),
                    Effect = new BlurEffect { Radius = size > 4 ? 3 : 1 }
                };
                particles.Children.Add(particle);
                UiAnimations.FloatParticle(particle, 8 + rng.Next(15), 3 + rng.NextDouble() * 3);
            }
            root.Children.Add(particles);

            // ---- Main card ----
            var card = new Border
            {
                Width = 440,
                Height = 480,
                CornerRadius = new CornerRadius(28),
                Background = new SolidColorBrush(Color.FromArgb(210, 8, 8, 14)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 30,
                    BlurRadius = 60,
                    Opacity = 0.6
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Panel.SetZIndex(card, 15);

            // Animated neon border glow ring
            var glowRing = new Border
            {
                Width = 444,
                Height = 484,
                CornerRadius = new CornerRadius(30),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B)),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Opacity = 0.8
            };
            Panel.SetZIndex(glowRing, 14);
            UiAnimations.PulseOpacity(glowRing, 0.4, 1.0, 2500);

            // Top accent bar with gradient
            var accentBar = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 0),
                        new GradientStop(Color.FromRgb(_theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 0.5),
                        new GradientStop(Color.FromArgb(0, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 1)
                    }
                },
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(30, 0, 30, 0)
            };
            card.Child = accentBar;

            // Content
            var content = new Grid { Margin = new Thickness(38, 40, 38, 32) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Logo ----
            var logoBorder = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(_theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    BlurRadius = 25,
                    Opacity = 0.35,
                    ShadowDepth = 0
                }
            };
            var logoGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(40, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 0),
                    new GradientStop(Color.FromArgb(20, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 1)
                }
            };
            logoBorder.Background = logoGradient;
            var logoText = new TextBlock
            {
                Text = "S",
                Foreground = new SolidColorBrush(_theme.Accent),
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                FontFamily = DisplayFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoBorder.Child = logoText;

            var headerPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            headerPanel.Children.Add(logoBorder);
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Welcome back",
                Foreground = new SolidColorBrush(Color.FromRgb(245, 242, 255)),
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                FontFamily = DisplayFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Enter your license key to continue",
                Foreground = new SolidColorBrush(Color.FromArgb(160, 200, 210, 235)),
                FontSize = 13,
                FontFamily = BodyFont,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetRow(headerPanel, 0);
            content.Children.Add(headerPanel);

            // ---- Form ----
            var form = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };

            // Input with floating label
            var inputContainer = new Grid { Height = 56, Margin = new Thickness(0, 0, 0, 4) };

            var inputBg = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(235, 14, 16, 26)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B)),
                BorderThickness = new Thickness(1.5)
            };
            inputContainer.Children.Add(inputBg);

            var keyBox = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 242, 255)),
                CaretBrush = new SolidColorBrush(_theme.Accent),
                BorderThickness = new Thickness(0),
                FontSize = 15,
                FontFamily = MonoFont,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(18, 0, 18, 0),
                SnapsToDevicePixels = true
            };
            TextOptions.SetTextFormattingMode(keyBox, TextFormattingMode.Display);
            keyBox.GotFocus += (s, e) =>
            {
                var ca = new ColorAnimation(
                    Color.FromArgb(70, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    Color.FromArgb(200, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                inputBg.BorderBrush = new SolidColorBrush(_theme.Accent);
                (inputBg.BorderBrush as SolidColorBrush)?.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            };
            keyBox.LostFocus += (s, e) =>
            {
                var ca = new ColorAnimation(
                    Color.FromArgb(200, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    Color.FromArgb(70, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                if (inputBg.BorderBrush is SolidColorBrush b)
                    b.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            };
            keyBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(keyBox.Text))
                    onKeySubmit(keyBox.Text);
            };
            inputContainer.Children.Add(keyBox);

            // Placeholder label (floats up on focus)
            var placeHolder = new TextBlock
            {
                Text = "License Key",
                Foreground = new SolidColorBrush(Color.FromArgb(120, 180, 190, 215)),
                FontSize = 14,
                FontFamily = BodyFont,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(20, 0, 0, 0),
                IsHitTestVisible = false
            };
            inputContainer.Children.Add(placeHolder);

            keyBox.GotFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(keyBox.Text))
                {
                    var pa = new ThicknessAnimation(new Thickness(20, 0, 0, 0), new Thickness(20, -28, 0, 0),
                        TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    placeHolder.BeginAnimation(FrameworkElement.MarginProperty, pa);
                    var sa = new DoubleAnimation(14, 10, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    placeHolder.BeginAnimation(TextBlock.FontSizeProperty, sa);
                    var oa = new DoubleAnimation(120, 180, TimeSpan.FromMilliseconds(200));
                    placeHolder.Foreground = new SolidColorBrush(Color.FromArgb(180, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B));
                }
            };
            keyBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(keyBox.Text))
                {
                    var pa = new ThicknessAnimation(new Thickness(20, -28, 0, 0), new Thickness(20, 0, 0, 0),
                        TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    placeHolder.BeginAnimation(FrameworkElement.MarginProperty, pa);
                    var sa = new DoubleAnimation(10, 14, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    placeHolder.BeginAnimation(TextBlock.FontSizeProperty, sa);
                    placeHolder.Foreground = new SolidColorBrush(Color.FromArgb(120, 180, 190, 215));
                }
            };

            form.Children.Add(inputContainer);

            // Submit button with glow
            var submitBtn = BuildNeonButton("AUTHENTICATE", () => onKeySubmit(keyBox.Text), true);
            submitBtn.Height = 50;
            submitBtn.Margin = new Thickness(0, 16, 0, 0);
            form.Children.Add(submitBtn);

            // Divider
            var div = new Grid { Margin = new Thickness(0, 20, 0, 0) };
            div.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            div.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            div.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dl = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(dl, 0); div.Children.Add(dl);
            var dt = new TextBlock { Text = "or", Foreground = new SolidColorBrush(Color.FromArgb(80, 200, 210, 235)), FontSize = 11, FontFamily = BodyFont, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dt, 1); div.Children.Add(dt);
            var dr = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            Grid.SetColumn(dr, 2); div.Children.Add(dr);
            form.Children.Add(div);

            // Discord button
            var discordBtn = BuildNeonButton("LOGIN WITH DISCORD", onDiscordLogin, false);
            discordBtn.Height = 44;
            discordBtn.Margin = new Thickness(0, 12, 0, 0);
            form.Children.Add(discordBtn);

            Grid.SetRow(form, 1);
            content.Children.Add(form);

            // Footer
            var footer = new TextBlock
            {
                Text = "Syntax v2.0  \u00B7  Secure Connection",
                Foreground = new SolidColorBrush(Color.FromArgb(70, 160, 170, 200)),
                FontSize = 10,
                FontFamily = BodyFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetRow(footer, 2);
            content.Children.Add(footer);

            card.Child = content;
            root.Children.Add(card);
            root.Children.Add(glowRing);

            // Loading overlay
            _loadingOverlay = BuildLoadingOverlay();
            root.Children.Add(_loadingOverlay);
            _loadingOverlay.Visibility = Visibility.Collapsed;

            return root;
        }

        private Border BuildLoadingOverlay()
        {
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(overlay, 100);
            var sp = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Spinner
            var spinner = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B)),
                BorderThickness = new Thickness(3),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 16)
            };
            // Spinner arc via a second border
            var arc = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                BorderBrush = new SolidColorBrush(_theme.Accent),
                BorderThickness = new Thickness(3),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 16),
                Opacity = 0.9
            };
            spinner.Child = arc;
            UiAnimations.RotateForever(spinner, 800);
            sp.Children.Add(spinner);

            sp.Children.Add(new TextBlock
            {
                Text = "Authenticating...",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 215)),
                FontSize = 14,
                FontFamily = BodyFont,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            overlay.Child = sp;
            return overlay;
        }

        public UIElement BuildUserProfileScreen(string discordUser, int accountCount, Action onContinue)
        {
            var root = new Grid();

            // Background blur/depth
            for (int i = 0; i < 2; i++)
            {
                var b = new Border
                {
                    Width = 250 + i * 150,
                    Height = 250 + i * 150,
                    CornerRadius = new CornerRadius(200),
                    Opacity = 0.06,
                    Background = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.5, 0.5),
                        Center = new Point(0.5, 0.5),
                        RadiusX = 0.5,
                        RadiusY = 0.5,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(80, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 0),
                            new GradientStop(Color.FromArgb(0, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 1)
                        }
                    },
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = i == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    Margin = new Thickness(-80, -80, -80, -80)
                };
                root.Children.Add(b);
            }

            var card = new Border
            {
                Width = 440,
                Height = 490,
                CornerRadius = new CornerRadius(28),
                Background = new SolidColorBrush(Color.FromArgb(210, 8, 8, 14)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 30,
                    BlurRadius = 60,
                    Opacity = 0.6
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var content = new Grid { Margin = new Thickness(38, 36, 38, 32) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Avatar with glow
            var avatar = new Border
            {
                Width = 96,
                Height = 96,
                CornerRadius = new CornerRadius(48),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(_theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    BlurRadius = 30,
                    Opacity = 0.4,
                    ShadowDepth = 0
                }
            };
            var avatarGrad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(_theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 0),
                    new GradientStop(Color.FromArgb(160, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 1)
                }
            };
            avatar.Background = avatarGrad;
            var letter = new TextBlock
            {
                Text = discordUser?.Length > 0 ? discordUser[0].ToString().ToUpper() : "U",
                Foreground = Brushes.White,
                FontSize = 40,
                FontWeight = FontWeights.Bold,
                FontFamily = DisplayFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatar.Child = letter;

            var header = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            header.Children.Add(avatar);
            header.Children.Add(new TextBlock
            {
                Text = "Welcome, " + (discordUser ?? "User"),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 242, 255)),
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                FontFamily = DisplayFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            header.Children.Add(new TextBlock
            {
                Text = "Account overview",
                Foreground = new SolidColorBrush(Color.FromArgb(140, 200, 210, 235)),
                FontSize = 13,
                FontFamily = BodyFont,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Grid.SetRow(header, 0);
            content.Children.Add(header);

            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            infoPanel.Children.Add(BuildProfileCard("Discord User", discordUser ?? "Unknown"));
            infoPanel.Children.Add(BuildProfileCard("Total Accounts", accountCount.ToString("N0")));
            infoPanel.Children.Add(BuildProfileCard("HWID", GetHWID()));

            var continueBtn = BuildNeonButton("CONTINUE TO APP", onContinue, true);
            continueBtn.Height = 48;
            continueBtn.Margin = new Thickness(0, 20, 0, 0);
            infoPanel.Children.Add(continueBtn);

            Grid.SetRow(infoPanel, 1);
            content.Children.Add(infoPanel);

            card.Child = content;
            root.Children.Add(card);
            return root;
        }

        public bool ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            return key.Equals("Dev", StringComparison.OrdinalIgnoreCase);
        }

        private Border BuildProfileCard(string label, string value)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lb = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 200, 210, 235)),
                FontSize = 11,
                FontFamily = BodyFont,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lb, 0); g.Children.Add(lb);
            var vb = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 242, 255)),
                FontSize = 14,
                FontFamily = MonoFont,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(vb, 1); g.Children.Add(vb);
            card.Child = g;
            return card;
        }

        private Border BuildNeonButton(string text, Action onClick, bool primary)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(16),
                Cursor = Cursors.Hand,
                Padding = new Thickness(24, 0, 24, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };

            if (primary)
            {
                b.Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(_theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 0),
                        new GradientStop(Color.FromArgb(200, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B), 1)
                    }
                };
                b.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(_theme.Accent.R, _theme.Accent.G, _theme.Accent.B),
                    BlurRadius = 24,
                    Opacity = 0.35,
                    ShadowDepth = 0
                };
            }
            else
            {
                b.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                b.BorderThickness = new Thickness(1);
            }

            var tb = new TextBlock
            {
                Text = text,
                Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                FontSize = 12,
                FontFamily = BodyFont,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            b.Child = tb;

            b.MouseEnter += (s, e) =>
            {
                if (primary)
                {
                    var sa = new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                    (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                    if (b.Effect is DropShadowEffect dse)
                    {
                        var ba = new DoubleAnimation(24, 36, TimeSpan.FromMilliseconds(200));
                        dse.BeginAnimation(DropShadowEffect.BlurRadiusProperty, ba);
                        var oa = new DoubleAnimation(0.35, 0.5, TimeSpan.FromMilliseconds(200));
                        dse.BeginAnimation(DropShadowEffect.OpacityProperty, oa);
                    }
                }
                else
                {
                    var ca = new ColorAnimation(Color.FromArgb(30, 255, 255, 255), Color.FromArgb(55, 255, 255, 255), TimeSpan.FromMilliseconds(200));
                    b.Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
                }
            };
            b.MouseLeave += (s, e) =>
            {
                if (primary)
                {
                    var sa = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                    (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                    if (b.Effect is DropShadowEffect dse)
                    {
                        var ba = new DoubleAnimation(36, 24, TimeSpan.FromMilliseconds(200));
                        dse.BeginAnimation(DropShadowEffect.BlurRadiusProperty, ba);
                        var oa = new DoubleAnimation(0.5, 0.35, TimeSpan.FromMilliseconds(200));
                        dse.BeginAnimation(DropShadowEffect.OpacityProperty, oa);
                    }
                }
                else
                {
                    b.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                }
            };
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                var pa = new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(70)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, pa);
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, pa);
            };
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                var ra = new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 } };
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, ra);
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, ra);
                if (b.IsMouseOver) onClick?.Invoke();
            };
            return b;
        }

        private string GetHWID()
        {
            if (!string.IsNullOrEmpty(_cachedHWID)) return _cachedHWID;
            try
            {
                var hwid = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.VersionString}|{Environment.ProcessorCount}";
                _cachedHWID = hwid.Length > 50 ? hwid.Substring(0, 50) + "..." : hwid;
                return _cachedHWID;
            }
            catch { return "Protected"; }
        }
    }
}
