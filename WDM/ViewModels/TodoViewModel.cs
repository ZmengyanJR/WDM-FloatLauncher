using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using WDM.Models;
using WDM.Services;

namespace WDM.ViewModels
{
    /// <summary>
    /// 单条待办的运行时模型（带变更通知，供 UI 绑定）。
    /// 与存储层的 AppConfig.TodoEntry 分离：存储层是纯数据，这里是可绑定对象。
    /// </summary>
    public class TodoItemViewModel : ObservableObject
    {
        private bool _isDone;

        /// <summary>待办内容（创建后不可变）。</summary>
        public string Text { get; }

        /// <summary>是否已完成（true 时 UI 显示删除线）。</summary>
        public bool IsDone
        {
            get => _isDone;
            set => SetProperty(ref _isDone, value);
        }

        public TodoItemViewModel(string text, bool isDone)
        {
            Text = text;
            _isDone = isDone;
        }
    }

    /// <summary>
    /// 待办面板的 ViewModel。
    ///
    /// 职责：
    ///   - 维护待办列表
    ///   - 增 / 完成 / 删除操作
    ///   - 每次变更后同步回 AppConfig 并落盘
    /// </summary>
    public class TodoViewModel : ObservableObject
    {
        /// <summary>该面板自己的持久化存储（面板之间互相独立）。</summary>
        private readonly List<AppConfig.TodoEntry> _storage;
        private readonly AppConfig.TodoPanelEntry _panel;

        public ObservableCollection<TodoItemViewModel> Items { get; } = new();

        /// <summary>可自定义的面板标题。</summary>
        public string Title
        {
            get => _panel.Title;
            set
            {
                if (_panel.Title == value) return;
                _panel.Title = value;
                RaisePropertyChanged();
                SettingsService.Save();
            }
        }

        /// <summary>切换完成状态（左键点击）。</summary>
        public ICommand ToggleCommand { get; }

        /// <summary>删除待办（右键点击）。</summary>
        public ICommand RemoveCommand { get; }

        public TodoViewModel(AppConfig.TodoPanelEntry panel)
        {
            _panel = panel;
            _storage = panel.Items;
            LoadFromConfig();

            ToggleCommand = new RelayCommand(p =>
            {
                if (p is TodoItemViewModel item) Toggle(item);
            });
            RemoveCommand = new RelayCommand(p =>
            {
                if (p is TodoItemViewModel item) Remove(item);
            });
        }

        /// <summary>新增一条待办（忽略空白内容）。</summary>
        public void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            Items.Add(new TodoItemViewModel(text.Trim(), false));
            Persist();
        }

        /// <summary>切换完成状态。</summary>
        public void Toggle(TodoItemViewModel item)
        {
            item.IsDone = !item.IsDone;
            Persist();
        }

        /// <summary>删除一条待办。</summary>
        public void Remove(TodoItemViewModel item)
        {
            if (Items.Remove(item)) Persist();
        }

        // ========================================================================
        // 持久化
        // ========================================================================

        private void LoadFromConfig()
        {
            foreach (var entry in _storage)
            {
                if (string.IsNullOrWhiteSpace(entry.Text)) continue;
                Items.Add(new TodoItemViewModel(entry.Text, entry.IsDone));
            }
        }

        /// <summary>把当前列表写回本面板的存储并保存（列表规模小，直接落盘即可）。</summary>
        private void Persist()
        {
            _storage.Clear();
            foreach (var item in Items)
            {
                _storage.Add(new AppConfig.TodoEntry
                {
                    Text = item.Text,
                    IsDone = item.IsDone
                });
            }
            SettingsService.Save();
        }
    }
}
