using System;
using System.IO;
using System.Media;

namespace WDM.Services
{
    /// <summary>
    /// 音效服务（极简实现，无第三方依赖）。
    ///
    /// 设计要点：
    ///   1. 懒加载 + 预加载：首次播放时把 effect.wav 一次性读入内存（Load），
    ///      之后每次悬浮只做一次 Play 调用，不产生磁盘 IO。
    ///   2. 静默降级：文件缺失或播放失败只写日志，绝不影响主流程。
    ///   3. 单实例：SoundPlayer 是进程级单例，避免每个图标按钮各自持有播放器。
    ///
    /// 后续扩展：若设置中要加"音效开关/音量"，只需在本类加一个 Enabled 属性，
    /// 调用方无需改动。
    /// </summary>
    public static class SoundService
    {
        /// <summary>音效文件名（相对程序目录）。</summary>
        private const string FileName = "effect.wav";

        private static SoundPlayer? _player;
        private static bool _initialized;

        /// <summary>
        /// 播放鼠标悬浮音效。
        /// 重复调用会打断上一次播放并从头开始（SoundPlayer.Play 的默认行为）。
        /// </summary>
        public static void PlayHover()
        {
            try
            {
                EnsureLoaded();
                _player?.Play();
            }
            catch (Exception ex)
            {
                AppenderService.Write("SoundService.PlayHover failed");
                AppenderService.Write(ex);
            }
        }

        /// <summary>
        /// 首次调用时创建 SoundPlayer 并预加载音频到内存。
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_initialized) return;
            _initialized = true;

            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                AppenderService.Write($"SoundService: sound file not found: {path}");
                return;
            }

            _player = new SoundPlayer(path);
            _player.Load(); // 一次性载入内存
        }
    }
}
