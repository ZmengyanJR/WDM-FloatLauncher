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

        /// <summary>
        /// 托盘图标双击时触发，外部（MainWindow）订阅以显示主窗口。
        /// </summary>
        public event Action? DoubleClicked;

        /// <summary>
        /// 用户从托盘菜单选择"设置中心"时触发。
        /// </summary>
        public event Action? SettingsRequested;

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
            _notifyIcon = new NotifyIcon
            {
                Icon = icon ?? GetDefaultIcon(),
                Text = "WDM - 桌面悬浮管理器",  // 鼠标悬停提示
                Visible = true,
                ContextMenuStrip = BuildContextMenu()
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
            var menu = new ContextMenuStrip();

            // 设置中心：合并了原"显示主窗口"和"设置"，点击后打开设置窗口
            var settingsItem = new ToolStripMenuItem("设置中心");
            settingsItem.Click += (s, e) => SettingsRequested?.Invoke();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            // 退出
            var exitItem = new ToolStripMenuItem("退出 WDM");
            exitItem.Click += (s, e) => ExitRequested?.Invoke();
            menu.Items.Add(exitItem);

            return menu;
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
