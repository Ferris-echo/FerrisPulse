// ============================================================================
// 模块名称: ReleaseNotesService.cs
// 所属区域: Services (后台服务层)
// 核心职责:
//   提供内嵌用户向历代更新日志加载、云端增量同步合并、跨版本区间日志聚合、
//   历史版本直链解析及可执行文件下载双校验（PE 头与 SHA256）
// 依赖关系: ReleaseNoteEntry, UpdateInfo, UpdateManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 3. [零依赖原则]: JSON 解析严格使用轻量状态机，严禁引入外部第三方库。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace RazerBatteryTray
{
    public static class ReleaseNotesService
    {
        public const string VersionBannerPrefix = "@@VERSION@@";
        private const string EmbeddedResourceName = "FerrisPulse.release-notes.json";
        private const long MinValidExecutableBytes = 204800L; // 200 KB

        private static readonly object _cacheLock = new object();
        private static List<ReleaseNoteEntry> _embeddedCache = null;

        public static string FormatVersionBanner(string version, string releaseDate, string badgeText)
        {
            string clean = CleanVer(version);
            string verTitle = string.IsNullOrEmpty(clean) ? "" : ("v" + clean);
            return VersionBannerPrefix + verTitle + "|" + (releaseDate ?? string.Empty) + "|" + (badgeText ?? string.Empty);
        }

        public static string GetReleaseDownloadUrl(string version)
        {
            string clean = CleanVer(version);
            if (string.IsNullOrEmpty(clean)) return string.Empty;
            return "https://gitee.com/Ferris-echo/ferris-pulse/releases/download/v" + clean + "/FerrisPulse.exe";
        }

        public static List<ReleaseNoteEntry> LoadEmbeddedReleaseNotes()
        {
            lock (_cacheLock)
            {
                if (_embeddedCache != null)
                {
                    return CloneList(_embeddedCache);
                }
            }

            List<ReleaseNoteEntry> parsed = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string jsonText = ReadEmbeddedResourceText(asm, EmbeddedResourceName);
                if (string.IsNullOrEmpty(jsonText))
                {
                    string[] names = asm.GetManifestResourceNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (names[i].EndsWith("release-notes.json", StringComparison.OrdinalIgnoreCase))
                        {
                            jsonText = ReadEmbeddedResourceText(asm, names[i]);
                            if (!string.IsNullOrEmpty(jsonText)) break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(jsonText))
                {
                    string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "release-notes.json");
                    if (File.Exists(diskPath))
                    {
                        jsonText = File.ReadAllText(diskPath, Encoding.UTF8);
                    }
                }

                if (!string.IsNullOrEmpty(jsonText))
                {
                    parsed = ParseReleaseNotesJson(jsonText);
                }
            }
            catch { }

            if (parsed == null) parsed = new List<ReleaseNoteEntry>();

            lock (_cacheLock)
            {
                if (_embeddedCache == null && parsed.Count > 0)
                {
                    _embeddedCache = CloneList(parsed);
                }
            }
            return parsed;
        }

        public static string[] GetReleaseNotesEndpoints()
        {
            long ticks = DateTime.UtcNow.Ticks;
            return new string[]
            {
                "https://gitee.com/Ferris-echo/ferris-pulse/raw/main/release-notes.json?_t=" + ticks,
                "https://gcore.jsdelivr.net/gh/Ferris-echo/FerrisPulse@main/release-notes.json?_t=" + ticks,
                "https://ghproxy.net/https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/release-notes.json?_t=" + ticks,
                "https://gh-proxy.com/https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/release-notes.json?_t=" + ticks,
                "https://fastly.jsdelivr.net/gh/Ferris-echo/FerrisPulse@main/release-notes.json?_t=" + ticks,
                "https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/release-notes.json?_t=" + ticks
            };
        }

        public static List<ReleaseNoteEntry> FetchRemoteReleaseNotesSync(int timeoutMs)
        {
            if (timeoutMs <= 0) timeoutMs = 4000;
            EnsureTlsSecurity();

            string[] endpoints = GetReleaseNotesEndpoints();
            for (int i = 0; i < endpoints.Length; i++)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoints[i]);
                    request.Timeout = timeoutMs;
                    request.ReadWriteTimeout = timeoutMs;
                    request.UserAgent = "FerrisPulse-Client";

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode != HttpStatusCode.OK) continue;
                        using (Stream stream = response.GetResponseStream())
                        {
                            if (stream == null) continue;
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                string json = reader.ReadToEnd();
                                List<ReleaseNoteEntry> list = ParseReleaseNotesJson(json);
                                if (list != null && list.Count > 0)
                                {
                                    return list;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        public static void FetchRemoteReleaseNotesAsync(Action<List<ReleaseNoteEntry>> callback)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                List<ReleaseNoteEntry> embedded = LoadEmbeddedReleaseNotes();
                List<ReleaseNoteEntry> remote = null;
                try
                {
                    remote = FetchRemoteReleaseNotesSync(4500);
                }
                catch { }

                List<ReleaseNoteEntry> merged = MergeReleaseNotes(remote, embedded);
                if (callback != null)
                {
                    try { callback(merged); } catch { }
                }
            });
        }

        public static List<ReleaseNoteEntry> MergeReleaseNotes(IList<ReleaseNoteEntry> primary, IList<ReleaseNoteEntry> secondary)
        {
            Dictionary<string, ReleaseNoteEntry> byVersion = new Dictionary<string, ReleaseNoteEntry>(StringComparer.OrdinalIgnoreCase);

            AddUniqueEntries(byVersion, primary);
            AddUniqueEntries(byVersion, secondary);

            List<ReleaseNoteEntry> result = new List<ReleaseNoteEntry>(byVersion.Values);
            SortDescending(result);
            return result;
        }

        public static List<ReleaseNoteEntry> GetReleasesBetween(IList<ReleaseNoteEntry> allReleases, string fromVersionExclusive, string toVersionInclusive)
        {
            List<ReleaseNoteEntry> result = new List<ReleaseNoteEntry>();
            if (allReleases == null || allReleases.Count == 0 || string.IsNullOrEmpty(toVersionInclusive))
            {
                return result;
            }

            bool hasLowerBound = !string.IsNullOrEmpty(CleanVer(fromVersionExclusive));
            for (int i = 0; i < allReleases.Count; i++)
            {
                ReleaseNoteEntry entry = allReleases[i];
                if (entry == null || string.IsNullOrEmpty(entry.Version)) continue;

                int cmpTo = UpdateManager.CompareVersions(entry.Version, toVersionInclusive);
                if (cmpTo > 0) continue;

                if (hasLowerBound)
                {
                    int cmpFrom = UpdateManager.CompareVersions(entry.Version, fromVersionExclusive);
                    if (cmpFrom <= 0) continue;
                }

                result.Add(CloneEntry(entry));
            }

            SortDescending(result);
            return result;
        }

        public static List<string> BuildCumulativeChangelogLines(
            IList<ReleaseNoteEntry> historyPool,
            string fromVersionExclusive,
            UpdateInfo targetInfo,
            out int spannedCount)
        {
            spannedCount = 0;
            List<string> lines = new List<string>();
            if (targetInfo == null)
            {
                return lines;
            }

            string targetVer = CleanVer(targetInfo.LatestVersion);
            string fromVer = CleanVer(fromVersionExclusive);

            // 若缺少起点或目标版本，直接回退为当前版本的单版日志
            if (string.IsNullOrEmpty(targetVer) || string.IsNullOrEmpty(fromVer) ||
                UpdateManager.CompareVersions(targetVer, fromVer) <= 0)
            {
                if (targetInfo.Changelog != null && targetInfo.Changelog.Count > 0)
                {
                    spannedCount = 1;
                    lines.AddRange(targetInfo.Changelog);
                }
                return lines;
            }

            List<ReleaseNoteEntry> matched = GetReleasesBetween(historyPool, fromVer, targetVer);

            // 若远端/内嵌历史中尚未收录目标最新版本（或条目日志为空），用 version.json 的最新数据补齐或合并
            ReleaseNoteEntry targetMatched = null;
            for (int i = 0; i < matched.Count; i++)
            {
                if (UpdateManager.CompareVersions(matched[i].Version, targetVer) == 0)
                {
                    targetMatched = matched[i];
                    break;
                }
            }

            if (targetMatched == null)
            {
                ReleaseNoteEntry synthetic = new ReleaseNoteEntry();
                synthetic.Version = targetVer;
                synthetic.ReleaseDate = targetInfo.ReleaseDate ?? string.Empty;
                if (targetInfo.Changelog != null)
                {
                    synthetic.Changelog.AddRange(targetInfo.Changelog);
                }
                matched.Insert(0, synthetic);
                SortDescending(matched);
            }
            else if ((targetMatched.Changelog == null || targetMatched.Changelog.Count == 0) &&
                     targetInfo.Changelog != null && targetInfo.Changelog.Count > 0)
            {
                targetMatched.Changelog = new List<string>(targetInfo.Changelog);
            }

            if (matched.Count == 0)
            {
                if (targetInfo.Changelog != null && targetInfo.Changelog.Count > 0)
                {
                    spannedCount = 1;
                    lines.AddRange(targetInfo.Changelog);
                }
                return lines;
            }

            if (matched.Count == 1)
            {
                spannedCount = 1;
                if (matched[0].Changelog != null && matched[0].Changelog.Count > 0)
                {
                    lines.AddRange(matched[0].Changelog);
                }
                else if (targetInfo.Changelog != null)
                {
                    lines.AddRange(targetInfo.Changelog);
                }
                return lines;
            }

            spannedCount = matched.Count;
            for (int i = 0; i < matched.Count; i++)
            {
                ReleaseNoteEntry entry = matched[i];
                lines.Add(FormatVersionBanner(entry.Version, entry.ReleaseDate, string.Empty));
                if (entry.Changelog != null && entry.Changelog.Count > 0)
                {
                    for (int j = 0; j < entry.Changelog.Count; j++)
                    {
                        if (!string.IsNullOrEmpty(entry.Changelog[j]))
                        {
                            lines.Add(entry.Changelog[j]);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(entry.Summary))
                {
                    lines.Add(entry.Summary);
                }
            }

            return lines;
        }

        public static bool ValidateDownloadedExecutable(string filePath, string expectedSha256, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    error = "下载文件不存在";
                    return false;
                }

                FileInfo fi = new FileInfo(filePath);
                if (fi.Length < MinValidExecutableBytes)
                {
                    error = "下载文件体积异常，可能被镜像节点拦截";
                    return false;
                }

                byte[] header = new byte[2];
                using (FileStream fs = File.OpenRead(filePath))
                {
                    int read = fs.Read(header, 0, 2);
                    if (read < 2 || header[0] != 0x4D || header[1] != 0x5A)
                    {
                        error = "下载内容并非有效程序包 (PE 头校验失败)";
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(expectedSha256) && expectedSha256.Length == 64 && !expectedSha256.StartsWith("dummy"))
                {
                    string localSha = UpdateManager.ComputeFileSha256(filePath);
                    if (!string.Equals(localSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "更新包哈希不匹配 (已尝试自愈切换备用镜像源)";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static List<ReleaseNoteEntry> ParseReleaseNotesJson(string jsonText)
        {
            List<ReleaseNoteEntry> entries = new List<ReleaseNoteEntry>();
            if (string.IsNullOrEmpty(jsonText)) return entries;

            try
            {
                int idx = 0;
                int len = jsonText.Length;
                int releasesArrayStart = -1;

                while (idx < len)
                {
                    if (jsonText[idx] == '"')
                    {
                        string key = ReadJsonString(jsonText, ref idx);
                        SkipWhitespace(jsonText, ref idx);
                        if (idx < len && jsonText[idx] == ':')
                        {
                            idx++;
                            SkipWhitespace(jsonText, ref idx);
                            if (string.Equals(key, "releases", StringComparison.OrdinalIgnoreCase) && idx < len && jsonText[idx] == '[')
                            {
                                releasesArrayStart = idx;
                                break;
                            }
                        }
                    }
                    else
                    {
                        idx++;
                    }
                }

                if (releasesArrayStart < 0) return entries;

                int releasesArrayEnd = FindBalancedEnd(jsonText, releasesArrayStart, '[', ']');
                if (releasesArrayEnd <= releasesArrayStart) return entries;

                int pos = releasesArrayStart + 1;
                while (pos < releasesArrayEnd)
                {
                    SkipWhitespace(jsonText, ref pos);
                    if (pos >= releasesArrayEnd) break;

                    if (jsonText[pos] == '{')
                    {
                        int objEnd = FindBalancedEnd(jsonText, pos, '{', '}');
                        if (objEnd <= pos) break;

                        ReleaseNoteEntry entry = ParseReleaseObject(jsonText, pos, objEnd);
                        if (entry != null && !string.IsNullOrEmpty(entry.GetCleanVersion()))
                        {
                            if (string.IsNullOrEmpty(entry.DownloadUrl))
                            {
                                entry.DownloadUrl = GetReleaseDownloadUrl(entry.Version);
                            }
                            entries.Add(entry);
                        }
                        pos = objEnd + 1;
                    }
                    else
                    {
                        pos++;
                    }
                }

                SortDescending(entries);
            }
            catch { }

            return entries;
        }

        private static ReleaseNoteEntry ParseReleaseObject(string s, int startIdx, int endIdx)
        {
            ReleaseNoteEntry entry = new ReleaseNoteEntry();
            int idx = startIdx + 1;

            while (idx < endIdx)
            {
                SkipWhitespace(s, ref idx);
                if (idx >= endIdx) break;

                if (s[idx] == ',' || s[idx] == '}')
                {
                    idx++;
                    continue;
                }

                if (s[idx] != '"')
                {
                    idx++;
                    continue;
                }

                string key = ReadJsonString(s, ref idx);
                SkipWhitespace(s, ref idx);
                if (idx >= endIdx || s[idx] != ':') continue;
                idx++;
                SkipWhitespace(s, ref idx);
                if (idx >= endIdx) break;

                char valStart = s[idx];
                if (valStart == '"')
                {
                    string val = ReadJsonString(s, ref idx);
                    if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase)) entry.Version = CleanVer(val);
                    else if (string.Equals(key, "releaseDate", StringComparison.OrdinalIgnoreCase)) entry.ReleaseDate = val;
                    else if (string.Equals(key, "summary", StringComparison.OrdinalIgnoreCase)) entry.Summary = val;
                    else if (string.Equals(key, "sha256", StringComparison.OrdinalIgnoreCase)) entry.Sha256 = val;
                    else if (string.Equals(key, "downloadUrl", StringComparison.OrdinalIgnoreCase)) entry.DownloadUrl = val;
                }
                else if (valStart == '[')
                {
                    int arrEnd = FindBalancedEnd(s, idx, '[', ']');
                    if (arrEnd > idx)
                    {
                        if (string.Equals(key, "changelog", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Changelog = ReadStringArray(s, idx, arrEnd);
                        }
                        idx = arrEnd + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (valStart == '{')
                {
                    int nestedEnd = FindBalancedEnd(s, idx, '{', '}');
                    idx = (nestedEnd > idx) ? (nestedEnd + 1) : (idx + 1);
                }
                else
                {
                    while (idx < endIdx && s[idx] != ',' && s[idx] != '}') idx++;
                }
            }

            return entry;
        }

        private static List<string> ReadStringArray(string s, int startIdx, int endIdx)
        {
            List<string> items = new List<string>();
            int idx = startIdx + 1;
            while (idx < endIdx)
            {
                SkipWhitespace(s, ref idx);
                if (idx >= endIdx) break;
                if (s[idx] == '"')
                {
                    string val = ReadJsonString(s, ref idx);
                    if (!string.IsNullOrEmpty(val))
                    {
                        items.Add(val);
                    }
                }
                else if (s[idx] == '{')
                {
                    int objEnd = FindBalancedEnd(s, idx, '{', '}');
                    idx = (objEnd > idx) ? (objEnd + 1) : (idx + 1);
                }
                else
                {
                    idx++;
                }
            }
            return items;
        }

        private static string ReadJsonString(string s, ref int idx)
        {
            if (idx >= s.Length || s[idx] != '"') return string.Empty;
            idx++; // skip opening quote

            StringBuilder sb = new StringBuilder();
            while (idx < s.Length)
            {
                char ch = s[idx++];
                if (ch == '"')
                {
                    return sb.ToString();
                }
                if (ch == '\\' && idx < s.Length)
                {
                    char esc = s[idx++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (idx + 4 <= s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(idx, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    idx += 4;
                                }
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private static int FindBalancedEnd(string s, int startIdx, char openChar, char closeChar)
        {
            if (startIdx < 0 || startIdx >= s.Length || s[startIdx] != openChar) return -1;

            int depth = 0;
            bool inString = false;
            for (int i = startIdx; i < s.Length; i++)
            {
                char ch = s[i];
                if (inString)
                {
                    if (ch == '\\')
                    {
                        i++; // skip escaped character
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == openChar)
                {
                    depth++;
                }
                else if (ch == closeChar)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static void SkipWhitespace(string s, ref int idx)
        {
            while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;
        }

        private static string ReadEmbeddedResourceText(Assembly asm, string resourceName)
        {
            try
            {
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return string.Empty;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AddUniqueEntries(Dictionary<string, ReleaseNoteEntry> dict, IList<ReleaseNoteEntry> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                ReleaseNoteEntry entry = source[i];
                if (entry == null) continue;
                string key = entry.GetCleanVersion();
                if (string.IsNullOrEmpty(key)) continue;

                if (!dict.ContainsKey(key))
                {
                    dict[key] = CloneEntry(entry);
                }
            }
        }

        private static void SortDescending(List<ReleaseNoteEntry> list)
        {
            if (list == null || list.Count <= 1) return;
            list.Sort(delegate(ReleaseNoteEntry a, ReleaseNoteEntry b)
            {
                string va = a != null ? a.Version : "";
                string vb = b != null ? b.Version : "";
                return UpdateManager.CompareVersions(vb, va);
            });
        }

        private static List<ReleaseNoteEntry> CloneList(IList<ReleaseNoteEntry> source)
        {
            List<ReleaseNoteEntry> copy = new List<ReleaseNoteEntry>();
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null) copy.Add(CloneEntry(source[i]));
            }
            return copy;
        }

        private static ReleaseNoteEntry CloneEntry(ReleaseNoteEntry src)
        {
            ReleaseNoteEntry clone = new ReleaseNoteEntry();
            clone.Version = src.Version ?? string.Empty;
            clone.ReleaseDate = src.ReleaseDate ?? string.Empty;
            clone.Summary = src.Summary ?? string.Empty;
            clone.Sha256 = src.Sha256 ?? string.Empty;
            clone.DownloadUrl = string.IsNullOrEmpty(src.DownloadUrl) ? GetReleaseDownloadUrl(src.Version) : src.DownloadUrl;
            if (src.Changelog != null)
            {
                clone.Changelog.AddRange(src.Changelog);
            }
            return clone;
        }

        private static string CleanVer(string version)
        {
            if (string.IsNullOrEmpty(version)) return string.Empty;
            string clean = version.Trim().TrimStart('v', 'V');
            int sp = clean.IndexOf(' ');
            if (sp > 0) clean = clean.Substring(0, sp);
            return clean;
        }

        private static void EnsureTlsSecurity()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls;
            }
            catch { }
        }
    }
}
