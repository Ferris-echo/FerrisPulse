// ============================================================================
// 模块名称: GameDetector.cs
// 所属区域: Services (后台长效服务层)
// 核心职责:
//   前台游戏智能感知引擎：
//   1. 结合 Win32 SHQueryUserNotificationState (QUNS_RUNNING_D3D_FULL_SCREEN) 与屏幕边界几何全屏探测
//   2. 深度白名单过滤（桌面 Shell、系统进程、主流浏览器与媒体播放器）
//   3. 进程级智能守护仲裁（支持用户自定义守护进程列表）
// 依赖关系: NativeMethods.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增过滤进程或调整全屏判定逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public static class GameDetector
    {
        public static bool IsGameRunning(out string detectedTitle)
        {
            detectedTitle = "";
            try
            {
                int qState = 0;
                int hr = NativeMethods.SHQueryUserNotificationState(out qState);
                if (hr == 0 && qState == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN)
                {
                    IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
                    if (fgHwnd != IntPtr.Zero && !IsShellOrSystemWindow(fgHwnd, out detectedTitle))
                    {
                        return true;
                    }
                }

                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;

                if (IsShellOrSystemWindow(fg, out detectedTitle)) return false;

                NativeMethods.RECT r;
                if (NativeMethods.GetWindowRect(fg, out r))
                {
                    Screen screen = Screen.FromHandle(fg);
                    if (screen != null)
                    {
                        if (r.Left <= screen.Bounds.Left && r.Top <= screen.Bounds.Top &&
                            r.Right >= screen.Bounds.Right && r.Bottom >= screen.Bounds.Bottom)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Services/GameDetector.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static bool IsShellOrSystemWindow(IntPtr hWnd, out string procName)
        {
            procName = "";
            try
            {
                StringBuilder sbClass = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, sbClass, sbClass.Capacity);
                string cls = sbClass.ToString();

                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow")
                {
                    return true;
                }

                uint pid = 0;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                if (pid != 0)
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        procName = p.ProcessName;
                        string nameLower = p.ProcessName.ToLowerInvariant();
                        if (nameLower == "explorer" || nameLower == "lockapp" || 
                            nameLower == "searchhost" || nameLower == "shellexperiencehost" ||
                            nameLower == "startmenuexperiencehost" || nameLower == "taskmgr" ||
                            nameLower == "applicationframehost" || nameLower == "textinputhost" ||
                            nameLower == "ferrispulse" || nameLower == "chrome" ||
                            nameLower == "msedge" || nameLower == "firefox" ||
                            nameLower == "brave" || nameLower == "opera" ||
                            nameLower == "potplayer" || nameLower == "potplayermini64" ||
                            nameLower == "vlc" || nameLower == "mpc-hc64" ||
                            nameLower == "bilibili" || nameLower == "devenv" ||
                            // 补全常见「非游戏全屏程序」：视频播放器、办公放映、会议与直播工具。
                            // 原黑名单只覆盖了部分播放器，全屏看视频/开会/放 PPT 时会被误判为游戏。
                            nameLower == "mpv" || nameLower == "mpc-be64" || nameLower == "mpc-be" ||
                            nameLower == "wmplayer" || nameLower == "video.ui" ||
                            nameLower == "powerpnt" || nameLower == "winword" ||
                            nameLower == "wpp" || nameLower == "et" ||
                            nameLower == "zoom" || nameLower == "teams" || nameLower == "ms-teams" ||
                            nameLower == "discord" || nameLower == "obs64" || nameLower == "obs32" ||
                            nameLower == "webexmta" || nameLower == "wemeetapp" ||
                            nameLower == "dingtalk" || nameLower == "wxwork")
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Services/GameDetector.cs: 已忽略异常", diagEx); }
            return false;
        }
    }
}
