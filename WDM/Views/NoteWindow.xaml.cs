using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

        private void TopmostMenuItem_Click(object sender, RoutedEventArgs e) => Topmost = true;

        private void BottomMenuItem_Click(object sender, RoutedEventArgs e) => DesktopPanelService.SetBottom(this);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 叉号 = 删除这张便签；应用退出时的关闭仍会走 OnClosing 保存内容
            _deleted = true;
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
            SettingsService.Save();
        }

        /// <summary>立即把文本写回配置并保存。</summary>
        private void SaveNow()
        {
            _saveTimer.Stop();
            _entry.Text = NoteBox.Text;
            SettingsService.Save();
        }
    }
}
