using System.Collections.Generic;

namespace WDM.Models
{
    /// <summary>
    /// 应用配置模型。
    /// 包含 Dock 所需的全部持久化数据：
    ///   1. 用户添加的应用列表
    ///   2. 全局外观设置（透明度等）
    ///   3. 行为开关（开机自启）
    ///
    /// 设计原则：纯 POCO（Plain Old CLR Object），不依赖任何 WPF 类型，
    /// 便于 JSON 序列化/反序列化，也便于后续替换存储介质。
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// Dock 上显示的应用列表（仅保存路径和名称，图标运行时提取）。
        /// </summary>
        public List<AppEntry> Apps { get; set; } = new();

        /// <summary>
        /// 窗口透明度，范围 [0.2, 1.0]。
        /// 过低会导致无法点击/操作，因此下限设为 0.2。
        /// </summary>
        public double Opacity { get; set; } = 0.85;

        /// <summary>
        /// 是否在系统启动时自动运行本程序。
        /// </summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>
        /// 便签内容（旧版单便签字段，仅用于升级迁移）。
        /// 新版本使用 Notes 列表，加载时会把这里的文本迁移过去。
        /// </summary>
        public string NoteText { get; set; } = string.Empty;

        /// <summary>
        /// 桌面便签列表：每个便签对应一个独立面板。
        /// </summary>
        public List<NoteEntry> Notes { get; set; } = new();

        /// <summary>
        /// 是否启用夜间模式（便签 / 待办切换为黑色毛玻璃）。
        /// </summary>
        public bool NightMode { get; set; } = false;

        /// <summary>便签面板透明度（默认 1，保持原有观感）。</summary>
        public double NoteOpacity { get; set; } = 1.0;

        /// <summary>待办面板透明度（默认 1，保持原有观感）。</summary>
        public double TodoOpacity { get; set; } = 1.0;

        /// <summary>图标模式：false=默认彩色；true=科技霓虹。</summary>
        public bool TechIconMode { get; set; } = false;

        /// <summary>
        /// 科技图标色调（色相，0~360°）。默认 192° 接近原来的冰蓝色。
        /// </summary>
        public double IconTone { get; set; } = 192;

        /// <summary>
        /// 表示一张便签（文本内容 + 屏幕位置与大小）。
        /// Width / Height 为 0 表示还没有布局信息，打开时使用默认位置。
        /// </summary>
        public class NoteEntry
        {
            /// <summary>便签文本</summary>
            public string Text { get; set; } = string.Empty;

            /// <summary>窗口左边距</summary>
            public double X { get; set; }

            /// <summary>窗口上边距</summary>
            public double Y { get; set; }

            /// <summary>窗口宽度</summary>
            public double Width { get; set; }

            /// <summary>窗口高度</summary>
            public double Height { get; set; }
        }

        /// <summary>
        /// 待办面板的窗口布局与内容（每个面板各自独立）。
        /// </summary>
        public class TodoPanelEntry
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }

            /// <summary>该面板自己的待办事项</summary>
            public List<TodoEntry> Items { get; set; } = new();
        }

        /// <summary>
        /// 已打开的待办面板布局列表（启动时按此恢复）。
        /// </summary>
        public List<TodoPanelEntry> TodoPanels { get; set; } = new();

        /// <summary>
        /// 待办事项列表（桌面待办面板的数据）。
        /// </summary>
        public List<TodoEntry> Todos { get; set; } = new();

        /// <summary>
        /// 表示一条待办事项（仅文本和完成状态）。
        /// </summary>
        public class TodoEntry
        {
            /// <summary>待办内容</summary>
            public string Text { get; set; } = string.Empty;

            /// <summary>是否已完成</summary>
            public bool IsDone { get; set; }
        }

        /// <summary>
        /// 表示一条应用配置记录（仅路径和显示名）。
        /// 与运行时的 AppItem 区分开：AppItem 是 ViewModel 层带图标和通知的对象，
        /// 而 AppEntry 是存储层的轻量数据。
        /// </summary>
        public class AppEntry
        {
            /// <summary>可执行文件或快捷方式的完整路径</summary>
            public string Path { get; set; } = string.Empty;

            /// <summary>显示名称（用户可自定义）</summary>
            public string Name { get; set; } = string.Empty;
        }
    }
}
