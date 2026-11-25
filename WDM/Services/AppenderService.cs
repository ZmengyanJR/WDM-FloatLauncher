using System;
using System.IO;
using System.Threading;

namespace WDM.Services
{
    /// <summary>
    /// 简易日志服务（Debug/Release 都生效，不依赖第三方库）。
    /// 用于记录异常堆栈，方便排查用户反馈的崩溃问题。
    /// 日志文件位置：%LocalAppData%\WDM\crash.log
    ///
    /// 设计：
    ///   - 线程安全（lock）
    ///   - 单文件追加写入
    ///   - 自动限制文件大小（超过 1MB 自动清理前半部分）
    ///   - 空实现也可以，只是让日志为空，不影响主流程
    /// </summary>
    public static class AppenderService
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WDM", "crash.log");

        private static readonly object _lock = new();
        private const int MaxSize = 1024 * 1024; // 1 MB

        /// <summary>
        /// 写入一条日志（带时间戳）。
        /// </summary>
        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(LogPath)!;
                    Directory.CreateDirectory(dir);

                    // 文件过大时截断前半部分，保留最近的日志
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxSize)
                    {
                        var content = File.ReadAllText(LogPath);
                        File.WriteAllText(LogPath, content.Substring(content.Length / 2));
                    }

                    File.AppendAllText(LogPath,
                        $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // 日志服务本身不能抛异常
            }
        }

        /// <summary>
        /// 记录异常堆栈。
        /// </summary>
        public static void Write(Exception ex)
        {
            Write($"ERROR {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            if (ex.InnerException != null)
                Write($"  INNER {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
        }
    }
}
