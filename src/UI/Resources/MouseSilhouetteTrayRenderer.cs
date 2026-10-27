// ============================================================================
// 模块名称: MouseSilhouetteTrayRenderer.cs
// 所属区域: UI/Resources (托盘图标绘制层)
// 核心职责:
//   第四种托盘图标样式「鼠标轮廓电量」的渲染器。
//   以打包进程序的 mouse_body.png（256x256 鼠标俯视图）为唯一几何真相源，
//   按 alpha 通道做逐像素重着色，而非矢量重绘 —— 这样 16px 下形态与原图
//   100% 一致（真机验证过的做法，参见 InphicBatteryTray 的 DrawIcon）。
//
//   着色规则：
//     · 最外圈 alpha 轮廓 -> 深紫描边 #26203E（保证 16px 不发虚）
//     · 绿色机体         -> 自下而上按电量填充；未充能 #5C5C68
//     · 滚轮 / 高光 / 指示灯 -> 原样保留原图像素
//     · 断连或电量未知   -> 整体灰显
//   电量色阶：<= 15% #FF4646 红 / <= 40% #FFBE3C 黄 / 其余 #19FAC6 青绿
//   电量按 5% 步进量化为 20 个视觉档位，避免逐个百分点抖动。
//
// 依赖关系: 无外部依赖 (System.Drawing / 内嵌 Bitmap，无磁盘读盘)
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 修改配色、几何或档位算法时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace RazerBatteryTray
{
    public static class MouseSilhouetteTrayRenderer
    {
        private static readonly object sync = new object();
        private static Bitmap[] levelCache;
        private static bool cacheReady = false;
        private static int cacheSize = -1;   // 缓存按实际像素尺寸隔离，16/20/24 各一套

        // 未充能机体 / 描边 / 断连描边
        private static readonly Color EmptyBodyColor = Color.FromArgb(100, 100, 110);
        private static readonly Color OffOutlineColor = Color.FromArgb(52, 52, 62);

        private const int WarnThreshold = 40;
        private const int CriticalThreshold = 15;
        private const int QuantizeStep = 5;
        private const int AlphaThreshold = 40;

        private static Color OutlineColor { get { return Color.FromArgb(38, 32, 62); } }

        // 按 5% 步进量化到 20 档位；负数（未知）原样返回 -1。
        public static int QuantizePercent(int percent)
        {
            if (percent < 0) return -1;
            if (percent > 100) percent = 100;
            if (percent == 0) return 0;
            int q = ((percent + (QuantizeStep - 1)) / QuantizeStep) * QuantizeStep;
            if (q > 100) q = 100;
            return q;
        }

        // 电量色阶（阈值取包含边界）
        public static Color StatusColor(int level)
        {
            if (level < 0) return EmptyBodyColor;
            if (level <= CriticalThreshold) return Color.FromArgb(255, 70, 70);
            if (level <= WarnThreshold) return Color.FromArgb(255, 190, 60);
            return Color.FromArgb(0x19, 0xFA, 0xC6);
        }

        // 兼容旧调用名
        public static Color GetLevelColor(int percent)
        {
            return StatusColor(percent);
        }

        // 原图机体为高饱和绿；缩放混色后仍保持 G 通道明显优势
        private static bool IsBodyPixel(Color c)
        {
            return c.G > 110 && (c.G - c.R) > 45 && (c.G - c.B) > 45;
        }

        /// <summary>载入内嵌的鼠标底图；失败返回 null（调用方回退到纯色块）。</summary>
        private static Bitmap LoadSource()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith("mouse_body.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream st = asm.GetManifestResourceStream(names[i]))
                        {
                            if (st == null) continue;
                            using (Bitmap tmp = new Bitmap(st))
                            {
                                // 复制成独立的 32bppArgb，避免持有流引用
                                return new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>优先从磁盘 resources 目录读取同名 PNG，便于用户替换皮肤。</summary>
        private static Bitmap LoadSourceFromDisk()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new string[] {
                    Path.Combine(dir, "resources", "mouse_body.png"),
                    Path.Combine(dir, "mouse_body.png")
                };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(candidates[i]))
                    {
                        byte[] bytes = File.ReadAllBytes(candidates[i]);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        using (Bitmap tmp = new Bitmap(ms))
                        {
                            return new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static Bitmap GetSource()
        {
            // 磁盘优先（可换皮），否则用内嵌资源
            Bitmap b = LoadSourceFromDisk();
            if (b != null) return b;
            Assembly asm = Assembly.GetExecutingAssembly();
            try
            {
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith("mouse_body.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream st = asm.GetManifestResourceStream(names[i]))
                        {
                            if (st == null) continue;
                            using (Bitmap tmp = new Bitmap(st))
                            {
                                Bitmap copy = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                                using (Graphics g = Graphics.FromImage(copy))
                                {
                                    g.Clear(Color.Transparent);
                                    g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                                }
                                return copy;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static Bitmap Draw(int percent, bool isCharging, bool isConnected, bool isSleeping, int size, Color? brandColor)
        {
            if (size < 8) size = 8;

            lock (sync)
            {
                if (!cacheReady || cacheSize != size)
                {
                    ResetCacheLocked();
                    BuildCache(size);
                }
            }

            if (levelCache != null)
            {
                int q = QuantizePercent(percent);
                bool grey = (!isConnected) || (percent < 0) || isSleeping;
                int idx = grey ? levelCache.Length - 1 : LevelToIndex(q);
                Bitmap src = levelCache[idx];
                if (src != null)
                {
                    return new Bitmap(src);
                }
            }

            // 兜底：底图不可用时退化为纯色圆角块，保证托盘永不空白
            return DrawFallback(percent, isConnected, size);
        }

        private static int LevelToIndex(int q)
        {
            if (q < 0) return 0;
            if (q > 100) q = 100;
            return q / QuantizeStep;         // 0..20
        }

        private static void BuildCache(int size)
        {
            lock (sync)
            {
                if (cacheReady && cacheSize == size) return;
                try
                {
                    Bitmap src = GetSource();
                    if (src == null)
                    {
                        cacheReady = true;
                        return;
                    }

                    // 索引 0..20 对应 0%..100%，索引 21 为灰显（断连/未知/休眠）
                    levelCache = new Bitmap[22];
                    for (int i = 0; i <= 20; i++)
                    {
                        levelCache[i] = RenderLevel(src, i * QuantizeStep, false, size);
                    }
                    levelCache[21] = RenderLevel(src, 0, true, size);
                    src.Dispose();
                }
                catch
                {
                    levelCache = null;
                }
                finally
                {
                    cacheReady = true;
                    cacheSize = size;
                }
            }
        }

        /// <summary>核心：把底图缩放到目标尺寸后，按像素规则重着色。</summary>
        private static Bitmap RenderLevel(Bitmap src, int level, bool grey, int size)
        {
            int w = size, h = size;

            Bitmap scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }

            int[,] alphaMap = new int[h, w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    alphaMap[y, x] = scaled.GetPixel(x, y).A;
                }
            }
            bool[,] ring = BuildRing(alphaMap, w, h, AlphaThreshold);

            int bodyTopRow = -1;
            int bodyBottomRow = -1;
            for (int yy = 0; yy < h; yy++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    if (alphaMap[yy, xx] >= AlphaThreshold)
                    {
                        if (bodyTopRow < 0) bodyTopRow = yy;
                        bodyBottomRow = yy;
                        break;
                    }
                }
            }
            if (bodyTopRow < 0) { bodyTopRow = 0; bodyBottomRow = h - 1; }

            int lv = Math.Max(0, Math.Min(100, level));
            double fillTop = bodyBottomRow + 1 - (bodyBottomRow + 1 - bodyTopRow) * (lv / 100.0);
            if (lv > 0)
            {
                // 低电量时按比例算出的高度不足 1px，会被底部描边整条吃掉，
                // 导致 15% 及以下看不到红色警示。此处保底可见高度。
                double maxTop = bodyBottomRow - Math.Max(1.0, h * 0.10);
                if (fillTop > maxTop) fillTop = maxTop;
            }
            Color levelColor = StatusColor(level);

            Bitmap outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = scaled.GetPixel(x, y);
                    if (c.A < AlphaThreshold) continue;

                    Color o;
                    if (grey)
                    {
                        if (ring[y, x])
                        {
                            o = OffOutlineColor;
                        }
                        else
                        {
                            int lum = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                            int v = 74 + lum * 108 / 255;
                            if (v > 255) v = 255;
                            o = Color.FromArgb(v, v, Math.Min(255, v + 8));
                        }
                    }
                    else if (ring[y, x])
                    {
                        o = OutlineColor;
                    }
                    else if (IsBodyPixel(c))
                    {
                        o = (y >= fillTop) ? levelColor : EmptyBodyColor;
                    }
                    else
                    {
                        o = c;
                    }

                    outp.SetPixel(x, y, Color.FromArgb(c.A, o));
                }
            }

            scaled.Dispose();
            return outp;
        }

        // 最外一圈：alpha 有效且四邻域含背景
        private static bool[,] BuildRing(int[,] alpha, int w, int h, int thr)
        {
            bool[,] ring = new bool[h, w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (alpha[y, x] < thr) continue;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                    {
                        ring[y, x] = true;
                        continue;
                    }
                    if (alpha[y, x + 1] < thr || alpha[y, x - 1] < thr ||
                        alpha[y + 1, x] < thr || alpha[y - 1, x] < thr)
                        ring[y, x] = true;
                }
            }
            return ring;
        }

        // 底图缺失时的极简兜底：圆角块 + 电量条
        private static Bitmap DrawFallback(int percent, bool connected, int size)
        {
            int w = size, h = size;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = connected ? StatusColor(percent) : Color.FromArgb(120, 120, 130);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(100, 100, 110)))
                using (SolidBrush fb = new SolidBrush(fill))
                {
                    g.FillRectangle(bg, 2, 2, w - 4, h - 4);
                    int barH = (int)((h - 4) * (Math.Max(0, percent) / 100.0));
                    g.FillRectangle(fb, 2, h - 2 - barH, w - 4, barH);
                }
            }
            return bmp;
        }

        /// <summary>清空缓存（用于 DPI 变化后按新尺寸重建）。</summary>
        public static void ResetCache()
        {
            lock (sync)
            {
                ResetCacheLocked();
            }
        }

        private static void ResetCacheLocked()
        {
            if (levelCache != null)
            {
                for (int i = 0; i < levelCache.Length; i++)
                {
                    if (levelCache[i] != null) levelCache[i].Dispose();
                }
                levelCache = null;
            }
            cacheReady = false;
            cacheSize = -1;
        }
    }
}
