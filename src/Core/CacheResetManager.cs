// ============================================================================
// 模块名称: CacheResetManager.cs
// 所属区域: Core (核心管理层)
// 核心职责:
//   设备缓存与识别状态的一键清除（「清除缓存」功能的核心实现）：
//   1. 清空前先把【注册表设备树 / 应用设置树 / 图标与标题缓存】备份到
//      %LOCALAPPDATA%\FerrisPulse-Backup\<yyyyMMdd_HHmmss>\（含可直接双击导入的 .reg）
//   2. 删除 HKCU\Software\RazerBatteryTray（DeviceState / CustomDevices / HiddenDevices /
//      各品牌电量缓存 / PriorityOrder / PrimaryDeviceId 全部设备识别记忆）
//   3. 删除 HKCU\Software\FerrisPulse（应用设置与设备外观，回到出厂默认）
//   4. 删除 %LOCALAPPDATA%\FerrisPulse（GameIcons / GameTitles 等图标缓存）
//   5. 清空 %APPDATA%\FerrisPulse 的运行缓存，但**保留 adapters\**（用户自定义驱动脚本）
//   6. 保留 自启动项 / 快捷方式 / AppUserModelId 键（这些不属于缓存，属于安装态）
//
//   【为什么需要它】
//   0x3554:0xFA09 这类公版 PID 被多家厂商共用，识别结果被历史缓存、隐藏标记与手动
//   自定义条目污染后，只改驱动代码仍可能看到旧卡片。此功能让用户不必手改注册表，
//   一键回到「干净识别」状态，且误点也能用备份还原。
//
//   【测试隔离】
//   RegistryRootOverride / LocalCacheRootOverride / RoamingCacheRootOverride /
//   BackupRootOverride 供自动化测试注入沙盒路径，测试永远不会触碰真实用户数据。
// 依赖关系: ConfigManager.cs（注册表树名）、StartupManager.cs（仅重启流程用到）
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 新增/迁移持久化根键或缓存目录时，必须同步更新本清理器与
//    docs/HARDWARE_PROTOCOLS.md、CHANGELOG，并补充测试断言。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [安全铁律]: 只删「缓存与识别记忆」；adapters 目录、自启动项与快捷方式必须保留。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    /// <summary>
    /// 「清除缓存」执行报告：备份位置、已清理项、保留项与失败项。
    /// </summary>
    public sealed class CacheResetReport
    {
        public string BackupDirectory = "";
        public readonly List<string> ClearedRegistryTrees = new List<string>();
        public readonly List<string> ClearedDirectories = new List<string>();
        public readonly List<string> PreservedPaths = new List<string>();
        public readonly List<string> Failures = new List<string>();

        public bool Success
        {
            get { return this.Failures.Count == 0; }
        }

        public string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("备份目录: " + this.BackupDirectory);
            sb.AppendLine("已清注册表树: " + this.ClearedRegistryTrees.Count + " 项");
            for (int i = 0; i < this.ClearedRegistryTrees.Count; i++) sb.AppendLine("  - " + this.ClearedRegistryTrees[i]);
            sb.AppendLine("已清缓存目录: " + this.ClearedDirectories.Count + " 项");
            for (int i = 0; i < this.ClearedDirectories.Count; i++) sb.AppendLine("  - " + this.ClearedDirectories[i]);
            if (this.PreservedPaths.Count > 0)
            {
                sb.AppendLine("已保留: " + this.PreservedPaths.Count + " 项");
                for (int i = 0; i < this.PreservedPaths.Count; i++) sb.AppendLine("  - " + this.PreservedPaths[i]);
            }
            if (this.Failures.Count > 0)
            {
                sb.AppendLine("失败: " + this.Failures.Count + " 项");
                for (int i = 0; i < this.Failures.Count; i++) sb.AppendLine("  - " + this.Failures[i]);
            }
            return sb.ToString().TrimEnd();
        }
    }

    public static class CacheResetManager
    {
        /// <summary>设备识别与电量状态树（旧版本命名空间，仍为当前设备数据主树）。</summary>
        public const string DeviceRegistryTree = @"Software\RazerBatteryTray";
        /// <summary>应用设置与设备外观树。</summary>
        public const string SettingsRegistryTree = @"Software\FerrisPulse";
        /// <summary>%LOCALAPPDATA% 下的图标/标题缓存目录名。</summary>
        public const string LocalCacheFolderName = "FerrisPulse";
        /// <summary>%APPDATA% 下的运行缓存目录名（其内 adapters 必须保留）。</summary>
        public const string RoamingCacheFolderName = "FerrisPulse";
        /// <summary>%APPDATA%\FerrisPulse 内必须保留的自定义驱动脚本目录名。</summary>
        public const string PreservedAdaptersFolderName = "adapters";
        /// <summary>备份根目录名（位于 %LOCALAPPDATA%，刻意放在被清理目录之外）。</summary>
        public const string BackupRootFolderName = "FerrisPulse-Backup";

        private const int RegExportTimeoutMs = 15000;

        /// <summary>测试隔离：非空时设备树改用 &lt;Override&gt;\Device。</summary>
        public static string RegistryRootOverride = null;
        /// <summary>测试隔离：非空时本地缓存根目录改用该路径。</summary>
        public static string LocalCacheRootOverride = null;
        /// <summary>测试隔离：非空时漫游缓存根目录改用该路径。</summary>
        public static string RoamingCacheRootOverride = null;
        /// <summary>测试隔离：非空时备份根目录改用该路径。</summary>
        public static string BackupRootOverride = null;

        public static string GetDeviceRegistryTree()
        {
            return string.IsNullOrEmpty(RegistryRootOverride) ? DeviceRegistryTree : RegistryRootOverride + @"\Device";
        }

        public static string GetSettingsRegistryTree()
        {
            return string.IsNullOrEmpty(RegistryRootOverride) ? SettingsRegistryTree : RegistryRootOverride + @"\Settings";
        }

        public static string GetLocalCacheRoot()
        {
            if (!string.IsNullOrEmpty(LocalCacheRootOverride)) return LocalCacheRootOverride;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return "";
            return Path.Combine(local, LocalCacheFolderName);
        }

        public static string GetRoamingCacheRoot()
        {
            if (!string.IsNullOrEmpty(RoamingCacheRootOverride)) return RoamingCacheRootOverride;
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(roaming)) return "";
            return Path.Combine(roaming, RoamingCacheFolderName);
        }

        public static string GetBackupRoot()
        {
            if (!string.IsNullOrEmpty(BackupRootOverride)) return BackupRootOverride;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return "";
            return Path.Combine(local, BackupRootFolderName);
        }

        /// <summary>%APPDATA%\FerrisPulse 内需要保留的条目名（当前仅自定义驱动脚本目录）。</summary>
        public static bool IsPreservedRoamingEntry(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return string.Equals(name, PreservedAdaptersFolderName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 执行「清除缓存」：备份 → 清注册表树 → 清图标/运行缓存（保留 adapters）。
        /// 任何单步失败都只记录到报告，不抛出异常，避免半途中断把用户卡在中间态。
        /// </summary>
        public static CacheResetReport Execute()
        {
            CacheResetReport report = new CacheResetReport();

            string backupRoot = GetBackupRoot();
            if (string.IsNullOrEmpty(backupRoot))
            {
                report.Failures.Add("找不到可用的备份目录（LOCALAPPDATA 未定义）");
                return report;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string backupDir = Path.Combine(backupRoot, stamp);
            try
            {
                Directory.CreateDirectory(backupDir);
                report.BackupDirectory = backupDir;
            }
            catch (Exception ex)
            {
                report.Failures.Add("创建备份目录失败: " + ex.Message);
                return report;
            }

            string deviceTree = GetDeviceRegistryTree();
            string settingsTree = GetSettingsRegistryTree();

            // 1. 备份注册表（reg.exe export，HKCU 前缀必须带上，否则导出的是 HKLM 视角）
            BackupRegistryTree(deviceTree, backupDir, "HKCU_RazerBatteryTray.reg", report);
            BackupRegistryTree(settingsTree, backupDir, "HKCU_FerrisPulse.reg", report);

            // 2. 备份图标/标题缓存（体积小，直接复制）
            BackupDirectoryIfExists(GetLocalCacheRoot(), Path.Combine(backupDir, "LocalAppData_FerrisPulse"), report);

            // 3. 清理注册表树
            DeleteRegistryTree(deviceTree, report);
            DeleteRegistryTree(settingsTree, report);

            // 4. 清理 %LOCALAPPDATA%\FerrisPulse（图标与标题缓存整体重建）
            string localRoot = GetLocalCacheRoot();
            if (!string.IsNullOrEmpty(localRoot) && Directory.Exists(localRoot))
            {
                try
                {
                    Directory.Delete(localRoot, true);
                    report.ClearedDirectories.Add(localRoot);
                }
                catch (Exception ex)
                {
                    report.Failures.Add("删除缓存目录失败: " + localRoot + " (" + ex.Message + ")");
                }
            }

            // 5. 清理 %APPDATA%\FerrisPulse 的运行缓存，保留 adapters
            string roamingRoot = GetRoamingCacheRoot();
            if (!string.IsNullOrEmpty(roamingRoot) && Directory.Exists(roamingRoot))
            {
                try
                {
                    string[] entries = Directory.GetFileSystemEntries(roamingRoot);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        string entry = entries[i];
                        string leaf = Path.GetFileName(entry);
                        if (IsPreservedRoamingEntry(leaf))
                        {
                            report.PreservedPaths.Add(entry);
                            continue;
                        }
                        try
                        {
                            if (Directory.Exists(entry)) Directory.Delete(entry, true);
                            else File.Delete(entry);
                            report.ClearedDirectories.Add(entry);
                        }
                        catch (Exception ex)
                        {
                            report.Failures.Add("删除运行缓存失败: " + entry + " (" + ex.Message + ")");
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.Failures.Add("枚举运行缓存失败: " + roamingRoot + " (" + ex.Message + ")");
                }
            }

            // 6. 保留项写清楚，避免用户误以为自启动被清掉
            report.PreservedPaths.Add(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FerrisPulse（自启动项）");

            WriteBackupReadme(backupDir, report);
            return report;
        }

        private static void BackupRegistryTree(string tree, string backupDir, string fileName, CacheResetReport report)
        {
            if (string.IsNullOrEmpty(tree) || string.IsNullOrEmpty(backupDir)) return;
            string target = Path.Combine(backupDir, fileName);
            try
            {
                using (RegistryKey probe = Registry.CurrentUser.OpenSubKey(tree, false))
                {
                    if (probe == null)
                    {
                        // 树本来就不存在：跳过导出，不算失败
                        return;
                    }
                }

                string regExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");
                if (!File.Exists(regExe)) regExe = "reg.exe";

                ProcessStartInfo psi = new ProcessStartInfo(regExe);
                psi.Arguments = string.Format(CultureInfo.InvariantCulture,
                    "export \"HKCU\\{0}\" \"{1}\" /y", tree, target);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        report.Failures.Add("启动 reg.exe 失败: " + tree);
                        return;
                    }
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(RegExportTimeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        report.Failures.Add("导出注册表超时: " + tree);
                        return;
                    }
                }

                if (!File.Exists(target) || new FileInfo(target).Length <= 0)
                {
                    report.Failures.Add("导出注册表为空: " + tree);
                }
            }
            catch (Exception ex)
            {
                report.Failures.Add("导出注册表失败: " + tree + " (" + ex.Message + ")");
            }
        }

        private static void BackupDirectoryIfExists(string source, string target, CacheResetReport report)
        {
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) return;
            try
            {
                CopyDirectory(source, target);
            }
            catch (Exception ex)
            {
                report.Failures.Add("备份缓存目录失败: " + source + " (" + ex.Message + ")");
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                File.Copy(files[i], Path.Combine(target, name), true);
            }
            string[] dirs = Directory.GetDirectories(source);
            for (int i = 0; i < dirs.Length; i++)
            {
                CopyDirectory(dirs[i], Path.Combine(target, Path.GetFileName(dirs[i])));
            }
        }

        private static void DeleteRegistryTree(string tree, CacheResetReport report)
        {
            if (string.IsNullOrEmpty(tree)) return;
            try
            {
                using (RegistryKey probe = Registry.CurrentUser.OpenSubKey(tree, false))
                {
                    if (probe == null) return;
                }
                Registry.CurrentUser.DeleteSubKeyTree(tree, false);
                report.ClearedRegistryTrees.Add(@"HKCU\" + tree);
            }
            catch (Exception ex)
            {
                report.Failures.Add("删除注册表树失败: " + tree + " (" + ex.Message + ")");
            }
        }

        private static void WriteBackupReadme(string backupDir, CacheResetReport report)
        {
            if (string.IsNullOrEmpty(backupDir)) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FerrisPulse · 灵脉 — 清除缓存前自动备份");
                sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("目录内容:");
                sb.AppendLine("  HKCU_RazerBatteryTray.reg  设备识别/电量/自定义/隐藏/优先级数据");
                sb.AppendLine("  HKCU_FerrisPulse.reg       应用设置与设备外观");
                sb.AppendLine("  LocalAppData_FerrisPulse\\ 游戏图标与标题缓存");
                sb.AppendLine();
                sb.AppendLine("如何还原:");
                sb.AppendLine("  1. 先退出 FerrisPulse（含托盘图标）");
                sb.AppendLine("  2. 双击对应的 .reg 文件，选择「是」导入");
                sb.AppendLine("  3. 重新启动 FerrisPulse");
                sb.AppendLine();
                sb.AppendLine("备注:");
                sb.AppendLine("  · adapters\\ 自定义驱动脚本目录未被删除，无需还原");
                sb.AppendLine("  · 自启动项与快捷方式未被移除");
                sb.AppendLine();
                sb.AppendLine("本次执行摘要:");
                sb.AppendLine(report.BuildSummary());
                File.WriteAllText(Path.Combine(backupDir, "README.txt"), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
