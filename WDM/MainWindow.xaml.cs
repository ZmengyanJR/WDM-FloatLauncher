using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WDM.Models;
using WDM.Services;
using WDM.Views;

namespace WDM
{
    /// <summary>
    /// 悬浮 Dock 主窗口的代码隐藏。
    ///
    /// View 层职责：
    ///   1. 窗口拖动（鼠标左键拖动无边框窗口）
    ///   2. 位置持久化（窗口位置变化时写入 JSON）
    ///   3. 外部拖拽接收（AllowDrop + DragOver + Drop）
    ///   4. 右键菜单处理（移除 / 打开文件位置）
    ///   5. 透明度绑定（从 AppConfig 读取，响应设置变更）
    ///   6. 单实例消息监听（WM_SHOW_ME 唤醒）
    ///   7. 托盘交互（关闭按钮最小化到托盘）
    ///
    /// 异常策略：所有事件处理都 try/catch + 写日志，绝不向上冒泡。
    /// </summary>
    public partial class MainWindow : Window
    {
        private HwndSource? _hwndSource;

        public MainWindow()
        {
            InitializeComponent();

            // --- 窗口事件 ---
            Loaded += OnLoaded;
            LocationChanged += OnLocationChanged;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            Closing += OnClosing;

            // --- 拖拽事件 ---
            DragOver += OnDragOver;
            Drop += OnDrop;

            // --- 全局设置变更 ---
            App.ConfigChanged += OnConfigChanged;
        }

        // ============================================================================
        // 窗口生命周期
        // ============================================================================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 位置恢复
                var pos = WindowPositionService.Load();
                if (pos != null)
                {
                    Left = pos.Left;
                    Top = pos.Top;
                }
                else
                {
                    Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2;
                    Top = SystemParameters.WorkArea.Height - ActualHeight - 20;
                }

                UpdateOpacityFromConfig();

                // 从 exe 自身提取图标设置到 Window（csproj 的 ApplicationIcon 已嵌入）
                try
                {
                    var exePath = System.Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var winFormsIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (winFormsIcon != null)
                        {
                            // HICON → WPF ImageSource
                            using (winFormsIcon)
                            {
                                var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    winFormsIcon.Handle, Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                bs.Freeze();
                                Icon = bs;
                            }
                        }
                    }
                }
                catch { /* 图标提取失败不阻塞启动 */ }

                // 注册 Win32 消息钩子
                _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).EnsureHandle());
                _hwndSource?.AddHook(WndProc);

                App.InitializeTray(this);
                AppenderService.Write("MainWindow loaded");
            }
            catch (Exception ex)
            {
                AppenderService.Write("MainWindow.OnLoaded failed");
                AppenderService.Write(ex);
            }
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            try { WindowPositionService.Save(Left, Top); } catch { }
        }

        /// <summary>拦截关闭 → 隐藏到托盘（不退出）。</summary>
        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        /// <summary>应用退出时的资源清理。</summary>
        public void Cleanup()
        {
            _hwndSource?.RemoveHook(WndProc);
            App.ConfigChanged -= OnConfigChanged;
        }

        // ============================================================================
        // 窗口拖动
        // ============================================================================

        /// <summary>
        /// 鼠标左键按下时拖动无边框窗口。
        /// Button 上的左键会被 Button.Click 拦截不会冒泡，所以不会冲突。
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        // ============================================================================
        // 拖拽处理（桌面快捷方式 → Dock）
        // ============================================================================

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// 拖拽放下：逐个文件尝试添加到 Dock。
        /// 所有异常都被 catch，保证一次失败不影响其他文件。
        /// </summary>
        private void OnDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Length == 0) return;

                if (DataContext is ViewModels.MainViewModel vm)
                {
                    int success = 0;
                    foreach (var file in files)
                    {
                        if (vm.AddApp(file)) success++;
                    }
                    AppenderService.Write($"OnDrop processed {files.Length} files, {success} added");
                }
            }
            catch (Exception ex)
            {
                AppenderService.Write("OnDrop crashed (caught by Window)");
                AppenderService.Write(ex);
            }
        }

        // ============================================================================
        // 悬浮音效
        // ============================================================================

        /// <summary>
        /// 鼠标进入图标时播放音效（由 DockIconButtonStyle 的 EventSetter 调用）。
        /// 播放逻辑收敛在 SoundService 中，这里只负责触发。
        /// </summary>
        private void DockIcon_MouseEnter(object sender, MouseEventArgs e)
        {
            SoundService.PlayHover();
        }

        // ============================================================================
        // 右键菜单处理
        // ============================================================================
        // ContextMenu 挂在 DockIconButtonStyle 上，其 DataContext 会继承自被点击的 Button
        // （即当前的 AppItem）。所以 MenuItem.DataContext 就是 AppItem，直接用即可。

        /// <summary>右键菜单 → 移除应用。</summary>
        private void RemoveAppMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not MenuItem mi) return;
                if (mi.DataContext is not AppItem app) return;
                if (DataContext is not ViewModels.MainViewModel vm) return;

                vm.RemoveApp(app);
            }
            catch (Exception ex)
            {
                AppenderService.Write("RemoveAppMenuItem_Click failed");
                AppenderService.Write(ex);
            }
        }

        /// <summary>右键菜单 → 打开文件位置（用资源管理器定位文件）。</summary>
        private void OpenFileLocationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not MenuItem mi) return;
                if (mi.DataContext is not AppItem app) return;

                var targetPath = ShortcutParserService.Resolve(app.ExecutablePath) ?? app.ExecutablePath;
                if (!File.Exists(targetPath)) return;

                // "/select,\"path\"" 让资源管理器打开文件夹并选中该文件
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{targetPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppenderService.Write("OpenFileLocationMenuItem_Click failed");
                AppenderService.Write(ex);
            }
        }

        // ============================================================================
        // 透明度 & 设置变更
        // ============================================================================

        private void UpdateOpacityFromConfig()
        {
            try { Opacity = SettingsService.Current.Opacity; } catch { }
        }

        private void OnConfigChanged()
        {
            try
            {
                if (Dispatcher.CheckAccess())
                    UpdateOpacityFromConfig();
                else
                    Dispatcher.Invoke(UpdateOpacityFromConfig);
            }
            catch (Exception ex)
            {
                AppenderService.Write("OnConfigChanged failed");
                AppenderService.Write(ex);
            }
        }

        // ============================================================================
        // 托盘交互
        // ============================================================================

        /// <summary>托盘双击 / 菜单"显示主窗口"调用。</summary>
        public void ShowFromTray()
        {
            try
            {
                Show();
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
            }
            catch (Exception ex)
            {
                AppenderService.Write("ShowFromTray failed");
                AppenderService.Write(ex);
            }
        }

        // ============================================================================
        // Win32 消息钩子（单实例唤醒）
        // ============================================================================

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == SingleInstanceService.WM_SHOW_ME)
            {
                ShowFromTray();
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
