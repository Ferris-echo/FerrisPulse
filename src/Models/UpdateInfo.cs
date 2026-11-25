// ============================================================================
// 模块名称: UpdateInfo.cs
// 所属区域: Models (数据模型层)
// 核心职责:
//   表示从云端分发仓库 (version.json) 获取的版本元数据与更新包描述信息
// 依赖关系: 无外部依赖
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public class UpdateInfo
    {
        public string LatestVersion { get; set; }
        public string ReleaseDate { get; set; }
        public string MinSupportedVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public long FileSize { get; set; }
        public List<string> Changelog { get; set; }
        public List<ReleaseNoteEntry> ReleaseHistory { get; set; }

        public UpdateInfo()
        {
            LatestVersion = string.Empty;
            ReleaseDate = string.Empty;
            MinSupportedVersion = string.Empty;
            DownloadUrl = string.Empty;
            Sha256 = string.Empty;
            FileSize = 0;
            Changelog = new List<string>();
            ReleaseHistory = new List<ReleaseNoteEntry>();
        }
    }
}
