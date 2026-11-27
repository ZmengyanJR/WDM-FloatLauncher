using System;
using System.Drawing;
using System.Windows.Forms;

namespace WDM.Services
{
    /// <summary>
    /// 系统托盘图标服务。
    /// 封装 Windows Forms 的 NotifyIcon，提供：
    ///   - 托盘图标显示/隐藏
    ///   - 右键菜单（设置中心 / 退出）
    ///   - 双击托盘图标快速显示主窗口
    ///
    /// 设计为单例 + 延迟初始化：通过 Create() 显式创建，Dispose() 释放。
    /// 不依赖 IoC 容器，保持零复杂度。
    ///
    /// 注意：使用 Windows Forms 的 NotifyIcon 需要在 .csproj 中添加
    ///   <UseWindowsForms>true</UseWindowsForms>
    /// WPF 和 WinForms 可以在同一进程中共存，无需额外 NuGet 包。
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;

        /// <summary>
        /// 托盘图标双击时触发，外部（MainWindow）订阅以显示主窗口。
        /// </summary>
        public event Action? DoubleClicked;

        /// <summary>
        /// 用户从托盘菜单选择"设置中心"时触发。
        /// </summary>
        public event Action? SettingsRequested;

        /// <summary>
        /// 用户从托盘菜单选择"记录面板"时触发。
        /// </summary>
        public event Action? RecordsRequested;

        /// <summary>
        /// 用户从托盘菜单选择"退出"时触发。
        /// </summary>
        public event Action? ExitRequested;

        /// <summary>
        /// 创建并初始化托盘图标。
        /// 需要在 UI 线程上调用。
        /// </summary>
        /// <param name="icon">托盘使用的图标；传 null 则使用应用默认图标</param>
        public TrayIconService(Icon? icon = null)
        {
            _menu = BuildContextMenu();
            RefreshSkin();

            _notifyIcon = new NotifyIcon
            {
                Icon = icon ?? GetDefaultIcon(),
                Text = "WDM - 桌面悬浮管理器",  // 鼠标悬停提示
                Visible = true,
                ContextMenuStrip = _menu
            };

            _notifyIcon.DoubleClick += OnDoubleClick;
            _notifyIcon.MouseUp += OnMouseUp;
        }

        /// <summary>
        /// 构建右键菜单。
        /// 之所以用 ContextMenuStrip（.NET 2.0+）而不是旧的 ContextMenu，
        /// 是因为它支持 ShowItemToolTips，且在高 DPI 下渲染更好。
        /// </summary>
        private ContextMenuStrip BuildContextMenu()
        {
            // 托盘菜单是 WinForms 控件，不受 WPF Theme.xaml 影响，
            // 这里用自定义 Renderer 套用同一套深色调音台配色。
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                Padding = new Padding(4)
            };

            // 设置中心：合并了原"显示主窗口"和"设置"，点击后打开设置窗口
            var settingsItem = new ToolStripMenuItem("设置中心") { Padding = new Padding(10, 5, 10, 5) };
            settingsItem.Click += (s, e) => SettingsRequested?.Invoke();
            menu.Items.Add(settingsItem);

            // 记录面板：按时间查看便签 / 待办记录
            var recordsItem = new ToolStripMenuItem("记录面板") { Padding = new Padding(10, 5, 10, 5) };
            recordsItem.Click += (s, e) => RecordsRequested?.Invoke();
            menu.Items.Add(recordsItem);

            menu.Items.Add(new ToolStripSeparator());

            // 退出
            var exitItem = new ToolStripMenuItem("退出 WDM") { Padding = new Padding(10, 5, 10, 5) };
            exitItem.Click += (s, e) => ExitRequested?.Invoke();
            menu.Items.Add(exitItem);

            return menu;
        }

        /// <summary>按当前皮肤刷新托盘菜单配色（WPF 的 Theme.xaml 管不到 WinForms 菜单）。</summary>
        public void RefreshSkin()
        {
            var bg = ToDrawing(ThemeService.MenuBackground);
            var text = ToDrawing(ThemeService.MenuText);
            var hover = ToDrawing(ThemeService.MenuHover);
            var hoverText = ToDrawing(ThemeService.MenuHoverText);
            var border = ToDrawing(ThemeService.MenuBorder);

            _menu.BackColor = bg;
            _menu.ForeColor = text;
            _menu.Renderer = new SkinMenuRenderer(bg, text, hover, border, hoverText);
        }

        private static Color ToDrawing(System.Windows.Media.Color c) =>
            Color.FromArgb(c.A, c.R, c.G, c.B);

        /// <summary>托盘菜单配色表（颜色来自当前皮肤）。</summary>
        private sealed class SkinMenuColorTable : ProfessionalColorTable
        {
            private readonly Color _bg, _hover, _border;

            public SkinMenuColorTable(Color bg, Color hover, Color border)
            {
                _bg = bg;
                _hover = hover;
                _border = border;
            }

            public override Color ToolStripDropDownBackground => _bg;
            public override Color ImageMarginGradientBegin => _bg;
            public override Color ImageMarginGradientMiddle => _bg;
            public override Color ImageMarginGradientEnd => _bg;
            public override Color MenuBorder => _border;
            public override Color MenuItemBorder => _hover;
            public override Color MenuItemSelected => _hover;
            public override Color MenuItemSelectedGradientBegin => _hover;
            public override Color MenuItemSelectedGradientEnd => _hover;
            public override Color SeparatorDark => _border;
            public override Color SeparatorLight => _bg;
        }

        /// <summary>菜单渲染器：背景 / 悬停 / 文字 / 边框 / 分隔线统一跟随皮肤。</summary>
        private sealed class SkinMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly Color _bg, _text, _border, _hoverText;

            public SkinMenuRenderer(Color bg, Color text, Color hover, Color border, Color hoverText)
                : base(new SkinMenuColorTable(bg, hover, border))
            {
                _bg = bg;
                _text = text;
                _border = border;
                _hoverText = hoverText;
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item?.Enabled == false)
                    e.TextColor = Color.FromArgb(0x88, 0x99, 0xA3);
                else if (e.Item?.Selected == true)
                    e.TextColor = _hoverText;   // 亮蓝选中态上用深色文字
                else
                    e.TextColor = _text;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using var brush = new SolidBrush(_bg);
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var pen = new Pen(_border);
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        /// <summary>
        /// 从当前 WPF 应用提取默认图标（即 .exe 的图标）。
        /// 避免硬编码图标文件，让托盘图标与任务栏图标保持一致。
        /// </summary>
        private static Icon GetDefaultIcon()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
            }
            catch
            {
                // 忽略图标提取失败，使用兜底图标
            }
            return SystemIcons.Application;
        }

        private void OnDoubleClick(object? sender, EventArgs e)
        {
            DoubleClicked?.Invoke();
        }

        /// <summary>
        /// 拦截右键释放事件：让 ContextMenuStrip 在正确位置弹出。
        /// NotifyIcon 默认右键会自动弹出 ContextMenuStrip，
        /// 这里主要是为了未来扩展（比如根据状态动态改菜单项）。
        /// </summary>
        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // ContextMenuStrip 会自动处理弹出，这里留作 hook 点
            }
        }

        /// <summary>
        /// 显示托盘气泡通知（可选功能，暂不对外暴露参数）。
        /// </summary>
        public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
