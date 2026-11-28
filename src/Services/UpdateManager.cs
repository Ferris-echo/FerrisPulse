// ============================================================================
// 模块名称: UpdateManager.cs
// 所属区域: Services (后台服务层)
// 核心职责:
//   提供云端版本探测、容灾镜像故障转移、SemVer 版本比对、文件流式下载与 SHA256 校验
//   自更新采用 TLS 1.2 严格校验证书（U1）；原位替换前后用 UpdatePending 标记支撑自动回滚（U2）
// 依赖关系: UpdateInfo, ConfigManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 3. [零依赖原则]: JSON 解析与网络通信严禁引入外部第三方 NuGet 包。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public static class UpdateManager
    {
        // [U1] 显式启用 TLS 1.2 / 1.3，且【禁止】再设置进程级 ServerCertificateValidationCallback：
        // 版本检查、release-notes 与安装包下载全走 HTTPS，sha256 与 downloadUrl 又来自同一个响应，
        // 一旦放行任意证书，中间人可同时伪造包与校验值，校验形同虚设。
        private static void EnsureTlsSecurity()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch { }
        }

        public static string CurrentVersion
        {
            get
            {
                try
                {
                    Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (v != null && v.Major > 0)
                    {
                        return "v" + v.Major + "." + v.Minor + "." + v.Build;
                    }
                }
                catch { }
                return "v2.0.0";
            }
        }


        private static UpdateInfo _pendingUpdate = null;
        private static readonly object _pendingLock = new object();
        private static bool _hasCompletedCheck = false;
        private static bool _lastCheckSucceeded = false;
        public static event Action OnPendingUpdateChanged;

        public static bool HasCompletedCheck
        {
            get { lock (_pendingLock) { return _hasCompletedCheck; } }
            private set { lock (_pendingLock) { _hasCompletedCheck = value; } }
        }

        public static bool LastCheckSucceeded
        {
            get { lock (_pendingLock) { return _lastCheckSucceeded; } }
            private set { lock (_pendingLock) { _lastCheckSucceeded = value; } }
        }

        public static void NotifyUpdateStateChanged()
        {
            if (OnPendingUpdateChanged != null)
            {
                try { OnPendingUpdateChanged(); } catch { }
            }
        }

        public static UpdateInfo PendingUpdate
        {
            get
            {
                lock (_pendingLock) { return _pendingUpdate; }
            }
            set
            {
                bool changed = false;
                lock (_pendingLock)
                {
                    if (_pendingUpdate != value)
                    {
                        _pendingUpdate = value;
                        changed = true;
                    }
                }
                if (changed && OnPendingUpdateChanged != null)
                {
                    try { OnPendingUpdateChanged(); } catch { }
                }
            }
        }

        public static void ClearPendingUpdate()
        {
            PendingUpdate = null;
        }

        public static string[] GetUpdateEndpoints()
        {
            long ticks = DateTime.UtcNow.Ticks;
            return new string[]
            {
                // 1. Gitee 权威国内直连源 (国内 100% 极速直连，双账号容灾备选)
                "https://gitee.com/feitian-zhang-fei/ferris-pulse/raw/main/version.json?_t=" + ticks,
                "https://gitee.com/Ferris-echo/ferris-pulse/raw/main/version.json?_t=" + ticks,
                "https://gitee.com/feitian-zhang-fei/FerrisPulse/raw/main/version.json?_t=" + ticks,
                "https://gitee.com/Ferris-echo/FerrisPulse/raw/main/version.json?_t=" + ticks,
                "https://gitee.com/feitian-zhang-fei/RazerBatteryTray/raw/main/version.json?_t=" + ticks,
                "https://gitee.com/Ferris-echo/RazerBatteryTray/raw/main/version.json?_t=" + ticks,

                // 2. GCore 全球优质 CDN (实测国内 1.6 秒极速直连)
                "https://gcore.jsdelivr.net/gh/Ferris-echo/FerrisPulse@main/version.json?_t=" + ticks,
                "https://gcore.jsdelivr.net/gh/Ferris-echo/RazerBatteryTray@main/version.json?_t=" + ticks,

                // 3. 国内专用 GitHub 高速镜像代理
                "https://ghproxy.net/https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/version.json?_t=" + ticks,
                "https://gh-proxy.com/https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/version.json?_t=" + ticks,
                "https://ghproxy.net/https://raw.githubusercontent.com/Ferris-echo/RazerBatteryTray/main/version.json?_t=" + ticks,
                "https://gh-proxy.com/https://raw.githubusercontent.com/Ferris-echo/RazerBatteryTray/main/version.json?_t=" + ticks,

                // 4. Fastly CDN 与 原版 GitHub (海外直连保底)
                "https://fastly.jsdelivr.net/gh/Ferris-echo/FerrisPulse@main/version.json?_t=" + ticks,
                "https://cdn.jsdelivr.net/gh/Ferris-echo/FerrisPulse@main/version.json?_t=" + ticks,
                "https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/version.json?_t=" + ticks,
                "https://raw.githubusercontent.com/Ferris-echo/RazerBatteryTray/main/version.json?_t=" + ticks
            };
        }

        public static int CompareVersions(string v1, string v2)
        {
            if (string.IsNullOrEmpty(v1) && string.IsNullOrEmpty(v2)) return 0;
            if (string.IsNullOrEmpty(v1)) return -1;
            if (string.IsNullOrEmpty(v2)) return 1;

            string clean1 = v1.Trim().TrimStart('v', 'V');
            string clean2 = v2.Trim().TrimStart('v', 'V');

            string[] parts1 = clean1.Split('.');
            string[] parts2 = clean2.Split('.');

            int maxLen = Math.Max(parts1.Length, parts2.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int num1 = 0;
                int num2 = 0;

                if (i < parts1.Length)
                {
                    int.TryParse(parts1[i], out num1);
                }
                if (i < parts2.Length)
                {
                    int.TryParse(parts2[i], out num2);
                }

                if (num1 > num2) return 1;
                if (num1 < num2) return -1;
            }

            return 0;
        }

        public static bool IsNewerVersion(string remoteVersion, string localVersion)
        {
            return CompareVersions(remoteVersion, localVersion) > 0;
        }

        public static UpdateInfo ParseVersionJson(string jsonText)
        {
            if (string.IsNullOrEmpty(jsonText)) return null;

            try
            {
                UpdateInfo info = new UpdateInfo();

                info.LatestVersion = ExtractJsonString(jsonText, "latestVersion");
                info.ReleaseDate = ExtractJsonString(jsonText, "releaseDate");
                info.MinSupportedVersion = ExtractJsonString(jsonText, "minSupportedVersion");
                info.DownloadUrl = ExtractJsonString(jsonText, "downloadUrl");
                info.Sha256 = ExtractJsonString(jsonText, "sha256");

                string sizeStr = ExtractJsonNumber(jsonText, "fileSize");
                long sizeVal;
                if (long.TryParse(sizeStr, out sizeVal))
                {
                    info.FileSize = sizeVal;
                }

                info.Changelog = ExtractJsonStringArray(jsonText, "changelog");

                return info;
            }
            catch
            {
                return null;
            }
        }

        public static string ComputeFileSha256(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            using (SHA256 sha = SHA256.Create())
            {
                using (FileStream stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha.ComputeHash(stream);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        sb.Append(hashBytes[i].ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
        }

        public static void CheckUpdateAsync(Action<bool, UpdateInfo, string> callback)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                EnsureTlsSecurity();

                string[] endpoints = GetUpdateEndpoints();
                string lastError = "无法连接到更新服务器";
                UpdateInfo bestInfo = null;

                for (int i = 0; i < endpoints.Length; i++)
                {
                    string url = endpoints[i];
                    try
                    {
                        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                        request.Timeout = 6000;
                        request.ReadWriteTimeout = 6000;
                        request.UserAgent = "FerrisPulse-Client";

                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        {
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                using (Stream stream = response.GetResponseStream())
                                {
                                    if (stream != null)
                                    {
                                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                                        {
                                            string json = reader.ReadToEnd();
                                            UpdateInfo info = ParseVersionJson(json);
                                            if (info != null && !string.IsNullOrEmpty(info.LatestVersion))
                                            {
                                                // 若发现有比当前版本更新的候选，立即采纳并挂载历史发布记录以支持跨版本汇总日志
                                                if (IsNewerVersion(info.LatestVersion, CurrentVersion))
                                                {
                                                    try
                                                    {
                                                        List<ReleaseNoteEntry> remoteHist = ReleaseNotesService.FetchRemoteReleaseNotesSync(3500);
                                                        info.ReleaseHistory = ReleaseNotesService.MergeReleaseNotes(remoteHist, ReleaseNotesService.LoadEmbeddedReleaseNotes());
                                                    }
                                                    catch { }

                                                    HasCompletedCheck = true;
                                                    LastCheckSucceeded = true;
                                                    PendingUpdate = info;
                                                    if (callback != null)
                                                    {
                                                        callback(true, info, null);
                                                    }
                                                    return;
                                                }

                                                bestInfo = info;
                                                // 权威首选源（Gitee 直连）已成功响应且明确版本，无需再降级遍历后续节点
                                                if (i == 0)
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (WebException wex)
                    {
                        lastError = wex.Message;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }

                // 所有端点探测完毕：如果获得了有效响应（虽然不比本地新），返回该最新已知信息
                if (bestInfo != null)
                {
                    HasCompletedCheck = true;
                    LastCheckSucceeded = true;
                    if (IsNewerVersion(bestInfo.LatestVersion, CurrentVersion))
                    {
                        PendingUpdate = bestInfo;
                    }
                    else
                    {
                        ClearPendingUpdate();
                        NotifyUpdateStateChanged();
                    }

                    if (callback != null)
                    {
                        callback(true, bestInfo, null);
                    }
                    return;
                }

                HasCompletedCheck = true;
                LastCheckSucceeded = false;
                NotifyUpdateStateChanged();

                if (callback != null)
                {
                    callback(false, null, lastError);
                }
            });
        }

        public static void DownloadFileAsync(string url, string targetPath, Action<long, long, double> progressCallback, Action<bool, string> completedCallback)
        {
            DownloadFileAsync(url, targetPath, null, progressCallback, completedCallback);
        }

        public static void DownloadFileAsync(string url, string targetPath, string expectedSha256, Action<long, long, double> progressCallback, Action<bool, string> completedCallback)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                EnsureTlsSecurity();

                // 如果是 github.com 或 gitee.com 的下载链，构造高质量双平台互备与国内极速直连源
                List<string> candidateUrls = new List<string>();
                if (url.Contains("github.com/Ferris-echo/FerrisPulse/releases/download/"))
                {
                    candidateUrls.Add(url.Replace("github.com/Ferris-echo/FerrisPulse", "gitee.com/Ferris-echo/ferris-pulse"));
                }
                if (url.Contains("github.com/"))
                {
                    candidateUrls.Add("https://ghproxy.net/" + url);
                    candidateUrls.Add("https://gh-proxy.com/" + url);
                    candidateUrls.Add("https://ghfast.top/" + url);
                }
                if (url.Contains("gitee.com/Ferris-echo/ferris-pulse/releases/download/"))
                {
                    candidateUrls.Add(url);
                    string ghUrl = url.Replace("gitee.com/Ferris-echo/ferris-pulse", "github.com/Ferris-echo/FerrisPulse");
                    candidateUrls.Add("https://ghproxy.net/" + ghUrl);
                    candidateUrls.Add("https://gh-proxy.com/" + ghUrl);
                    candidateUrls.Add(ghUrl);
                }
                if (!candidateUrls.Contains(url))
                {
                    candidateUrls.Add(url);
                }

                string lastError = string.Empty;

                for (int c = 0; c < candidateUrls.Count; c++)
                {
                    string candidateUrl = candidateUrls[c];
                    FileStream fs = null;
                    try
                    {
                        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(candidateUrl);
                        request.Timeout = 15000;
                        request.ReadWriteTimeout = 30000;
                        request.UserAgent = "FerrisPulse-Downloader";

                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                            {
                                continue;
                            }

                            long totalBytes = response.ContentLength;
                            long totalBytesRead = 0;

                            string dir = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                            byte[] buffer = new byte[32768];

                            using (Stream stream = response.GetResponseStream())
                            {
                                if (stream == null) continue;

                                Stopwatch sw = Stopwatch.StartNew();
                                long lastReportBytes = 0;
                                long lastReportTimeMs = 0;
                                double currentSpeed = 0;

                                int bytesRead;
                                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    fs.Write(buffer, 0, bytesRead);
                                    totalBytesRead += bytesRead;

                                    long elapsedMs = sw.ElapsedMilliseconds;
                                    if (elapsedMs - lastReportTimeMs >= 300 || totalBytesRead == totalBytes)
                                    {
                                        long bytesDiff = totalBytesRead - lastReportBytes;
                                        long timeDiff = elapsedMs - lastReportTimeMs;
                                        if (timeDiff > 0)
                                        {
                                            currentSpeed = (bytesDiff * 1000.0) / timeDiff;
                                        }

                                        lastReportBytes = totalBytesRead;
                                        lastReportTimeMs = elapsedMs;

                                        if (progressCallback != null)
                                        {
                                            progressCallback(totalBytesRead, totalBytes, currentSpeed);
                                        }
                                    }
                                }
                            }

                            fs.Flush();
                            fs.Close();
                            fs = null;

                            // 校验可信可执行文件头 (PE MZ)、最小有效体积以及预期 SHA256 哈希；异常时自愈切换下一个备用镜像源
                            string validationErr;
                            if (!ReleaseNotesService.ValidateDownloadedExecutable(targetPath, expectedSha256, out validationErr))
                            {
                                lastError = string.IsNullOrEmpty(validationErr) ? "更新包校验失败 (已尝试自愈切换备用镜像源)" : validationErr;
                                try { File.Delete(targetPath); } catch { }
                                continue;
                            }

                            if (completedCallback != null)
                            {
                                completedCallback(true, null);
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        if (fs != null)
                        {
                            try { fs.Close(); } catch { }
                            fs = null;
                        }
                        if (File.Exists(targetPath))
                        {
                            try { File.Delete(targetPath); } catch { }
                        }
                    }
                }

                if (completedCallback != null)
                {
                    completedCallback(false, string.IsNullOrEmpty(lastError) ? "文件下载失败" : lastError);
                }
            });
        }

        // ===== [U2 原位替换回滚] =====
        // 更新是"一次性不可逆动作"：新 exe 能启动但随即崩溃/卡死时，用户机器上只剩坏版本。
        // 这里引入两阶段状态机标记：
        //   1. 替换前 LaunchSelfReplace 写 UpdatePending = "staged|<ver>|<time>"；
        //   2. 新版首次启动进入 TryRollbackPendingUpdate() 见到 staged| → 翻转为 "booting|..." 并放行进入主循环；
        //   3. MainForm.OnLoad 走完确认健康后调用 ConfirmUpdateHealthy() 清标记并删除 .old；
        //   4. 若第 2 步启动后在中途崩溃未走到 OnLoad，下次启动见到 "booting|..." 且 .old 存在 → 触发自动回滚。
        private const string UpdateStateRegPath = @"Software\FerrisPulse";
        private const string UpdatePendingValName = "UpdatePending";
        private const string UpdatePendingStageStaged = "staged";
        private const string UpdatePendingStageBooting = "booting";

        private static string GetUpdatePendingRaw()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UpdateStateRegPath, false))
                {
                    if (key == null) return null;
                    object v = key.GetValue(UpdatePendingValName);
                    return v != null ? v.ToString() : null;
                }
            }
            catch { }
            return null;
        }

        private static void SetUpdatePendingRaw(string value)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UpdateStateRegPath))
                {
                    if (key != null)
                    {
                        key.SetValue(UpdatePendingValName, value ?? "");
                    }
                }
            }
            catch { }
        }

        public static bool HasPendingUpdate()
        {
            string raw = GetUpdatePendingRaw();
            return !string.IsNullOrEmpty(raw);
        }

        /// <summary>替换前写标记：首阶段为 staged，记录目标版本与时间。</summary>
        public static void MarkUpdatePending(string targetVersion)
        {
            SetUpdatePendingRaw(
                UpdatePendingStageStaged + "|" +
                (targetVersion == null ? "" : targetVersion) + "|" +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private static void ClearUpdatePendingMarker()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UpdateStateRegPath, true))
                {
                    if (key != null) key.DeleteValue(UpdatePendingValName, false);
                }
            }
            catch { }
        }

        /// <summary>
        /// 新版本"活着走到主循环"的确认：清标记并删掉唯一的退路 .old。
        /// </summary>
        public static void ConfirmUpdateHealthy()
        {
            if (!HasPendingUpdate()) return;
            ClearUpdatePendingMarker();
            try
            {
                string oldExe = Application.ExecutablePath + ".old";
                if (File.Exists(oldExe)) File.Delete(oldExe);
            }
            catch { }
        }

        /// <summary>
        /// 启动期回滚：上次启动进入 booting 后未走到主循环即崩溃，本轮自动回滚到 .old 并拉起旧版。
        /// 对替换后的第一次启动（staged|...），仅将标记推进到 booting|... 并返回 false 放行启动，避免误杀健康新版。
        /// </summary>
        public static bool TryRollbackPendingUpdate()
        {
            string raw = GetUpdatePendingRaw();
            if (string.IsNullOrEmpty(raw)) return false;

            if (raw.StartsWith(UpdatePendingStageStaged + "|", StringComparison.OrdinalIgnoreCase))
            {
                SetUpdatePendingRaw(UpdatePendingStageBooting + raw.Substring(UpdatePendingStageStaged.Length));
                return false;
            }

            try
            {
                string exePath = Application.ExecutablePath;
                string oldExe = exePath + ".old";
                if (!File.Exists(oldExe))
                {
                    // 没有退路可用：清掉标记，避免每次启动都做无意义的重试
                    ClearUpdatePendingMarker();
                    return false;
                }

                string rollback = exePath + ".rollback";
                string failed = exePath + ".failed";
                try { if (File.Exists(rollback)) File.Delete(rollback); } catch { }
                try { if (File.Exists(failed)) File.Delete(failed); } catch { }

                File.Copy(oldExe, rollback, true);
                File.Move(exePath, failed);
                try
                {
                    File.Move(rollback, exePath);
                }
                catch
                {
                    // 原子替换失败：把坏版本放回原位，保留 .old 与 .failed 供人工处理
                    try { File.Move(failed, exePath); } catch { }
                    try { if (File.Exists(rollback)) File.Delete(rollback); } catch { }
                    return false;
                }

                // 回滚成功：保留 .failed 一份便于取证，清标记后拉起旧版
                ClearUpdatePendingMarker();
                try { Process.Start(exePath, "--rolledback"); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool LaunchSelfReplace(string updateFilePath)
        {
            try
            {
                if (!File.Exists(updateFilePath)) return false;

                string exePath = Application.ExecutablePath;
                string oldExe = exePath + ".old";

                // 核心安全策略：全程采用 Windows 原生运行时重命名机制 (In-Process Native Rename)
                // 在 Windows NTFS 下，运行中的 exe 支持直接重命名（MoveFile），随后将新 exe 移入原路径并拉起。
                // 全程为纯 C# 托管操作，严禁包含或拉起隐藏 PowerShell/CMD 脚本，彻底杜绝 AMSI 与 Dropper 杀软误报。
                try
                {
                    if (File.Exists(oldExe))
                    {
                        try { File.Delete(oldExe); } catch { }
                    }
                    // [U2] 先落 pending 标记：新版本若起不来，下次启动据此自动回滚
                    MarkUpdatePending(null);
                    File.Move(exePath, oldExe);
                    File.Move(updateFilePath, exePath);

                    Process.Start(exePath, "--updated");
                    Application.Exit();
                    return true;
                }
                catch
                {
                    // 仅当目录只读或非 NTFS 文件系统导致原位替换受限时，打开新版本所在目录引导手动替换
                    try
                    {
                        Process.Start("explorer.exe", "/select,\"" + updateFilePath + "\"");
                    }
                    catch { }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        #region JSON 解析辅助方法 (轻量零依赖)

        private static string ExtractJsonString(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return string.Empty;
        }

        private static string ExtractJsonNumber(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*([0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "0";
        }

        private static List<string> ExtractJsonStringArray(string json, string key)
        {
            List<string> list = new List<string>();
            Match arrayMatch = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (arrayMatch.Success)
            {
                string arrayContent = arrayMatch.Groups[1].Value;
                MatchCollection itemMatches = Regex.Matches(arrayContent, "\"([^\"]*)\"");
                foreach (Match m in itemMatches)
                {
                    string raw = m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    if (raw.Contains("【") && raw.IndexOf("【") != raw.LastIndexOf("【"))
                    {
                        string[] subItems = Regex.Split(raw, @"(?=【)");
                        foreach (string sub in subItems)
                        {
                            string cleanSub = sub.Trim().TrimEnd(',', '，', ';', '；');
                            if (!string.IsNullOrEmpty(cleanSub))
                            {
                                list.Add(cleanSub);
                            }
                        }
                    }
                    else
                    {
                        list.Add(raw);
                    }
                }
            }
            return list;
        }

        #endregion
    }
}
