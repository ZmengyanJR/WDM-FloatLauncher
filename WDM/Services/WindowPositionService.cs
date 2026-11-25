using System;
using System.IO;
using System.Text.Json;

namespace WDM.Services
{
    /// <summary>
    /// 窗口位置持久化服务。
    /// 将窗口的 Left/Top 坐标保存到用户配置目录的 JSON 文件中，
    /// 以便下次启动时 Dock 仍停留在用户上次拖动的位置。
    /// </summary>
    public static class WindowPositionService
    {
        /// <summary>配置文件路径：%LocalAppData%\WDM\window_position.json</summary>
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WDM", "window_position.json");

        /// <summary>
        /// 保存窗口位置。
        /// </summary>
        public static void Save(double left, double top)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                var data = new PositionData { Left = left, Top = top };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(data));
            }
            catch
            {
                // 持久化失败不影响主流程，静默处理
            }
        }

        /// <summary>
        /// 读取窗口位置，若不存在或读取失败返回 null。
        /// </summary>
        public static PositionData? Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return null;

                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<PositionData>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>窗口位置数据结构</summary>
        public class PositionData
        {
            public double Left { get; set; }
            public double Top { get; set; }
        }
    }
}
