using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SyntaX
{
    public partial class MainWindow
    {
        internal const string AppProfileKey = "LOCAL";
        private const int AccountsPerPage = 20;

        private static readonly FontFamily TabDisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily TabBodyFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        private static readonly FontFamily TabMonoFont = new FontFamily("Cascadia Code, Consolas");

        private enum MainTab { Accounts, Settings }

        private MainTab _activeTab = MainTab.Accounts;
        private int _accountsPage = 1;
        private Grid _mainShellGrid;
        private Grid _tabContentHost;
        private Border _tabAccountsBtn;
        private Border _tabSettingsBtn;
        private Border _tabUnderline;

        private void ShowMainApp()
        {
            _mainShellGrid = new Grid { Margin = new Thickness(28, 22, 28, 18) };
            _mainShellGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _mainShellGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _mainShellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var top = BuildTopBar(AppName, FormatAccountPoolCount());
            Grid.SetRow(top, 0);
            _mainShellGrid.Children.Add(top);

            var tabBar = BuildTabBar();
            Grid.SetRow(tabBar, 1);
            _mainShellGrid.Children.Add(tabBar);

            _tabContentHost = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(_tabContentHost, 2);
            _mainShellGrid.Children.Add(_tabContentHost);

            AccountSwitchService.LoadForLicenseKey(AppProfileKey);
            _activeTab = MainTab.Accounts;
            RenderActiveTab();
            UiAnimations.Enter(_mainShellGrid, 400, 20);
            _contentHost.Children.Clear();
            _contentHost.Children.Add(_mainShellGrid);
        }

        private Border BuildTabBar()
        {
            var bar = new Grid { Margin = new Thickness(0, 14, 0, 0), Height = 42 };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition());

            _tabAccountsBtn = BuildTabButton("ACCOUNTS", MainTab.Accounts);
            _tabSettingsBtn = BuildTabButton("SETTINGS", MainTab.Settings);

            Grid.SetColumn(_tabAccountsBtn, 0);
            Grid.SetColumn(_tabSettingsBtn, 1);
            bar.Children.Add(_tabAccountsBtn);
            bar.Children.Add(_tabSettingsBtn);

            // Animated underline indicator
            _tabUnderline = new Border
            {
                Height = 2.5,
                Width = 80,
                CornerRadius = new CornerRadius(1.5),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, T.Accent.R, T.Accent.G, T.Accent.B), 0),
                        new GradientStop(Color.FromRgb(T.Accent.R, T.Accent.G, T.Accent.B), 0.5),
                        new GradientStop(Color.FromArgb(0, T.Accent.R, T.Accent.G, T.Accent.B), 1)
                    }
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
                IsHitTestVisible = false
            };
            bar.Children.Add(_tabUnderline);
            Grid.SetColumnSpan(_tabUnderline, 2);

            UpdateTabBarStyles();
            return new Border { Child = bar };
        }

        private Border BuildTabButton(string label, MainTab tab)
        {
            var btn = new Border
            {
                Height = 38,
                Margin = new Thickness(4, 0, 4, 0),
                CornerRadius = new CornerRadius(10),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    FontFamily = TabBodyFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(T.TabInactiveForeground)
                }
            };

            btn.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (_activeTab == tab) return;
                UiAnimations.TapPulse(btn);
                _activeTab = tab;
                UpdateTabBarStyles();
                AnimateUnderline();
                RenderActiveTab();
            };
            btn.MouseEnter += (s, e) =>
            {
                if (_activeTab != tab && btn.Child is TextBlock tb)
                    tb.Foreground = T.TextPrimaryBrush;
            };
            btn.MouseLeave += (s, e) =>
            {
                if (_activeTab != tab && btn.Child is TextBlock tb)
                    tb.Foreground = new SolidColorBrush(T.TabInactiveForeground);
            };
            return btn;
        }

        private void AnimateUnderline()
        {
            if (_tabUnderline == null) return;
            var targetBtn = _activeTab == MainTab.Accounts ? _tabAccountsBtn : _tabSettingsBtn;
            var btnCenter = targetBtn.TranslatePoint(new Point(targetBtn.ActualWidth / 2, 0), _tabUnderline.Parent as UIElement);
            var parent = _tabUnderline.Parent as FrameworkElement;
            if (parent == null) return;
            var centerInParent = targetBtn.TranslatePoint(new Point(targetBtn.ActualWidth / 2, 0), parent);
            // Animate margin to move underline
            var targetMargin = new Thickness(centerInParent.X - 40, 0, 0, 2);
            var anim = new ThicknessAnimation(_tabUnderline.Margin, targetMargin, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _tabUnderline.BeginAnimation(FrameworkElement.MarginProperty, anim);
        }

        private void UpdateTabBarStyles()
        {
            StyleTabButton(_tabAccountsBtn, MainTab.Accounts);
            StyleTabButton(_tabSettingsBtn, MainTab.Settings);
            if (_tabUnderline != null)
            {
                var targetBtn = _activeTab == MainTab.Accounts ? _tabAccountsBtn : _tabSettingsBtn;
                var parent = _tabUnderline.Parent as FrameworkElement;
                if (parent != null && targetBtn.IsLoaded)
                {
                    var center = targetBtn.TranslatePoint(new Point(targetBtn.ActualWidth / 2, 0), parent);
                    _tabUnderline.Margin = new Thickness(center.X - 40, 0, 0, 2);
                }
            }
        }

        private void StyleTabButton(Border btn, MainTab tab)
        {
            if (btn?.Child is TextBlock text)
            {
                bool active = _activeTab == tab;
                btn.Background = active
                    ? new SolidColorBrush(Color.FromArgb(25, T.Accent.R, T.Accent.G, T.Accent.B))
                    : Brushes.Transparent;
                text.Foreground = active ? T.AccentBrush : new SolidColorBrush(T.TabInactiveForeground);
            }
        }

        private void RenderActiveTab()
        {
            if (_tabContentHost == null) return;
            UIElement content;
            switch (_activeTab)
            {
                case MainTab.Accounts: content = BuildAccountsTabContent(); break;
                case MainTab.Settings: content = BuildSettingsTab(); break;
                default: return;
            }
            UiAnimations.SwapContent(_tabContentHost, content);
        }

        private UIElement BuildAccountsTabContent()
        {
            var accounts = GetValidAccounts();
            int totalPages = Math.Max(1, (int)Math.Ceiling(accounts.Count / (double)AccountsPerPage));
            if (_accountsPage > totalPages) _accountsPage = totalPages;
            if (_accountsPage < 1) _accountsPage = 1;

            int startIndex = (_accountsPage - 1) * AccountsPerPage;
            var pageAccounts = accounts.Skip(startIndex).Take(AccountsPerPage).ToList();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = new TextBlock
            {
                Text = accounts.Count == 0
                    ? "No accounts loaded"
                    : "Showing " + (startIndex + 1) + "\u2013" + Math.Min(startIndex + pageAccounts.Count, accounts.Count)
                      + " of " + accounts.Count.ToString("N0"),
                Foreground = T.TextSubtleBrush,
                FontSize = 11,
                FontFamily = TabBodyFont,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerText, 0);
            header.Children.Add(headerText);

            if (accounts.Count > 0)
            {
                var countBadge = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(25, T.Accent.R, T.Accent.G, T.Accent.B)),
                    Padding = new Thickness(10, 3, 10, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = accounts.Count + " total",
                        Foreground = T.AccentBrush,
                        FontSize = 10,
                        FontFamily = TabBodyFont,
                        FontWeight = FontWeights.Bold
                    }
                };
                Grid.SetColumn(countBadge, 1);
                header.Children.Add(countBadge);
            }

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 8, 6, 0)
            };
            var list = new StackPanel();

            if (pageAccounts.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No valid accounts available.",
                    Foreground = T.TextDimBrush,
                    FontSize = 14,
                    FontFamily = TabBodyFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 60, 0, 0)
                });
            }
            else
            {
                for (int i = 0; i < pageAccounts.Count; i++)
                {
                    var card = BuildAccountListCard(pageAccounts[i], startIndex + i + 1);
                    list.Children.Add(card);
                    UiAnimations.EnterStagger(card, i);
                }
            }

            scroll.Content = list;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var pager = BuildPaginationBar(totalPages, accounts.Count);
            Grid.SetRow(pager, 2);
            root.Children.Add(pager);

            return root;
        }

        private List<SteamAccount> GetValidAccounts()
            => _accountPool?.Where(a => a != null && a.IsValid).ToList() ?? new List<SteamAccount>();

        private Border BuildAccountListCard(SteamAccount account, int displayNumber)
        {
            bool selected = _currentAccount != null
                && !string.IsNullOrEmpty(account?.FullKey)
                && string.Equals(_currentAccount.FullKey, account.FullKey, StringComparison.Ordinal);

            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderBrush = selected
                    ? new SolidColorBrush(Color.FromArgb(120, T.Accent.R, T.Accent.G, T.Accent.B))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderThickness = new Thickness(selected ? 1.5 : 1),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 8,
                    BlurRadius = 20,
                    Opacity = 0.2
                },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };

            // Selected accent left border
            if (selected)
            {
                var accentStripe = new Border
                {
                    Width = 3,
                    CornerRadius = new CornerRadius(1.5, 0, 0, 1.5),
                    Background = new SolidColorBrush(T.Accent),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0, 4, 0, 4),
                    IsHitTestVisible = false
                };
                card.Child = accentStripe;
            }

            card.MouseEnter += (s, e) =>
            {
                var sa = new DoubleAnimation(1.015, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (card.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                (card.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(70, T.Accent.R, T.Accent.G, T.Accent.B));
            };
            card.MouseLeave += (s, e) =>
            {
                var sa = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                (card.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                (card.RenderTransform as ScaleTransform)?.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                if (!selected)
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Avatar circle
            var avatarCircle = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromArgb(30, T.Accent.R, T.Accent.G, T.Accent.B)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var initial = (account?.Username?.Length > 0 ? account.Username[0].ToString().ToUpper() : "?");
            avatarCircle.Child = new TextBlock
            {
                Text = initial,
                Foreground = T.AccentBrush,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                FontFamily = TabDisplayFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(avatarCircle, 0);
            grid.Children.Add(avatarCircle);

            // Info
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = account?.Username ?? "Unknown",
                Foreground = T.TextPrimaryBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FontFamily = TabDisplayFont
            });
            info.Children.Add(new TextBlock
            {
                Text = account?.SteamId ?? "\u2014",
                Foreground = T.TextDimBrush,
                FontSize = 10,
                FontFamily = TabMonoFont,
                Margin = new Thickness(0, 2, 0, 0),
                Opacity = 0.7
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // Selected badge or sign-in button
            if (selected)
            {
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(30, T.Accent.R, T.Accent.G, T.Accent.B)),
                    Padding = new Thickness(12, 6, 12, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "ACTIVE",
                        Foreground = T.AccentBrush,
                        FontSize = 9,
                        FontFamily = TabBodyFont,
                        FontWeight = FontWeights.Bold
                    }
                };
                Grid.SetColumn(badge, 2);
                grid.Children.Add(badge);
            }
            else
            {
                var signIn = BuildSecondaryButton("SIGN IN", () => SignInAccount(account));
                Grid.SetColumn(signIn, 2);
                grid.Children.Add(signIn);
            }

            if (selected && card.Child is Border stripe)
            {
                var innerGrid = new Grid();
                innerGrid.Children.Add(grid);
                // Rebuild: card -> border(accentStripe) + content
                var stack = new Grid();
                stack.Children.Add(stripe);
                stack.Children.Add(innerGrid);
                card.Child = stack;
            }
            else
            {
                card.Child = grid;
            }
            return card;
        }

        private UIElement BuildPaginationBar(int totalPages, int totalAccounts)
        {
            var bar = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var prev = BuildSecondaryButton("PREV", () => GoToPage(_accountsPage - 1, totalPages));
            prev.HorizontalAlignment = HorizontalAlignment.Left;
            prev.Opacity = _accountsPage <= 1 ? 0.35 : 1;
            prev.IsHitTestVisible = _accountsPage > 1;
            Grid.SetColumn(prev, 0);
            bar.Children.Add(prev);

            var label = new TextBlock
            {
                Text = totalAccounts == 0 ? "Page 0 of 0" : "Page " + _accountsPage + " of " + totalPages,
                Foreground = T.TextPrimaryBrush,
                FontSize = 11,
                FontFamily = TabBodyFont,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            bar.Children.Add(label);

            var next = BuildSecondaryButton("NEXT", () => GoToPage(_accountsPage + 1, totalPages));
            next.HorizontalAlignment = HorizontalAlignment.Right;
            next.Opacity = _accountsPage >= totalPages ? 0.35 : 1;
            next.IsHitTestVisible = _accountsPage < totalPages;
            Grid.SetColumn(next, 2);
            bar.Children.Add(next);

            return bar;
        }

        private void GoToPage(int page, int totalPages)
        {
            if (page < 1 || page > totalPages || page == _accountsPage) return;
            _accountsPage = page;
            RenderActiveTab();
        }

        private UIElement BuildSettingsTab()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 6, 0)
            };
            var panel = new StackPanel { MaxWidth = 540 };

            // Theme section header
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 16),
                Child = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(),
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Appearance",
                            Foreground = T.TextPrimaryBrush,
                            FontSize = 16,
                            FontFamily = TabDisplayFont,
                            FontWeight = FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            });

            // Theme cards grid
            var themeGrid = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 250 };
            foreach (var theme in AppThemeManager.All)
                themeGrid.Children.Add(BuildThemeCard(theme));
            panel.Children.Add(themeGrid);

            // User Info section
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 16),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Account Info",
                            Foreground = T.TextPrimaryBrush,
                            FontSize = 16,
                            FontFamily = TabDisplayFont,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 0, 0, 14)
                        }
                    }
                }
            });

            var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            infoPanel.Children.Add(BuildInfoRow("Discord User", "Dev User"));
            infoPanel.Children.Add(BuildInfoRow("Total Accounts", _accountPool.Count.ToString("N0")));
            infoPanel.Children.Add(BuildInfoRow("HWID", GetHWID()));
            infoPanel.Children.Add(BuildInfoRow("License Key", "Dev"));
            panel.Children.Add(infoPanel);

            var logoutBtn = BuildPrimaryButton("LOGOUT", Logout);
            logoutBtn.Margin = new Thickness(0, 18, 0, 0);
            logoutBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(logoutBtn);

            scroll.Content = panel;
            return scroll;
        }

        private Border BuildThemeCard(AppTheme theme)
        {
            bool selected = string.Equals(AppThemeManager.Current.Id, theme.Id, StringComparison.OrdinalIgnoreCase);
            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = selected
                    ? new SolidColorBrush(theme.Accent)
                    : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Padding = new Thickness(14),
                Margin = new Thickness(4, 0, 4, 10),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();
            var preview = new Border
            {
                Height = 56,
                CornerRadius = new CornerRadius(10),
                Background = theme.CreateBackgroundBrush(),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(preview);

            var nameText = new TextBlock
            {
                Text = theme.Name,
                Foreground = T.TextPrimaryBrush,
                FontSize = 13,
                FontFamily = TabBodyFont,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(nameText);

            if (selected)
            {
                var activeBadge = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(40, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Active",
                        Foreground = new SolidColorBrush(theme.Accent),
                        FontSize = 9,
                        FontFamily = TabBodyFont,
                        FontWeight = FontWeights.Bold
                    }
                };
                stack.Children.Add(activeBadge);
            }

            card.Child = stack;
            card.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                UiAnimations.TapPulse(card);
                ApplyTheme(theme.Id, refreshTabs: true);
                Notify("Theme: " + theme.Name, NotificationKind.Success);
            };
            return card;
        }

        private UIElement BuildInfoRow(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = T.TextSubtleBrush,
                FontSize = 12,
                FontFamily = TabBodyFont,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = T.TextPrimaryBrush,
                FontSize = 13,
                FontFamily = TabMonoFont,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);

            return row;
        }

        private string GetHWID()
        {
            try
            {
                var hwid = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.VersionString}|{Environment.ProcessorCount}";
                return hwid.Length > 45 ? hwid.Substring(0, 45) + "..." : hwid;
            }
            catch { return "Protected"; }
        }

        private void Logout()
        {
            _accountPool.Clear();
            _currentAccount = null;
            UserConfigStore.SetThemeId("forest");
            AppThemeManager.Apply("forest");
            Notify("Logged out", NotificationKind.Info);
            _contentHost.Children.Clear();
            var loginScreen = _authService.BuildLoginScreen(ValidateAndSubmitKey, ShowDiscordLogin);
            _contentHost.Children.Add(loginScreen);
            UiAnimations.Enter(loginScreen, 500);
        }

        private void ApplyTheme(string themeId, bool refreshTabs)
        {
            AppThemeManager.Apply(themeId);
            UserConfigStore.SetThemeId(themeId);
            ApplyShellTheme();
            if (refreshTabs && _mainShellGrid != null)
            {
                UpdateTabBarStyles();
                RenderActiveTab();
            }
        }

        private void ToggleSettings()
        {
            _activeTab = _activeTab == MainTab.Accounts ? MainTab.Settings : MainTab.Accounts;
            if (_tabUnderline != null) AnimateUnderline();
            UpdateTabBarStyles();
            RenderActiveTab();
        }
    }
}
