using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WDM.Models;
using WDM.Services;
using WDM.ViewModels;

namespace WDM.Views
{
    /// <summary>桌面待办面板。</summary>
    public partial class TodoWindow : Window
    {
        private readonly AppConfig.TodoPanelEntry? _layout;
        private TodoViewModel Vm => (TodoViewModel)DataContext;
        public bool Deleted { get; private set; }

        public TodoWindow(TodoViewModel viewModel, AppConfig.TodoPanelEntry? layout = null)
        {
            InitializeComponent();
            DataContext = viewModel;
            _layout = layout;
            Closing += OnClosing;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_layout == null || Deleted) return;
            _layout.X = Left; _layout.Y = Top;
            _layout.Width = ActualWidth; _layout.Height = ActualHeight;
            SettingsService.Save();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            try { DragMove(); } catch { }
        }

        private void TopmostMenuItem_Click(object sender, RoutedEventArgs e) => Topmost = true;
        private void BottomMenuItem_Click(object sender, RoutedEventArgs e) => DesktopPanelService.SetBottom(this);
        private void CloseButton_Click(object sender, RoutedEventArgs e) { Deleted = true; Close(); }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (SizeToContent != SizeToContent.Manual)
            {
                var h = ActualHeight;
                SizeToContent = SizeToContent.Manual;
                Height = h;
                MaxHeight = SystemParameters.WorkArea.Height;
            }
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Vm.Add(InputBox.Text);
            InputBox.Clear();
            e.Handled = true;
        }

        // ============================================================================
        // 条目交互
        // ============================================================================

        /// <summary>左键：切换完成 / 未完成。</summary>
        private void TodoItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoItemViewModel item)
                Vm.Toggle(item);
            e.Handled = true;
        }

        /// <summary>圆点也可以左键切换完成。</summary>
        private void Dot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoItemViewModel item)
                Vm.Toggle(item);
            e.Handled = true;
        }

        /// <summary>星标：切换重要。</summary>
        private void Star_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoItemViewModel item)
                Vm.ToggleImportant(item);
            e.Handled = true;
        }

        // ============================================================================
        // 右键菜单：删除 / 修改 / 置顶
        // ============================================================================

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is TodoItemViewModel item)
                Vm.Remove(item);
        }

        private void EditItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is TodoItemViewModel item)
            {
                // 删除当前条 + 内容塞到底部输入框，用户改完回车即为修改
                Vm.Remove(item);
                InputBox.Text = item.Text;
                InputBox.Focus();
                InputBox.SelectAll();
            }
        }

        private void MoveToTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is TodoItemViewModel item)
                Vm.MoveToTop(item);
        }

        // ============================================================================
        // 主题：背景透明度 + 夜间模式
        // ============================================================================

        private double _lastBgOpacity = 1.0;
        private Thickness _savedBorderThickness;
        private System.Windows.Media.Effects.Effect? _savedBorderEffect;
        private bool _borderDefaultsCached;

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

        public void ReapplyBackgroundOpacity() => SetBackgroundOpacity(_lastBgOpacity);
    }
}
