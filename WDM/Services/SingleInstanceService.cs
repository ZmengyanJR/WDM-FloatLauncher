using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WDM.Services
{
    /// <summary>
    /// 单实例检测服务。
    /// 通过操作系统级别的命名互斥体（Named Mutex）确保同一时刻只有一个 WDM 实例在运行。
    ///
    /// 工作流程：
    ///   1. App 启动时调用 TryAcquire()，尝试创建一个全局唯一名称的 Mutex
    ///   2. 如果创建成功 → 说明是第一个实例，持有 Mutex 直到程序退出
    ///   3. 如果创建失败（WaitOne 返回 false）→ 说明已有实例在运行，调用 NotifyExisting()
    ///      通过 Win32 消息机制通知已有实例将窗口显示出来，然后当前进程直接退出
    ///
    /// 为什么用 Mutex 而不是 Process.GetProcessesByName：
    ///   - Mutex 由操作系统内核管理，原子操作，不存在竞态条件
    ///   - 进程名相同但路径不同时也能准确识别
    ///   - 性能远高于遍历进程列表
    /// </summary>
    public static class SingleInstanceService
    {
        /// <summary>
        /// Mutex 名称前缀使用 "Local\" 限定在当前会话内，
        /// 避免多终端服务会话冲突；后缀是应用唯一标识。
        /// </summary>
        private const string MutexName = "Local\\WDM_FloatLauncher_SingleInstance";

        /// <summary>
        /// 用于通知已有实例的自定义 Windows 消息 ID。
        /// 已有实例注册这个消息，收到后会将主窗口恢复并置顶。
        /// </summary>
        public static readonly int WM_SHOW_ME = RegisterWindowMessage("WDM_SHOW_EXISTING_INSTANCE");

        private static Mutex? _mutex;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>HWND_BROADCAST：向所有顶层窗口广播消息。</summary>
        private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

        /// <summary>
        /// 尝试获取单实例锁。
        /// </summary>
        /// <returns>true 表示当前实例是第一个，可以继续启动；false 表示已存在实例，应退出</returns>
        public static bool TryAcquire()
        {
            try
            {
                // 创建 Mutex；第二个参数 initialOwner=false，所以需要显式 WaitOne
                _mutex = new Mutex(false, MutexName, out var createdNew);
                if (createdNew)
                {
                    // 成功创建 → 我们是第一个实例
                    // 注意：Mutex 在 GC 回收或进程退出时会自动释放，
                    // 这里不需要显式持有，只要不 Dispose 它就会一直存活
                    return true;
                }

                // Mutex 已存在 → 已有实例在运行
                _mutex.Dispose();
                _mutex = null;
                NotifyExisting();
                return false;
            }
            catch (AbandonedMutexException)
            {
                // 极端情况：上一个实例崩溃，Mutex 被遗弃
                // 此时 createdNew 为 true，可以继续运行
                return true;
            }
        }

        /// <summary>
        /// 通知已存在的实例把自己显示出来。
        /// 实现方式：向所有顶层窗口广播自定义消息 WM_SHOW_ME。
        /// 已有实例的主窗口在 HwndSourceHook 里监听该消息，收到后 Show() + Activate()。
        ///
        /// 为什么用广播而不是 FindWindow 按标题查找：
        ///   窗口标题随时可能改动（例如加版本号/改名），按标题查找极易失效；
        ///   广播消息只依赖 RegisterWindowMessage 注册的消息 ID，稳定性更高。
        /// </summary>
        private static void NotifyExisting()
        {
            try
            {
                PostMessage(HWND_BROADCAST, WM_SHOW_ME, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // 通知失败不影响退出流程
            }
        }

        /// <summary>
        /// 释放 Mutex（正常退出时可选调用，不调用也会被 OS 回收）。
        /// </summary>
        public static void Release()
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
