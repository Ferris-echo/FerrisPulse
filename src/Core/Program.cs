// ============================================================================
// 模块名称: Program.cs
// 所属区域: Core (核心管理层)
// 核心职责:
//   应用程序顶层入口中枢：
//   1. 锁定基准工作目录，杜绝开机自启时被重定向至 System32 的路径漂移
//   2. 声明 Windows 硬件高 DPI 感知 (Per-Monitor DPI Aware)
//   3. 系统全局命名互斥体 (Mutex) 单实例保护，自动接管不同路径/低版本的旧进程，多开时发送系统广播唤醒已有实例并安全退出
//   4. 解析 --autostart 命令行参数与未捕获异常统一兜底弹窗
// 依赖关系: NativeMethods.cs, StartupManager.cs, AppResetManager.cs, MainForm.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增启动流程或参数时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    static class Program
    {
        private const string SingleInstanceMutexName = @"Global\FerrisPulse_SingleInstance_Mutex";
        public const string SingleInstanceEventName = @"Global\FerrisPulse_Wakeup_Event";
        public static readonly int WM_SHOWME = NativeMethods.RegisterWindowMessage("FerrisPulse_ActivateExistingInstance");

        private static Mutex appMutex = null;

        public static void ReleaseSingleInstanceMutex()
        {
            if (appMutex != null)
            {
                try { appMutex.ReleaseMutex(); } catch { }
                try { appMutex.Close(); } catch { }
                appMutex = null;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // 1. 强制将工作目录锚定在程序运行目录，彻底根治开机启动被重定向至 C:\Windows\System32 的路径破坏
            try
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            }
            catch { }

            // 1.1 静默清理自更新残留的旧版本备份 (.old)
            try
            {
                string oldBackup = Application.ExecutablePath + ".old";
                if (File.Exists(oldBackup))
                {
                    File.Delete(oldBackup);
                }
            }
            catch { }

            // 2. 声明系统物理级 High-DPI 感知
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch { }

            // 3. 同步开始菜单与 AUMID 快捷方式
            try
            {
                StartupManager.EnsureAppShortcutWithAumid();
            }
            catch { }

            // 4. 全局未捕获异常安全兜底
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => {
                string msg = e.Exception != null ? (e.Exception.Message + "\n\n" + e.Exception.StackTrace) : "未知线程异常";
                MessageBox.Show("程序发生异常: " + msg, "FerrisPulse · 灵脉 - 错误提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                Exception ex = e.ExceptionObject as Exception;
                string msg = ex != null ? (ex.Message + "\n\n" + ex.StackTrace) : "未知系统错误";
                MessageBox.Show("未处理的致命异常: " + msg, "FerrisPulse · 灵脉 - 致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // 5. 解析命令行参数
            bool isAutoStart = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && args[i].IndexOf("autostart", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isAutoStart = true;
                        break;
                    }
                }
            }

            // 5.1 跨路径/跨版本旧实例自动接管：
            // 若后台运行着旧文件夹路径下的 FerrisPulse.exe、更低版本的进程或历史旧名进程 (RazerBatteryTray)，
            // 直接终止旧进程并自动修正开机自启路径，彻底根除“两个软件同时开”及“唤醒旧版看不到新设备”的问题！
            try
            {
                int takenOver = AppResetManager.TerminateOtherInstances(true);
                if (takenOver > 0)
                {
                    StartupManager.SyncAutoStartRegistry();
                }
            }
            catch { }

            // 6. 系统全局单实例检测与健康活体监测（彻底根除死进程锁死导致打不开软件的问题）
            Process currentProc = Process.GetCurrentProcess();
            Process[] allProcs = Process.GetProcessesByName("FerrisPulse");
            Process existingProc = null;
            for (int i = 0; i < allProcs.Length; i++)
            {
                var p = allProcs[i];
                if (p.Id != currentProc.Id)
                {
                    existingProc = p;
                    break;
                }
            }

            bool isNewInstance = false;
            try
            {
                appMutex = new Mutex(true, SingleInstanceMutexName, out isNewInstance);
            }
            catch (AbandonedMutexException)
            {
                // 上一个进程异常终止未释放互斥体，本实例自动接管主控权
                isNewInstance = true;
            }
            catch
            {
                isNewInstance = (existingProc == null);
            }

            // 6.1 活体健康检查：若互斥体被占有但已有进程已僵死或无响应，强制回收清理并由新实例无缝接管
            if (existingProc != null)
            {
                bool isAliveAndHealthy = false;
                try
                {
                    isAliveAndHealthy = !existingProc.HasExited && existingProc.Responding;
                }
                catch { }

                if (!isAliveAndHealthy)
                {
                    try
                    {
                        existingProc.Kill();
                        existingProc.WaitForExit(1500);
                    }
                    catch { }
                    isNewInstance = true;
                }
            }
            else
            {
                // 系统中无任何其他运行中的 FerrisPulse 进程，绝对接管为主实例
                isNewInstance = true;
            }

            if (!isNewInstance)
            {
                // 如果已有健康实例运行且本次是用户手动双击快捷方式唤醒，激活并置顶前台
                if (!isAutoStart)
                {
                    if (existingProc != null)
                    {
                        try
                        {
                            // 授予已有实例前台窗口抢占权限 (Bypass Windows Focus Stealing Prevention)
                            NativeMethods.AllowSetForegroundWindow((uint)existingProc.Id);
                        }
                        catch { }
                    }

                    // 1. 发送全局内核唤醒事件 (100% 免疫 UIPI 与消息泵队列阻塞)
                    try
                    {
                        using (var wakeupEvt = EventWaitHandle.OpenExisting(SingleInstanceEventName))
                        {
                            wakeupEvt.Set();
                        }
                    }
                    catch { }

                    // 2. 广播系统注册窗口消息双重保底
                    try
                    {
                        NativeMethods.PostMessage((IntPtr)0xffff, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch { }

                    // 3. 看门狗定时等待检测（最长等待 1200ms）
                    // 彻底根除用户“打不开软件一直卡着”的问题：如果已有实例在 1.2 秒内未能成功显示主窗口，
                    // 说明已有实例可能卡死在消息阻塞、死锁或后台悬挂，直接清理僵尸进程，由本实例接管前台启动！
                    bool successfullyWoken = false;
                    for (int w = 0; w < 12; w++)
                    {
                        Thread.Sleep(100);
                        try
                        {
                            if (existingProc == null || existingProc.HasExited)
                            {
                                break;
                            }
                            existingProc.Refresh();
                            IntPtr hWnd = existingProc.MainWindowHandle;
                            if (hWnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hWnd))
                            {
                                successfullyWoken = true;
                                break;
                            }
                        }
                        catch { }
                    }

                    if (!successfullyWoken)
                    {
                        try
                        {
                            if (existingProc != null && !existingProc.HasExited)
                            {
                                existingProc.Kill();
                                existingProc.WaitForExit(1000);
                            }
                        }
                        catch { }
                        isNewInstance = true;
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            // 7. 启动主窗体消息循环
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm(isAutoStart));
            }
            finally
            {
                ReleaseSingleInstanceMutex();
            }
        }
    }
}
