using System;
using System.Windows.Input;
using System.Windows.Threading;
using WDM.Services;

namespace WDM.ViewModels
{
    /// <summary>
    /// 设置中心的 ViewModel。
    ///
    /// 职责：
    ///   - 表盘：默认调节 Dock 透明度；点击便签 / 待办 / 图标模式行后切换目标
    ///   - 开机自启动、图标模式开关（皮肤切换在记录面板的「插件皮肤」中）
    ///   - 科技图标色调（图标的霓虹颜色）
    ///   - 桌面组件入口命令（便签 / 待办）
    ///
    /// 细节：
    ///   拖动表盘 / 滑轨时数值会高频变化。如果每次都写 JSON 会造成大量磁盘 IO，
    ///   所以采用 400ms 防抖：修改立即生效（广播 ConfigChanged），
    ///   停止拖动后才真正落盘。
    /// </summary>
    public class SettingsViewModel : ObservableObject
    {
        /// <summary>透明度下限：允许完全透明（0），此时面板文字悬浮在桌面上。</summary>
        public const double MinOpacity = 0.0;

        /// <summary>透明度上限。</summary>
        public const double MaxOpacity = 1.0;

        private readonly DispatcherTimer _saveTimer;
        private bool _autoStart;
        private bool _nightMode;
        private bool _techIconMode;
        private string _gaugeTarget = "Dock";
        private bool _showIconLabels;

        /// <summary>图标模式行下方的下一级选项区域是否展开（UI 临时状态，关闭设置即重置）。</summary>
        public bool ShowIconLabels
        {
            get => _showIconLabels;
            set => SetProperty(ref _showIconLabels, value);
        }

        /// <summary>
        /// 当前表盘调节目标：Dock（默认）/ Note / Todo / Icon。
        /// 只用于切换表盘数值与说明文字，不在功能行上显示选中态。
        /// </summary>
        public string GaugeTarget
        {
            get => _gaugeTarget;
            private set => SetProperty(ref _gaugeTarget, value);
        }

        /// <summary>表盘下方说明文字，随调节目标切换。</summary>
        public string GaugeCaption => GaugeTarget switch
        {
            "Note" => "便签透明度 · 拖动指针调节",
            "Todo" => "待办透明度 · 拖动指针调节",
            "Icon" => "图标色调 · 拖动指针调节",
            _ => "Dock 透明度 · 拖动指针调节"
        };

        /// <summary>表盘读数：透明度显示百分比，色调显示角度。</summary>
        public string GaugeText => GaugeTarget == "Icon"
            ? $"{IconTone:F0}°"
            : $"{CurrentOpacity:P0}";

        /// <summary>表盘两端刻度文字。</summary>
        public string GaugeMinLabel => GaugeTarget == "Icon" ? "0°" : "0%";

        /// <summary>表盘两端刻度文字。</summary>
        public string GaugeMaxLabel => GaugeTarget == "Icon" ? "360°" : "100%";

        /// <summary>表盘指针位置（0~1），与具体目标无关。</summary>
        public double GaugeValue => GaugeTarget switch
        {
            "Note" => ToNormalized(SettingsService.Current.NoteOpacity),
            "Todo" => ToNormalized(SettingsService.Current.TodoOpacity),
            "Icon" => Math.Clamp(SettingsService.Current.IconTone / 360.0, 0, 1),
            _ => ToNormalized(SettingsService.Current.Opacity)
        };

        /// <summary>当前目标的透明度（0.2 ~ 1.0）。</summary>
        public double CurrentOpacity
        {
            get => Math.Clamp(ReadTargetOpacity(), MinOpacity, MaxOpacity);
            set
            {
                var clamped = Math.Clamp(value, MinOpacity, MaxOpacity);
                if (Math.Abs(ReadTargetOpacity() - clamped) < 0.0001) return;

                WriteTargetOpacity(clamped);
                RaisePropertyChanged(nameof(CurrentOpacity));
                RaisePropertyChanged(nameof(GaugeText));
                RaisePropertyChanged(nameof(GaugeValue));
                App.RaiseConfigChanged(); // Dock / 已打开的面板立即响应
                RestartSaveTimer();       // 停止拖动后再落盘
            }
        }

        /// <summary>科技图标色调（色相 0~360°），修改后 Dock 图标实时重算。</summary>
        public double IconTone
        {
            get => SettingsService.Current.IconTone;
            set
            {
                var clamped = Math.Clamp(value, 0, 360);
                if (Math.Abs(SettingsService.Current.IconTone - clamped) < 0.5) return;

                SettingsService.Current.IconTone = clamped;
                ThemeService.ApplyAccent(clamped);   // 便签 / 待办边框与 Dock 名称颜色即时跟随
                RaisePropertyChanged(nameof(IconTone));

                if (GaugeTarget == "Icon")
                {
                    RaisePropertyChanged(nameof(GaugeText));
                    RaisePropertyChanged(nameof(GaugeValue));
                }

                App.RaiseConfigChanged();
                RestartSaveTimer();
            }
        }

        /// <summary>是否开机自启动（以注册表实际状态为准）。</summary>
        public bool AutoStart
        {
            get => _autoStart;
            set
            {
                if (!SetProperty(ref _autoStart, value)) return;

                SettingsService.Current.AutoStart = value;
                SettingsService.Save();
                StartupService.SetEnabled(value);
            }
        }

        /// <summary>暗夜模式：便签 / 待办切换为黑色透明毛玻璃，立即生效并保存。</summary>
        public bool NightMode
        {
            get => _nightMode;
            set
            {
                if (!SetProperty(ref _nightMode, value)) return;

                SettingsService.Current.NightMode = value;
                ThemeService.ApplyGlass(value);
                SettingsService.Save();
            }
        }

        /// <summary>图标模式：Dock 图标与倒影切换为科技霓虹风格。</summary>
        public bool TechIconMode
        {
            get => _techIconMode;
            set
            {
                if (!SetProperty(ref _techIconMode, value)) return;

                SettingsService.Current.TechIconMode = value;
                SettingsService.Save();
                App.RaiseConfigChanged();
            }
        }

        /// <summary>全局开关：在 Dock 图标下方显示多行发光应用名。</summary>
        public bool ShowAppLabels
        {
            get => SettingsService.Current.ShowAppLabels;
            set
            {
                if (SettingsService.Current.ShowAppLabels == value) return;

                SettingsService.Current.ShowAppLabels = value;
                SettingsService.Save();
                App.RaiseConfigChanged();
                RaisePropertyChanged();
            }
        }

        /// <summary>应用名颜色跟随全局色调（true），否则默认白色。</summary>
        public bool AppLabelColorFollowTone
        {
            get => SettingsService.Current.AppLabelColorFollowTone;
            set
            {
                if (SettingsService.Current.AppLabelColorFollowTone == value) return;

                SettingsService.Current.AppLabelColorFollowTone = value;
                SettingsService.Save();
                App.RaiseConfigChanged();
                RaisePropertyChanged();
            }
        }

        /// <summary>Dock 图标像素大小（28~64）。</summary>
        public int DockIconSize
        {
            get => SettingsService.Current.DockIconSize;
            set
            {
                int clamped = Math.Clamp(value, 28, 64);
                if (SettingsService.Current.DockIconSize == clamped) return;

                SettingsService.Current.DockIconSize = clamped;
                SettingsService.Save();
                App.RaiseConfigChanged();
                RaisePropertyChanged();
            }
        }

        /// <summary>应用名字体像素大小（7~16）。</summary>
        public int AppLabelFontSize
        {
            get => SettingsService.Current.AppLabelFontSize;
            set
            {
                int clamped = Math.Clamp(value, 7, 16);
                if (SettingsService.Current.AppLabelFontSize == clamped) return;

                SettingsService.Current.AppLabelFontSize = clamped;
                SettingsService.Save();
                App.RaiseConfigChanged();
                RaisePropertyChanged();
            }
        }

        /// <summary>在桌面打开便签面板。</summary>
        public ICommand AddNoteCommand { get; }

        /// <summary>在桌面打开待办面板。</summary>
        public ICommand AddTodoCommand { get; }

        public SettingsViewModel()
        {
            _autoStart = StartupService.IsEnabled;
            _nightMode = SettingsService.Current.NightMode;
            _techIconMode = SettingsService.Current.TechIconMode;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SettingsService.Save();
            };

            AddNoteCommand = new RelayCommand(_ => DesktopPanelService.OpenNote());
            AddTodoCommand = new RelayCommand(_ => DesktopPanelService.OpenTodo());
        }

        /// <summary>
        /// 点击便签 / 待办 / 图标模式行切换表盘目标；再次点击同一行返回默认 Dock。
        /// </summary>
        public void SelectGaugeTarget(string? target)
        {
            if (target != "Note" && target != "Todo" && target != "Icon") return;

            GaugeTarget = GaugeTarget == target ? "Dock" : target;
            RaisePropertyChanged(nameof(GaugeValue));
            RaisePropertyChanged(nameof(GaugeText));
            RaisePropertyChanged(nameof(GaugeMinLabel));
            RaisePropertyChanged(nameof(GaugeMaxLabel));
            RaisePropertyChanged(nameof(GaugeCaption));

            // 点击图标模式行：同时切换下一层级选项（显示应用名称 / 颜色跟随）的展开 / 收起
            if (target == "Icon")
                ShowIconLabels = GaugeTarget == "Icon";
        }

        /// <summary>表盘拖动：把 0~1 的位置映射成当前目标的值。</summary>
        public void SetGaugeFromNormalized(double t)
        {
            t = Math.Clamp(t, 0, 1);

            if (GaugeTarget == "Icon")
            {
                IconTone = t * 360;
                return;
            }

            CurrentOpacity = MinOpacity + t * (MaxOpacity - MinOpacity);
        }

        private static double ToNormalized(double opacity) =>
            (opacity - MinOpacity) / (MaxOpacity - MinOpacity);

        private double ReadTargetOpacity() => GaugeTarget switch
        {
            "Note" => SettingsService.Current.NoteOpacity,
            "Todo" => SettingsService.Current.TodoOpacity,
            _ => SettingsService.Current.Opacity
        };

        private void WriteTargetOpacity(double value)
        {
            switch (GaugeTarget)
            {
                case "Note": SettingsService.Current.NoteOpacity = value; break;
                case "Todo": SettingsService.Current.TodoOpacity = value; break;
                default: SettingsService.Current.Opacity = value; break;
            }
        }

        /// <summary>重置防抖计时器：每次修改后重新计时。</summary>
        private void RestartSaveTimer()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }
}
