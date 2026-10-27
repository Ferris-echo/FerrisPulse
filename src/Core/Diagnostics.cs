// ============================================================================
// 模块名称: Diagnostics.cs
// 所属区域: Core (核心管理层)
// 核心职责:
//   轻量诊断日志：把原本被 `catch { }` 静默吞掉的异常落盘，
//   用于排查「设备莫名消失」「设置不保存」「自启动失效」这类无报错故障。
//   约束：绝不抛异常、不阻塞调用线程、单文件超过 1MB 自动轮转一份。
// 依赖关系: 无（仅 BCL）
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 新增日志分类时，必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace RazerBatteryTray
{
    public static class Diagnostics
    {
        private static readonly object sync = new object();

        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FerrisPulse", "logs");

        private static readonly string LogPath = Path.Combine(LogDir, "app.log");
        private const long MaxBytes = 1024 * 1024;

        public static string CurrentLogPath
        {
            get { return LogPath; }
        }

        public static void Log(string message)
        {
            Log(message, null);
        }

        public static void Log(string message, Exception ex)
        {
            try
            {
                lock (sync)
                {
                    if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);

                    try
                    {
                        FileInfo fi = new FileInfo(LogPath);
                        if (fi.Exists && fi.Length > MaxBytes)
                        {
                            string bak = LogPath + ".1";
                            if (File.Exists(bak)) File.Delete(bak);
                            File.Move(LogPath, bak);
                        }
                    }
                    catch { }

                    StringBuilder sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    sb.Append("  ");
                    sb.Append(message == null ? "(null)" : message);
                    if (ex != null)
                    {
                        sb.Append("  |  ");
                        sb.Append(ex.GetType().Name);
                        sb.Append(": ");
                        sb.Append(ex.Message);
                    }
                    sb.Append("\r\n");

                    File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 诊断日志本身绝不允许影响主流程
            }
        }
    }
}
