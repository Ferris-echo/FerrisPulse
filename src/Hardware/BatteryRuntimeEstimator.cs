using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    // 剩余续航估算：无电流传感器可读，用固件电量计随时间的下降速率做最小二乘拟合。
    // 仅统计放电段；充电时清空历史。历史持久化到注册表，重启后继续累计。
    public static class BatteryRuntimeEstimator
    {
        private class Sample
        {
            public DateTime Time;
            public int Percent;
        }

        private class VoltSample
        {
            public DateTime Time;
            public int Millivolts;
        }

        private const string RegKey = @"Software\FerrisPulse\BatteryRuntime";
        private const int FormatVersion = 3; // 升级格式（含跳变过滤）后旧数据自动作废
        private static readonly List<Sample> samples = new List<Sample>();
        private static readonly List<VoltSample> voltSamples = new List<VoltSample>();
        private static readonly object sync = new object();
        private static DateTime sessionStart = DateTime.MinValue;
        private static int sessionStartPercent = -1;
        private static bool loaded = false;
        private static int chargingHits = 0;

        private const int MaxSamples = 512;
        private static readonly TimeSpan MaxWindow = TimeSpan.FromHours(10);
        private static readonly TimeSpan MinSpan = TimeSpan.FromHours(1.5);
        private const int MinDropPercent = 3;

        public static void Reset()
        {
            lock (sync)
            {
                samples.Clear();
                voltSamples.Clear();
                sessionStart = DateTime.MinValue;
                sessionStartPercent = -1;
                chargingHits = 0;
                SaveLocked();
            }
        }

        public static void Record(int percent, bool charging, DateTime now)
        {
            Record(percent, charging, now, 0);
        }

        public static void Record(int percent, bool charging, DateTime now, int voltageMv)
        {
            if (percent < 0 || percent > 100) return;
            lock (sync)
            {
                if (!loaded) { loaded = true; LoadLocked(); }

                // 充电判定做 3 次防抖（约 90 秒），避免瞬时误报清零
                if (charging)
                {
                    chargingHits++;
                    if (chargingHits >= 3) Reset();
                    return;
                }
                chargingHits = 0;

                // 跳变过滤：单次采样掉 ≥3% 视为电量计校准跳变（非真实放电），丢弃旧曲线重新测算
                if (samples.Count > 0 && percent <= samples[samples.Count - 1].Percent - 3)
                {
                    Reset();
                    sessionStart = now;
                    sessionStartPercent = percent;
                    samples.Add(new Sample { Time = now, Percent = percent });
                    SaveLocked();
                    return;
                }

                // 电量回升超过 3%（校准/换电池）才重来，避免 ±1% 抖动清零
                if (sessionStartPercent >= 0 && percent > sessionStartPercent + 3)
                {
                    Reset();
                }
                if (sessionStart == DateTime.MinValue)
                {
                    sessionStart = now;
                    sessionStartPercent = percent;
                }
                if (samples.Count == 0 || samples[samples.Count - 1].Percent != percent)
                {
                    samples.Add(new Sample { Time = now, Percent = percent });
                    while (samples.Count > MaxSamples) samples.RemoveAt(0);
                    SaveLocked();
                }
                DateTime cutoff = now - MaxWindow;
                while (samples.Count > 1 && samples[0].Time < cutoff) samples.RemoveAt(0);

                // 电压样本：变化 >=4mV 才记录，避免噪声堆积
                if (voltageMv > 0)
                {
                    bool voltageAdded = false;
                    if (voltSamples.Count == 0 || Math.Abs(voltageMv - voltSamples[voltSamples.Count - 1].Millivolts) >= 4)
                    {
                        // 电压回升 >10mV：发生了充电/插拔，旧放电趋势作废，重新积累
                        if (voltSamples.Count > 0 && voltageMv > voltSamples[voltSamples.Count - 1].Millivolts + 10)
                        {
                            voltSamples.Clear();
                        }
                        voltSamples.Add(new VoltSample { Time = now, Millivolts = voltageMv });
                        while (voltSamples.Count > MaxSamples) voltSamples.RemoveAt(0);
                        voltageAdded = true;
                    }
                    while (voltSamples.Count > 1 && voltSamples[0].Time < cutoff) voltSamples.RemoveAt(0);
                    // 仅在真正新增电压样本时落盘，避免每轮都写注册表
                    if (voltageAdded) SaveLocked();
                }
            }
        }

        // 采集进度：用于界面提示「测算中」
        public static bool TryGetProgress(out double spanHours, out int dropPercent)
        {
            spanHours = 0;
            dropPercent = 0;
            lock (sync)
            {
                if (!loaded) { loaded = true; LoadLocked(); }
                if (sessionStart == DateTime.MinValue || samples.Count == 0) return false;
                spanHours = (DateTime.Now - sessionStart).TotalHours;
                dropPercent = sessionStartPercent - samples[samples.Count - 1].Percent;
                return spanHours > 0;
            }
        }

        // 粗估：≥15 分钟且下降 ≥1%（误差大，界面标注「粗估」）
        public static bool TryEstimateRough(out double hoursRemaining, out double percentPerHour)
        {
            return TryEstimateInternal(0.25, 1, out hoursRemaining, out percentPerHour);
        }

        // 正式值：≥1.5 小时或累计下降 ≥3%
        public static bool TryEstimate(out double hoursRemaining, out double percentPerHour)
        {
            return TryEstimateInternal(MinSpan.TotalHours, MinDropPercent, out hoursRemaining, out percentPerHour);
        }

        private static bool TryEstimateInternal(double minSpanHours, int minDrop, out double hoursRemaining, out double percentPerHour)
        {
            hoursRemaining = 0;
            percentPerHour = 0;
            double spanHours = 0;
            int drop = 0;
            if (!TryGetProgress(out spanHours, out drop)) return false;
            if (spanHours < minSpanHours && drop < minDrop) return false;
            if (drop <= 0) return false;

            lock (sync)
            {
                int n = samples.Count;
                if (n == 0) return false;
                DateTime t0 = samples[0].Time;
                double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
                for (int i = 0; i < n; i++)
                {
                    double x = (samples[i].Time - t0).TotalHours;
                    double y = samples[i].Percent;
                    sumX += x;
                    sumY += y;
                    sumXY += x * y;
                    sumXX += x * x;
                }
                double den = n * sumXX - sumX * sumX;
                double slope;
                if (n == 1 || Math.Abs(den) < 1e-9)
                {
                    slope = -drop / spanHours; // 单样本：首末差 / 时长
                }
                else
                {
                    slope = (n * sumXY - sumX * sumY) / den;
                }
                if (slope > -0.05) return false;
                percentPerHour = -slope;
                double remain = samples[n - 1].Percent / percentPerHour;
                if (remain <= 0 || remain > 1000) return false;
                hoursRemaining = remain;
                return true;
            }
        }

        // 电压趋势兜底估算：固件电量计长时间不刷新时使用（误差约 +-30%）
        // 条件：电压样本跨度 >=30 分钟，累计下降 >=15mV（高于 +-3mV 噪声）
        public static bool TryEstimateFromVoltage(int currentMv, int percent, out double hoursRemaining, out double percentPerHour)
        {
            hoursRemaining = 0;
            percentPerHour = 0;
            if (currentMv <= 0 || percent <= 0 || percent > 100) return false;

            lock (sync)
            {
                if (!loaded) { loaded = true; LoadLocked(); }
                int n = voltSamples.Count;
                if (n < 2) return false;

                // 只取尾部「连续放电段」：相邻样本间隔 ≤10 分钟，跨睡眠/中断的旧样本不参与
                int start = n - 1;
                while (start > 0 && (voltSamples[start].Time - voltSamples[start - 1].Time).TotalMinutes <= 10)
                {
                    start--;
                }
                DateTime t0 = voltSamples[start].Time;
                DateTime t1 = voltSamples[n - 1].Time;
                double spanHours = (t1 - t0).TotalHours;
                if (spanHours < 0.5) return false;

                int dropMv = voltSamples[start].Millivolts - voltSamples[n - 1].Millivolts;
                if (dropMv < 15) return false;

                double mvPerHour = dropMv / spanHours;
                double mvPerPercent = VgnDeviceHelper.MillivoltsPerPercent(currentMv);
                if (mvPerPercent <= 0.5) return false;

                double pctPerHour = mvPerHour / mvPerPercent;
                if (pctPerHour < 0.2 || pctPerHour > 60) return false;

                double remain = percent / pctPerHour;
                if (remain <= 0 || remain > 500) return false;

                hoursRemaining = remain;
                percentPerHour = pctPerHour;
                return true;
            }
        }

        // 电压样本进度（用于提示「电压趋势已采集 N 分钟」）
        public static bool TryGetVoltageProgress(out double spanHours, out int dropMv)
        {
            spanHours = 0;
            dropMv = 0;
            lock (sync)
            {
                if (!loaded) { loaded = true; LoadLocked(); }
                if (voltSamples.Count < 2) return false;
                int st = voltSamples.Count - 1;
                while (st > 0 && (voltSamples[st].Time - voltSamples[st - 1].Time).TotalMinutes <= 10)
                {
                    st--;
                }
                spanHours = (voltSamples[voltSamples.Count - 1].Time - voltSamples[st].Time).TotalHours;
                dropMv = voltSamples[st].Millivolts - voltSamples[voltSamples.Count - 1].Millivolts;
                return spanHours > 0;
            }
        }

        private static void SaveLocked()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    if (key == null) return;
                    if (samples.Count == 0 || sessionStart == DateTime.MinValue)
                    {
                        key.DeleteValue("Samples", false);
                        key.DeleteValue("VSamples", false);
                        key.DeleteValue("Start", false);
                        key.DeleteValue("StartPct", false);
                        return;
                    }
                    key.SetValue("V", FormatVersion);
                    key.SetValue("Start", sessionStart.ToString("o"));
                    key.SetValue("StartPct", sessionStartPercent);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < samples.Count; i++)
                    {
                        if (i > 0) sb.Append(';');
                        sb.Append(samples[i].Time.Ticks).Append(':').Append(samples[i].Percent);
                    }
                    key.SetValue("Samples", sb.ToString());

                    StringBuilder vb = new StringBuilder();
                    for (int i = 0; i < voltSamples.Count; i++)
                    {
                        if (i > 0) vb.Append(';');
                        vb.Append(voltSamples[i].Time.Ticks).Append(':').Append(voltSamples[i].Millivolts);
                    }
                    if (vb.Length > 0) key.SetValue("VSamples", vb.ToString());
                    else key.DeleteValue("VSamples", false);
                }
            }
            catch { }
        }

        private static void LoadLocked()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKey, false))
                {
                    if (key == null) return;
                    // 旧格式（无 V 或版本不符）直接丢弃，避免历史的跳变样本污染
                    object vObj = key.GetValue("V");
                    if (vObj == null || Convert.ToInt32(vObj) != FormatVersion) return;
                    object startObj = key.GetValue("Start");
                    object pctObj = key.GetValue("StartPct");
                    object samplesObj = key.GetValue("Samples");
                    if (startObj == null || pctObj == null || samplesObj == null) return;

                    DateTime start;
                    if (!DateTime.TryParse(Convert.ToString(startObj), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start)) return;
                    int startPct = Convert.ToInt32(pctObj);
                    string raw = Convert.ToString(samplesObj);
                    if (string.IsNullOrEmpty(raw)) return;

                    string[] parts = raw.Split(';');
                    List<Sample> restored = new List<Sample>();
                    DateTime last = DateTime.MinValue;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string[] kv = parts[i].Split(':');
                        if (kv.Length != 2) continue;
                        long ticks;
                        int pct;
                        if (!long.TryParse(kv[0], out ticks)) continue;
                        if (!int.TryParse(kv[1], out pct)) continue;
                        DateTime t = new DateTime(ticks, DateTimeKind.Local);
                        restored.Add(new Sample { Time = t, Percent = pct });
                        if (t > last) last = t;
                    }
                    if (restored.Count == 0) return;
                    // 超过 10 小时未更新则丢弃
                    if ((DateTime.Now - last) > MaxWindow) return;

                    samples.Clear();
                    samples.AddRange(restored);

                    object vSamplesObj = key.GetValue("VSamples");
                    string vRaw = (vSamplesObj == null) ? null : Convert.ToString(vSamplesObj);
                    voltSamples.Clear();
                    if (!string.IsNullOrEmpty(vRaw))
                    {
                        string[] vparts = vRaw.Split(';');
                        for (int i = 0; i < vparts.Length; i++)
                        {
                            string[] kvv = vparts[i].Split(':');
                            if (kvv.Length != 2) continue;
                            long vticks;
                            int mvv;
                            if (!long.TryParse(kvv[0], out vticks)) continue;
                            if (!int.TryParse(kvv[1], out mvv)) continue;
                            voltSamples.Add(new VoltSample { Time = new DateTime(vticks, DateTimeKind.Local), Millivolts = mvv });
                        }
                    }
                    sessionStart = start;
                    sessionStartPercent = startPct;
                }
            }
            catch { }
        }
    }
}
