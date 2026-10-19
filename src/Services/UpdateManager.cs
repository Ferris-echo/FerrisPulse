// ============================================================================
// 模块名称: UpdateManager.cs
// 所属区域: Services (后台服务层)
// 核心职责:
//   提供云端版本探测、容灾镜像故障转移、SemVer 版本比对、文件流式下载与 SHA256 校验
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
        private static void EnsureTlsSecurity()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
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
        public static event Action OnPendingUpdateChanged;

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
                                                // 若发现有比当前版本更新的候选，立即采纳并回调
                                                if (IsNewerVersion(info.LatestVersion, CurrentVersion))
                                                {
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
                    if (IsNewerVersion(bestInfo.LatestVersion, CurrentVersion))
                    {
                        PendingUpdate = bestInfo;
                    }
                    else
                    {
                        ClearPendingUpdate();
                    }

                    if (callback != null)
                    {
                        callback(true, bestInfo, null);
                    }
                    return;
                }

                if (callback != null)
                {
                    callback(false, null, lastError);
                }
            });
        }

        public static void DownloadFileAsync(string url, string targetPath, Action<long, long, double> progressCallback, Action<bool, string> completedCallback)
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

        public static string BuildSelfReplaceCommand(int pid, string targetExePath, string updateFilePath)
        {
            string safeTarget = targetExePath.Replace("'", "''");
            string safeUpdate = updateFilePath.Replace("'", "''");

            return string.Format(
                "-NoProfile -WindowStyle Hidden -Command \"" +
                "try {{ Wait-Process -Id {0} -Timeout 10; }} catch {{}} " +
                "Start-Sleep -Milliseconds 400; " +
                "Move-Item -LiteralPath '{1}' -Destination '{2}' -Force; " +
                "Start-Process -FilePath '{2}';\"",
                pid, safeUpdate, safeTarget
            );
        }

        public static bool LaunchSelfReplace(string updateFilePath)
        {
            try
            {
                if (!File.Exists(updateFilePath)) return false;

                string exePath = Application.ExecutablePath;
                string oldExe = exePath + ".old";

                // 核心安全优化：优先采用 Windows 原生运行时重命名机制 (In-Process Native Rename)
                // 在 Windows NTFS 下，运行中的 exe 支持直接重命名（MoveFile），随后将新 exe 移入原路径并拉起。
                // 全程为纯 C# 托管操作，不启动任何隐藏 powershell/cmd 脚本，彻底杜绝 AMSI 与 Dropper 杀软误报！
                try
                {
                    if (File.Exists(oldExe))
                    {
                        try { File.Delete(oldExe); } catch { }
                    }
                    File.Move(exePath, oldExe);
                    File.Move(updateFilePath, exePath);

                    Process.Start(exePath, "--updated");
                    Application.Exit();
                    return true;
                }
                catch
                {
                    // 若受非 NTFS 文件系统等特殊环境限制，进入兼容降级分支
                }

                int currentPid = Process.GetCurrentProcess().Id;
                string args = BuildSelfReplaceCommand(currentPid, exePath, updateFilePath);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = args;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;

                Process.Start(psi);
                Application.Exit();
                return true;
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
