using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WDM.Services
{
    /// <summary>
    /// 全局皮肤服务（暗夜冰蓝 / 裸色小麦 / 浅清桃粉 / 美式拿铁）。
    ///
    /// 实现方式：
    ///   Theme.xaml 里所有语义色（面板、文字、选中态、卡片、日历…）都以
    ///   {DynamicResource} 引用；这里按皮肤整体替换这些资源实例，
    ///   已打开的窗口会自动重绘，无需重建界面。
    ///
    /// 新增一套皮肤只需：
    ///   1. 在下方声明一个 SkinPalette（照抄 Dark 的字段）；
    ///   2. 在 ApplySkin 与 CurrentPalette 里补一个分支；
    ///   3. 界面无需改动。
    ///
    /// 注意：便签 / 待办玻璃面板与悬浮图标的名称配色不随皮肤变化：
    ///   - 玻璃底色 / 文字由 ApplyGlass() 按"日间 / 暗夜"切换；
    ///   - 边框、发光、名称颜色由 ApplyAccent() 按全局色调切换。
    /// 这里不要覆盖 Glass* / PanelAccent*。
    /// </summary>
    public static class ThemeService
    {
        public const string DarkSkin = "dark";
        public const string PaperSkin = "paper";
        public const string MintSkin = "mint";
        public const string LatteSkin = "latte";

        /// <summary>当前皮肤 Id。</summary>
        public static string CurrentSkin { get; private set; } = DarkSkin;

        /// <summary>当前是否为暗夜模式（便签 / 待办使用黑色毛玻璃）。</summary>
        public static bool IsNight { get; private set; }

        /// <summary>皮肤切换完成（托盘菜单等非 WPF 界面订阅后自行刷新）。</summary>
        public static event Action? SkinChanged;

        /// <summary>一套皮肤的完整调色板。</summary>
        private sealed class SkinPalette
        {
            public string Id = DarkSkin;

            // 弹窗 / 设置中心
            public Color PanelTop, PanelBottom, PanelStroke;
            public Color TextPrimary, TextMuted, Divider, RowHover, Card;

            // 日历卡片：底色 + 日号文字（浅底卡片，各皮肤都用深色字）
            public Color CalendarSurface, CalendarText, CalendarTextMuted;

            // 选中态（导航项 / 菜单悬停 / 列表选中）
            public Color Selection, SelectionText, SelectionSoft;

            // 强调色（AccentLine = 20% 强调色，用于图标底框描边等细线）
            public Color Accent, AccentHover, AccentSoft, AccentLine;
        }

        private static readonly SkinPalette Dark = new()
        {
            Id = DarkSkin,
            PanelTop = Color.FromArgb(0xF5, 0x18, 0x1C, 0x22),
            PanelBottom = Color.FromArgb(0xF5, 0x0D, 0x0F, 0x13),
            PanelStroke = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            TextPrimary = Color.FromRgb(0xEA, 0xF7, 0xFB),
            TextMuted = Color.FromRgb(0x7F, 0x93, 0xA0),
            Divider = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            RowHover = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            Card = Color.FromArgb(0x33, 0x00, 0x00, 0x00),
            CalendarSurface = Color.FromArgb(0xF2, 0xF5, 0xF8, 0xFB),
            CalendarText = Color.FromRgb(0x1B, 0x24, 0x30),
            CalendarTextMuted = Color.FromRgb(0x6C, 0x78, 0x83),
            // 科技感亮蓝：选中项是高亮青蓝，文字用深色反白
            Selection = Color.FromArgb(0xE6, 0x2C, 0xC7, 0xF7),
            SelectionText = Color.FromRgb(0x07, 0x12, 0x18),
            SelectionSoft = Color.FromArgb(0x33, 0x2C, 0xC7, 0xF7),
            Accent = Color.FromRgb(0x2C, 0xC7, 0xF7),
            AccentHover = Color.FromRgb(0x6B, 0xDD, 0xFF),
            AccentSoft = Color.FromArgb(0x55, 0x2C, 0xC7, 0xF7),
            AccentLine = Color.FromArgb(0x33, 0x2C, 0xC7, 0xF7),
        };

        /// <summary>
        /// 裸色小麦：裸杏色系（浅杏底 #F5E8DA + 暖杏棕 #B5805A），
        /// 与美式拿铁的红棕 / 墨绿明确区分，避免两套皮肤撞色。
        /// </summary>
        private static readonly SkinPalette Paper = new()
        {
            Id = PaperSkin,
            PanelTop = Color.FromArgb(0xF8, 0xFB, 0xF6, 0xEF),
            PanelBottom = Color.FromArgb(0xF8, 0xF5, 0xE8, 0xDA),
            PanelStroke = Color.FromArgb(0x55, 0xB5, 0x80, 0x5A),
            TextPrimary = Color.FromRgb(0x4A, 0x3B, 0x2E),
            TextMuted = Color.FromRgb(0xA5, 0x8C, 0x74),
            Divider = Color.FromArgb(0x2E, 0xB5, 0x80, 0x5A),
            RowHover = Color.FromArgb(0x1A, 0xB5, 0x80, 0x5A),
            Card = Color.FromArgb(0xE6, 0xFF, 0xFB, 0xF4),
            CalendarSurface = Color.FromArgb(0xFF, 0xFB, 0xF3, 0xE8),
            CalendarText = Color.FromRgb(0x4A, 0x3B, 0x2E),
            CalendarTextMuted = Color.FromRgb(0xA5, 0x8C, 0x74),
            // 暖杏棕高亮，选中文字用米白反白
            Selection = Color.FromArgb(0xE6, 0xB5, 0x80, 0x5A),
            SelectionText = Color.FromRgb(0xFF, 0xF8, 0xEE),
            SelectionSoft = Color.FromArgb(0x33, 0xB5, 0x80, 0x5A),
            Accent = Color.FromRgb(0xB5, 0x80, 0x5A),
            AccentHover = Color.FromRgb(0xC9, 0x94, 0x70),
            AccentSoft = Color.FromArgb(0x55, 0xB5, 0x80, 0x5A),
            AccentLine = Color.FromArgb(0x33, 0xB5, 0x80, 0x5A),
        };

        /// <summary>
        /// 浅清桃粉：桃粉为主、浅青为辅的柔和浅色。
        /// 背景为"桃粉白 → 浅青"的双色渐变，强调色用低饱和玫瑰粉。
        /// </summary>
        private static readonly SkinPalette Mint = new()
        {
            Id = MintSkin,
            PanelTop = Color.FromArgb(0xF8, 0xFB, 0xF5, 0xF6),
            PanelBottom = Color.FromArgb(0xF8, 0xE9, 0xF1, 0xEF),
            PanelStroke = Color.FromArgb(0x55, 0xD9, 0x9A, 0xA6),
            TextPrimary = Color.FromRgb(0x3B, 0x2C, 0x31),
            TextMuted = Color.FromRgb(0x94, 0x80, 0x8A),
            Divider = Color.FromArgb(0x2E, 0xD9, 0x9A, 0xA6),
            RowHover = Color.FromArgb(0x1A, 0xD9, 0x9A, 0xA6),
            Card = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
            CalendarSurface = Color.FromArgb(0xFF, 0xFD, 0xF8, 0xF9),
            CalendarText = Color.FromRgb(0x3B, 0x2C, 0x31),
            CalendarTextMuted = Color.FromRgb(0x9A, 0x87, 0x90),
            Selection = Color.FromArgb(0xE6, 0xD9, 0x8E, 0x9C),
            SelectionText = Color.FromRgb(0x3A, 0x23, 0x29),
            SelectionSoft = Color.FromArgb(0x40, 0xD9, 0x8E, 0x9C),
            Accent = Color.FromRgb(0xD9, 0x8E, 0x9C),
            AccentHover = Color.FromRgb(0xE8, 0xAE, 0xB7),
            AccentSoft = Color.FromArgb(0x55, 0xD9, 0x8E, 0x9C),
            AccentLine = Color.FromArgb(0x33, 0xD9, 0x8E, 0x9C),
        };

        /// <summary>
        /// 美式拿铁：深咖啡棕 + 海报配色——
        ///   #2C3735 深炭绿（主文字）、#5E4130 深咖啡棕（强调 / 选中，去红调）、
        ///   #557B78 雾蓝绿（次级文字 / 分隔线）、#D5C09C 米色（背景渐变底色）。
        /// </summary>
        private static readonly SkinPalette Latte = new()
        {
            Id = LatteSkin,
            PanelTop = Color.FromArgb(0xF8, 0xF7, 0xF0, 0xE2),
            PanelBottom = Color.FromArgb(0xF8, 0xD5, 0xC0, 0x9C),
            PanelStroke = Color.FromArgb(0x55, 0x5E, 0x41, 0x30),
            TextPrimary = Color.FromRgb(0x2C, 0x37, 0x35),
            TextMuted = Color.FromRgb(0x55, 0x7B, 0x78),
            Divider = Color.FromArgb(0x2E, 0x55, 0x7B, 0x78),
            RowHover = Color.FromArgb(0x1A, 0x55, 0x7B, 0x78),
            Card = Color.FromArgb(0xE6, 0xFF, 0xFB, 0xF3),
            CalendarSurface = Color.FromArgb(0xFF, 0xFA, 0xF4, 0xE6),
            CalendarText = Color.FromRgb(0x2C, 0x37, 0x35),
            CalendarTextMuted = Color.FromRgb(0x55, 0x7B, 0x78),
            Selection = Color.FromArgb(0xE6, 0x5E, 0x41, 0x30),
            SelectionText = Color.FromRgb(0xF7, 0xF0, 0xE2),
            SelectionSoft = Color.FromArgb(0x33, 0x5E, 0x41, 0x30),
            Accent = Color.FromRgb(0x5E, 0x41, 0x30),
            AccentHover = Color.FromRgb(0x78, 0x56, 0x41),
            AccentSoft = Color.FromArgb(0x55, 0x5E, 0x41, 0x30),
            AccentLine = Color.FromArgb(0x33, 0x5E, 0x41, 0x30),
        };

        private static SkinPalette CurrentPalette => CurrentSkin switch
        {
            PaperSkin => Paper,
            MintSkin => Mint,
            LatteSkin => Latte,
            _ => Dark
        };

        /// <summary>托盘菜单用色（WinForms 端转成 System.Drawing.Color）。</summary>
        public static Color MenuBackground => CurrentPalette.PanelTop;
        public static Color MenuText => CurrentPalette.TextPrimary;
        public static Color MenuHover => CurrentPalette.Selection;
        public static Color MenuHoverText => CurrentPalette.SelectionText;
        public static Color MenuBorder => CurrentPalette.PanelStroke;

        // ========================================================================
        // 便签 / 待办玻璃：日间浅色毛玻璃 ↔ 暗夜黑色毛玻璃
        // ========================================================================

        /// <summary>
        /// 切换便签 / 待办面板的玻璃配色（只影响 Glass* 资源，与皮肤无关）。
        /// </summary>
        public static void ApplyGlass(bool night)
        {
            IsNight = night;

            if (night)
            {
                SetDiagonalGradient("GlassBrush",
                    Color.FromArgb(0xC0, 0x0D, 0x0F, 0x12),
                    Color.FromArgb(0xA0, 0x08, 0x0A, 0x0C));
                SetSolid("GlassStrokeBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                SetSolid("GlassTextBrush", Color.FromRgb(0xE8, 0xF2, 0xF7));
                SetSolid("GlassTextMutedBrush", Color.FromRgb(0x8B, 0x98, 0xA3));
                SetSolid("GlassHoverBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                SetSolid("GlassDividerBrush", Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                SetDiagonalGradient("GlassBrush",
                    Color.FromArgb(0xB8, 0xF5, 0xF8, 0xFB),
                    Color.FromArgb(0x98, 0xE8, 0xEE, 0xF2));
                SetSolid("GlassStrokeBrush", Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                SetSolid("GlassTextBrush", Color.FromRgb(0x2B, 0x32, 0x3A));
                SetSolid("GlassTextMutedBrush", Color.FromRgb(0x8A, 0x95, 0xA0));
                SetSolid("GlassHoverBrush", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                SetSolid("GlassDividerBrush", Color.FromArgb(0x33, 0x80, 0x8B, 0x95));
            }

            // 面板背景是"基底画刷颜色 × 用户透明度"，换底色后需要重新叠一次
            DesktopPanelService.ReapplyBackgroundOpacities();
        }

        /// <summary>
        /// 按全局色调更新便签 / 待办边框、发光与 Dock 应用名颜色。
        /// </summary>
        public static void ApplyAccent(double hue)
        {
            if (Application.Current == null) return;

            var color = IconExtractorService.HueColor(hue);
            SetSolid("PanelAccentBrush", color);
            SetGlow("PanelAccentGlow", color, 10, 0.6);
            SetGlow("PanelAccentGlowSoft", color, 6, 0.9);
        }

        /// <summary>
        /// 应用皮肤。启动时调用一次，之后由皮肤列表切换调用。
        /// </summary>
        public static void ApplySkin(string? skinId)
        {
            var p = skinId switch
            {
                PaperSkin => Paper,
                MintSkin => Mint,
                LatteSkin => Latte,
                _ => Dark
            };
            CurrentSkin = p.Id;

            SetVerticalGradient("PanelDarkBrush", p.PanelTop, p.PanelBottom);
            SetSolid("PanelStrokeBrush", p.PanelStroke);
            SetSolid("TextPrimaryBrush", p.TextPrimary);
            SetSolid("TextMutedBrush", p.TextMuted);
            SetSolid("DividerBrush", p.Divider);
            SetSolid("RowHoverBrush", p.RowHover);
            SetSolid("CardBrush", p.Card);

            SetSolid("CalendarSurfaceBrush", p.CalendarSurface);
            SetSolid("CalendarTextBrush", p.CalendarText);
            SetSolid("CalendarTextMutedBrush", p.CalendarTextMuted);

            SetSolid("SelectionBrush", p.Selection);
            SetSolid("SelectionTextBrush", p.SelectionText);
            SetSolid("SelectionSoftBrush", p.SelectionSoft);

            SetSolid("AccentBrush", p.Accent);
            SetSolid("AccentHoverBrush", p.AccentHover);
            SetSolid("AccentSoftBrush", p.AccentSoft);
            SetSolid("AccentLineBrush", p.AccentLine);

            SetGlow("AccentGlow", p.Accent, 12, 0.75);

            // 便签 / 待办玻璃面板与悬浮图标名称不跟随皮肤：
            // 玻璃配色由 ApplyGlass() 控制，边框与名称颜色由 ApplyAccent() 按全局色调控制。

            SkinChanged?.Invoke();
        }

        // ========================================================================
        // 资源替换工具（写入 App 级资源，覆盖 Theme.xaml 中的同名键）
        // ========================================================================

        private static void SetSolid(string key, Color color)
        {
            if (Application.Current == null) return;
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        private static void SetVerticalGradient(string key, Color top, Color bottom)
        {
            if (Application.Current == null) return;
            Application.Current.Resources[key] = new LinearGradientBrush(
                top, bottom, new Point(0, 0), new Point(0, 1));
        }

        private static void SetDiagonalGradient(string key, Color start, Color end)
        {
            if (Application.Current == null) return;
            Application.Current.Resources[key] = new LinearGradientBrush(
                start, end, new Point(0, 0), new Point(1, 1));
        }

        private static void SetGlow(string key, Color color, double blur, double opacity)
        {
            if (Application.Current == null) return;
            Application.Current.Resources[key] = new DropShadowEffect
            {
                Color = color,
                BlurRadius = blur,
                ShadowDepth = 0,
                Opacity = opacity
            };
        }
    }
}
