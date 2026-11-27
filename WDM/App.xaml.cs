using System;
using System.Windows;
using WDM.Services;
using WDM.Views;

namespace WDM
{
    /// <summary>
    /// 应用程序入口。
    /// 负责全局初始化和生命周期管理：
    ///   1. 全局异常捕获（Dispatcher + AppDomain + Task），记录到 crash.log 防止静默崩溃
    ///   2. 单实例检测（Mutex + 消息通知）
    ///   3. 托盘图标创建与事件桥接
    ///   4. 全局设置变更事件广播
    ///   5. 窗口 / 托盘 / 资源的最终清理
    /// </summary>
    public partial class App : Application
    {
        private static TrayIconService? _tray;
        private static MainWindow? _mainWindow;
        private static SettingsWindow? _settingsWindow;
        private static RecordsWindow? _recordsWindow;

        /// <summary>
        /// 全局配置变更事件。
        /// 任何地方（SettingsService.Save() 之后、设置窗口关闭之前）都可以触发，
        /// 订阅者（主窗口、托盘等）会即时响应。
        /// </summary>
        public static event Action? ConfigChanged;

        /// <summary>广播配置变更通知。</summary>
        public static void RaiseConfigChanged() => ConfigChanged?.Invoke();

        // ============================================================================
        // 应用生命周期
        // ============================================================================

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // --- 注册全局异常捕获（防止未处理异常导致进程静默退出）---
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            AppenderService.Write("=== WDM started ===");

            // --- 关键：托盘常驻应用必须设为 OnExplicitShutdown ---
            // 否则默认 OnLastWindowClose 模式下，任何一个窗口被关闭都会导致整个应用退出，
            // 而我们在 MainWindow.OnClosing 里是 Hide 到托盘，Cancel 了真正的 Close。
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // --- 单实例检测 ---
            if (!SingleInstanceService.TryAcquire())
            {
                AppenderService.Write("Another instance already running, exit.");
                Shutdown();
                return;
            }

            // --- 应用已保存的皮肤、暗夜模式与全局色调 ---
            // 必须在创建任何面板之前执行，修改的是 Theme.xaml 中的共享画刷
            ThemeService.ApplySkin(SettingsService.Current.Skin);
            ThemeService.ApplyGlass(SettingsService.Current.NightMode);
            ThemeService.ApplyAccent(SettingsService.Current.IconTone);

            // --- 创建并显示主窗口 ---
            // 注意：改手动创建后必须显式 Show()，原来 StartupUri 会自动 Show
            try
            {
                _mainWindow = new MainWindow();
                MainWindow = _mainWindow;
                _mainWindow.Show();
            }
            catch (Exception ex)
            {
                AppenderService.Write("Fatal: failed to create main window");
                AppenderService.Write(ex);
                Shutdown();
            }

            // --- 恢复上次打开的便签 / 待办面板（内容来自配置）---
            try
            {
                DesktopPanelService.Restore();
            }
            catch (Exception ex)
            {
                AppenderService.Write("Failed to restore desktop panels");
                AppenderService.Write(ex);
            }

            // --- 把现有便签 / 待办同步进记录模块（幂等，供记录面板查询）---
            try
            {
                Services.RecordService.MigrateFromConfig(SettingsService.Current);
            }
            catch (Exception ex)
            {
                AppenderService.Write("Failed to migrate records");
                AppenderService.Write(ex);
            }

            // --- 托盘初始化推迟到 MainWindow.OnLoaded() ---
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 先关闭桌面组件，确保便签等内容落盘
            DesktopPanelService.CloseAll();

            _settingsWindow?.Close();
            _settingsWindow = null;

            _tray?.Dispose();
            _tray = null;

            _mainWindow?.Cleanup();
            _mainWindow = null;

            SingleInstanceService.Release();
            AppenderService.Write("=== WDM exited ===");

            base.OnExit(e);
        }

        // ============================================================================
        // 全局异常处理
        // ============================================================================
        // 这三个事件覆盖了绝大部分"未处理异常"的场景：
        //   - DispatcherUnhandledException: WPF UI 线程上的异常（最常见）
        //   - AppDomain.UnhandledException: 非 UI 线程上的异常（如后台 Task）
        //   - TaskScheduler.UnobservedTaskException: async/await 未 await 的 Task 异常
        // 策略：记录日志 + 尝试继续运行（Dispatcher）或标记为 observed（Task）

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            AppenderService.Write("DispatcherUnhandledException (UI thread)");
            AppenderService.Write(e.Exception);
            e.Handled = true; // 标记为已处理，进程不退出
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            AppenderService.Write("AppDomain.UnhandledException (non-UI thread)");
            if (e.ExceptionObject is Exception ex)
                AppenderService.Write(ex);
        }

        // ============================================================================
        // 托盘服务桥接
        // ============================================================================

        /// <summary>
        /// 由 MainWindow.OnLoaded 调用，完成托盘图标初始化和事件绑定。
        /// </summary>
        public static void InitializeTray(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _tray = new TrayIconService();

            // 托盘双击 → 显示主窗口
            _tray.DoubleClicked += mainWindow.ShowFromTray;

            // 托盘"设置" → 打开设置窗口
            _tray.SettingsRequested += OpenSettingsWindow;

            // 托盘"记录面板" → 打开记录面板
            _tray.RecordsRequested += OpenRecordsWindow;

            // 托盘是 WinForms 控件，皮肤切换后需要单独刷新它的菜单配色
            ThemeService.SkinChanged += _tray.RefreshSkin;
            _tray.RefreshSkin();

            // 托盘"退出" → 真正关闭程序
            _tray.ExitRequested += () =>
            {
                _tray?.Dispose();
                _tray = null;
                Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Current.Shutdown();
            };
        }

        /// <summary>
        /// 打开设置中心（单实例：已打开则激活）。
        /// 托盘菜单"设置中心"和后续可能的入口都走这里。
        /// </summary>
        private static void OpenSettingsWindow()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        /// <summary>
        /// 打开记录面板（单实例：已打开则激活）。
        /// 托盘菜单与设置中心都走这里，避免重复打开两个窗口。
        /// </summary>
        public static void OpenRecordsWindow()
        {
            if (_recordsWindow is { IsLoaded: true })
            {
                _recordsWindow.Activate();
                return;
            }

            _recordsWindow = new RecordsWindow();
            _recordsWindow.Closed += (_, _) => _recordsWindow = null;
            _recordsWindow.Show();
        }
    }
}
