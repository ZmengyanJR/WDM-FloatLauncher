using System;
using System.Collections.Generic;
using System.Windows;
using WDM.Models;
using WDM.ViewModels;
using WDM.Views;

namespace WDM.Services
{
    /// <summary>
    /// 桌面小组件面板管理。
    ///
    /// 职责：
    ///   - 每次点击"＋"都新建一个面板（可同时存在多个便签 / 多个待办窗口）
    ///   - 统一计算初始摆放位置（工作区右上角按列排布，避免完全重叠）
    ///   - 每个待办窗口有独立的列表，互不影响
    ///   - 应用退出时统一关闭，确保便签内容落盘
    ///
    /// 不引入容器或事件总线，静态列表 + 简单方法即可满足当前需求。
    /// </summary>
    public static class DesktopPanelService
    {
        /// <summary>所有已打开的便签窗口。</summary>
        private static readonly List<NoteWindow> Notes = new();

        /// <summary>所有已打开的待办窗口。</summary>
        private static readonly List<TodoWindow> Todos = new();

        /// <summary>监听全局设置变更，让已打开的面板透明度实时跟随表盘。</summary>
        static DesktopPanelService()
        {
            App.ConfigChanged += ApplyOpacity;
        }

        /// <summary>新建一张空白便签面板。</summary>
        public static void OpenNote()
        {
            var entry = new AppConfig.NoteEntry();
            SettingsService.Current.Notes.Add(entry);
            ShowNote(entry);
        }

        /// <summary>新建一个待办面板（多个窗口共享同一份待办数据）。</summary>
        public static void OpenTodo()
        {
            var layout = new AppConfig.TodoPanelEntry();
            SettingsService.Current.TodoPanels.Add(layout);
            ShowTodo(layout);
        }

        /// <summary>启动时按配置恢复上次的便签 / 待办面板（内容、位置、大小）。</summary>
        public static void Restore()
        {
            foreach (var entry in SettingsService.Current.Notes.ToArray())
                ShowNote(entry);

            var layouts = SettingsService.Current.TodoPanels;
            MigrateLegacyTodos(layouts);

            foreach (var layout in layouts.ToArray())
                ShowTodo(layout);
        }

        /// <summary>
        /// 旧版本把所有待办存在全局 Todos 中；升级后迁移到第一个面板，
        /// 之后新建的面板都从空白开始。
        /// </summary>
        private static void MigrateLegacyTodos(List<AppConfig.TodoPanelEntry> layouts)
        {
            var legacy = SettingsService.Current.Todos;
            if (legacy.Count == 0) return;

            foreach (var layout in layouts)
                if (layout.Items.Count > 0) return;

            if (layouts.Count == 0) layouts.Add(new AppConfig.TodoPanelEntry());

            layouts[0].Items.AddRange(legacy);
            legacy.Clear();
        }

        private static void ShowNote(AppConfig.NoteEntry entry)
        {
            var window = new NoteWindow(entry);
            window.Opacity = SettingsService.Current.NoteOpacity;
            Notes.Add(window);
            window.Closed += (_, _) => Notes.Remove(window);

            Place(window);
            if (entry.Width > 0 && entry.Height > 0)
            {
                window.Width = entry.Width;
                window.Height = entry.Height;
                window.Left = entry.X;
                window.Top = entry.Y;
            }
            window.Show();
        }

        private static void ShowTodo(AppConfig.TodoPanelEntry layout)
        {
            var vm = new TodoViewModel(layout);
            var window = new TodoWindow(vm, layout);
            window.Opacity = SettingsService.Current.TodoOpacity;
            Todos.Add(window);
            window.Closed += (_, _) =>
            {
                Todos.Remove(window);
                // 用户点叉号删除的面板，不再参与下次恢复
                if (window.Deleted && SettingsService.Current.TodoPanels.Remove(layout))
                    SettingsService.Save();
            };

            Place(window);
            if (layout.Width > 0 && layout.Height > 0)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Width = layout.Width;
                window.Height = layout.Height;
                window.Left = layout.X;
                window.Top = layout.Y;
            }
            window.Show();
        }

        /// <summary>取消置顶并把面板压到普通窗口底部，不激活窗口。</summary>
        public static void SetBottom(Window window)
        {
            window.Topmost = false;
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        private static readonly IntPtr HwndBottom = new(1);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        /// <summary>把当前配置的面板透明度应用到所有已打开的窗口。</summary>
        private static void ApplyOpacity()
        {
            foreach (var window in Notes)
                window.Opacity = SettingsService.Current.NoteOpacity;
            foreach (var window in Todos)
                window.Opacity = SettingsService.Current.TodoOpacity;
        }

        /// <summary>关闭所有面板（应用退出时调用，会触发内容保存）。</summary>
        public static void CloseAll()
        {
            // 复制一份再遍历，避免 Closed 回调修改列表
            foreach (var window in Notes.ToArray()) window.Close();
            foreach (var window in Todos.ToArray()) window.Close();

            Notes.Clear();
            Todos.Clear();
        }

        /// <summary>
        /// 把面板放到工作区右上角：按打开顺序横向排开，三列后换行。
        /// </summary>
        private static void Place(Window window)
        {
            int index = Notes.Count + Todos.Count - 1;
            int column = index % 3;
            int row = index / 3;

            var area = SystemParameters.WorkArea;

            // 固定列间距 340，保证不同宽度的面板（便签 320 / 待办 300）也不会重叠
            const double columnPitch = 340;
            double x = area.Right - window.Width - 24 - column * columnPitch;
            double y = area.Top + 90 + row * 40;

            window.Left = Math.Max(area.Left + 16, x);
            window.Top = Math.Min(area.Bottom - 180, y);
        }
    }
}
