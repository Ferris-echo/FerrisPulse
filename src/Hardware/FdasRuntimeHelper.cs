// ============================================================================
// 模块名称: FdasRuntimeHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   FDAS v1.0 规范体系运行时动态驱动引擎。自动扫描本地 adapters/ 目录下的所有
//   *.fp-adapter.json 适配包，动态提取端点拓扑与协议报文，主动下发查询报文与控制指令，
//   支持动态调节 DPI、档位与回报率，无需重新编译即可实时驱动新外设。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, Models.DeviceCategory
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RazerBatteryTray
{
    public class FdasAdapterDefinition
    {
        public string Brand;
        public string Model;
        public DeviceCategory Category;
        public string Author;
        public int VendorId;
        public int ProductId;
        public int UsagePage;
        public int Usage;
        public int ReportLength;

        // FDAS metadata.brandColor（#RRGGBB）。为空时沿用内置的品牌配色表。
        public string BrandColorHex;

        // 以下三项仅做声明式承载，不参与报文构造：
        // description 供人工查阅，sensorDescription 供 UI 展示，sampleTxHex 供样本校验。
        public string Description;
        public string SensorDescription;
        public string SampleTxHex;

        // FDAS capabilities.battery.reportId。0 表示未声明，报文保持原样不做任何前插，
        // 因此所有既有 1.0 适配包的行为完全不变。
        public int ReportId;

        // FDAS hardware.devices 多 PID 矩阵。为空表示适配包未声明该字段，
        // 设备身份沿用旧的 FDAS:VID:PID 规则，行为与之前完全一致。
        public string[] DeviceProductIds;
        public string[] DeviceModels;
        public int BatteryOffset;
        public int ChargingOffset;
        public byte ChargingMask;
        public string BatteryFormula;
        public int SampleBattery;
        public bool SampleCharging;
        public string FilePath;

        // Protocol specification
        public string QueryBatteryTxHex;
        public string[] QueryBatteryPipeline;
        public string QueryDpiTxHex;
        public string SetDpiTxTemplate;
        public string SetDpiValueTxTemplate;
        public string SetPollingRateTxTemplate;
        public string ChecksumType;
        public string ReportType; // "Interrupt" or "Feature"
        public int ReadTimeoutMs;
        public Dictionary<string, string> Variables;
        public Dictionary<string, string> RateCodeMap;

        // Mouse extensions
        public int MinDpi;
        public int MaxDpi;
        public int DpiStep;
        public bool SupportsXyIndependence;
        public int ActiveRate;
        public int MaxRate;
        public int[] DefaultStages;
        public int[] SupportedRates;
        public Dictionary<int, int> RateIndexMap;

        // Runtime dynamic states (preserved across periodic reloads)
        public int CurrentDpi;
        public int CurrentDpiY;
        public int CurrentDpiStage;
        public int CurrentPollingRate;
        public int LastBattery;
        public bool LastCharging;
        public DateTime LastBatteryTime;
    }

    public static class FdasRuntimeHelper
    {
        private static readonly object syncLock = new object();
        private static List<FdasAdapterDefinition> loadedAdapters = new List<FdasAdapterDefinition>();
        private static DateTime lastScanTime = DateTime.MinValue;

        public static string GetAdaptersDirectory()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = Path.Combine(appDir, "adapters");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetAppDataAdaptersDirectory()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, Path.Combine("FerrisPulse", "adapters"));
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static FdasAdapterDefinition ParseAdapterJson(string jsonText, string filePath)
        {
            if (string.IsNullOrEmpty(jsonText)) return null;
            try
            {
                string ver = ExtractJsonString(jsonText, "fdasVersion");
                // 同时接受 1.0 与 2.0：原来只认 "1.0"，导致 v2.0 适配包被整包丢弃。
                // 2.0 的新增字段（devices / customActions 等）由后续解析按需读取，
                // 未识别的键一律忽略，不影响 1.0 包的行为。
                if (ver != "1.0" && ver != "2.0") return null;

                FdasAdapterDefinition def = new FdasAdapterDefinition();
                def.FilePath = filePath;
                def.Brand = ExtractJsonString(jsonText, "brand");
                def.Model = ExtractJsonString(jsonText, "model");
                def.Author = ExtractJsonString(jsonText, "author");
                def.BrandColorHex = ExtractJsonString(jsonText, "brandColor");
                def.Description = ExtractJsonString(jsonText, "description");
                def.SensorDescription = ExtractJsonString(jsonText, "sensorDescription");
                def.SampleTxHex = ExtractJsonString(jsonText, "sampleTxHex");
                def.ReportId = ConvertHexToInt(ExtractJsonString(jsonText, "reportId"));

                // 多 PID 矩阵：同一型号的有线/无线 PID 归并为一个设备身份
                string devicesBlock = ExtractJsonArrayBlock(jsonText, "devices");
                if (!string.IsNullOrEmpty(devicesBlock))
                {
                    List<string> pids = ExtractJsonStringValues(devicesBlock, "productId");
                    List<string> models = ExtractJsonStringValues(devicesBlock, "model");
                    if (pids.Count > 0)
                    {
                        def.DeviceProductIds = pids.ToArray();
                        def.DeviceModels = models.ToArray();
                    }
                }

                string catStr = ExtractJsonString(jsonText, "category");
                def.Category = DeviceCategory.Mouse;
                if (catStr.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)) def.Category = DeviceCategory.Keyboard;
                else if (catStr.Equals("Headset", StringComparison.OrdinalIgnoreCase)) def.Category = DeviceCategory.Headset;
                else if (catStr.Equals("Earbuds", StringComparison.OrdinalIgnoreCase)) def.Category = DeviceCategory.Earbuds;
                else if (catStr.Equals("Gamepad", StringComparison.OrdinalIgnoreCase)) def.Category = DeviceCategory.Gamepad;
                else if (catStr.Equals("Dongle", StringComparison.OrdinalIgnoreCase)) def.Category = DeviceCategory.Dongle;
                else if (catStr.Equals("Generic", StringComparison.OrdinalIgnoreCase)) def.Category = DeviceCategory.Generic;

                string vidStr = ExtractJsonString(jsonText, "vendorId");
                string pidStr = ExtractJsonString(jsonText, "productId");
                def.VendorId = ConvertHexToInt(vidStr);
                def.ProductId = ConvertHexToInt(pidStr);

                string upStr = ExtractJsonString(jsonText, "usagePage");
                string uStr = ExtractJsonString(jsonText, "usage");
                def.UsagePage = ConvertHexToInt(upStr);
                def.Usage = ConvertHexToInt(uStr);
                def.ReportLength = ExtractJsonInt(jsonText, "reportLength", 64);
                def.ReportType = ExtractJsonString(jsonText, "reportType");
                if (string.IsNullOrEmpty(def.ReportType)) def.ReportType = "Interrupt";
                def.ReadTimeoutMs = ExtractJsonInt(jsonText, "readTimeoutMs", 400);

                def.BatteryOffset = ExtractJsonInt(jsonText, "batteryPercentByteOffset", 4);
                def.ChargingOffset = ExtractJsonInt(jsonText, "chargingByteOffset", 5);
                string maskStr = ExtractJsonString(jsonText, "chargingBitMask");
                def.ChargingMask = !string.IsNullOrEmpty(maskStr) ? (byte)ConvertHexToInt(maskStr) : (byte)0x01;
                def.BatteryFormula = ExtractJsonString(jsonText, "batteryFormula");
                if (string.IsNullOrEmpty(def.BatteryFormula)) def.BatteryFormula = "DirectPercent";

                def.MinDpi = ExtractJsonInt(jsonText, "minDpi", 50);
                def.MaxDpi = ExtractJsonInt(jsonText, "maxDpi", 30000);
                def.DpiStep = ExtractJsonInt(jsonText, "step", 50);
                string xyStr = ExtractJsonString(jsonText, "supportsXyIndependence");
                def.SupportsXyIndependence = xyStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                def.DefaultStages = ExtractJsonIntArray(jsonText, "defaultStages");
                if (def.DefaultStages == null || def.DefaultStages.Length == 0)
                {
                    def.DefaultStages = new int[] { 400, 800, 1600, 3200, 6400 };
                }

                def.ActiveRate = ExtractJsonInt(jsonText, "defaultRate", 1000);
                def.MaxRate = ExtractJsonInt(jsonText, "maxRate", 8000);
                def.SupportedRates = ExtractJsonIntArray(jsonText, "supportedRates");
                if (def.SupportedRates == null || def.SupportedRates.Length == 0)
                {
                    def.SupportedRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                }
                def.RateIndexMap = ExtractJsonIntMap(jsonText, "rateIndexMap");
                def.RateCodeMap = ExtractJsonStringMap(jsonText, "rateCodeMap");

                // Protocol commands & templates
                def.QueryBatteryTxHex = ExtractJsonString(jsonText, "queryBatteryTxHex");
                def.QueryBatteryPipeline = ExtractJsonStringArray(jsonText, "queryBatteryPipeline");
                def.QueryDpiTxHex = ExtractJsonString(jsonText, "queryDpiTxHex");
                def.SetDpiTxTemplate = ExtractJsonString(jsonText, "setDpiTxTemplate");
                def.SetDpiValueTxTemplate = ExtractJsonString(jsonText, "setDpiValueTxTemplate");
                def.SetPollingRateTxTemplate = ExtractJsonString(jsonText, "setPollingRateTxTemplate");
                def.ChecksumType = ExtractJsonString(jsonText, "checksumType");
                def.Variables = ExtractJsonStringMap(jsonText, "variables");

                // Intelligent default: 0x3554 (Compx) requires VgnCompxSum55 checksum if not specified
                if (def.VendorId == 0x3554 && string.IsNullOrEmpty(def.ChecksumType))
                {
                    def.ChecksumType = "VgnCompxSum55";
                }

                // Parse sample hex to extract sample battery & charging
                string sampleHex = ExtractJsonString(jsonText, "sampleRxHex");
                if (!string.IsNullOrEmpty(sampleHex))
                {
                    byte[] rx = ParseHexBytes(sampleHex);
                    if (rx != null && def.BatteryOffset < rx.Length)
                    {
                        int raw = (int)rx[def.BatteryOffset];
                        if (def.BatteryFormula == "Max10Scale") def.SampleBattery = raw * 10;
                        else if (def.BatteryFormula == "XorMask") def.SampleBattery = raw ^ 0xFF;
                        else def.SampleBattery = raw;

                        if (def.SampleBattery < 0) def.SampleBattery = 0;
                        if (def.SampleBattery > 100) def.SampleBattery = 100;

                        if (def.ChargingOffset < rx.Length)
                        {
                            def.SampleCharging = ((rx[def.ChargingOffset] & def.ChargingMask) != 0);
                        }
                    }
                }

                if (def.VendorId != 0 && def.ProductId != 0 && !string.IsNullOrEmpty(def.Brand))
                {
                    return def;
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
            return null;
        }

        public static void ReloadAdapters()
        {
            lock (syncLock)
            {
                List<FdasAdapterDefinition> list = new List<FdasAdapterDefinition>();
                List<string> dirs = new List<string>();

                string d1 = GetAdaptersDirectory();
                if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1)) dirs.Add(d1);

                string d2 = GetAppDataAdaptersDirectory();
                if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !dirs.Contains(d2)) dirs.Add(d2);

                for (int di = 0; di < dirs.Count; di++)
                {
                    string dir = dirs[di];
                    try
                    {
                        List<string> candidateFiles = new List<string>();
                        string[] fpFiles = Directory.GetFiles(dir, "*.fp-adapter.json");
                        if (fpFiles != null) candidateFiles.AddRange(fpFiles);
                        string[] allJson = Directory.GetFiles(dir, "*.json");
                        if (allJson != null)
                        {
                            for (int ji = 0; ji < allJson.Length; ji++)
                            {
                                if (!candidateFiles.Contains(allJson[ji]))
                                {
                                    candidateFiles.Add(allJson[ji]);
                                }
                            }
                        }

                        for (int fi = 0; fi < candidateFiles.Count; fi++)
                        {
                            string file = candidateFiles[fi];
                            try
                            {
                                string text = File.ReadAllText(file, Encoding.UTF8);
                                FdasAdapterDefinition def = ParseAdapterJson(text, file);
                                if (def != null)
                                {
                                    bool exists = false;
                                    for (int i = 0; i < list.Count; i++)
                                    {
                                        if (list[i].VendorId == def.VendorId && list[i].ProductId == def.ProductId)
                                        {
                                            exists = true;
                                            break;
                                        }
                                    }
                                    if (!exists)
                                    {
                                        // Preserve dynamic runtime state if already loaded previously
                                        for (int j = 0; j < loadedAdapters.Count; j++)
                                        {
                                            if (loadedAdapters[j].VendorId == def.VendorId && loadedAdapters[j].ProductId == def.ProductId)
                                            {
                                                def.CurrentDpi = loadedAdapters[j].CurrentDpi;
                                                def.CurrentDpiY = loadedAdapters[j].CurrentDpiY;
                                                def.CurrentDpiStage = loadedAdapters[j].CurrentDpiStage;
                                                def.CurrentPollingRate = loadedAdapters[j].CurrentPollingRate;
                                                def.LastBattery = loadedAdapters[j].LastBattery;
                                                def.LastCharging = loadedAdapters[j].LastCharging;
                                                def.LastBatteryTime = loadedAdapters[j].LastBatteryTime;
                                                break;
                                            }
                                        }
                                        list.Add(def);
                                    }
                                }
                            }
                            catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
                        }
                    }
                    catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
                }

                loadedAdapters = list;
                lastScanTime = DateTime.Now;
            }
        }

        public static List<MouseBatteryInfo> QueryAllFdasDevices()
        {
            lock (syncLock)
            {
                if ((DateTime.Now - lastScanTime).TotalSeconds > 5 || loadedAdapters.Count == 0)
                {
                    ReloadAdapters();
                }

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                if (loadedAdapters == null || loadedAdapters.Count == 0) return result;

                for (int i = 0; i < loadedAdapters.Count; i++)
                {
                    FdasAdapterDefinition def = loadedAdapters[i];
                    try
                    {
                        List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(def.VendorId, def.ProductId);
                        if (descriptors == null || descriptors.Count == 0) continue;

                        HidDeviceDescriptor target = descriptors[0];
                        if (def.UsagePage != 0 && def.Usage != 0)
                        {
                            for (int di = 0; di < descriptors.Count; di++)
                            {
                                if (descriptors[di].UsagePage == def.UsagePage && descriptors[di].Usage == def.Usage)
                                {
                                    target = descriptors[di];
                                    break;
                                }
                            }
                        }

                        int batt = def.LastBattery > 0 ? def.LastBattery : (def.SampleBattery > 0 ? def.SampleBattery : 80);
                        bool chg = def.LastCharging;

                        IntPtr handle = HidTransport.Open(target.DevicePath);
                        if (HidTransport.IsValidHandle(handle))
                        {
                            try
                            {
                                // Drain any lingering packets
                                HidTransport.DrainReports(handle, def.ReportLength > 0 ? def.ReportLength : 64, 3);



                                // 1. Active query TX dispatch (Essential for 2.4G wireless receivers)
                                List<string> txSteps = new List<string>();
                                if (def.QueryBatteryPipeline != null && def.QueryBatteryPipeline.Length > 0)
                                {
                                    txSteps.AddRange(def.QueryBatteryPipeline);
                                }
                                else if (!string.IsNullOrEmpty(def.QueryBatteryTxHex))
                                {
                                    txSteps.Add(def.QueryBatteryTxHex);
                                }

                                for (int si = 0; si < txSteps.Count; si++)
                                {
                                    byte[] tx = ApplyReportId(ParseHexBytes(txSteps[si]), def.ReportId);
                                    if (tx != null && tx.Length > 0)
                                    {
                                        int reqLen = def.ReportLength > 0 ? def.ReportLength : tx.Length;
                                        if (tx.Length < reqLen)
                                        {
                                            byte[] padded = new byte[reqLen];
                                            Array.Copy(tx, padded, tx.Length);
                                            tx = padded;
                                        }
                                        else if (tx.Length > reqLen)
                                        {
                                            byte[] trimmed = new byte[reqLen];
                                            Array.Copy(tx, trimmed, reqLen);
                                            tx = trimmed;
                                        }

                                        ApplyChecksum(tx, def.ChecksumType, def.VendorId, def.ReportId);

                                        if (def.ReportType.Equals("Feature", StringComparison.OrdinalIgnoreCase))
                                        {
                                            HidTransport.SetFeature(handle, tx);
                                        }
                                        else
                                        {
                                            HidTransport.WriteReport(handle, tx, tx.Length, 300);
                                        }

                                        System.Threading.Thread.Sleep(35);
                                    }
                                }

                                // 2. Read back report
                                byte[] inBuf = null;
                                bool readOk = false;
                                if (def.ReportType.Equals("Feature", StringComparison.OrdinalIgnoreCase))
                                {
                                    inBuf = new byte[def.ReportLength > 0 ? def.ReportLength : 64];
                                    readOk = HidTransport.GetFeature(handle, inBuf);
                                }
                                else
                                {
                                    int waitMs = def.ReadTimeoutMs > 0 ? def.ReadTimeoutMs : 400;
                                    readOk = HidTransport.ReadReport(handle, def.ReportLength > 0 ? def.ReportLength : 64, waitMs, out inBuf);
                                }

                                // For CompX/VGN, if the first packet was an echo of another command, read the pending 0x04 report
                                if (readOk && inBuf != null && def.VendorId == 0x3554 && inBuf.Length >= 2 && inBuf[1] != 0x04)
                                {
                                    byte[] retryBuf = null;
                                    if (HidTransport.ReadReport(handle, def.ReportLength > 0 ? def.ReportLength : 17, 300, out retryBuf) && retryBuf != null && retryBuf.Length >= 2 && retryBuf[1] == 0x04)
                                    {
                                        inBuf = retryBuf;
                                    }
                                }

                                if (readOk && inBuf != null && def.BatteryOffset < inBuf.Length)
                                {
                                    int raw = (int)inBuf[def.BatteryOffset];
                                    int bVal = raw;
                                    if (def.BatteryFormula == "Max10Scale") bVal = raw * 10;
                                    else if (def.BatteryFormula == "XorMask") bVal = raw ^ 0xFF;

                                    if (bVal < 0) bVal = 0;
                                    if (bVal > 100) bVal = 100;
                                    batt = bVal;
                                    def.LastBattery = bVal;
                                    def.LastBatteryTime = DateTime.Now;

                                    if (def.ChargingOffset < inBuf.Length)
                                    {
                                        chg = ((inBuf[def.ChargingOffset] & def.ChargingMask) != 0);
                                        def.LastCharging = chg;
                                    }
                                }
                                else
                                {
                                    // On timeout: keep last known battery if available, never inject fake sample battery
                                    if (def.LastBattery > 0)
                                    {
                                        batt = def.LastBattery;
                                    }
                                    else
                                    {
                                        batt = -1; // -1 indicates device not yet read or sleeping
                                    }

                                    // 2.4G wireless mice that timed out / went to sleep are not charging
                                    chg = false;
                                }
                            }
                            finally
                            {
                                HidTransport.Close(handle);
                            }
                        }

                        MouseBatteryInfo dev = new MouseBatteryInfo();
                        // 声明了 devices 矩阵时，用型号做稳定分组键，
                        // 避免同一鼠标在有线/无线之间切换被认成两台设备。
                        bool hasPidMatrix = def.DeviceProductIds != null && def.DeviceProductIds.Length > 0;
                        if (hasPidMatrix)
                        {
                            dev.DeviceId = string.Format("FDAS:{0:X4}:{1}", def.VendorId, SanitizeIdPart(def.Model));
                        }
                        else
                        {
                            dev.DeviceId = string.Format("FDAS:{0:X4}:{1:X4}", def.VendorId, def.ProductId);
                        }
                        dev.Brand = def.Brand;
                        dev.Category = def.Category;
                        dev.OverrideBrandColor = ParseHexColor(def.BrandColorHex);
                        dev.SensorDescription = def.SensorDescription;
                        string matchedModel = null;
                        if (hasPidMatrix && def.DeviceModels != null)
                        {
                            for (int mi = 0; mi < def.DeviceProductIds.Length; mi++)
                            {
                                if (mi < def.DeviceModels.Length
                                    && ConvertHexToInt(def.DeviceProductIds[mi]) == def.ProductId)
                                {
                                    matchedModel = def.DeviceModels[mi];
                                    break;
                                }
                            }
                        }
                        dev.DeviceName = !string.IsNullOrEmpty(matchedModel)
                            ? matchedModel
                            : (def.Brand + " " + def.Model);
                                                dev.IsConnected = (batt >= 0);
                        dev.BatteryPercent = batt >= 0 ? batt : 0;
                        dev.IsCharging = chg;
                        dev.LastUpdated = DateTime.Now;
                        dev.Transport = "2.4G 无线";

                        if (def.Category == DeviceCategory.Mouse)
                        {
                                                        dev.DpiStages = (def.DefaultStages != null && def.DefaultStages.Length > 0)
                                ? def.DefaultStages
                                : new int[] { 400, 800, 1600, 3200, 6400 };
                            dev.DpiStageCount = dev.DpiStages.Length;
                            dev.Dpi = def.CurrentDpi > 0 ? def.CurrentDpi : (dev.DpiStages.Length > 2 ? dev.DpiStages[2] : dev.DpiStages[0]);
                            dev.DpiStage = def.CurrentDpiStage > 0 ? def.CurrentDpiStage : 3;
                            dev.IsXyIndependent = def.SupportsXyIndependence;

                            dev.SupportedPollingRates = (def.SupportedRates != null && def.SupportedRates.Length > 0)
                                ? def.SupportedRates
                                : new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                            dev.MaxPollingRate = def.MaxRate > 0 ? def.MaxRate : 8000;
                            dev.PollingRate = def.CurrentPollingRate > 0 ? def.CurrentPollingRate : (def.ActiveRate > 0 ? def.ActiveRate : 1000);
                        }

                        result.Add(dev);
                    }
                    catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
                }

                return result;
            }
        }

        // 批量写入全部 DPI 档位。与其他品牌（雷蛇/雷柏/皂品）的 Update*HardwareStages 对齐：
        // 原来 FDAS 设备只写当前档位，改档位后其余档位的数值不会被持久化。
        //
        // 安全闸门：只有适配包声明了 setDpiValueTxTemplate（"写某档数值"）时才批量写；
        // 否则 SetDpi 会退化成 setDpiTxTemplate（"切换档位"），循环会把设备反复切档。
        public static bool UpdateStages(string deviceId, int activeStage, int[] stagesX, int[] stagesY)
        {
            if (stagesX == null || stagesX.Length == 0) return false;
            FdasAdapterDefinition def = FindAdapterByDeviceId(deviceId);
            if (def == null || string.IsNullOrEmpty(def.SetDpiValueTxTemplate))
            {
                // 没有"写数值"模板：保持旧行为，只写当前档位
                int ax = (activeStage >= 1 && activeStage <= stagesX.Length) ? stagesX[activeStage - 1] : stagesX[0];
                int ay = (stagesY != null && activeStage >= 1 && activeStage <= stagesY.Length) ? stagesY[activeStage - 1] : ax;
                return SetDpi(deviceId, ax, ay, activeStage > 0 ? activeStage : 1);
            }

            bool any = false;
            for (int i = 0; i < stagesX.Length && i < 8; i++)
            {
                int dx = stagesX[i];
                int dy = (stagesY != null && i < stagesY.Length) ? stagesY[i] : dx;
                if (SetDpi(deviceId, dx, dy, i + 1)) any = true;
            }
            return any;
        }

        public static bool SetDpi(string deviceId, int dpiX, int dpiY, int stage)
        {
            lock (syncLock)
            {
                FdasAdapterDefinition def = FindAdapterByDeviceId(deviceId);
                if (def == null || string.IsNullOrEmpty(def.SetDpiTxTemplate)) return false;

                int st0 = Math.Max(0, stage - 1);
                byte stageParity = (byte)((0x55 - stage) & 0xFF);
                byte stage0Parity = (byte)((0x55 - st0) & 0xFF);

                // CompX / VGN encoded DPI
                int encodedDpi = (dpiX / 50) - 1;
                if (encodedDpi < 0) encodedDpi = 0;
                byte dpiLow = (byte)(encodedDpi & 0xFF);
                byte dpiHigh = (byte)((encodedDpi >> 8) & 0x03);
                byte dpiFlags = (byte)((dpiHigh << 2) | (dpiHigh << 6));
                byte dpiParity = (byte)((0x55 - dpiLow - dpiLow - dpiFlags) & 0xFF);
                int dpiAddr = 0x000C + st0 * 4;

                var context = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                context["stage"] = stage;
                context["stage0"] = st0;
                context["stageParity"] = stageParity;
                context["stage0Parity"] = stage0Parity;
                context["dpi"] = dpiX;
                context["dpiX"] = dpiX;
                context["dpiY"] = dpiY;
                context["dpiX_lo"] = dpiX & 0xFF;
                context["dpiX_hi"] = (dpiX >> 8) & 0xFF;
                context["dpiY_lo"] = dpiY & 0xFF;
                context["dpiY_hi"] = (dpiY >> 8) & 0xFF;
                context["dpi_lo"] = dpiX & 0xFF;
                context["dpi_hi"] = (dpiX >> 8) & 0xFF;

                context["compxDpiLow"] = dpiLow;
                context["compxDpiFlags"] = dpiFlags;
                context["compxDpiParity"] = dpiParity;
                context["compxDpiAddrLo"] = dpiAddr & 0xFF;
                context["compxDpiAddrHi"] = (dpiAddr >> 8) & 0xFF;

                string tpl = ExpressionEvaluator.ExpandTemplate(def.SetDpiTxTemplate, context, def.Variables);

                byte[] tx = ApplyReportId(ParseHexBytes(tpl), def.ReportId);
                if (tx == null || tx.Length == 0) return false;

                int reqLen = def.ReportLength > 0 ? def.ReportLength : tx.Length;
                if (tx.Length < reqLen)
                {
                    byte[] padded = new byte[reqLen];
                    Array.Copy(tx, padded, tx.Length);
                    tx = padded;
                }
                else if (tx.Length > reqLen)
                {
                    byte[] trimmed = new byte[reqLen];
                    Array.Copy(tx, trimmed, reqLen);
                    tx = trimmed;
                }

                ApplyChecksum(tx, def.ChecksumType, def.VendorId, def.ReportId);

                bool sent = SendReportToDevice(def, tx);

                // CompX / VGN architecture or generic SetDpiValueTxTemplate: write precise DPI resolution to EEPROM
                if (sent)
                {
                    if (!string.IsNullOrEmpty(def.SetDpiValueTxTemplate))
                    {
                        string valTpl = ExpressionEvaluator.ExpandTemplate(def.SetDpiValueTxTemplate, context, def.Variables);
                        byte[] dpiValPacket = ApplyReportId(ParseHexBytes(valTpl), def.ReportId);
                        if (dpiValPacket != null && dpiValPacket.Length > 0)
                        {
                            ApplyChecksum(dpiValPacket, def.ChecksumType, def.VendorId, def.ReportId);
                            SendReportToDevice(def, dpiValPacket);
                        }
                    }
                    else if (def.VendorId == 0x3554 || string.Equals(def.ChecksumType, "VgnCompxSum55", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] dpiValPacket = new byte[17];
                        dpiValPacket[0] = 0x08; // Report ID
                        dpiValPacket[1] = 0x07; // VGN_COMMAND.write
                        dpiValPacket[2] = 0x00;
                        dpiValPacket[3] = (byte)((dpiAddr >> 8) & 0xFF);
                        dpiValPacket[4] = (byte)(dpiAddr & 0xFF);
                        dpiValPacket[5] = 0x04; // 4 bytes data
                        dpiValPacket[6] = dpiLow;
                        dpiValPacket[7] = dpiLow;
                        dpiValPacket[8] = dpiFlags;
                        dpiValPacket[9] = dpiParity;
                        ApplyChecksum(dpiValPacket, "VgnCompxSum55", def.VendorId, def.ReportId);
                        SendReportToDevice(def, dpiValPacket);
                    }
                }

                if (sent)
                {
                    def.CurrentDpi = dpiX;
                    def.CurrentDpiY = dpiY;
                    def.CurrentDpiStage = stage;
                }
                return sent;
            }
        }

        public static bool SetPollingRate(string deviceId, int rateHz)
        {
            lock (syncLock)
            {
                FdasAdapterDefinition def = FindAdapterByDeviceId(deviceId);
                if (def == null || string.IsNullOrEmpty(def.SetPollingRateTxTemplate)) return false;

                int rateIndex = 0;
                if (def.RateIndexMap != null && def.RateIndexMap.ContainsKey(rateHz))
                {
                    rateIndex = def.RateIndexMap[rateHz];
                }
                else
                {
                    switch (rateHz)
                    {
                        case 125: rateIndex = 0; break;
                        case 250: rateIndex = 1; break;
                        case 500: rateIndex = 2; break;
                        case 1000: rateIndex = 3; break;
                        case 2000: rateIndex = 4; break;
                        case 4000: rateIndex = 5; break;
                        case 8000: rateIndex = 6; break;
                        default: rateIndex = 3; break;
                    }
                }

                byte compxCode = 0x01; // 1000Hz default
                bool customFound = false;
                if (def.RateCodeMap != null)
                {
                    string rk = rateHz.ToString();
                    if (def.RateCodeMap.ContainsKey(rk))
                    {
                        compxCode = (byte)ConvertHexToInt(def.RateCodeMap[rk]);
                        customFound = true;
                    }
                }

                if (!customFound)
                {
                    switch (rateHz)
                    {
                        case 125: compxCode = 0x08; break;
                        case 250: compxCode = 0x04; break;
                        case 500: compxCode = 0x02; break;
                        case 1000: compxCode = 0x01; break;
                        case 2000: compxCode = 0x10; break;
                        case 4000: compxCode = 0x20; break;
                        case 8000: compxCode = 0x40; break;
                    }
                }

                byte compxRateParity = (byte)((0x55 - compxCode) & 0xFF);

                var context = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                context["rateHz"] = rateHz;
                context["rateIndex"] = rateIndex;
                context["rateCode"] = compxCode;
                context["rateParity"] = compxRateParity;
                context["compxRateCode"] = compxCode;
                context["compxRateParity"] = compxRateParity;

                string tpl = ExpressionEvaluator.ExpandTemplate(def.SetPollingRateTxTemplate, context, def.Variables);

                byte[] tx = ApplyReportId(ParseHexBytes(tpl), def.ReportId);
                if (tx == null || tx.Length == 0) return false;

                int reqLen = def.ReportLength > 0 ? def.ReportLength : tx.Length;
                if (tx.Length < reqLen)
                {
                    byte[] padded = new byte[reqLen];
                    Array.Copy(tx, padded, tx.Length);
                    tx = padded;
                }
                else if (tx.Length > reqLen)
                {
                    byte[] trimmed = new byte[reqLen];
                    Array.Copy(tx, trimmed, reqLen);
                    tx = trimmed;
                }

                ApplyChecksum(tx, def.ChecksumType, def.VendorId, def.ReportId);

                bool sent = SendReportToDevice(def, tx);
                if (sent)
                {
                    def.CurrentPollingRate = rateHz;
                }
                return sent;
            }
        }

        public static FdasAdapterDefinition FindAdapterByDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            lock (syncLock)
            {
                if (loadedAdapters == null || loadedAdapters.Count == 0)
                {
                    ReloadAdapters();
                }

                if (loadedAdapters != null)
                {
                    for (int i = 0; i < loadedAdapters.Count; i++)
                    {
                        string id = string.Format("FDAS:{0:X4}:{1:X4}", loadedAdapters[i].VendorId, loadedAdapters[i].ProductId);
                        if (id.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            return loadedAdapters[i];
                        }
                        string suffix = string.Format("{0:X4}:{1:X4}", loadedAdapters[i].VendorId, loadedAdapters[i].ProductId);
                        if (deviceId.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return loadedAdapters[i];
                        }
                    }
                }
            }
            return null;
        }

        public static bool IsFdasDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            if (deviceId.StartsWith("FDAS:", StringComparison.OrdinalIgnoreCase)) return true;
            return FindAdapterByDeviceId(deviceId) != null;
        }

        public static bool IsFdasMouse(MouseBatteryInfo dev)
        {
            if (dev == null) return false;
            if (dev.Category != DeviceCategory.Mouse) return false;
            return IsFdasDevice(dev.DeviceId);
        }

        private static bool SendReportToDevice(FdasAdapterDefinition def, byte[] tx)
        {
            if (def == null || tx == null || tx.Length == 0) return false;
            try
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(def.VendorId, def.ProductId);
                if (descriptors == null || descriptors.Count == 0) return false;

                HidDeviceDescriptor target = descriptors[0];
                if (def.UsagePage != 0 && def.Usage != 0)
                {
                    for (int di = 0; di < descriptors.Count; di++)
                    {
                        if (descriptors[di].UsagePage == def.UsagePage && descriptors[di].Usage == def.Usage)
                        {
                            target = descriptors[di];
                            break;
                        }
                    }
                }

                IntPtr handle = HidTransport.Open(target.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;

                try
                {


                    if (def.ReportType.Equals("Feature", StringComparison.OrdinalIgnoreCase))
                    {
                        return HidTransport.SetFeature(handle, tx);
                    }
                    else
                    {
                        return HidTransport.WriteReport(handle, tx, tx.Length, 300);
                    }
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyChecksum(byte[] buf, string checksumType, int vendorId, int reportId)
        {
            if (buf == null || buf.Length <= 1) return;

            if (string.Equals(checksumType, "None", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool isCompxSum55 = (!string.IsNullOrEmpty(checksumType) && checksumType.Equals("VgnCompxSum55", StringComparison.OrdinalIgnoreCase)) ||
                                (string.IsNullOrEmpty(checksumType) && vendorId == 0x3554);

            if (isCompxSum55)
            {
                // CompX 原生校验从 cmd 字节开始求和，不包含前导 Report ID。
                // 适配包声明了 reportId 且报文首字节确实是它时，跳过首字节，
                // 否则会整体偏低 0x08，设备直接拒收。
                // 未声明 reportId 的旧适配包 sumStart=0，行为与之前完全一致。
                int sumStart = (reportId > 0 && buf[0] == (byte)reportId) ? 1 : 0;
                byte sum = 0;
                for (int i = sumStart; i < buf.Length - 1; i++)
                {
                    sum = (byte)(sum + buf[i]);
                }
                // CompX 真实校验：尾字节 = 0x55 - sum - 8（与 VgnDeviceHelper.CalcChecksum 一致）。
                // 此前漏减 8，导致 FDAS 动态通道下发的 CompX 报文校验位偏 8，设备不认。
                buf[buf.Length - 1] = (byte)((0x55 - sum - 8) & 0xFF);
            }
            else if (!string.IsNullOrEmpty(checksumType) && checksumType.Equals("SumMod256", StringComparison.OrdinalIgnoreCase))
            {
                byte sum = 0;
                for (int i = 0; i < buf.Length - 1; i++)
                {
                    sum = (byte)(sum + buf[i]);
                }
                buf[buf.Length - 1] = sum;
            }
            else if (!string.IsNullOrEmpty(checksumType) && checksumType.Equals("XorSum", StringComparison.OrdinalIgnoreCase))
            {
                byte xor = 0;
                for (int i = 0; i < buf.Length - 1; i++)
                {
                    xor ^= buf[i];
                }
                buf[buf.Length - 1] = xor;
            }
        }

        #region JSON 解析辅助函数

        private static string ExtractJsonString(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return string.Empty;
        }

        private static int ExtractJsonInt(string json, string key, int defaultValue)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*([0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                int val;
                if (int.TryParse(match.Groups[1].Value, out val))
                {
                    return val;
                }
            }
            return defaultValue;
        }

        private static int[] ExtractJsonIntArray(string json, string key)
        {
            try
            {
                Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string inner = match.Groups[1].Value;
                    string[] parts = inner.Split(new char[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    List<int> list = new List<int>();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        int v;
                        if (int.TryParse(parts[i].Trim(), out v))
                        {
                            list.Add(v);
                        }
                    }
                    if (list.Count > 0) return list.ToArray();
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
            return null;
        }

        private static Dictionary<int, int> ExtractJsonIntMap(string json, string key)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            try
            {
                Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{([^\\}]*)\\}", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string inner = match.Groups[1].Value;
                    MatchCollection pairs = Regex.Matches(inner, "\"([0-9]+)\"\\s*:\\s*([0-9]+)");
                    foreach (Match p in pairs)
                    {
                        int k, v;
                        if (int.TryParse(p.Groups[1].Value, out k) && int.TryParse(p.Groups[2].Value, out v))
                        {
                            map[k] = v;
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
            return map;
        }

        private static Dictionary<string, string> ExtractJsonStringMap(string json, string key)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{([^\\}]*)\\}", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string inner = match.Groups[1].Value;
                    MatchCollection pairs = Regex.Matches(inner, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
                    foreach (Match p in pairs)
                    {
                        map[p.Groups[1].Value] = p.Groups[2].Value;
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
            return map;
        }

        private static string[] ExtractJsonStringArray(string json, string key)
        {
            try
            {
                Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string inner = match.Groups[1].Value;
                    MatchCollection mc = Regex.Matches(inner, "\"([^\"]*)\"");
                    List<string> list = new List<string>();
                    foreach (Match m in mc)
                    {
                        list.Add(m.Groups[1].Value);
                    }
                    if (list.Count > 0) return list.ToArray();
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
            return null;
        }

        // 解析 #RRGGBB / RRGGBB 形式的品牌色；无法解析时返回 null，
        // 调用方会回退到内置的品牌配色表，不会把颜色画成黑色。
        private static Color? ParseHexColor(string hexStr)
        {
            if (string.IsNullOrEmpty(hexStr)) return null;
            string clean = hexStr.Trim().TrimStart('#');
            if (clean.Length != 6) return null;
            try
            {
                int r = Convert.ToInt32(clean.Substring(0, 2), 16);
                int g = Convert.ToInt32(clean.Substring(2, 2), 16);
                int b = Convert.ToInt32(clean.Substring(4, 2), 16);
                return Color.FromArgb(r, g, b);
            }
            catch
            {
                return null;
            }
        }

        // 适配包声明 reportId 时，若报文首字节不是该 ID 则前插一位。
        // reportId <= 0（未声明）时原样返回，保证旧适配包零影响。
        private static byte[] ApplyReportId(byte[] tx, int reportId)
        {
            if (tx == null || tx.Length == 0 || reportId <= 0) return tx;
            if (tx[0] == (byte)reportId) return tx;
            byte[] withId = new byte[tx.Length + 1];
            withId[0] = (byte)reportId;
            Array.Copy(tx, 0, withId, 1, tx.Length);
            return withId;
        }

        // 取出形如 "key": [ ... ] 的数组块（支持嵌套），找不到返回 null。
        private static string ExtractJsonArrayBlock(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = json.IndexOf('[', idx);
            if (start < 0) return null;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        // 提取块内所有 "key": "value" 的字符串值，按出现顺序返回。
        private static List<string> ExtractJsonStringValues(string block, string key)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(block)) return list;
            MatchCollection mc = Regex.Matches(block, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            foreach (Match m in mc) list.Add(m.Groups[1].Value);
            return list;
        }

        // 型号转成可用作注册表/配置键的片段
        private static string SanitizeIdPart(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "DEV";
            StringBuilder sb = new StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length == 0 || sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string outStr = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(outStr) ? "DEV" : outStr;
        }

        private static int ConvertHexToInt(string hexStr)
        {
            if (string.IsNullOrEmpty(hexStr)) return 0;
            string clean = hexStr.Trim();
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(2);
            }
            try
            {
                return Convert.ToInt32(clean, 16);
            }
            catch
            {
                return 0;
            }
        }

        private static byte[] ParseHexBytes(string hexStr)
        {
            if (string.IsNullOrEmpty(hexStr)) return new byte[0];
            string clean = hexStr.Trim().Replace("0x", "").Replace("0X", "");
            string[] tokens = clean.Split(new char[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < tokens.Length; i++)
            {
                try
                {
                    bytes.Add(Convert.ToByte(tokens[i], 16));
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/FdasRuntimeHelper.cs: 已忽略异常", diagEx); }
            }
            return bytes.ToArray();
        }

        #endregion
    }
}
