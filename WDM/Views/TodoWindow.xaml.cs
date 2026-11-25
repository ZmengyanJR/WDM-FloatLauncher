using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WDM.Models;
using WDM.Services;
using WDM.ViewModels;

namespace WDM.Views
{
    /// <summary>
    /// 桌面待办面板。
    /// 交互：左键点击条目切换完成状态，右键删除；底部输入框回车添加。
    /// 所有数据逻辑在 TodoViewModel 中，这里只处理视图事件。
    /// </summary>
    public partial class TodoWindow : Window
    {
        private readonly AppConfig.TodoPanelEntry? _layout;

        private TodoViewModel Vm => (TodoViewModel)DataContext;

        /// <summary>是否是用户点叉号删除（删除的面板下次不再恢复）。</summary>
        public bool Deleted { get; private set; }

        public TodoWindow(TodoViewModel viewModel, AppConfig.TodoPanelEntry? layout = null)
        {
            InitializeComponent();
            DataContext = viewModel;

            _layout = layout;
            Closing += OnClosing;
        }

        /// <summary>关闭时保存面板位置与大小，下次启动原样恢复。</summary>
        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_layout == null || Deleted) return;

            _layout.X = Left;
            _layout.Y = Top;
            _layout.Width = ActualWidth;
            _layout.Height = ActualHeight;
            SettingsService.Save();
        }

        // ============================================================================
        // 窗口操作
        // ============================================================================

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Deleted = true;
            Close();
        }

        /// <summary>
        /// 右下角手柄拖动：调整面板大小。
        /// 待办面板默认高度随内容自适应（SizeToContent），一旦手动拖拽就切换为固定高度，
        /// 后续新增条目改为列表内滚动，避免把窗口越撑越高。
        /// </summary>
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (SizeToContent != SizeToContent.Manual)
            {
                var currentHeight = ActualHeight;
                SizeToContent = SizeToContent.Manual;
                Height = currentHeight;
                MaxHeight = SystemParameters.WorkArea.Height - 40;
            }

            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        }

        // ============================================================================
        // 添加待办
        // ============================================================================

        /// <summary>输入框回车：提交一条待办。</summary>
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

        /// <summary>左键点击：切换完成状态。</summary>
        private void TodoItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoItemViewModel item)
            {
                Vm.Toggle(item);
                e.Handled = true;
            }
        }

        /// <summary>右键点击：删除该条待办。</summary>
        private void TodoItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoItemViewModel item)
            {
                Vm.Remove(item);
                e.Handled = true;
            }
        }
    }
}
