using System;
using System.IO;
using System.Text.Json;
using WDM.Models;

namespace WDM.Services
{
    /// <summary>
    /// 应用配置持久化服务。
    /// 将 AppConfig 以 JSON 格式保存到 %LocalAppData%\WDM\config.json。
    ///
    /// 设计为单例：通过 Lazy 延迟初始化，确保只在第一次访问时读取磁盘。
    /// 读取是线程安全的（Lazy 保证），写入场景本身在 UI 线程，无需额外锁。
    /// </summary>
    public static class SettingsService
    {
        /// <summary>配置文件存储目录</summary>
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WDM");

        /// <summary>配置文件完整路径</summary>
        private static readonly string ConfigFilePath = Path.Combine(AppDataDir, "config.json");

        /// <summary>
        /// 延迟加载的配置实例。
        /// 首次访问时从磁盘读取，之后全进程共享。
        /// </summary>
        private static readonly Lazy<AppConfig> LazyConfig = new(LoadFromDisk);

        /// <summary>
        /// 获取当前配置实例（进程内单例）。
        /// 对返回对象的修改不会自动写回磁盘，必须显式调用 Save()。
        /// </summary>
        public static AppConfig Current => LazyConfig.Value;

        /// <summary>
        /// 将当前配置写回磁盘。
        /// 采用 "先写临时文件再原子替换" 的模式，避免写入过程中断导致配置损坏。
        /// </summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                var tempPath = ConfigFilePath + ".tmp";
                var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(tempPath, json);
                // 原子替换：如果目标已存在则覆盖
                if (File.Exists(ConfigFilePath))
                    File.Replace(tempPath, ConfigFilePath, null);
                else
                    File.Move(tempPath, ConfigFilePath);
            }
            catch
            {
                // 配置保存失败不应影响主流程，静默处理
                // 后续可考虑写入日志文件
            }
        }

        /// <summary>
        /// 从磁盘加载配置。若文件不存在或损坏，返回默认配置。
        /// </summary>
        private static AppConfig LoadFromDisk()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return CreateDefault();

                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config == null) return CreateDefault();

                MigrateLegacyNotes(config);
                return config;
            }
            catch
            {
                // 配置文件损坏，降级为默认配置
                return CreateDefault();
            }
        }

        /// <summary>
        /// 数据升级：把旧版单便签字段 NoteText 迁移到 Notes 列表。
        /// 只在 Notes 为空且 NoteText 有内容时执行一次。
        /// </summary>
        private static void MigrateLegacyNotes(AppConfig config)
        {
            config.Notes ??= new();
            if (config.Notes.Count > 0) return;

            if (!string.IsNullOrWhiteSpace(config.NoteText))
            {
                config.Notes.Add(new AppConfig.NoteEntry { Text = config.NoteText });
                config.NoteText = string.Empty;
            }
        }

        /// <summary>
        /// 创建默认配置。
        /// 内置系统自带应用作为开箱即用的示例，用户后续可拖拽替换。
        /// </summary>
        private static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                Apps = new()
                {
                    new AppConfig.AppEntry { Name = "记事本", Path = "notepad.exe" },
                    new AppConfig.AppEntry { Name = "计算器", Path = "calc.exe" },
                    new AppConfig.AppEntry { Name = "画图",   Path = "mspaint.exe" },
                },
                Opacity = 0.85,
                AutoStart = false
            };
        }
    }
}
