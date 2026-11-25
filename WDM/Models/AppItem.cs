using System.Windows.Media;
using WDM.ViewModels;

namespace WDM.Models
{
    /// <summary>
    /// 表示 Dock 上的一个应用项。
    /// 继承 ObservableObject 以支持图标等属性变化时通知 UI。
    /// </summary>
    public class AppItem : ObservableObject
    {
        private string _name = string.Empty;
        private string _executablePath = string.Empty;
        private ImageSource? _icon;
        private string? _tooltip;
        /// <summary>应用显示名称</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>可执行文件完整路径（用于启动和提取图标）</summary>
        public string ExecutablePath
        {
            get => _executablePath;
            set => SetProperty(ref _executablePath, value);
        }

        /// <summary>应用图标，从 exe 提取，绑定到 Image.Source</summary>
        public ImageSource? Icon
        {
            get => _icon;
            set => SetProperty(ref _icon, value);
        }

        /// <summary>原始彩色图标（切换模式时恢复用）。</summary>
        public ImageSource? OriginalIcon { get; set; }

        /// <summary>科技霓虹图标缓存：只转换一次，避免切透明度等操作重复计算。</summary>
        public ImageSource? TechIcon { get; set; }

        /// <summary>鼠标悬停提示文本，默认为应用名称</summary>
        public string? Tooltip
        {
            get => _tooltip ?? _name;
            set => SetProperty(ref _tooltip, value);
        }
    }
}
