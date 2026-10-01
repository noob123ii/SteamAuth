using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SyntaX
{
    public sealed class AppTheme
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Color BgA { get; set; }
        public Color BgB { get; set; }
        public Color BgC { get; set; }
        public Color TextPrimary { get; set; }
        public Color TextSubtle { get; set; }
        public Color TextDim { get; set; }
        public Color FaintBorder { get; set; }
        public Color InputBackground { get; set; }
        public Color PrimaryButtonBackground { get; set; }
        public Color PrimaryButtonForeground { get; set; }
        public Color PrimaryButtonHover { get; set; }
        public Color Accent { get; set; }
        public Color TabActiveBackground { get; set; }
        public Color TabInactiveForeground { get; set; }
        public Color ShellBorder { get; set; }

        public SolidColorBrush TextPrimaryBrush => new SolidColorBrush(TextPrimary);
        public SolidColorBrush TextSubtleBrush => new SolidColorBrush(TextSubtle);
        public SolidColorBrush TextDimBrush => new SolidColorBrush(TextDim);
        public SolidColorBrush FaintBorderBrush => new SolidColorBrush(FaintBorder);
        public SolidColorBrush AccentBrush => new SolidColorBrush(Accent);

        public LinearGradientBrush CreateBackgroundBrush()
        {
            return new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(BgA, 0),
                    new GradientStop(BgB, 0.5),
                    new GradientStop(BgC, 1)
                }
            };
        }

        private static AppTheme Make(string id, string name,
            byte a1, byte a2, byte a3, byte b1, byte b2, byte b3, byte c1, byte c2, byte c3,
            byte accentR, byte accentG, byte accentB,
            byte btnR, byte btnG, byte btnB, bool lightButtons)
        {
            var accent = Color.FromRgb(accentR, accentG, accentB);
            return new AppTheme
            {
                Id = id,
                Name = name,
                BgA = Color.FromRgb(a1, a2, a3),
                BgB = Color.FromRgb(b1, b2, b3),
                BgC = Color.FromRgb(c1, c2, c3),
                TextPrimary = Color.FromRgb(240, 238, 255),
                TextSubtle = Color.FromArgb(190, 195, 210, 235),
                TextDim = Color.FromArgb(130, 155, 170, 200),
                FaintBorder = Color.FromArgb(45, 120, 130, 160),
                InputBackground = Color.FromArgb(235, 20, 22, 40),
                PrimaryButtonBackground = lightButtons ? Colors.White : Color.FromRgb(btnR, btnG, btnB),
                PrimaryButtonForeground = lightButtons ? Color.FromRgb(20, 20, 30) : Colors.White,
                PrimaryButtonHover = lightButtons ? Color.FromRgb(225, 225, 225) : Color.FromRgb(
                    (byte)(btnR * 0.85), (byte)(btnG * 0.85), (byte)(btnB * 0.85)),
                Accent = accent,
                TabActiveBackground = Color.FromArgb(80, accentR, accentG, accentB),
                TabInactiveForeground = Color.FromArgb(140, 170, 180, 210),
                ShellBorder = Color.FromArgb(70, 120, 130, 160)
            };
        }

        public static AppTheme Classic => Make("classic", "Classic Mono",
            0, 0, 0, 24, 24, 24, 8, 8, 8, 255, 255, 255, 255, 255, 255, true);

        public static AppTheme Midnight => Make("midnight", "Midnight Blue",
            6, 8, 24, 12, 16, 40, 4, 6, 20, 96, 165, 255, 59, 130, 246, false);

        public static AppTheme Forest => Make("forest", "Sunset Ember",
            40, 18, 10, 52, 24, 14, 35, 14, 8, 255, 130, 54, 255, 145, 70, false);

        public static AppTheme Crimson => Make("crimson", "Crimson Glow",
            18, 4, 8, 38, 8, 16, 10, 3, 6, 248, 113, 113, 220, 38, 38, false);

        public static AppTheme Violet => Make("violet", "Violet Haze",
            12, 6, 22, 28, 14, 44, 8, 4, 18, 192, 132, 252, 139, 92, 246, false);

        public static AppTheme Sand => Make("sand", "Warm Sand",
            26, 20, 14, 44, 34, 24, 16, 12, 8, 251, 191, 36, 245, 158, 11, true);

        public static AppTheme Aurora => Make("aurora", "Aurora Teal",
            4, 16, 18, 8, 30, 34, 3, 12, 14, 52, 211, 193, 16, 185, 129, false);

        public static AppTheme Nebula => Make("nebula", "Nebula Pink",
            18, 6, 22, 34, 12, 40, 12, 4, 16, 244, 114, 182, 236, 72, 153, false);
    }

    internal static class AppThemeManager
    {
        private static readonly Dictionary<string, AppTheme> _themes = new Dictionary<string, AppTheme>(System.StringComparer.OrdinalIgnoreCase)
        {
            { AppTheme.Classic.Id, AppTheme.Classic },
            { AppTheme.Midnight.Id, AppTheme.Midnight },
            { AppTheme.Forest.Id, AppTheme.Forest },
            { AppTheme.Crimson.Id, AppTheme.Crimson },
            { AppTheme.Violet.Id, AppTheme.Violet },
            { AppTheme.Sand.Id, AppTheme.Sand },
            { AppTheme.Aurora.Id, AppTheme.Aurora },
            { AppTheme.Nebula.Id, AppTheme.Nebula }
        };

        public static AppTheme Current { get; private set; } = AppTheme.Forest;

        public static IReadOnlyList<AppTheme> All => new List<AppTheme>
        {
            AppTheme.Classic,
            AppTheme.Midnight,
            AppTheme.Forest,
            AppTheme.Crimson,
            AppTheme.Violet,
            AppTheme.Sand,
            AppTheme.Aurora,
            AppTheme.Nebula
        };

        public static void Apply(string themeId)
        {
            if (string.IsNullOrWhiteSpace(themeId) || !_themes.TryGetValue(themeId, out var theme))
                theme = AppTheme.Classic;
            Current = theme;
        }
    }
}
