using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WDM.Models;
using WDM.Services;

namespace WDM.ViewModels
{
    public class TodoItemViewModel : ObservableObject
    {
        private string _text;
        private bool _isDone;
        private bool _isImportant;

        /// <summary>关联的记录 Id（同步 records.json）。</summary>
        public string RecordId { get; set; } = string.Empty;

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }
        public bool IsDone
        {
            get => _isDone;
            set => SetProperty(ref _isDone, value);
        }
        public bool IsImportant
        {
            get => _isImportant;
            set => SetProperty(ref _isImportant, value);
        }

        public TodoItemViewModel(string text, bool isDone, bool isImportant, string recordId = "")
        {
            _text = text;
            _isDone = isDone;
            _isImportant = isImportant;
            RecordId = recordId;
        }
    }

    public class TodoViewModel : ObservableObject
    {
        private readonly AppConfig.TodoPanelEntry _panel;
        private readonly List<AppConfig.TodoEntry> _storage;

        public ObservableCollection<TodoItemViewModel> Items { get; } = new();

        public string Title
        {
            get => _panel.Title;
            set
            {
                if (_panel.Title == value) return;
                _panel.Title = value;
                RaisePropertyChanged();
                Persist();
            }
        }

        public TodoViewModel(AppConfig.TodoPanelEntry panel)
        {
            _panel = panel;
            _storage = panel.Items;
            LoadFromConfig();
        }

        public void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var newItem = new TodoItemViewModel(text.Trim(), false, false);
            var insertAt = Items.ToList().FindIndex(i => i.IsDone);
            if (insertAt < 0) insertAt = Items.Count;
            Items.Insert(insertAt, newItem);
            Persist();
        }

        /// <summary>切换完成状态。完成后自动移到底部。</summary>
        public void Toggle(TodoItemViewModel item)
        {
            item.IsDone = !item.IsDone;
            if (item.IsDone)
            {
                Items.Remove(item);
                Items.Add(item);
            }
            else
            {
                Items.Remove(item);
                var insertAt = Items.ToList().FindIndex(i => i.IsDone);
                if (insertAt < 0) insertAt = Items.Count;
                Items.Insert(insertAt, item);
            }
            Persist();
        }

        /// <summary>切换星标。</summary>
        public void ToggleImportant(TodoItemViewModel item)
        {
            item.IsImportant = !item.IsImportant;
            Persist();
        }

        /// <summary>删除一条待办（记录同步物理删除）。</summary>
        public void Remove(TodoItemViewModel item)
        {
            if (!Items.Remove(item)) return;
            RecordService.Delete(item.RecordId);
            Persist();
        }

        /// <summary>置顶：移到未完成区域最前面。</summary>
        public void MoveToTop(TodoItemViewModel item)
        {
            if (item.IsDone) return; // 已完成的不置顶
            Items.Remove(item);
            Items.Insert(0, item);
            Persist();
        }

        private void LoadFromConfig()
        {
            foreach (var entry in _storage)
            {
                if (string.IsNullOrWhiteSpace(entry.Text)) continue;
                Items.Add(new TodoItemViewModel(entry.Text, entry.IsDone, entry.IsImportant, entry.RecordId));
            }
        }

        internal void Persist()
        {
            _storage.Clear();
            foreach (var item in Items)
            {
                // 同步到记录模块（批量写入，最后统一落盘一次）
                item.RecordId = RecordService.Upsert(
                    RecordKind.Todo, item.RecordId, _panel.Title,
                    item.Text, item.IsDone, item.IsImportant,
                    deferSave: true, groupId: _panel.Id) ?? string.Empty;

                _storage.Add(new AppConfig.TodoEntry
                {
                    RecordId = item.RecordId,
                    Text = item.Text,
                    IsDone = item.IsDone,
                    IsImportant = item.IsImportant
                });
            }
            RecordService.Flush();
            SettingsService.Save();
        }
    }
}
