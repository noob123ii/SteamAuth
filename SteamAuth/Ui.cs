using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace SyntaX
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            try { AntiTamper.Initialize(); } catch { }
            var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new MainWindow());
        }
    }

    public partial class MainWindow : Window
    {
        private AppTheme T => AppThemeManager.Current;
        private AuthenticationService _authService;

        private Border _shell;
        private Grid _contentHost;
        private StackPanel _notificationStack;
        private NotificationHost _notifications;
        private List<SteamAccount> _accountPool = new List<SteamAccount>();
        private SteamAccount _currentAccount;

        private const string AppName = "GorillaTagUnbanner";

        private static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily BodyFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Code, Consolas");

        private void Notify(string message, NotificationKind kind = NotificationKind.Info)
            => _notifications?.Show(message, kind);

        public MainWindow()
        {
            Title = AppName;
            Width = 960;
            Height = 680;
            MinWidth = 800;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            FontFamily = BodyFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);

            try
            {
                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Syntax.png", UriKind.Absolute));
            }
            catch { }

            AppThemeManager.Apply(UserConfigStore.GetThemeId());
            _authService = new AuthenticationService(this, T);

            _shell = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(0),
                SnapsToDevicePixels = true,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 20,
                    BlurRadius = 60,
                    Opacity = 0.65
                }
            };
            ApplyShellTheme();
            _contentHost = new Grid();
            _notificationStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 16, 16, 0),
                IsHitTestVisible = true
            };
            Panel.SetZIndex(_notificationStack, 100);

            var shellGrid = new Grid();
            shellGrid.Children.Add(_contentHost);
            shellGrid.Children.Add(_notificationStack);
            _shell.Child = shellGrid;

            // Outer border accent ring
            var accentRing = new Border
            {
                CornerRadius = new CornerRadius(17),
                BorderBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.Children.Add(_shell);
            root.Children.Add(accentRing);
            Content = root;
            Opacity = 0;
            _shell.Opacity = 0;
            _shell.RenderTransform = new ScaleTransform(0.92, 0.92);
            _shell.RenderTransformOrigin = new Point(0.5, 0.5);

            MouseLeftButtonDown += (s, e) =>
            {
                if (IsInteractiveClick(e.OriginalSource as DependencyObject))
                    return;
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            Loaded += async (s, e) => await OnWindowLoadedAsync();
        }

        private async Task OnWindowLoadedAsync()
        {
            PlayStartupFade();
            await Task.Delay(1000);
            await LoadAndShowAsync();
        }

        private void PlayStartupFade()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
            _shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });

            var scale = _shell.RenderTransform as ScaleTransform;
            if (scale != null)
            {
                var anim = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(900))
                {
                    EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 7 }
                };
                anim.Completed += (s, e) =>
                {
                    _shell.RenderTransform = Transform.Identity;
                    var border = (Content as Grid)?.Children[1] as Border;
                    if (border != null)
                        border.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
        }

        private static bool IsInteractiveClick(DependencyObject obj)
        {
            while (obj != null)
            {
                if (obj is TextBox || obj is PasswordBox || obj is Button)
                    return true;
                if (obj is Border border && border.Cursor == Cursors.Hand)
                    return true;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        private async Task LoadAndShowAsync() => ShowKeySystemLogin();

        private void ShowKeySystemLogin()
        {
            var loginScreen = _authService.BuildLoginScreen(ValidateAndSubmitKey, ShowDiscordLogin);
            _contentHost.Children.Clear();
            _contentHost.Children.Add(loginScreen);
            UiAnimations.Enter(loginScreen, 600, 30);
        }

        private void ShowDiscordLogin() => Notify("Discord login coming soon", NotificationKind.Info);

        private void ValidateAndSubmitKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                Notify("Please enter a key", NotificationKind.Error);
                return;
            }
            if (_authService.ValidateKey(key))
            {
                Notify("Access granted", NotificationKind.Success);
                LoadMainApp();
            }
            else
            {
                Notify("Invalid key", NotificationKind.Error);
            }
        }

        private async void LoadMainApp()
        {
            Notify("Loading accounts...", NotificationKind.Info);

            AccountsLoadResult loaded;
            try { loaded = await AuthService.FetchAllUniqueValidAccountsAsync(); }
            catch (Exception ex)
            {
                Notify("Failed to load accounts", NotificationKind.Error);
                ShowError("account load failed: " + ex.Message);
                return;
            }

            if (!loaded.Success)
            {
                Notify(loaded.Message ?? "Could not load accounts", NotificationKind.Error);
                ShowError(loaded.Message ?? "Could not load accounts");
                return;
            }

            _accountPool = loaded.Accounts ?? new List<SteamAccount>();
            if (_accountPool.Count == 0)
            {
                Notify("No valid accounts available", NotificationKind.Warning);
                ShowError("no valid accounts available");
                return;
            }

            AccountSwitchService.LoadForLicenseKey(AppProfileKey);
            _currentAccount = AccountSwitchService.ResolveSavedAccount(AppProfileKey, _accountPool);

            Notify("Loaded " + _accountPool.Count.ToString("N0") + " accounts", NotificationKind.Success);
            ShowUserProfile();
        }

        private void ShowUserProfile()
        {
            var profileScreen = _authService.BuildUserProfileScreen("Dev User", _accountPool.Count, ShowMainApp);
            _contentHost.Children.Clear();
            _contentHost.Children.Add(profileScreen);
            UiAnimations.Enter(profileScreen, 500, 25);
        }

        private void SignInAccount(SteamAccount account)
        {
            if (account == null) { Notify("Invalid account", NotificationKind.Warning); return; }

            _currentAccount = account;
            AccountSwitchService.SaveActiveAccount(AppProfileKey, account, startCooldown: false);

            try
            {
                Program.LoginToAccount(account);
                Notify("Logged in as " + (account.Username ?? "Unknown"), NotificationKind.Success);
            }
            catch (Exception ex) { Notify("Steam login failed: " + ex.Message, NotificationKind.Error); }

            if (_activeTab == MainTab.Accounts) RenderActiveTab();
        }

        private static string FormatAccountPoolCount(int count)
            => count <= 0 ? "No accounts available" : count.ToString("N0") + (count == 1 ? " account" : " accounts");
        private string FormatAccountPoolCount() => FormatAccountPoolCount(_accountPool?.Count ?? 0);

        private void ApplyShellTheme()
        {
            if (_shell == null) return;
            _shell.Background = T.CreateBackgroundBrush();
            _shell.BorderBrush = new SolidColorBrush(T.ShellBorder);
        }

        private Border WrapInput(FrameworkElement input)
        {
            var wrap = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(T.InputBackground),
                BorderBrush = T.FaintBorderBrush,
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 14),
                SnapsToDevicePixels = true
            };
            input.GotFocus += (s, e) =>
            {
                wrap.BorderBrush = new SolidColorBrush(T.Accent);
                ((SolidColorBrush)wrap.BorderBrush).BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(Color.FromArgb(50, T.Accent.R, T.Accent.G, T.Accent.B), Color.FromRgb(T.Accent.R, T.Accent.G, T.Accent.B), TimeSpan.FromMilliseconds(250)));
            };
            input.LostFocus += (s, e) =>
            {
                var brush = new SolidColorBrush(T.FaintBorder);
                wrap.BorderBrush = brush;
                brush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(Color.FromRgb(T.Accent.R, T.Accent.G, T.Accent.B), T.FaintBorder, TimeSpan.FromMilliseconds(250)));
            };
            return wrap;
        }

        private Border BuildTextInput(out TextBox box)
        {
            box = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = T.TextPrimaryBrush,
                CaretBrush = T.TextPrimaryBrush,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontFamily = BodyFont,
                SnapsToDevicePixels = true
            };
            TextOptions.SetTextFormattingMode(box, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(box, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(box, ClearTypeHint.Enabled);
            return WrapInput(box);
        }

        private Border BuildPasswordInput(out PasswordBox box)
        {
            box = new PasswordBox
            {
                Background = Brushes.Transparent,
                Foreground = T.TextPrimaryBrush,
                CaretBrush = T.TextPrimaryBrush,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontFamily = BodyFont,
                PasswordChar = '\u2022'
            };
            return WrapInput(box);
        }

        private Grid BuildTopBar(string title, string subtitle)
        {
            var top = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = T.TextPrimaryBrush,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                FontFamily = DisplayFont
            });
            if (!string.IsNullOrEmpty(subtitle))
            {
                heading.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Foreground = T.TextSubtleBrush,
                    FontSize = 12,
                    FontFamily = BodyFont,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
            Grid.SetColumn(heading, 0);
            top.Children.Add(heading);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(BuildIconButton("\u2699", ToggleSettings));
            btnRow.Children.Add(BuildIconButton("\u2013", () => WindowState = WindowState.Minimized));
            btnRow.Children.Add(BuildIconButton("\u2715", Close));
            Grid.SetColumn(btnRow, 1);
            top.Children.Add(btnRow);

            // Accent line below top bar
            var line = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -8)
            };
            top.Children.Add(line);
            Grid.SetColumnSpan(line, 2);

            return top;
        }

        private void ShowError(string message)
        {
            var root = new Grid();

            // Subtle background depth
            var bgBlob = new Border
            {
                Width = 300,
                Height = 300,
                CornerRadius = new CornerRadius(150),
                    Background = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.5, 0.5),
                        Center = new Point(0.5, 0.5),
                        RadiusX = 0.5,
                        RadiusY = 0.5,
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(30, T.Accent.R, T.Accent.G, T.Accent.B), 0),
                            new GradientStop(Color.FromArgb(0, T.Accent.R, T.Accent.G, T.Accent.B), 1)
                        }
                    },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 200, 50)
            };
            root.Children.Add(bgBlob);

            var card = new Border
            {
                Width = 400,
                CornerRadius = new CornerRadius(24),
                Background = new SolidColorBrush(Color.FromArgb(210, 8, 8, 14)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(40, 36, 40, 32),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 40,
                    Opacity = 0.5,
                    ShadowDepth = 16
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            var iconBorder = new Border
            {
                Width = 72,
                Height = 72,
                CornerRadius = new CornerRadius(36),
                Background = new SolidColorBrush(Color.FromArgb(30, 248, 113, 113)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            iconBorder.Child = new TextBlock
            {
                Text = "!",
                Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                FontSize = 36,
                FontWeight = FontWeights.Bold,
                FontFamily = DisplayFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(iconBorder);

            content.Children.Add(new TextBlock
            {
                Text = "Something went wrong",
                Foreground = new SolidColorBrush(Color.FromRgb(245, 242, 255)),
                FontSize = 20,
                FontFamily = DisplayFont,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });
            content.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 200, 210, 235)),
                FontSize = 13,
                FontFamily = BodyFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 24)
            });

            var closeBtn = BuildPrimaryButton("DISMISS", Close);
            closeBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.Children.Add(closeBtn);

            card.Child = content;
            root.Children.Add(card);
            FadeInView(root);
        }

        private void FadeInView(UIElement view, int durationMs = 300)
        {
            _contentHost.Children.Clear();
            _contentHost.Children.Add(view);
            UiAnimations.Enter(view, durationMs, 22);
        }

        private void WirePrimaryButton(Border b, Action onClick)
        {
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                UiAnimations.PressScale(b);
                b.CaptureMouse();
            };
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                UiAnimations.ReleaseScale(b);
                if (b.IsMouseCaptured) b.ReleaseMouseCapture();
                if (b.IsMouseOver) onClick?.Invoke();
            };
            b.MouseLeave += (s, e) => UiAnimations.ReleaseScale(b);
        }

        private Border BuildIconButton(string glyph, Action onClick)
        {
            var b = new Border
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph,
                    Foreground = T.TextPrimaryBrush,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; UiAnimations.PressScale(b); };
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                UiAnimations.ReleaseScale(b);
                onClick();
            };
            b.MouseLeave += (s, e) => UiAnimations.ReleaseScale(b);
            return b;
        }

        private Border BuildPrimaryButton(string text, Action onClick)
        {
            var b = new Border
            {
                Height = 44,
                MinWidth = 130,
                CornerRadius = new CornerRadius(14),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(T.PrimaryButtonBackground, 0),
                        new GradientStop(Color.FromArgb(210, T.PrimaryButtonBackground.R, T.PrimaryButtonBackground.G, T.PrimaryButtonBackground.B), 1)
                    }
                },
                Cursor = Cursors.Hand,
                Padding = new Thickness(28, 0, 28, 0),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(T.PrimaryButtonBackground.R, T.PrimaryButtonBackground.G, T.PrimaryButtonBackground.B),
                    BlurRadius = 20,
                    Opacity = 0.3,
                    ShadowDepth = 0
                },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(T.PrimaryButtonForeground),
                    FontSize = 12,
                    FontFamily = BodyFont,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                }
            };
            b.MouseEnter += (s, e) =>
            {
                var sa = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                if (b.Effect is DropShadowEffect dse)
                {
                    dse.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(20, 30, TimeSpan.FromMilliseconds(200)));
                    dse.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.3, 0.5, TimeSpan.FromMilliseconds(200)));
                }
            };
            b.MouseLeave += (s, e) =>
            {
                var sa = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                if (b.Effect is DropShadowEffect dse)
                {
                    dse.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(30, 20, TimeSpan.FromMilliseconds(200)));
                    dse.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.5, 0.3, TimeSpan.FromMilliseconds(200)));
                }
            };
            if (onClick != null) WirePrimaryButton(b, onClick);
            return b;
        }

        private TextBlock BuildTextLink(string text, Action onClick)
        {
            var link = new TextBlock
            {
                Text = text,
                Foreground = T.TextPrimaryBrush,
                FontSize = 12,
                FontFamily = BodyFont,
                FontWeight = FontWeights.SemiBold,
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            link.MouseEnter += (s, e) => link.Foreground = T.TextSubtleBrush;
            link.MouseLeave += (s, e) => link.Foreground = T.TextPrimaryBrush;
            link.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick(); };
            return link;
        }

        private Border BuildSecondaryButton(string text, Action onClick)
        {
            var b = new Border
            {
                Height = 38,
                MinWidth = 100,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(25, T.Accent.R, T.Accent.G, T.Accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, T.Accent.R, T.Accent.G, T.Accent.B)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Padding = new Thickness(22, 0, 22, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.FromArgb(220, T.Accent.R, T.Accent.G, T.Accent.B)),
                    FontSize = 11,
                    FontFamily = BodyFont,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += (s, e) =>
            {
                b.Background = new SolidColorBrush(Color.FromArgb(50, T.Accent.R, T.Accent.G, T.Accent.B));
                var sa = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
            };
            b.MouseLeave += (s, e) =>
            {
                b.Background = new SolidColorBrush(Color.FromArgb(25, T.Accent.R, T.Accent.G, T.Accent.B));
                var sa = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                (b.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
            };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; UiAnimations.PressScale(b); };
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                UiAnimations.ReleaseScale(b);
                onClick();
            };
            b.MouseLeave += (s, e) => UiAnimations.ReleaseScale(b);
            return b;
        }

        private static string TryBase64Decode(string input)
        {
            if (input == null) return null;
            string s = input.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return null;
            }
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return null; }
        }
    }
}
