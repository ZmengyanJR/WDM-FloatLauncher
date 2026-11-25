using System;
using System.Collections.Generic;
using System.IO;

namespace WDM.Services
{
    /// <summary>
    /// 快捷方式路径解析服务（精简版）。
    ///
    /// 为什么"精简版"：
    ///   我们发现 Shell32.ExtractIconEx 原生支持 .lnk 文件（能直接从 .lnk 提取目标 exe 的图标），
    ///   Process.Start + UseShellExecute 也会自动解析 .lnk。
    ///   所以**完全不需要自己解析 .lnk 的目标路径**——我们只需要告诉调用方"这是一个可接受的路径"。
    ///
    /// 之前用 IShellLinkW 自己解析是多余的，还引入了 COM 对象释放导致的 AccessViolationException。
    ///
    /// 现在 Resolve 只做：扩展名白名单检查 + 存在性检查，返回原路径即可。
    /// 代码量降了 70%，也不可能再 AccessViolation。
    /// </summary>
    public static class ShortcutParserService
    {
        /// <summary>
        /// 可接受的文件扩展名（小写）。
        /// - .lnk 桌面快捷方式：ExtractIconEx 和 ShellExecute 都能直接处理
        /// - .exe/.com/.bat/.cmd/.msc 常见可执行
        /// - .jar Java 程序、.ps1 PowerShell 脚本、.url 网页快捷方式
        /// </summary>
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".lnk", ".exe", ".com", ".bat", ".cmd", ".msc", ".jar", ".ps1", ".url"
        };

        /// <summary>
        /// 判断文件路径是否可接受，并返回"原样"路径。
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>原路径（如果合法）；否则 null</returns>
        public static string? Resolve(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return null;

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (!AllowedExtensions.Contains(ext))
                {
                    AppenderService.Write($"ShortcutParser: unsupported extension '{ext}' for '{filePath}'");
                    return null;
                }

                // 对于完整路径，检查文件确实存在；
                // 对于 PATH 内的短命令（如 "notepad.exe"），File.Exists 会返回 false，
                // 但我们仍然允许它通过（Process.Start 会自己在 PATH 中搜索）
                if (Path.IsPathRooted(filePath) && !File.Exists(filePath))
                {
                    AppenderService.Write($"ShortcutParser: file not found '{filePath}'");
                    return null;
                }

                return filePath; // 直接返回原路径，不解析 .lnk 目标
            }
            catch (Exception ex)
            {
                AppenderService.Write($"ShortcutParser.Resolve crashed: {filePath}");
                AppenderService.Write(ex);
                return null;
            }
        }

        /// <summary>
        /// 推断显示名称（取不含扩展名的文件名）。
        /// </summary>
        public static string InferDisplayName(string filePath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                return string.IsNullOrWhiteSpace(fileName) ? "未知应用" : fileName;
            }
            catch
            {
                return "未知应用";
            }
        }
    }
}
