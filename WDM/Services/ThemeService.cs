using System.Windows;
using System.Windows.Media;

namespace WDM.Services
{
    /// <summary>
    /// 日间 / 夜间主题切换服务。
    ///
    /// 实现方式：
    ///   Theme.xaml 中"毛玻璃"相关画刷在 XAML 加载时会被 WPF 自动冻结（IsFrozen = true），
    ///   无法在运行时直接改颜色。因此这里改为"替换资源实例"：
    ///   把新的画刷写回 Application.Resources 的同名键，覆盖合并字典里的旧实例。
    ///   所有界面统一用 {DynamicResource} 引用这批画刷，
    ///   资源替换后已打开的便签 / 待办窗口会自动重绘，无需重建界面。
    ///
    /// 只影响便签 / 待办面板；设置中心本身始终是深色调音台风格。
    /// 新增受主题影响的颜色时，在 ApplyDay / ApplyNight 中各加一行即可。
    /// </summary>
    public static class ThemeService
    {
        /// <summary>当前是否为夜间模式。</summary>
        public static bool IsNight { get; private set; }

        /// <summary>应用主题。</summary>
        public static void Apply(bool night)
        {
            IsNight = night;
            if (night) ApplyNight();
            else ApplyDay();
        }

        /// <summary>按设置中心的色相更新面板边框与发光资源，已打开的窗口会自动跟随。</summary>
        public static void ApplyAccent(double hue)
        {
            if (Application.Current == null) return;

            var color = IconExtractorService.HueColor(hue);
            Application.Current.Resources["PanelAccentBrush"] = new SolidColorBrush(color);
            Application.Current.Resources["PanelAccentGlow"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = color,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.6
            };
            Application.Current.Resources["PanelAccentGlowSoft"] = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = color,
                BlurRadius = 6,
                ShadowDepth = 0,
                Opacity = 0.9
            };
        }

        // ========================================================================
        // 日间：浅色毛玻璃（保持原有视觉）
        // ========================================================================
        private static void ApplyDay()
        {
            SetGradient("GlassBrush",
                Color.FromArgb(0xB8, 0xF5, 0xF8, 0xFB),
                Color.FromArgb(0x98, 0xE8, 0xEE, 0xF2));

            SetSolid("GlassStrokeBrush", Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            SetSolid("GlassTextBrush", Color.FromRgb(0x2B, 0x32, 0x3A));
            SetSolid("GlassTextMutedBrush", Color.FromRgb(0x8A, 0x95, 0xA0));
            SetSolid("GlassHoverBrush", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            SetSolid("GlassDividerBrush", Color.FromArgb(0x33, 0x80, 0x8B, 0x95));
        }

        // ========================================================================
        // 夜间：黑色半透明毛玻璃 + 浅色文字
        // ========================================================================
        private static void ApplyNight()
        {
            SetGradient("GlassBrush",
                Color.FromArgb(0xC0, 0x0D, 0x0F, 0x12),
                Color.FromArgb(0xA0, 0x08, 0x0A, 0x0C));

            SetSolid("GlassStrokeBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            SetSolid("GlassTextBrush", Color.FromRgb(0xE8, 0xF2, 0xF7));
            SetSolid("GlassTextMutedBrush", Color.FromRgb(0x8B, 0x98, 0xA3));
            SetSolid("GlassHoverBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            SetSolid("GlassDividerBrush", Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        }

        // ========================================================================
        // 画刷替换工具（写入 App 级资源，覆盖 Theme.xaml 中的同名键）
        // ========================================================================

        /// <summary>用新的 SolidColorBrush 替换同名资源。</summary>
        private static void SetSolid(string key, Color color)
        {
            if (Application.Current == null) return;
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        /// <summary>用新的双色对角线渐变画刷替换同名资源。</summary>
        private static void SetGradient(string key, Color start, Color end)
        {
            if (Application.Current == null) return;
            Application.Current.Resources[key] = new LinearGradientBrush(
                start, end, new Point(0, 0), new Point(1, 1));
        }
    }
}
