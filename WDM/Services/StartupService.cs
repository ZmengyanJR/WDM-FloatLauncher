using System;
using Microsoft.Win32;
using System.Reflection;

namespace WDM.Services
{
    /// <summary>
    /// 开机自启动服务。
    /// 通过写入/删除注册表键 HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
    /// 来实现"用户登录时自动启动"。
    ///
    /// 为什么用 HKCU 而不是 HKLM：
    ///   - HKCU 不需要管理员权限，普通用户就能写
    ///   - 每个用户独立配置，互不影响
    ///
    /// 为什么不用"启动"文件夹：
    ///   - 注册表方式更隐蔽，用户不容易误删
    ///   - 文件夹方式会在开始菜单的启动文件夹中显示快捷方式
    /// </summary>
    public static class StartupService
    {
        /// <summary>注册表 Run 键路径（HKCU）</summary>
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>注册表值名称，唯一标识本程序</summary>
        private const string AppValueName = "WDM_FloatLauncher";

        /// <summary>
        /// 当前程序是否已配置为开机自启动。
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                    if (key == null) return false;
                    var value = key.GetValue(AppValueName) as string;
                    // 注册表值存在且与当前程序路径一致才算启用
                    return !string.IsNullOrEmpty(value) &&
                           string.Equals(value, GetLaunchCommand(), StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 设置开机自启动开关。
        /// </summary>
        /// <param name="enabled">true 启用，false 禁用</param>
        public static void SetEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                    key?.SetValue(AppValueName, GetLaunchCommand(), RegistryValueKind.String);
                }
                else
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                    key?.DeleteValue(AppValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // 注册表操作失败静默处理（可能是权限问题）
            }
        }

        /// <summary>
        /// 获取用于注册表的启动命令字符串。
        /// 格式："完整程序路径"（加引号防止路径中有空格）。
        /// </summary>
        private static string GetLaunchCommand()
        {
            // Assembly.Location 是 .dll 路径，但 WDM 是 .exe 启动的
            // 在 .NET 7 单文件模式下可能被混淆，所以用 Process.GetCurrentProcess().MainModule?.FileName
            // 这里用更简单可靠的方式
            var exePath = Environment.ProcessPath;
            return $"\"{exePath}\"";
        }
    }
}
