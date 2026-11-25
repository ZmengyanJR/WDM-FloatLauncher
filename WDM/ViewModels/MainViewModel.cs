using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using WDM.Models;
using WDM.Services;

namespace WDM.ViewModels
{
    /// <summary>
    /// 悬浮 Dock 的主视图模型。
    ///
    /// 职责：
    ///   1. 维护 Dock 上的应用列表（ObservableCollection<AppItem>）
    ///   2. 从 SettingsService 加载 / 持久化应用列表到 JSON 配置
    ///   3. 提供 AddApp / RemoveApp 方法供 View 层调用（拖拽添加、右键移除）
    ///   4. 暴露 LaunchCommand 供按钮点击绑定
    ///
    /// 异常策略：所有公开方法都 try/catch + 写日志，保证任何异常都不会冒泡到 UI 层
    /// 导致 DispatcherUnhandledException。
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public ObservableCollection<AppItem> Apps { get; } = new();
        public ICommand LaunchCommand { get; }

        /// <summary>全局开关：在 Dock 图标下方显示多行发光应用名。</summary>
        public bool ShowAppLabels => SettingsService.Current.ShowAppLabels;

        /// <summary>应用名颜色跟随全局色调（true），否则默认白色。</summary>
        public bool AppLabelColorFollowTone => SettingsService.Current.AppLabelColorFollowTone;

        /// <summary>Dock 图标像素大小（让 XAML 可以 DynamicResource 之外绑定）。</summary>
        public int DockIconSize => SettingsService.Current.DockIconSize;

        /// <summary>应用名字体大小。</summary>
        public int AppLabelFontSize => SettingsService.Current.AppLabelFontSize;

        /// <summary>记录当前图标模式，避免透明度等频繁配置变更触发图标重算。</summary>
        private bool _techIconMode = SettingsService.Current.TechIconMode;

        /// <summary>当前科技图标色调，用于判断是否需要重算图标。</summary>
        private double _iconTone = SettingsService.Current.IconTone;

        /// <summary>拖动色调滑轨时防抖，避免每一帧都重算全部图标。</summary>
        private readonly System.Windows.Threading.DispatcherTimer _toneRefreshTimer;

        public MainViewModel()
        {
            LaunchCommand = new RelayCommand(ExecuteLaunch);

            _toneRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(160)
            };
            _toneRefreshTimer.Tick += (_, _) =>
            {
                _toneRefreshTimer.Stop();
                RefreshTechIcons();
            };

            App.ConfigChanged += OnConfigChanged;
            try
            {
                LoadAppsFromConfig();
            }
            catch (Exception ex)
            {
                AppenderService.Write("MainViewModel ctor failed");
                AppenderService.Write(ex);
            }
        }

        // ========================================================================
        // 内部：配置加载
        // ========================================================================

        /// <summary>
        /// 从持久化配置加载应用列表。
        /// 失败不抛异常，记录日志即可。
        /// </summary>
        private void LoadAppsFromConfig()
        {
            var entries = SettingsService.Current.Apps;
            if (entries == null) return;

            foreach (var entry in entries)
            {
                try
                {
                    var app = CreateAppItem(entry.Path, entry.Name);
                    Apps.Add(app);
                }
                catch (Exception ex)
                {
                    AppenderService.Write($"LoadAppsFromConfig skip: {entry.Path}");
                    AppenderService.Write(ex);
                }
            }
        }

        /// <summary>
        /// 从单个文件路径创建 AppItem。自动提取图标、推断名称。
        /// </summary>
        private static AppItem CreateAppItem(string path, string? displayName = null)
        {
            // Resolve 返回 null 则回退用原始路径（ExtractIconEx/Process.Start 自己会在 PATH 搜索）
            var resolvedPath = ShortcutParserService.Resolve(path) ?? path;
            var name = displayName ?? ShortcutParserService.InferDisplayName(resolvedPath);

            // 图标提取也加 try/catch，避免个别损坏的 exe 导致整个按钮无法渲染
            System.Windows.Media.ImageSource? icon = null;
            try
            {
                icon = IconExtractorService.Extract(resolvedPath);
            }
            catch
            {
                // 图标提取失败，按钮仍会显示，只是没有图标
            }

            var app = new AppItem
            {
                Name = name,
                ExecutablePath = path,
                OriginalIcon = icon
            };

            // 科技模式下先转成霓虹图标；原图始终保留，切回默认时直接恢复
            if (SettingsService.Current.TechIconMode && icon != null)
            {
                app.TechIcon = IconExtractorService.ToNeonIcon(icon, SettingsService.Current.IconTone);
                app.Icon = app.TechIcon;
            }
            else
            {
                app.Icon = icon;
            }

            return app;
        }

        // ========================================================================
        // 图标模式切换（倒影是 IconBar 的 VisualBrush，会随 Icon 自动更新）
        // ========================================================================

        private void OnConfigChanged()
        {
            bool tech = SettingsService.Current.TechIconMode;

            // 应用名相关开关、图标大小、字号：无论打开还是关闭都要通知 Dock
            RaisePropertyChanged(nameof(ShowAppLabels));
            RaisePropertyChanged(nameof(AppLabelColorFollowTone));
            RaisePropertyChanged(nameof(DockIconSize));
            RaisePropertyChanged(nameof(AppLabelFontSize));
            double tone = SettingsService.Current.IconTone;

            bool modeChanged = tech != _techIconMode;
            bool toneChanged = Math.Abs(tone - _iconTone) > 0.5;

            if (!modeChanged && !toneChanged) return;
            _techIconMode = tech;
            _iconTone = tone;

            if (!tech)
            {
                // 切回默认模式：直接恢复原图，无需重算
                _toneRefreshTimer.Stop();
                foreach (var app in Apps)
                {
                    if (app.OriginalIcon != null) app.Icon = app.OriginalIcon;
                }
                return;
            }

            if (modeChanged)
                RefreshTechIcons(); // 开 / 关模式：立即渲染
            else
            {
                // 拖动色调滑轨：等停下再重算全部图标
                _toneRefreshTimer.Stop();
                _toneRefreshTimer.Start();
            }
        }

        /// <summary>按当前色调重建所有科技图标。</summary>
        private void RefreshTechIcons()
        {
            foreach (var app in Apps)
            {
                if (app.OriginalIcon == null) continue;

                app.TechIcon = IconExtractorService.ToNeonIcon(app.OriginalIcon, _iconTone);
                app.Icon = app.TechIcon;
            }
        }

        // ========================================================================
        // 公开 API：增删（供 View 层调用）
        // ========================================================================

        /// <summary>
        /// 添加一个应用到 Dock。
        /// 供 MainWindow.OnDrop 调用。
        /// </summary>
        /// <param name="filePath">用户拖拽进来的文件路径</param>
        /// <returns>true=成功；false=路径无效或已存在或出错</returns>
        public bool AddApp(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                var resolved = ShortcutParserService.Resolve(filePath);
                if (resolved == null)
                {
                    AppenderService.Write($"AddApp rejected (not executable): {filePath}");
                    return false;
                }

                // 去重：按解析后的真实路径比对
                foreach (var existing in Apps)
                {
                    var existResolved = ShortcutParserService.Resolve(existing.ExecutablePath)
                                        ?? existing.ExecutablePath;
                    if (string.Equals(existResolved, resolved, StringComparison.OrdinalIgnoreCase))
                    {
                        AppenderService.Write($"AddApp rejected (duplicate): {filePath}");
                        return false;
                    }
                }

                var app = CreateAppItem(filePath);
                Apps.Add(app);
                PersistApps();
                AppenderService.Write($"AddApp success: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                AppenderService.Write("AddApp crashed");
                AppenderService.Write(ex);
                return false;
            }
        }

        /// <summary>
        /// 从 Dock 移除一个应用。
        /// 供右键菜单"移除"调用。
        /// </summary>
        public bool RemoveApp(AppItem app)
        {
            try
            {
                if (app == null) return false;
                if (!Apps.Remove(app)) return false;

                PersistApps();
                AppenderService.Write($"RemoveApp: {app.ExecutablePath}");
                return true;
            }
            catch (Exception ex)
            {
                AppenderService.Write("RemoveApp crashed");
                AppenderService.Write(ex);
                return false;
            }
        }

        // ========================================================================
        // 内部：持久化 + 启动
        // ========================================================================

        /// <summary>
        /// 将当前 Apps 集合同步回 AppConfig 并写盘。
        /// </summary>
        private void PersistApps()
        {
            try
            {
                SettingsService.Current.Apps.Clear();
                foreach (var app in Apps)
                {
                    SettingsService.Current.Apps.Add(new AppConfig.AppEntry
                    {
                        Path = app.ExecutablePath,
                        Name = app.Name
                    });
                }
                SettingsService.Save();
            }
            catch (Exception ex)
            {
                AppenderService.Write("PersistApps failed");
                AppenderService.Write(ex);
            }
        }

        private void ExecuteLaunch(object? parameter)
        {
            try
            {
                if (parameter is AppItem app)
                    AppLauncherService.Launch(app.ExecutablePath);
            }
            catch (Exception ex)
            {
                AppenderService.Write("Launch failed");
                AppenderService.Write(ex);
            }
        }
    }
}
