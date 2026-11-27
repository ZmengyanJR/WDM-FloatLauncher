using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WDM.Models;
using WDM.Services;

namespace WDM.Views
{
    /// <summary>
    /// 桌面便签面板。
    ///
    /// 每张便签绑定一个 AppConfig.NoteEntry：
    ///   - 输入时 600ms 防抖保存，避免每次敲键写盘
    ///   - 关闭时立即保存；如果内容为空，则把这张便签从配置中移除
    /// </summary>
    public partial class NoteWindow : Window
    {
        private readonly DispatcherTimer _saveTimer;
        private readonly AppConfig.NoteEntry _entry;
        private bool _deleted;

        public NoteWindow(AppConfig.NoteEntry entry)
        {
            InitializeComponent();

            _entry = entry;
            DataContext = entry;

            // 先创建计时器：下面给 NoteBox 赋值会触发 TextChanged
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, _) => SaveNow();

            NoteBox.Text = entry.Text;
            NoteBox.CaretIndex = NoteBox.Text.Length;

            Closing += OnClosing;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox) return; // 标题编辑时不拖动
            try { DragMove(); } catch { }
        }

        /// <summary>标题变化同样走 600ms 防抖，合并保存（同时同步到记录模块）。</summary>
        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void TopmostMenuItem_Click(object sender, RoutedEventArgs e) => Topmost = true;

        private void BottomMenuItem_Click(object sender, RoutedEventArgs e) => DesktopPanelService.SetBottom(this);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 关闭便签：内容非空 → 保留到记录面板（只从桌面移除，不删记录）；
            // 内容为空 → 这条记录没有意义，直接从库中删掉。
            _deleted = true;
            _entry.Text = NoteBox.Text;
            if (!string.IsNullOrWhiteSpace(NoteBox.Text))
                SyncRecord();
            else
                RecordService.Delete(_entry.RecordId);

            SettingsService.Current.Notes.Remove(_entry);
            SettingsService.Save();
            Close();
        }

        /// <summary>右下角手柄拖动：调整面板大小（限制最小尺寸，避免拖没）。</summary>
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        }

        private void NoteBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>窗口关闭：保存内容与屏幕位置，下次启动原样恢复。</summary>
        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_deleted) return; // 已按删除处理，不再写回

            _saveTimer.Stop();
            _entry.Text = NoteBox.Text;
            _entry.X = Left;
            _entry.Y = Top;
            _entry.Width = ActualWidth;
            _entry.Height = ActualHeight;
            SyncRecord();
            SettingsService.Save();
        }

        /// <summary>立即把文本写回配置并保存。</summary>
        private void SaveNow()
        {
            _saveTimer.Stop();
            _entry.Text = NoteBox.Text;
            SyncRecord();
            SettingsService.Save();
        }

        /// <summary>把便签内容同步到记录模块（便签记录面板使用）。</summary>
        private void SyncRecord()
        {
            _entry.RecordId = RecordService.Upsert(
                RecordKind.Note, _entry.RecordId, _entry.Title, NoteBox.Text) ?? string.Empty;
        }

        private double _lastBgOpacity = 1.0;
        private Thickness _savedBorderThickness;
        private System.Windows.Media.Effects.Effect? _savedBorderEffect;
        private bool _borderDefaultsCached;

        /// <summary>
        /// 只改面板背景透明度，字体保持不透明。
        /// 当 opacity == 0 时同时隐藏边框和发光效果，完全沉入桌面。
        /// </summary>
        public void SetBackgroundOpacity(double opacity)
        {
            _lastBgOpacity = Math.Clamp(opacity, 0, 1);

            if (Application.Current?.Resources["GlassBrush"] is not LinearGradientBrush baseBrush) return;

            var newBrush = new LinearGradientBrush
            {
                StartPoint = baseBrush.StartPoint,
                EndPoint = baseBrush.EndPoint
            };
            foreach (var gs in baseBrush.GradientStops)
            {
                var c = gs.Color;
                c.A = (byte)Math.Round(_lastBgOpacity * 255);
                newBrush.GradientStops.Add(new GradientStop(c, gs.Offset));
            }
            PanelBorder.Background = newBrush;

            // opacity == 0 时隐藏边框 + 发光，沉入桌面；opacity > 0 时恢复
            if (_lastBgOpacity <= 0.001)
            {
                if (!_borderDefaultsCached)
                {
                    _savedBorderThickness = PanelBorder.BorderThickness;
                    _savedBorderEffect = PanelBorder.Effect;
                    _borderDefaultsCached = true;
                }
                PanelBorder.BorderThickness = new Thickness(0);
                PanelBorder.Effect = null;
            }
            else if (_borderDefaultsCached)
            {
                PanelBorder.BorderThickness = _savedBorderThickness;
                PanelBorder.Effect = _savedBorderEffect;
            }
        }

        /// <summary>主题切换后，用上次的 opacity 重新应用最新基底 Brush。</summary>
        public void ReapplyBackgroundOpacity() => SetBackgroundOpacity(_lastBgOpacity);
    }
}
