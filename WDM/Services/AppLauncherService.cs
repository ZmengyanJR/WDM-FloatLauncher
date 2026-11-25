using System.Diagnostics;

namespace WDM.Services
{
    /// <summary>
    /// 应用启动服务。
    /// 通过 Process.Start 以 Shell 方式启动指定可执行文件。
    /// </summary>
    public static class AppLauncherService
    {
        /// <summary>
        /// 启动指定路径的可执行文件。
        /// </summary>
        /// <param name="executablePath">可执行文件完整路径</param>
        /// <returns>启动是否成功</returns>
        public static bool Launch(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return false;

            try
            {
                Process.Start(new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
