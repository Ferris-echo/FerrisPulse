// ============================================================================
// 模块名称: AppResetManager.cs
// 所属区域: Core (核心管理层)
// 核心职责:
//   全量历史数据清理、深度重置与跨路径/跨版本旧实例自动接管中枢：
//   1. 智能识别不同目录、低版本或历史进程名 (RazerBatteryTray 等) 的旧实例并安全接管
//   2. 彻底清空 HKCU\Software\FerrisPulse 与 HKCU\Software\RazerBatteryTray 全注册表树
//   3. 深度扫描并清除 Run 注册表、StartupApproved 与启动文件夹中的所有历史自启残留
//   4. 清理 %AppData%\FerrisPulse 扩展缓存、开始菜单与桌面快捷方式（支持重置重启与纯净卸载双模式）
// 依赖关系: StartupManager.cs, NativeMethods.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增注册表根键或本地持久化目录时，【必须】同步更新本清理器。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class AppResetManager
    {
        public static readonly string[] AllKnownProcessNames = new string[]
        {
            "FerrisPulse",
            "RazerBatteryTray",
            "RazerBattery",
            "RazerTray"
        };

        /// <summary>
        /// 判断当前启动的新实例是否应当主动终止并接管后台已运行的实例：
        /// 1. 后台进程名属于历史旧名称（如 RazerBatteryTray） -> 必须接管
        /// 2. 后台进程的可执行文件路径与当前进程路径不一致（如旧文件夹自启残留） -> 必须接管
        /// 3. 后台进程的文件版本号低于当前实例 -> 必须接管
        /// </summary>
        public static bool ShouldTakeOverProcess(
            string existingExePath,
            Version existingVersion,
            string currentExePath,
            Version currentVersion,
            string existingProcessName)
        {
            if (!string.IsNullOrEmpty(existingProcessName))
            {
                string procName = existingProcessName.Trim();
                if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    procName = procName.Substring(0, procName.Length - 4);
                }
                if (!procName.Equals("FerrisPulse", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string normExisting = NormalizePath(existingExePath);
            string normCurrent = NormalizePath(currentExePath);
            if (!string.IsNullOrEmpty(normExisting) && !string.IsNullOrEmpty(normCurrent))
            {
                if (!normExisting.Equals(normCurrent, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (existingVersion != null && currentVersion != null)
            {
                if (existingVersion < currentVersion)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try
            {
                string cleaned = path.Trim().Trim('"');
                return Path.GetFullPath(cleaned);
            }
            catch
            {
                return path.Trim().Trim('"');
            }
        }

        /// <summary>
        /// 扫描并终止系统中其他运行的 FerrisPulse / RazerBatteryTray 实例。
        /// 若 onlyLegacyOrDifferentPath 为 true，则仅终止历史旧名、不同目录或更低版本的旧进程；
        /// 若为 false（如执行一键重置/卸载时），则终止除当前进程外的所有同系进程。
        /// </summary>
        public static int TerminateOtherInstances(bool onlyLegacyOrDifferentPath)
        {
            int terminatedCount = 0;
            try
            {
                Process currentProc = Process.GetCurrentProcess();
                int currentPid = currentProc.Id;
                string currentExePath = StartupManager.GetApplicationExecutablePath();
                Version currentVer = typeof(AppResetManager).Assembly.GetName().Version;

                Process[] allProcesses = Process.GetProcesses();
                for (int i = 0; i < allProcesses.Length; i++)
                {
                    Process p = allProcesses[i];
                    try
                    {
                        if (p.Id == currentPid) continue;

                        string pName = p.ProcessName ?? string.Empty;
                        if (!IsKnownFamilyProcessName(pName)) continue;

                        bool shouldKill = !onlyLegacyOrDifferentPath;
                        if (onlyLegacyOrDifferentPath)
                        {
                            string existingPath = string.Empty;
                            Version existingVer = null;
                            try
                            {
                                if (p.MainModule != null)
                                {
                                    existingPath = p.MainModule.FileName;
                                    if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                                    {
                                        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(existingPath);
                                        if (fvi != null && !string.IsNullOrEmpty(fvi.FileVersion))
                                        {
                                            try { existingVer = new Version(fvi.FileVersion); } catch { }
                                        }
                                    }
                                }
                            }
                            catch { }

                            shouldKill = ShouldTakeOverProcess(existingPath, existingVer, currentExePath, currentVer, pName);
                        }

                        if (shouldKill)
                        {
                            try
                            {
                                if (!p.HasExited)
                                {
                                    p.Kill();
                                    p.WaitForExit(1500);
                                    terminatedCount++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
            return terminatedCount;
        }

        private static bool IsKnownFamilyProcessName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            for (int i = 0; i < AllKnownProcessNames.Length; i++)
            {
                if (processName.Equals(AllKnownProcessNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            if (processName.StartsWith("FerrisPulse", StringComparison.OrdinalIgnoreCase) ||
                processName.StartsWith("RazerBatteryTray", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 彻底清除所有开机自启动通道（含 Run 键、StartupApproved 仲裁表以及 Startup 文件夹快捷方式），
        /// 包括用户重命名或遗留在旧文件夹下的任何包含 FerrisPulse / RazerBatteryTray 的启动项。
        /// </summary>
        public static void ClearAllAutoStartEntries()
        {
            try
            {
                string dummyErr;
                StartupManager.SetAutoStart(false, out dummyErr);
            }
            catch { }

            List<string> matchedValueNames = new List<string>();
            matchedValueNames.Add(StartupManager.AppName);
            for (int i = 0; i < StartupManager.LegacyAppNames.Length; i++)
            {
                matchedValueNames.Add(StartupManager.LegacyAppNames[i]);
            }

            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(StartupManager.RunRegistryKey, true))
                {
                    if (runKey != null)
                    {
                        string[] valNames = runKey.GetValueNames();
                        if (valNames != null)
                        {
                            for (int i = 0; i < valNames.Length; i++)
                            {
                                string vName = valNames[i];
                                if (string.IsNullOrEmpty(vName)) continue;

                                string vData = string.Empty;
                                try
                                {
                                    object raw = runKey.GetValue(vName);
                                    if (raw != null) vData = raw.ToString();
                                }
                                catch { }

                                bool isMatch = false;
                                for (int k = 0; k < AllKnownProcessNames.Length; k++)
                                {
                                    string token = AllKnownProcessNames[k];
                                    if (vName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        vData.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isMatch = true;
                                        break;
                                    }
                                }

                                if (isMatch)
                                {
                                    if (!matchedValueNames.Contains(vName))
                                    {
                                        matchedValueNames.Add(vName);
                                    }
                                    try { runKey.DeleteValue(vName, false); } catch { }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey approvedRun = Registry.CurrentUser.OpenSubKey(StartupManager.StartupApprovedRunKey, true))
                {
                    if (approvedRun != null)
                    {
                        for (int i = 0; i < matchedValueNames.Count; i++)
                        {
                            try { approvedRun.DeleteValue(matchedValueNames[i], false); } catch { }
                        }
                    }
                }
            }
            catch { }

            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrEmpty(startupFolder) && Directory.Exists(startupFolder))
                {
                    string[] lnkFiles = Directory.GetFiles(startupFolder, "*.lnk");
                    for (int i = 0; i < lnkFiles.Length; i++)
                    {
                        string fileName = Path.GetFileName(lnkFiles[i]);
                        if (fileName.IndexOf("FerrisPulse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fileName.IndexOf("灵脉", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fileName.IndexOf("RazerBattery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fileName.IndexOf("RazerTray", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try { File.Delete(lnkFiles[i]); } catch { }
                            try
                            {
                                using (RegistryKey approvedFolder = Registry.CurrentUser.OpenSubKey(StartupManager.StartupApprovedFolderKey, true))
                                {
                                    if (approvedFolder != null)
                                    {
                                        approvedFolder.DeleteValue(fileName, false);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 递归清空 FerrisPulse 与历史版本 RazerBatteryTray 在 HKCU 下的所有注册表配置、隐藏设备列表、自定义配置与电量缓存。
        /// </summary>
        /// <summary>
        /// 测试隔离属性：单元测试可注入独立隔离注册表树（例如 Software\FerrisPulse_UnitTest），
        /// 彻底杜绝自动化回归测试误清空或污染开发人员与用户的真实配置。
        /// </summary>
        public static string RegistryRootOverride = null;

        /// <summary>
        /// 递归清空 FerrisPulse 与历史版本 RazerBatteryTray 在 HKCU 下的所有注册表配置、隐藏设备列表、自定义配置与电量缓存。
        /// </summary>
        public static void ClearAllRegistryData()
        {
            string primaryTree = !string.IsNullOrEmpty(RegistryRootOverride) ? RegistryRootOverride : ConfigManager.ConfigRegistryKey;
            string legacyTree = !string.IsNullOrEmpty(RegistryRootOverride) ? (RegistryRootOverride + "_Legacy") : ConfigManager.LegacyConfigKey;

            string[] regTrees = new string[]
            {
                primaryTree,
                legacyTree,
                @"Software\Classes\AppUserModelId\" + StartupManager.AppUserModelId,
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + StartupManager.AppUserModelId
            };

            for (int i = 0; i < regTrees.Length; i++)
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(regTrees[i], false);
                }
                catch { }
            }
        }

        /// <summary>
        /// 清理 %AppData%\FerrisPulse 本地扩展数据、自更新临时文件，并按需移除开始菜单与桌面快捷方式。
        /// </summary>
        public static void ClearAppDataAndShortcuts(bool removeShortcuts)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    string fpAppData = Path.Combine(appData, "FerrisPulse");
                    if (Directory.Exists(fpAppData))
                    {
                        try { Directory.Delete(fpAppData, true); } catch { }
                    }
                    string legacyAppData = Path.Combine(appData, "RazerBatteryTray");
                    if (Directory.Exists(legacyAppData))
                    {
                        try { Directory.Delete(legacyAppData, true); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                string exePath = StartupManager.GetApplicationExecutablePath();
                if (!string.IsNullOrEmpty(exePath))
                {
                    string oldFile = exePath + ".old";
                    string tmpFile = exePath + ".update.tmp";
                    if (File.Exists(oldFile)) { try { File.Delete(oldFile); } catch { } }
                    if (File.Exists(tmpFile)) { try { File.Delete(tmpFile); } catch { } }
                }
            }
            catch { }

            if (removeShortcuts)
            {
                try
                {
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
                    if (Directory.Exists(startMenu))
                    {
                        string lnk1 = Path.Combine(startMenu, "FerrisPulse · 灵脉.lnk");
                        string lnk2 = Path.Combine(startMenu, "RazerBatteryTray.lnk");
                        if (File.Exists(lnk1)) { try { File.Delete(lnk1); } catch { } }
                        if (File.Exists(lnk2)) { try { File.Delete(lnk2); } catch { } }
                    }
                }
                catch { }

                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (Directory.Exists(desktop))
                    {
                        string dLnk1 = Path.Combine(desktop, "FerrisPulse · 灵脉.lnk");
                        string dLnk2 = Path.Combine(desktop, "RazerBatteryTray.lnk");
                        if (File.Exists(dLnk1)) { try { File.Delete(dLnk1); } catch { } }
                        if (File.Exists(dLnk2)) { try { File.Delete(dLnk2); } catch { } }
                    }
                }
                catch { }

                try
                {
                    NativeMethods.SHChangeNotify(0x7FFFFFFF, 0x1000, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
            }
        }

        /// <summary>
        /// 执行一键全量重置或卸载清理
        /// </summary>
        public static bool PerformFullReset(bool removeShortcuts, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                TerminateOtherInstances(false);
                ClearAllAutoStartEntries();
                ClearAllRegistryData();
                ClearAppDataAndShortcuts(removeShortcuts);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
