// ============================================================================
// 模块名称: GameProfile.cs
// 所属区域: Models (领域模型层)
// 核心职责:
//   电竞独立游戏专属硬件性能配置策略模型：
//   1. 记录指定游戏 EXE 的专属 DPI 档位（1~5 档，0 表示跟随主界面默认电竞 DPI）
//   2. 记录指定游戏 EXE 的专属回报率（125..8000 Hz，0 表示跟随主界面默认电竞回报率）
//   3. 记录可选的本地 EXE 绝对路径，协助离线状态下的高清图标精准提取
//   4. 记录游戏友好运行时名称 (FriendlyTitle，如 PUBG)，确保退出游戏后依然稳定展示
//   5. 提供高效紧凑的注册表字符串序列化 (ToConfigString/Parse) 与集合批量持久化
// 依赖关系: 无外部依赖，仅使用 System 与 System.Text
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或序列化格式时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace RazerBatteryTray
{
    public class GameProfile
    {
        public string ProcessName { get; set; }
        public int CustomDpiStage { get; set; }
        public int CustomPollingRate { get; set; }
        public string ExePath { get; set; }
        public string FriendlyTitle { get; set; }

        public GameProfile()
        {
            this.ProcessName = "";
            this.CustomDpiStage = 0;
            this.CustomPollingRate = 0;
            this.ExePath = "";
            this.FriendlyTitle = "";
        }

        public GameProfile(string processName) : this(processName, 0, 0, "", "")
        {
        }

        public GameProfile(string processName, int dpiStage, int pollingRate, string exePath)
            : this(processName, dpiStage, pollingRate, exePath, "")
        {
        }

        public GameProfile(string processName, int dpiStage, int pollingRate, string exePath, string friendlyTitle)
        {
            this.ProcessName = processName != null ? processName : "";
            this.CustomDpiStage = dpiStage;
            this.CustomPollingRate = pollingRate;
            this.ExePath = exePath != null ? exePath : "";
            this.FriendlyTitle = friendlyTitle != null ? friendlyTitle : "";
        }

        public bool IsCustomConfigured
        {
            get { return CustomDpiStage > 0 || CustomPollingRate > 0; }
        }

        public string ToConfigString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(ProcessName != null ? ProcessName.ToLowerInvariant() : "");
            sb.Append(":dpi=").Append(CustomDpiStage);
            sb.Append(",rate=").Append(CustomPollingRate);
            if (!string.IsNullOrEmpty(ExePath))
            {
                try
                {
                    byte[] pathBytes = Encoding.UTF8.GetBytes(ExePath);
                    sb.Append(",path=").Append(Convert.ToBase64String(pathBytes));
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(FriendlyTitle))
            {
                try
                {
                    byte[] titleBytes = Encoding.UTF8.GetBytes(FriendlyTitle);
                    sb.Append(",title=").Append(Convert.ToBase64String(titleBytes));
                }
                catch { }
            }
            return sb.ToString();
        }

        public static GameProfile Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            int colonIdx = raw.IndexOf(':');
            if (colonIdx <= 0) return null;

            string pName = raw.Substring(0, colonIdx).Trim().ToLowerInvariant();
            string rest = raw.Substring(colonIdx + 1);
            int dpi = 0;
            int rate = 0;
            string path = "";
            string title = "";

            string[] tokens = rest.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim();
                if (tok.StartsWith("dpi=", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(tok.Substring(4), out v)) dpi = v;
                }
                else if (tok.StartsWith("rate=", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(tok.Substring(5), out v)) rate = v;
                }
                else if (tok.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] pb = Convert.FromBase64String(tok.Substring(5));
                        path = Encoding.UTF8.GetString(pb);
                    }
                    catch { }
                }
                else if (tok.StartsWith("title=", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] tb = Convert.FromBase64String(tok.Substring(6));
                        title = Encoding.UTF8.GetString(tb);
                    }
                    catch { }
                }
            }

            return new GameProfile(pName, dpi, rate, path, title);
        }

        public static string SerializeProfiles(Dictionary<string, GameProfile> profiles)
        {
            if (profiles == null || profiles.Count == 0) return "";
            List<string> list = new List<string>();
            foreach (var kvp in profiles)
            {
                if (kvp.Value != null && !string.IsNullOrEmpty(kvp.Key))
                {
                    list.Add(kvp.Value.ToConfigString());
                }
            }
            return string.Join(";", list.ToArray());
        }

        public static Dictionary<string, GameProfile> DeserializeProfiles(string raw)
        {
            var dict = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return dict;

            string[] items = raw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                var p = Parse(items[i]);
                if (p != null && !string.IsNullOrEmpty(p.ProcessName))
                {
                    dict[p.ProcessName] = p;
                }
            }
            return dict;
        }
    }
}
