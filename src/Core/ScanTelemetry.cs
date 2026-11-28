// ============================================================================
// 模块名称: ScanTelemetry.cs
// 所属区域: Core (核心管理层 - 可观测性)
// 核心职责:
//   全量扫描可观测性：ScanTimer 为一次全量扫描做分段计时（按品牌 helper 一段），
//   同时统计该段内部触发过的 HidTransport HID 枚举次数；扫描结束时把
//   "总耗时 / 各分段耗时 / 枚举次数 / Top5 最慢分段" 汇总成单行追加写入
//   exe 所在目录的 scan_trace.log（滚动保留最近 MaxTraceLines 行）。
//   本模块只用于测量，任何异常必须被吞掉，绝不允许影响扫描主流程；
//   日志落盘目录取程序集所在目录（铁律：禁用 Path.GetTempPath）。
// 依赖关系: HidTransport.cs（仅读取进程内枚举计数器），无任何品牌 Helper 依赖
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改日志格式或计时口径时，必须同步更新上方说明与 docs/OPTIMIZATION_BACKLOG.md M2 验证口径。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"" 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace RazerBatteryTray
{
    /// <summary>
    /// 一次扫描中某个分段（通常是某个品牌 helper 调用）的耗时与枚举次数。
    /// </summary>
    internal sealed class ScanProbeMark
    {
        public string Name;
        public long ElapsedMs;
        public int Enumerations;
    }

    /// <summary>
    /// 单次全量扫描的分段计时器。仅在扫描线程内使用，不做跨线程共享。
    /// </summary>
    internal sealed class ScanTimer
    {
        private readonly Stopwatch totalWatch;
        private readonly List<ScanProbeMark> marks = new List<ScanProbeMark>();
        private readonly bool active;

        internal ScanTimer(bool active)
        {
            this.active = active;
            totalWatch = Stopwatch.StartNew();
        }

        internal bool IsActive { get { return active; } }
        internal List<ScanProbeMark> Marks { get { return marks; } }
        internal long TotalMs { get { return totalWatch.ElapsedMilliseconds; } }

        /// <summary>
        /// 计时一个带返回值的 helper 调用；被测调用抛异常时仍然记录耗时。
        /// </summary>
        public T Time<T>(string name, Func<T> body)
        {
            if (body == null) return default(T);
            if (!active) return body();
            int enumBefore = HidTransport.EnumerationCount;
            long started = Stopwatch.GetTimestamp();
            try
            {
                return body();
            }
            finally
            {
                Record(name, started, enumBefore);
            }
        }

        /// <summary>
        /// 计时一个无返回值的调用（如内置驱动注册、脚本加载）。
        /// 注：不能与 Time&lt;T&gt; 同名重载——方法组在 Func&lt;T&gt; 与 Action 两个候选间会产生 CS0121 歧义。
        /// </summary>
        public void TimeAction(string name, Action body)
        {
            if (body == null) return;
            if (!active)
            {
                body();
                return;
            }
            int enumBefore = HidTransport.EnumerationCount;
            long started = Stopwatch.GetTimestamp();
            try
            {
                body();
            }
            finally
            {
                Record(name, started, enumBefore);
            }
        }

        private void Record(string name, long started, int enumBefore)
        {
            try
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - started;
                long ms = (long)(elapsedTicks * 1000.0 / Stopwatch.Frequency);
                ScanProbeMark mark = new ScanProbeMark();
                mark.Name = string.IsNullOrEmpty(name) ? "?" : name;
                mark.ElapsedMs = ms < 0 ? 0 : ms;
                int enumDelta = HidTransport.EnumerationCount - enumBefore;
                mark.Enumerations = enumDelta < 0 ? 0 : enumDelta;
                marks.Add(mark);
            }
            catch { }
        }
    }

    /// <summary>
    /// 扫描遥测入口：Begin 抢一代计时器，Complete 汇总落盘（单行、滚动、异常全吞）。
    /// </summary>
    internal static class ScanTelemetry
    {
        public const string TraceFileName = "scan_trace.log";

        private const int MaxTraceLines = 400;
        private const int KeepLinesAfterRotate = 200;
        private const int MaxLineLength = 1900;
        private const int TopCount = 5;

        private static readonly object fileSyncRoot = new object();
        private static int scanSequence;
        // 默认关闭按需启用（设环境变量 FERRISPULSE_SCAN_TRACE=1 开启），避免日常在用户目录写入 scan_trace.log
        private static volatile bool enabled = string.Equals(Environment.GetEnvironmentVariable("FERRISPULSE_SCAN_TRACE"), "1", StringComparison.OrdinalIgnoreCase);

        public static bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        public static ScanTimer Begin()
        {
            try { Interlocked.Increment(ref scanSequence); }
            catch { }
            try { HidTransport.ResetEnumerationCount(); }
            catch { }
            try { HidTransport.ResetEnumerationCacheHits(); }
            catch { }
            return new ScanTimer(enabled);
        }

        public static void Complete(ScanTimer timer, int deviceCount)
        {
            if (timer == null || !timer.IsActive) return;
            try
            {
                List<ScanProbeMark> marks = timer.Marks;
                long tappedMs = 0;
                int tappedEnum = 0;
                for (int i = 0; i < marks.Count; i++)
                {
                    tappedMs += marks[i].ElapsedMs;
                    tappedEnum += marks[i].Enumerations;
                }

                StringBuilder sb = new StringBuilder(640);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" | total=").Append(timer.TotalMs).Append("ms");
                sb.Append(" | devices=").Append(deviceCount);
                sb.Append(" | tapped=").Append(tappedMs).Append("ms");
                sb.Append(" | hidEnums=").Append(tappedEnum);
                sb.Append(" | hidCached=").Append(HidTransport.EnumerationCacheHits);
                sb.Append(" | segs=").Append(marks.Count);
                sb.Append(" | top=").Append(BuildTopSummary(marks));
                sb.Append(" | trace=");
                for (int i = 0; i < marks.Count; i++)
                {
                    if (i > 0) sb.Append(" > ");
                    sb.Append(marks[i].Name).Append(':').Append(marks[i].ElapsedMs).Append("ms/").Append(marks[i].Enumerations).Append('e');
                }

                string line = sb.ToString();
                if (line.Length > MaxLineLength) line = line.Substring(0, MaxLineLength) + "...";
                AppendTraceLine(line);
            }
            catch { }
        }

        private static string BuildTopSummary(List<ScanProbeMark> marks)
        {
            try
            {
                if (marks == null || marks.Count == 0) return "-";
                List<ScanProbeMark> copy = new List<ScanProbeMark>(marks);
                copy.Sort(delegate(ScanProbeMark a, ScanProbeMark b)
                {
                    if (b.ElapsedMs != a.ElapsedMs) return b.ElapsedMs.CompareTo(a.ElapsedMs);
                    return string.CompareOrdinal(a.Name, b.Name);
                });
                StringBuilder sb = new StringBuilder(160);
                int limit = copy.Count < TopCount ? copy.Count : TopCount;
                for (int i = 0; i < limit; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(copy[i].Name).Append('=').Append(copy[i].ElapsedMs).Append("ms");
                }
                return sb.ToString();
            }
            catch { return "-"; }
        }

        private static void AppendTraceLine(string line)
        {
            try
            {
                string dir = ResolveTraceDirectory();
                if (string.IsNullOrEmpty(dir)) return;
                string path = Path.Combine(dir, TraceFileName);
                lock (fileSyncRoot)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            string[] existing = File.ReadAllLines(path);
                            if (existing.Length >= MaxTraceLines)
                            {
                                int keepFrom = existing.Length - KeepLinesAfterRotate;
                                if (keepFrom < 0) keepFrom = 0;
                                List<string> keep = new List<string>(KeepLinesAfterRotate + 1);
                                for (int i = keepFrom; i < existing.Length; i++) keep.Add(existing[i]);
                                File.WriteAllLines(path, keep.ToArray(), new UTF8Encoding(false));
                            }
                        }
                    }
                    catch { }
                    try { File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false)); }
                    catch { }
                }
            }
            catch { }
        }

        private static string ResolveTraceDirectory()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    string appDir = Path.Combine(localAppData, "FerrisPulse");
                    if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
                    return appDir;
                }
            }
            catch { }
            try
            {
                string location = typeof(ScanTelemetry).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir)) return baseDir;
            }
            catch { }
            return null;
        }
    }
}
