using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WDM.Services;

namespace WDM.Services
{
    /// <summary>
    /// 图标提取服务。
    ///
    /// 策略：三级回退，保证几乎不会返回 null：
    ///   1. Shell32.SHGetFileInfo   —— Shell 原生解析，支持 .lnk/.exe/.url/.msc 等所有文件类型
    ///   2. Shell32.ExtractIconEx   —— 针对 .exe 的传统 API（作为 SHGetFileInfo 失败时的回退）
    ///   3. SystemIcons.Application —— 系统默认应用图标兜底，保证永远有图标
    ///
    /// 历史：
    ///   只用 ExtractIconEx 时，某些 .lnk 文件会返回 0 导致空图标。
    ///   SHGetFileInfo 是 Shell 的统一入口，能正确解析任何 Shell 关联文件的图标。
    /// </summary>
    public static class IconExtractorService
    {
        // ========== Win32 API 声明 ==========

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_EXETYPE = 0x000000002;
        private const uint SHGFI_SYSICONINDEX = 0x000004000;

        /// <summary>SHIL_JUMBO：256×256 系统图标列表。</summary>
        private const int SHIL_JUMBO = 4;
        private const uint ILD_TRANSPARENT = 0x00000001;

        /// <summary>IImageList.GetIcon 的 StdCall 签名（避免生成 COM RCW）。</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIconDelegate(IntPtr self, int index, uint flags, out IntPtr hIcon);

        [DllImport("Shell32.dll")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string fileName, int iconIndex,
            IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // ========== 公开 API ==========

        /// <summary>
        /// 从指定路径提取图标（优先用 Shell 原生 SHGetFileInfo）。
        /// </summary>
        /// <returns>WPF ImageSource；最差情况返回系统默认应用图标（永不返回 null）</returns>
        public static ImageSource Extract(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return GetFallbackIcon();

            // 尝试 1: 系统 Jumbo 图标列表（256×256，避免 32px 放大到 48px 发虚）
            var result = TryExtractHighResolution(path);
            if (result != null) return result;

            // 尝试 2: SHGetFileInfo（兼容特殊 Shell 关联）
            result = TryExtractViaSHGetFileInfo(path);
            if (result != null) return result;

            // 尝试 3: ExtractIconEx（针对 .exe 的传统 API）
            result = TryExtractViaExtractIconEx(path);
            if (result != null) return result;

            // 最终兜底：系统默认应用图标
            AppenderService.Write($"IconExtractor: all methods failed for '{path}', using fallback");
            return GetFallbackIcon();
        }

        // ========== 科技模式：霓虹描边图标 ==========

        //调整发光感的核心参数：光晕颜色定义、模糊半径/强度、合成权、透明度。。。

        /// <summary>
        /// 把彩色图标转换为「霓虹线稿」风格：
        ///   - 内部只保留很淡的体色，避免整块实心色块压住桌面
        ///   - 按明暗分区提取轮廓线，高亮描边
        ///   - 轮廓外做两次盒式模糊，形成柔和光晕
        ///
        /// 所有颜色由同一个色相 hue（0~360°）推导，滑块调色就是在调这个值。
        /// 光晕直接烘焙进像素，因此倒影（VisualBrush）会自然带上同样的发光，
        /// 也不需要每个图标挂 WPF Effect，渲染开销更低。
        /// </summary>
        public static ImageSource ToNeonIcon(ImageSource source, double hue = 192)
        {
            if (source is not BitmapSource bitmap) return source;

            // 四种角色的颜色全部由色相推导，保证整体统一
            var bandColor = FromHsl(hue, 0.8, 0.9);   // 光带（接近白）
            var glyphColor = FromHsl(hue, 0.50, 0.60);  // 前景图形
            var plateColor = FromHsl(hue, 0.40, 0.30);  // 底板
            var haloColor = FromHsl(hue, 0.90, 0.62);   // 光晕——一{光晕色相和饱和度：色相，饱和度，亮度}

            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            int count = width * height;
            int threshold = CalculateOtsuThreshold(pixels, width, height, out bool invert);

            // 1. 分类：body=主体亮区，其余为细节/暗部
            var opaque = new bool[count];
            var body = new bool[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    int i = y * stride + x * 4;
                    opaque[idx] = pixels[i + 3] >= 24;

                    int luminance = (299 * pixels[i + 2] + 587 * pixels[i + 1] + 114 * pixels[i]) / 1000;
                    body[idx] = opaque[idx] && ((luminance >= threshold) != invert);
                }
            }

            // 2. 轮廓：与相邻像素的明暗分区不同，或紧邻透明边缘
            var edge = new bool[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (!opaque[idx]) continue;

                    for (int k = 0; k < 4 && !edge[idx]; k++)
                    {
                        int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0);
                        int ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                        int n = ny * width + nx;
                        if (!opaque[n] || body[n] != body[idx]) edge[idx] = true;
                    }
                }
            }

            // 3. 加粗轮廓：256px 下 1px 的线缩到 48px 会消失，先膨胀成光带——二{描边的发光面积设置}
            var band = Dilate(edge, width, height, 2);

            // 4. 辉光：近光 + 远光两层模糊，衰减更自然——三{光晕扩散范围，核心参数}
            var near = BoxBlur(band, width, height, 12);
            var far = BoxBlur(band, width, height, 24);

            // 5. 合成：光带最亮，本体极淡，外围是青色光晕
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    int i = y * stride + x * 4;

                    double glow = Math.Min(1.0, near[idx] * 0.9 + far[idx] * 1.15);//四——{光晕强度权重}
                    double haloAlpha = glow * 235;//五——光晕透明度上限，可见度

                    byte r, g, b, a;//a——{各图层受光晕影响程度（内部透出量）}
                    if (band[idx])
                    {
                        // 光带：最亮，才有霓虹灯管的感觉——六{光带（描边）亮度（视觉上的“灯芯”）}
                        r = bandColor.R; g = bandColor.G; b = bandColor.B; a = 240;
                    }
                    else if (opaque[idx] && !body[idx])
                    {
                        // 前景图形（占面积较小的一类）：半透明，保证图标可辨识
                        r = glyphColor.R; g = glyphColor.G; b = glyphColor.B;
                        a = (byte)Math.Max(180, haloAlpha * 0.7);
                    }
                    else if (opaque[idx])
                    {
                        // 底板：很淡的体色，让轮廓内部透出桌面
                        r = plateColor.R; g = plateColor.G; b = plateColor.B;
                        a = (byte)Math.Max(100, haloAlpha * 0.3);
                    }
                    else
                    {
                        // 透明区域：只保留光晕
                        r = haloColor.R; g = haloColor.G; b = haloColor.B;
                        a = (byte)haloAlpha;
                    }

                    pixels[i] = b;
                    pixels[i + 1] = g;
                    pixels[i + 2] = r;
                    pixels[i + 3] = a;
                }
            }

            var result = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        /// <summary>HSL 转 RGB 的结果（0~255）。</summary>
        private readonly struct Rgb
        {
            public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
            public byte R { get; }
            public byte G { get; }
            public byte B { get; }
        }

        /// <summary>把 HSL（色相 0~360，饱和度/亮度 0~1）转换为 RGB。</summary>
        private static Rgb FromHsl(double hue, double saturation, double lightness)
        {
            hue = ((hue % 360) + 360) % 360;
            double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            double m = lightness - c / 2;

            (double r, double g, double b) = hue switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x)
            };

            return new Rgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        /// <summary>
        /// 按色相生成界面强调色（饱和度 / 亮度取待办绿色的基准，只随色相变化），
        /// 供设置中心的色调滑轨联动其他 UI 元素。
        /// </summary>
        public static Color HueColor(double hue, byte alpha = 255)
        {
            var c = FromHsl(hue, 0.62, 0.61);
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        /// <summary>
        /// 形态学膨胀（方形结构元素），把 1px 轮廓加粗成光带。
        /// 同样拆成横 / 纵两趟最大值滤波，复杂度 O(n·r)。
        /// </summary>
        private static bool[] Dilate(bool[] mask, int width, int height, int radius)
        {
            var horizontal = new bool[width * height];
            var result = new bool[width * height];

            // 横向：任一侧半径内存在轮廓即保留
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[y * width + x]) { horizontal[y * width + x] = true; continue; }

                    for (int k = -radius; k <= radius; k++)
                    {
                        int nx = Math.Clamp(x + k, 0, width - 1);
                        if (!mask[y * width + nx]) continue;

                        horizontal[y * width + x] = true;
                        break;
                    }
                }
            }

            // 纵向：把横向结果继续扩散，形成方形光带
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (horizontal[y * width + x]) { result[y * width + x] = true; continue; }

                    for (int k = -radius; k <= radius; k++)
                    {
                        int ny = Math.Clamp(y + k, 0, height - 1);
                        if (!horizontal[ny * width + x]) continue;

                        result[y * width + x] = true;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 可分离盒式模糊（横向 + 纵向），把轮廓蒙版扩散成柔和光晕。
        /// 结果范围 0~1，边缘外按最近像素延展，避免出现黑边。
        /// </summary>
        private static double[] BoxBlur(bool[] mask, int width, int height, int radius)
        {
            int window = radius * 2 + 1;
            var horizontal = new double[width * height];
            var result = new double[width * height];

            for (int y = 0; y < height; y++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += mask[y * width + Math.Clamp(k, 0, width - 1)] ? 1 : 0;

                for (int x = 0; x < width; x++)
                {
                    horizontal[y * width + x] = sum / window;

                    int addX = Math.Clamp(x + radius + 1, 0, width - 1);
                    int removeX = Math.Clamp(x - radius, 0, width - 1);
                    sum += (mask[y * width + addX] ? 1 : 0) - (mask[y * width + removeX] ? 1 : 0);
                }
            }

            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += horizontal[Math.Clamp(k, 0, height - 1) * width + x];

                for (int y = 0; y < height; y++)
                {
                    result[y * width + x] = sum / window;

                    int addY = Math.Clamp(y + radius + 1, 0, height - 1);
                    int removeY = Math.Clamp(y - radius, 0, height - 1);
                    sum += horizontal[addY * width + x] - horizontal[removeY * width + x];
                }
            }

            return result;
        }

        /// <summary>
        /// Otsu 最大类间方差阈值：按每张图标自己的明暗分布分成两档，
        /// 比固定 50% 阈值更适合深色、浅色差异很大的应用图标。
        /// </summary>
        private static int CalculateOtsuThreshold(byte[] pixels, int width, int height, out bool invert)
        {
            var histogram = new int[256];
            int stride = width * 4;
            int total = 0;
            invert = false;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * stride + x * 4;
                    if (pixels[i + 3] < 32) continue;

                    int luminance = (299 * pixels[i + 2] + 587 * pixels[i + 1] + 114 * pixels[i]) / 1000;
                    histogram[luminance]++;
                    total++;
                }
            }

            if (total == 0) return 128;

            int sum = 0;
            for (int t = 0; t < 256; t++) sum += t * histogram[t];

            int backgroundWeight = 0;
            int backgroundSum = 0;
            double maxVariance = -1;
            int threshold = 128;

            for (int t = 0; t < 256; t++)
            {
                backgroundWeight += histogram[t];
                if (backgroundWeight == 0) continue;

                int foregroundWeight = total - backgroundWeight;
                if (foregroundWeight == 0) break;

                backgroundSum += t * histogram[t];
                double backgroundMean = (double)backgroundSum / backgroundWeight;
                double foregroundMean = (double)(sum - backgroundSum) / foregroundWeight;
                double delta = backgroundMean - foregroundMean;
                double variance = (double)backgroundWeight * foregroundWeight * delta * delta;

                if (variance > maxVariance)
                {
                    maxVariance = variance;
                    threshold = t;
                }
            }

            int cyanWeight = 0;
            for (int t = threshold; t < 256; t++) cyanWeight += histogram[t];
            invert = cyanWeight < total / 2;

            return threshold;
        }

        // ========== 内部尝试 ==========

        /// <summary>
        /// 通过系统 Jumbo 图标列表提取 256×256 高清图标。
        /// SHGetFileInfo 先取得系统图标索引，再调用 IImageList.GetIcon。
        /// 这里直接读 COM 虚表并 Marshal.Release，不创建 RCW，避免旧版 COM 释放异常。
        /// </summary>
        private static ImageSource? TryExtractHighResolution(string path)
        {
            IntPtr imageList = IntPtr.Zero;
            IntPtr hIcon = IntPtr.Zero;

            try
            {
                var shfi = new SHFILEINFO();
                uint flags = SHGFI_SYSICONINDEX | SHGFI_LARGEICON;
                if (!Path.IsPathRooted(path) || !File.Exists(path))
                    flags |= SHGFI_USEFILEATTRIBUTES;

                IntPtr hResult = SHGetFileInfo(path, 0, ref shfi,
                    (uint)Marshal.SizeOf(shfi), flags);
                if (hResult == IntPtr.Zero) return null;

                var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IID_IImageList
                if (SHGetImageList(SHIL_JUMBO, ref iid, out imageList) != 0 ||
                    imageList == IntPtr.Zero)
                    return null;

                // IUnknown 占 3 个槽，GetIcon 是 IImageList 的第 8 个方法，因此槽位为 10。
                IntPtr vtable = Marshal.ReadIntPtr(imageList);
                IntPtr getIconAddress = Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size);
                var getIcon = Marshal.GetDelegateForFunctionPointer<GetIconDelegate>(getIconAddress);

                if (getIcon(imageList, shfi.iIcon, ILD_TRANSPARENT, out hIcon) != 0 ||
                    hIcon == IntPtr.Zero)
                    return null;

                return HIconToBitmapSource(hIcon);
            }
            catch (Exception ex)
            {
                AppenderService.Write($"IconExtractor.HighResolution failed: {path}");
                AppenderService.Write(ex);
                return null;
            }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
                if (imageList != IntPtr.Zero) Marshal.Release(imageList);
            }
        }

        /// <summary>
        /// 通过 SHGetFileInfo 获取文件的大图标。
        /// 这个 API 会走 Shell 的完整图标解析链路（包括 .lnk 目标查找、.url 关联图标等）。
        /// </summary>
        private static ImageSource? TryExtractViaSHGetFileInfo(string path)
        {
            try
            {
                var shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;

                // 如果文件不存在（比如 PATH 里的短命令 notepad.exe），
                // 加 SHGFI_USEFILEATTRIBUTES 让 Shell 只根据路径的扩展名来查找关联图标，
                // 不需要文件真的存在。
                if (!Path.IsPathRooted(path) || !File.Exists(path))
                    flags |= SHGFI_USEFILEATTRIBUTES;

                IntPtr hResult = SHGetFileInfo(path, 0, ref shfi,
                    (uint)Marshal.SizeOf(shfi), flags);

                if (hResult == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                    return null;

                var bitmap = HIconToBitmapSource(shfi.hIcon);
                DestroyIcon(shfi.hIcon);
                return bitmap;
            }
            catch (Exception ex)
            {
                AppenderService.Write($"IconExtractor.SHGetFileInfo failed: {path}");
                AppenderService.Write(ex);
                return null;
            }
        }

        /// <summary>
        /// 通过 ExtractIconEx 获取图标（传统方式，作为 SHGetFileInfo 的回退）。
        /// </summary>
        private static ImageSource? TryExtractViaExtractIconEx(string path)
        {
            try
            {
                var largeIcons = new IntPtr[1];
                uint count = ExtractIconEx(path, 0, largeIcons, null, 1);
                if (count == 0 || largeIcons[0] == IntPtr.Zero)
                    return null;

                var bitmap = HIconToBitmapSource(largeIcons[0]);
                DestroyIcon(largeIcons[0]);
                return bitmap;
            }
            catch (Exception ex)
            {
                AppenderService.Write($"IconExtractor.ExtractIconEx failed: {path}");
                AppenderService.Write(ex);
                return null;
            }
        }

        // ========== 工具方法 ==========

        /// <summary>
        /// 将 HICON 句柄转换为可绑定的 WPF BitmapSource，并 Freeze 使其跨线程可用。
        /// </summary>
        private static BitmapSource HIconToBitmapSource(IntPtr hIcon)
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// 系统默认应用图标兜底（保证任何情况下都有图标显示）。
        /// </summary>
        private static ImageSource GetFallbackIcon()
        {
            // SystemIcons.Application 是系统自带的通用应用图标
            // 转成 BitmapSource 后 Freeze 防止跨线程问题
            using var icon = System.Drawing.SystemIcons.Application;
            var hIcon = icon.Handle;
            var bs = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
    }
}
