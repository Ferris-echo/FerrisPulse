// ============================================================================
// 模块名称: ReleaseNoteEntry.cs
// 所属区域: Models (数据模型层)
// 核心职责:
//   表示单个软件版本的精简用户向更新日志条目及对应安装包校验/下载元数据
// 依赖关系: 无外部依赖
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System.Collections.Generic;

namespace RazerBatteryTray
{
    public class ReleaseNoteEntry
    {
        public string Version { get; set; }
        public string ReleaseDate { get; set; }
        public string Summary { get; set; }
        public string Sha256 { get; set; }
        public string DownloadUrl { get; set; }
        public List<string> Changelog { get; set; }

        public ReleaseNoteEntry()
        {
            Version = string.Empty;
            ReleaseDate = string.Empty;
            Summary = string.Empty;
            Sha256 = string.Empty;
            DownloadUrl = string.Empty;
            Changelog = new List<string>();
        }

        public string GetCleanVersion()
        {
            if (string.IsNullOrEmpty(Version)) return string.Empty;
            string clean = Version.Trim().TrimStart('v', 'V');
            int sp = clean.IndexOf(' ');
            if (sp > 0) clean = clean.Substring(0, sp);
            return clean;
        }
    }
}
