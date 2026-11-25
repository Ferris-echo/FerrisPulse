// ============================================================================
// 模块名称: TraySlotPlanner.cs
// 所属区域: UI/Tray (多托盘核心调度层)
// 核心职责:
//   纯静态无副作用的多设备独立托盘槽位规划器。
//   结合用户优先级列表、低电量预警仲裁与在场状态，确定每个托盘槽位的展示内容；
//   已连接休眠且有电量缓存的设备原位保留槽位并绘制琥珀色记忆电量，离线/无缓存设备显示断联图标，零设备时兜底返回 1 个断联槽位。
// 依赖关系: Models.MouseBatteryInfo, Hardware.DeviceManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值、?.、nameof、out var）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;

namespace RazerBatteryTray
{
    public sealed class TraySlotPlanItem
    {
        public int SlotIndex { get; set; }
        public string DeviceId { get; set; }
        public MouseBatteryInfo Device { get; set; }
        public bool IsFallback { get; set; }
        public int Percent { get; set; }
        public bool IsCharging { get; set; }
        public bool VisualConnected { get; set; }
        public bool IsSleeping { get; set; }
        public Color AccentColor { get; set; }
        public string TooltipText { get; set; }
    }

    public static class TraySlotPlanner
    {
        private const int LowBatteryThreshold = 30;

        public static List<TraySlotPlanItem> PlanSlots(
            IList<MouseBatteryInfo> allDevices,
            IList<string> priorityList,
            bool lowBatteryFirst,
            int maxSlots,
            Func<string, bool> isHiddenFn)
        {
            if (maxSlots < 1) maxSlots = 1;
            if (maxSlots > 16) maxSlots = 16;

            var candidates = new List<CandidateEntry>();
            if (allDevices != null)
            {
                for (int i = 0; i < allDevices.Count; i++)
                {
                    var d = allDevices[i];
                    if (d == null) continue;
                    if (isHiddenFn != null)
                    {
                        if (!string.IsNullOrEmpty(d.DeviceId) && isHiddenFn(d.DeviceId)) continue;
                        if (!string.IsNullOrEmpty(d.HardwareFingerprint) && isHiddenFn(d.HardwareFingerprint)) continue;
                    }

                    // 接收器在线 / 蓝牙或物理连接在场（含休眠态）均保留槽位；彻底拔出设备释放槽位
                    bool isPresent = d.IsConnected || d.IsDonglePresent;
                    if (!isPresent) continue;

                    int pIdx = -1;
                    if (priorityList != null && !string.IsNullOrEmpty(d.DeviceId))
                    {
                        var prioAsList = priorityList as List<string> ?? new List<string>(priorityList);
                        pIdx = DeviceManager.FindPriorityIndex(prioAsList, d.DeviceId);
                    }
                    if (pIdx < 0) pIdx = 9999;

                    bool lowAlarm = lowBatteryFirst &&
                                    d.IsConnected &&
                                    !d.IsSleeping &&
                                    !d.IsCharging &&
                                    d.BatteryPercent >= 0 &&
                                    d.BatteryPercent <= LowBatteryThreshold;

                    candidates.Add(new CandidateEntry
                    {
                        Device = d,
                        OriginalOrder = i,
                        PriorityIndex = pIdx,
                        IsLowBatteryAlarm = lowAlarm
                    });
                }
            }

            if (candidates.Count == 0)
            {
                var fallbackList = new List<TraySlotPlanItem>(1);
                fallbackList.Add(new TraySlotPlanItem
                {
                    SlotIndex = 0,
                    DeviceId = "",
                    Device = null,
                    IsFallback = true,
                    Percent = -1,
                    IsCharging = false,
                    VisualConnected = false,
                    AccentColor = Color.FromArgb(140, 150, 168),
                    TooltipText = "未检测到设备 (未连接)"
                });
                return fallbackList;
            }

            candidates.Sort(delegate(CandidateEntry a, CandidateEntry b)
            {
                if (a.IsLowBatteryAlarm != b.IsLowBatteryAlarm)
                {
                    return a.IsLowBatteryAlarm ? -1 : 1;
                }
                if (a.IsLowBatteryAlarm && b.IsLowBatteryAlarm)
                {
                    int batCmp = a.Device.BatteryPercent.CompareTo(b.Device.BatteryPercent);
                    if (batCmp != 0) return batCmp;
                }
                int prioCmp = a.PriorityIndex.CompareTo(b.PriorityIndex);
                if (prioCmp != 0) return prioCmp;

                int connCmp = (b.Device.IsConnected ? 1 : 0).CompareTo(a.Device.IsConnected ? 1 : 0);
                if (connCmp != 0) return connCmp;

                return a.OriginalOrder.CompareTo(b.OriginalOrder);
            });

            int slotCount = Math.Min(candidates.Count, maxSlots);
            var result = new List<TraySlotPlanItem>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                var d = candidates[i].Device;
                bool isAwakeConnected = d.IsConnected && !d.IsSleeping;
                bool isSleepingWithCache = d.IsConnected && d.IsSleeping && d.BatteryPercent >= 0;
                bool visualConnected = isAwakeConnected || isSleepingWithCache;
                string tooltip = BuildSlotTooltip(d, isAwakeConnected);

                result.Add(new TraySlotPlanItem
                {
                    SlotIndex = i,
                    DeviceId = d.DeviceId ?? "",
                    Device = d,
                    IsFallback = false,
                    Percent = d.BatteryPercent,
                    IsCharging = d.IsCharging && isAwakeConnected,
                    VisualConnected = visualConnected,
                    IsSleeping = isSleepingWithCache,
                    AccentColor = d.TrayAccentColor,
                    TooltipText = tooltip
                });
            }
            return result;
        }

        private static string BuildSlotTooltip(MouseBatteryInfo d, bool visualConnected)
        {
            string name = !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : "外设设备";
            string state;
            if (!d.IsConnected)
            {
                state = d.BatteryPercent > 0 ? string.Format("{0}% (离线)", d.BatteryPercent) : "未连接";
            }
            else if (d.IsSleeping)
            {
                state = d.BatteryPercent >= 0 ? string.Format("{0}% (休眠待机)", d.BatteryPercent) : "休眠待机";
            }
            else if (d.IsCharging)
            {
                state = d.BatteryPercent >= 0 ? string.Format("{0}% (充电中)", d.BatteryPercent) : "充电中";
            }
            else
            {
                state = d.BatteryPercent >= 0 ? (d.BatteryPercent.ToString() + "%") : "--";
            }

            string text = name + ": " + state;
            if (visualConnected && d.Category == DeviceCategory.Mouse && d.Dpi > 0 && text.Length < 48)
            {
                text += string.Format(" · {0} DPI", d.Dpi);
            }
            if (text.Length > 63) text = text.Substring(0, 63);
            return text;
        }

        private sealed class CandidateEntry
        {
            public MouseBatteryInfo Device;
            public int OriginalOrder;
            public int PriorityIndex;
            public bool IsLowBatteryAlarm;
        }
    }
}
