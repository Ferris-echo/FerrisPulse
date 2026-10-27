// ============================================================================
// 模块名称: FdasRuntimeHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   FDAS v1.0 & v2.0 规范体系运行时动态声明式驱动引擎。自动扫描本地 adapters/ 目录下的所有
//   *.fp-adapter.json 适配包，支持一对多 PID 矩阵 (devices)、主题色 (brandColor)、
//   传感器描述 (sensorDescription)、板载多档位写入模板 (setStagesTxTemplate)、
//   声明式自定义动作 (customActions) 以及基于 DriverStateStore 的冷启动休眠缓存，
//   并通过 FdasJsonDriver 标准封装挂载到 HardwareDriverRegistry (Priority = 200)。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, DeviceCapabilities.cs, CustomDeviceAction.cs,
//           HardwareDriverBase.cs, HardwareDriverRegistry.cs, DriverStateStore.cs, ExpressionEvaluator.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RazerBatteryTray
{
    public class FdasDeviceEntry
    {
        public int ProductId;
        public string Model;
        public string Transport;
        public int MaxRate;
    }

    public class FdasCustomActionDef
    {
        public string ActionId;
        public string Title;
        public CustomActionType ActionType;
        public string ActiveText;
        public string InactiveText;
        public string ValueSuffix;
        public int MinValue;
        public int MaxValue;
        public int DefaultValue;
        public bool ShowInTrayMenu;
        public string TxTemplate;
        public string QueryTxHex;
        public int RxByteOffset;
        public byte RxBitMask;
    }

    public class FdasAdapterDefinition
    {
        public string FdasVersion;
        public string Brand;
        public string Model;
        public DeviceCategory Category;
        public string Author;
        public string BrandColorHex;
        public string SensorDescription;
        public bool SupportsHardwareDpiMonitor;
        public int VendorId;
        public int ProductId;
        public List<FdasDeviceEntry> Devices;
        public int UsagePage;
        public int Usage;
        public int ReportLength;
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
        public string SetStagesTxTemplate;
        public string SetPollingRateTxTemplate;
        public string ChecksumType;
        public string ReportType; // "Interrupt" or "Feature"
        public int ReadTimeoutMs;
        public int ActiveStageOffset;
        public int DpiXLoOffset;
        public int DpiXHiOffset;
        public int DpiYLoOffset;
        public int DpiYHiOffset;
        public Dictionary<string, string> Variables;
        public Dictionary<string, string> RateCodeMap;
        public List<FdasCustomActionDef> CustomActionDefs;
        public string ScriptCs;

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

        public FdasAdapterDefinition()
        {
            FdasVersion = "1.0";
            BrandColorHex = "";
            SensorDescription = "";
            Devices = new List<FdasDeviceEntry>();
            CustomActionDefs = new List<FdasCustomActionDef>();
            ActiveStageOffset = -1;
            DpiXLoOffset = -1;
            DpiXHiOffset = -1;
            DpiYLoOffset = -1;
            DpiYHiOffset = -1;
        }

        public bool MatchesPid(int pid)
        {
            if (pid == 0) return false;
            if (ProductId == pid) return true;
            if (Devices != null)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    if (Devices[i] != null && Devices[i].ProductId == pid) return true;
                }
            }
            return false;
        }

        public FdasDeviceEntry FindDeviceEntry(int pid)
        {
            if (Devices != null)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    if (Devices[i] != null && Devices[i].ProductId == pid) return Devices[i];
                }
            }
            return null;
        }
    }

    public class FdasJsonDriver : HardwareDriverBase
    {
        private readonly FdasAdapterDefinition definition;

        public FdasAdapterDefinition Definition
        {
            get { return definition; }
        }

        public FdasJsonDriver(FdasAdapterDefinition def)
        {
            this.definition = def;
            if (def != null)
            {
                this.SourceFilePath = def.FilePath ?? "";
            }
        }

        public override string DriverId
        {
            get
            {
                if (definition == null) return "fdas.unknown";
                return string.Format("fdas.{0:X4}.{1:X4}", definition.VendorId, definition.ProductId);
            }
        }

        public override string Brand
        {
            get { return definition != null ? (definition.Brand ?? "FDAS") : "FDAS"; }
        }

        public override int Priority
        {
            get { return 200; }
        }

        public override bool IsExternalPackage
        {
            get { return true; }
        }

        public override bool MatchesVidPid(int vid, int pid)
        {
            if (definition == null || vid == 0 || pid == 0) return false;
            return definition.VendorId == vid && definition.MatchesPid(pid);
        }

        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            base.PopulateCapabilities(dev);
            if (dev == null || definition == null) return;
            FdasRuntimeHelper.PopulateDeviceCapabilities(dev, definition);
        }

        public override List<MouseBatteryInfo> QueryDevices()
        {
            if (definition == null) return new List<MouseBatteryInfo>();
            return FdasRuntimeHelper.QueryDevicesForAdapter(definition);
        }

        public override bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)
        {
            return FdasRuntimeHelper.FastQueryDpiForAdapter(definition, dev, out dpiX, out dpiY, out stage, out count);
        }

        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
        {
            if (definition == null) return false;
            string devId = (dev != null && !string.IsNullOrEmpty(dev.DeviceId))
                ? dev.DeviceId
                : string.Format("FDAS:{0:X4}:{1:X4}", definition.VendorId, definition.ProductId);
            return FdasRuntimeHelper.SetDpiForAdapter(definition, devId, dpiX, dpiY, stage);
        }

        public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
        {
            if (definition == null) return false;
            return FdasRuntimeHelper.UpdateHardwareStagesForAdapter(definition, dev, activeStage, stagesX, stagesY);
        }

        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)
        {
            if (definition == null) return false;
            string devId = (dev != null && !string.IsNullOrEmpty(dev.DeviceId))
                ? dev.DeviceId
                : string.Format("FDAS:{0:X4}:{1:X4}", definition.VendorId, definition.ProductId);
            return FdasRuntimeHelper.SetPollingRateForAdapter(definition, devId, rateHz);
        }

        public override bool SyncCustomActions(MouseBatteryInfo dev)
        {
            if (definition == null || dev == null) return false;
            PopulateCapabilities(dev);
            return true;
        }

        public override bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value)
        {
            if (definition == null) return false;
            return FdasRuntimeHelper.ExecuteCustomActionForAdapter(definition, dev, actionId, value);
        }
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

        public static List<FdasAdapterDefinition> GetLoadedAdapters()
        {
            lock (syncLock)
            {
                if (loadedAdapters == null || loadedAdapters.Count == 0)
                {
                    ReloadAdapters();
                }
                return new List<FdasAdapterDefinition>(loadedAdapters);
            }
        }

        public static FdasAdapterDefinition ParseAdapterJson(string jsonText, string filePath)
        {
            if (string.IsNullOrEmpty(jsonText)) return null;
            try
            {
                string ver = ExtractJsonString(jsonText, "fdasVersion");
                if (ver != "1.0" && ver != "2.0") return null;

                FdasAdapterDefinition def = new FdasAdapterDefinition();
                def.FdasVersion = ver;
                def.FilePath = filePath;
                def.Brand = ExtractJsonString(jsonText, "brand");
                def.Model = ExtractJsonString(jsonText, "model");
                def.Author = ExtractJsonString(jsonText, "author");
                def.BrandColorHex = ExtractJsonString(jsonText, "brandColor");
                def.SensorDescription = ExtractJsonString(jsonText, "sensorDescription");

                string hwMonStr = ExtractJsonBoolOrString(jsonText, "supportsHardwareDpiMonitor");
                def.SupportsHardwareDpiMonitor = string.Equals(hwMonStr, "true", StringComparison.OrdinalIgnoreCase);

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

                def.Devices = ExtractDeviceMatrix(jsonText);
                if (def.ProductId == 0 && def.Devices.Count > 0)
                {
                    def.ProductId = def.Devices[0].ProductId;
                    if (string.IsNullOrEmpty(def.Model)) def.Model = def.Devices[0].Model;
                }
                if (def.ProductId != 0 && !def.MatchesPid(def.ProductId))
                {
                    FdasDeviceEntry primary = new FdasDeviceEntry();
                    primary.ProductId = def.ProductId;
                    primary.Model = def.Model;
                    primary.Transport = "2.4G 无线";
                    primary.MaxRate = 0;
                    def.Devices.Insert(0, primary);
                }

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
                string xyStr = ExtractJsonBoolOrString(jsonText, "supportsXyIndependence");
                def.SupportsXyIndependence = xyStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                def.ActiveStageOffset = ExtractJsonInt(jsonText, "activeStageByteOffset", -1);
                def.DpiXLoOffset = ExtractJsonInt(jsonText, "dpiXLoByteOffset", -1);
                def.DpiXHiOffset = ExtractJsonInt(jsonText, "dpiXHiByteOffset", -1);
                def.DpiYLoOffset = ExtractJsonInt(jsonText, "dpiYLoByteOffset", -1);
                def.DpiYHiOffset = ExtractJsonInt(jsonText, "dpiYHiByteOffset", -1);

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
                def.SetStagesTxTemplate = ExtractJsonString(jsonText, "setStagesTxTemplate");
                def.SetPollingRateTxTemplate = ExtractJsonString(jsonText, "setPollingRateTxTemplate");
                def.ChecksumType = ExtractJsonString(jsonText, "checksumType");
                def.Variables = ExtractJsonStringMap(jsonText, "variables");
                def.CustomActionDefs = ExtractCustomActions(jsonText);
                def.ScriptCs = ExtractJsonString(jsonText, "scriptCs");

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
            catch { }
            return null;
        }

        public static void PopulateDeviceCapabilities(MouseBatteryInfo dev, FdasAdapterDefinition def)
        {
            if (dev == null || def == null) return;
            dev.DriverId = string.Format("fdas.{0:X4}.{1:X4}", def.VendorId, def.ProductId);

            DeviceCapabilities cap = dev.Capabilities;
            if (!string.IsNullOrEmpty(def.BrandColorHex))
            {
                cap.ThemeColorHex = def.BrandColorHex;
            }
            if (!string.IsNullOrEmpty(def.SensorDescription))
            {
                cap.SensorDescription = def.SensorDescription;
            }
            else if (def.Category == DeviceCategory.Mouse)
            {
                cap.SensorDescription = string.Format("步进: {0} DPI ({1} ~ {2:N0})",
                    def.DpiStep > 0 ? def.DpiStep : 50,
                    def.MinDpi > 0 ? def.MinDpi : 50,
                    def.MaxDpi > 0 ? def.MaxDpi : 30000);
            }

            if (def.Category == DeviceCategory.Mouse)
            {
                bool hasDpiWrite = !string.IsNullOrEmpty(def.SetDpiTxTemplate) || !string.IsNullOrEmpty(def.SetDpiValueTxTemplate) || !string.IsNullOrEmpty(def.SetStagesTxTemplate);
                bool hasRateWrite = !string.IsNullOrEmpty(def.SetPollingRateTxTemplate);
                cap.SupportsDpi = hasDpiWrite;
                cap.SupportsDpiStages = hasDpiWrite;
                cap.SupportsXyIndependence = def.SupportsXyIndependence;
                cap.SupportsPollingRate = hasRateWrite;
                cap.SupportsHardwareDpiMonitor = def.SupportsHardwareDpiMonitor && !string.IsNullOrEmpty(def.QueryDpiTxHex);
                cap.MinDpi = def.MinDpi > 0 ? def.MinDpi : 50;
                cap.MaxDpi = def.MaxDpi > 0 ? def.MaxDpi : 30000;
                cap.DpiStep = def.DpiStep > 0 ? def.DpiStep : 50;
            }

            cap.CustomActions.Clear();
            if (def.CustomActionDefs != null && def.CustomActionDefs.Count > 0)
            {
                for (int i = 0; i < def.CustomActionDefs.Count; i++)
                {
                    FdasCustomActionDef ad = def.CustomActionDefs[i];
                    if (ad == null || string.IsNullOrEmpty(ad.ActionId)) continue;
                    CustomDeviceAction act = new CustomDeviceAction();
                    act.ActionId = ad.ActionId;
                    act.Title = ad.Title;
                    act.ActionType = ad.ActionType;
                    act.ActiveText = ad.ActiveText;
                    act.InactiveText = ad.InactiveText;
                    act.ValueSuffix = ad.ValueSuffix;
                    act.MinValue = ad.MinValue;
                    act.MaxValue = ad.MaxValue > ad.MinValue ? ad.MaxValue : 100;
                    act.ShowInTrayMenu = ad.ShowInTrayMenu;

                    int savedVal;
                    if (!string.IsNullOrEmpty(dev.DeviceId) && DriverStateStore.TryLoadCustomActionValue(dev.DeviceId, ad.ActionId, out savedVal))
                    {
                        act.IntValue = savedVal;
                        act.BoolValue = (savedVal != 0);
                    }
                    else
                    {
                        act.IntValue = ad.DefaultValue;
                        act.BoolValue = (ad.DefaultValue != 0);
                    }

                    cap.CustomActions.Add(act);
                }
            }
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
                            catch { }
                        }
                    }
                    catch { }
                }

                loadedAdapters = list;
                lastScanTime = DateTime.Now;
            }
        }

        public static void RegisterLoadedDriversToRegistry()
        {
            List<FdasAdapterDefinition> adapters = GetLoadedAdapters();
            for (int i = 0; i < adapters.Count; i++)
            {
                FdasAdapterDefinition def = adapters[i];
                if (def != null)
                {
                    HardwareDriverRegistry.RegisterDriver(new FdasJsonDriver(def));
                    if (!string.IsNullOrEmpty(def.ScriptCs))
                    {
                        List<string> scriptErrors;
                        List<IHardwareDriver> embedded = CsScriptDriverLoader.CompileDriversFromSource(def.ScriptCs, def.FilePath ?? "embedded.fp-driver.cs", out scriptErrors);
                        if (embedded != null)
                        {
                            for (int s = 0; s < embedded.Count; s++)
                            {
                                HardwareDriverRegistry.RegisterDriver(embedded[s]);
                            }
                        }
                    }
                }
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
                    List<MouseBatteryInfo> sub = QueryDevicesForAdapter(loadedAdapters[i]);
                    if (sub != null && sub.Count > 0)
                    {
                        result.AddRange(sub);
                    }
                }

                return result;
            }
        }

        public static List<MouseBatteryInfo> QueryDevicesForAdapter(FdasAdapterDefinition def)
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            if (def == null) return result;

            List<int> pidsToCheck = new List<int>();
            if (def.ProductId != 0) pidsToCheck.Add(def.ProductId);
            if (def.Devices != null)
            {
                for (int d = 0; d < def.Devices.Count; d++)
                {
                    if (def.Devices[d] != null && def.Devices[d].ProductId != 0 && !pidsToCheck.Contains(def.Devices[d].ProductId))
                    {
                        pidsToCheck.Add(def.Devices[d].ProductId);
                    }
                }
            }

            for (int pi = 0; pi < pidsToCheck.Count; pi++)
            {
                int currentPid = pidsToCheck[pi];
                try
                {
                    List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(def.VendorId, currentPid);
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

                    string devId = string.Format("FDAS:{0:X4}:{1:X4}", def.VendorId, currentPid);
                    int batt = def.LastBattery > 0 ? def.LastBattery : -1;
                    bool chg = def.LastCharging;

                    if (batt <= 0)
                    {
                        int cachedBatt;
                        bool cachedChg;
                        string cachedName;
                        DateTime cachedTime;
                        if (DriverStateStore.TryLoadBatteryState(devId, out cachedBatt, out cachedChg, out cachedName, out cachedTime) && cachedBatt > 0)
                        {
                            batt = cachedBatt;
                            chg = cachedChg;
                            def.LastBattery = cachedBatt;
                            def.LastCharging = cachedChg;
                        }
                    }

                    IntPtr handle = HidTransport.Open(target.DevicePath);
                    if (HidTransport.IsValidHandle(handle))
                    {
                        try
                        {
                            HidTransport.DrainReports(handle, def.ReportLength > 0 ? def.ReportLength : 64, 3);

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
                                byte[] tx = ParseHexBytes(txSteps[si]);
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

                                    ApplyChecksum(tx, def.ChecksumType, def.VendorId);

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

                            if (readOk && inBuf != null && (def.VendorId == 0x3554 || string.Equals(def.ChecksumType, "VgnCompxSum55", StringComparison.OrdinalIgnoreCase)) && inBuf.Length >= 2 && inBuf[1] != 0x04)
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

                                DriverStateStore.SaveBatteryState(devId, batt, chg, def.Brand + " " + def.Model);
                            }
                            else
                            {
                                if (def.LastBattery > 0)
                                {
                                    batt = def.LastBattery;
                                }
                                else
                                {
                                    batt = -1;
                                }
                                chg = false;
                            }
                        }
                        finally
                        {
                            HidTransport.Close(handle);
                        }
                    }

                    FdasDeviceEntry entry = def.FindDeviceEntry(currentPid);
                    string modelName = (entry != null && !string.IsNullOrEmpty(entry.Model)) ? entry.Model : def.Model;
                    string transport = (entry != null && !string.IsNullOrEmpty(entry.Transport)) ? entry.Transport : "2.4G 无线";
                    int maxRate = (entry != null && entry.MaxRate > 0) ? entry.MaxRate : (def.MaxRate > 0 ? def.MaxRate : 8000);

                    MouseBatteryInfo dev = new MouseBatteryInfo();
                    dev.DeviceId = devId;
                    dev.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", def.VendorId, currentPid);
                    dev.DriverId = string.Format("fdas.{0:X4}.{1:X4}", def.VendorId, def.ProductId);
                    dev.Brand = def.Brand;
                    dev.Category = def.Category;
                    dev.DeviceName = def.Brand + " " + modelName;
                    dev.IsDonglePresent = true;
                    dev.IsConnected = (batt >= 0);
                    dev.BatteryPercent = batt >= 0 ? batt : 0;
                    dev.IsCharging = chg;
                    dev.LastUpdated = DateTime.Now;
                    dev.Transport = transport;

                    int savedDpiX, savedDpiY, savedStage, savedRate;
                    if (DriverStateStore.TryLoadDpiState(devId, out savedDpiX, out savedDpiY, out savedStage, out savedRate))
                    {
                        if (def.CurrentDpi <= 0 && savedDpiX > 0) def.CurrentDpi = savedDpiX;
                        if (def.CurrentDpiY <= 0 && savedDpiY > 0) def.CurrentDpiY = savedDpiY;
                        if (def.CurrentDpiStage <= 0 && savedStage > 0) def.CurrentDpiStage = savedStage;
                        if (def.CurrentPollingRate <= 0 && savedRate > 0) def.CurrentPollingRate = savedRate;
                    }

                    if (def.Category == DeviceCategory.Mouse)
                    {
                        dev.DpiStages = (def.DefaultStages != null && def.DefaultStages.Length > 0)
                            ? def.DefaultStages
                            : new int[] { 400, 800, 1600, 3200, 6400 };
                        dev.DpiStageCount = dev.DpiStages.Length;
                        dev.Dpi = def.CurrentDpi > 0 ? def.CurrentDpi : (dev.DpiStages.Length > 2 ? dev.DpiStages[2] : dev.DpiStages[0]);
                        dev.DpiY = def.CurrentDpiY > 0 ? def.CurrentDpiY : dev.Dpi;
                        dev.DpiStage = def.CurrentDpiStage > 0 ? def.CurrentDpiStage : 3;
                        dev.IsXyIndependent = def.SupportsXyIndependence;

                        dev.SupportedPollingRates = (def.SupportedRates != null && def.SupportedRates.Length > 0)
                            ? def.SupportedRates
                            : new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                        dev.MaxPollingRate = maxRate;
                        dev.PollingRate = def.CurrentPollingRate > 0 ? def.CurrentPollingRate : (def.ActiveRate > 0 ? def.ActiveRate : 1000);
                    }

                    PopulateDeviceCapabilities(dev, def);
                    result.Add(dev);
                }
                catch { }
            }

            return result;
        }

        public static bool FastQueryDpiForAdapter(FdasAdapterDefinition def, MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)
        {
            dpiX = 0;
            dpiY = 0;
            stage = 0;
            count = 0;
            if (def == null || string.IsNullOrEmpty(def.QueryDpiTxHex) || def.DpiXLoOffset < 0) return false;

            int vid = def.VendorId;
            int pid = def.ProductId;
            if (dev != null)
            {
                int v, p;
                if (HardwareDriverBase.TryExtractDeviceVidPid(dev, out v, out p))
                {
                    vid = v;
                    pid = p;
                }
            }

            try
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(vid, pid);
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
                    byte[] tx = ParseHexBytes(def.QueryDpiTxHex);
                    if (tx == null || tx.Length == 0) return false;
                    int reqLen = def.ReportLength > 0 ? def.ReportLength : tx.Length;
                    if (tx.Length < reqLen)
                    {
                        byte[] padded = new byte[reqLen];
                        Array.Copy(tx, padded, tx.Length);
                        tx = padded;
                    }
                    ApplyChecksum(tx, def.ChecksumType, def.VendorId);

                    if (def.ReportType.Equals("Feature", StringComparison.OrdinalIgnoreCase))
                    {
                        HidTransport.SetFeature(handle, tx);
                    }
                    else
                    {
                        HidTransport.WriteReport(handle, tx, tx.Length, 150);
                    }

                    byte[] rx = null;
                    bool ok = false;
                    if (def.ReportType.Equals("Feature", StringComparison.OrdinalIgnoreCase))
                    {
                        rx = new byte[reqLen];
                        ok = HidTransport.GetFeature(handle, rx);
                    }
                    else
                    {
                        ok = HidTransport.ReadReport(handle, reqLen, 150, out rx);
                    }

                    if (!ok || rx == null || def.DpiXLoOffset >= rx.Length) return false;
                    int loX = rx[def.DpiXLoOffset];
                    int hiX = (def.DpiXHiOffset >= 0 && def.DpiXHiOffset < rx.Length) ? rx[def.DpiXHiOffset] : 0;
                    dpiX = (hiX << 8) | loX;

                    if (def.DpiYLoOffset >= 0 && def.DpiYLoOffset < rx.Length)
                    {
                        int loY = rx[def.DpiYLoOffset];
                        int hiY = (def.DpiYHiOffset >= 0 && def.DpiYHiOffset < rx.Length) ? rx[def.DpiYHiOffset] : 0;
                        dpiY = (hiY << 8) | loY;
                    }
                    else
                    {
                        dpiY = dpiX;
                    }

                    if (def.ActiveStageOffset >= 0 && def.ActiveStageOffset < rx.Length)
                    {
                        stage = rx[def.ActiveStageOffset];
                    }
                    else
                    {
                        stage = def.CurrentDpiStage > 0 ? def.CurrentDpiStage : 1;
                    }

                    count = (def.DefaultStages != null && def.DefaultStages.Length > 0) ? def.DefaultStages.Length : 5;
                    return dpiX > 0;
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

        public static bool SetDpi(string deviceId, int dpiX, int dpiY, int stage)
        {
            lock (syncLock)
            {
                FdasAdapterDefinition def = FindAdapterByDeviceId(deviceId);
                return SetDpiForAdapter(def, deviceId, dpiX, dpiY, stage);
            }
        }

        public static bool SetDpiForAdapter(FdasAdapterDefinition def, string deviceId, int dpiX, int dpiY, int stage)
        {
            if (def == null || string.IsNullOrEmpty(def.SetDpiTxTemplate)) return false;

            int st0 = Math.Max(0, stage - 1);
            byte stageParity = (byte)((0x55 - stage) & 0xFF);
            byte stage0Parity = (byte)((0x55 - st0) & 0xFF);

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
            context["dpiLow"] = dpiLow;
            context["dpiHigh"] = dpiHigh;

            context["compxDpiLow"] = dpiLow;
            context["compxDpiFlags"] = dpiFlags;
            context["compxDpiParity"] = dpiParity;
            context["compxDpiAddrLo"] = dpiAddr & 0xFF;
            context["compxDpiAddrHi"] = (dpiAddr >> 8) & 0xFF;

            string tpl = ExpressionEvaluator.ExpandTemplate(def.SetDpiTxTemplate, context, def.Variables);

            byte[] tx = ParseHexBytes(tpl);
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

            ApplyChecksum(tx, def.ChecksumType, def.VendorId);

            int targetPid = ResolveTargetPid(def, deviceId);
            bool sent = SendReportToDevice(def, targetPid, tx);

            if (sent)
            {
                if (!string.IsNullOrEmpty(def.SetDpiValueTxTemplate))
                {
                    string valTpl = ExpressionEvaluator.ExpandTemplate(def.SetDpiValueTxTemplate, context, def.Variables);
                    byte[] dpiValPacket = ParseHexBytes(valTpl);
                    if (dpiValPacket != null && dpiValPacket.Length > 0)
                    {
                        ApplyChecksum(dpiValPacket, def.ChecksumType, def.VendorId);
                        SendReportToDevice(def, targetPid, dpiValPacket);
                    }
                }
                else if (def.VendorId == 0x3554 || string.Equals(def.ChecksumType, "VgnCompxSum55", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] dpiValPacket = new byte[17];
                    dpiValPacket[0] = 0x08;
                    dpiValPacket[1] = 0x07;
                    dpiValPacket[2] = 0x00;
                    dpiValPacket[3] = (byte)((dpiAddr >> 8) & 0xFF);
                    dpiValPacket[4] = (byte)(dpiAddr & 0xFF);
                    dpiValPacket[5] = 0x04;
                    dpiValPacket[6] = dpiLow;
                    dpiValPacket[7] = dpiLow;
                    dpiValPacket[8] = dpiFlags;
                    dpiValPacket[9] = dpiParity;
                    ApplyChecksum(dpiValPacket, "VgnCompxSum55", def.VendorId);
                    SendReportToDevice(def, targetPid, dpiValPacket);
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

        public static bool UpdateHardwareStagesForAdapter(FdasAdapterDefinition def, MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
        {
            if (def == null || stagesX == null || stagesX.Length == 0) return false;
            string devId = (dev != null && !string.IsNullOrEmpty(dev.DeviceId))
                ? dev.DeviceId
                : string.Format("FDAS:{0:X4}:{1:X4}", def.VendorId, def.ProductId);

            def.DefaultStages = stagesX;
            bool allOk = true;
            for (int i = 0; i < stagesX.Length; i++)
            {
                int dx = stagesX[i];
                int dy = (stagesY != null && i < stagesY.Length && stagesY[i] > 0) ? stagesY[i] : dx;
                if (i + 1 == activeStage)
                {
                    if (!SetDpiForAdapter(def, devId, dx, dy, activeStage))
                    {
                        allOk = false;
                    }
                }
            }
            return allOk;
        }

        public static bool SetPollingRate(string deviceId, int rateHz)
        {
            lock (syncLock)
            {
                FdasAdapterDefinition def = FindAdapterByDeviceId(deviceId);
                return SetPollingRateForAdapter(def, deviceId, rateHz);
            }
        }

        public static bool SetPollingRateForAdapter(FdasAdapterDefinition def, string deviceId, int rateHz)
        {
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

            byte compxCode = 0x01;
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

            byte[] tx = ParseHexBytes(tpl);
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

            ApplyChecksum(tx, def.ChecksumType, def.VendorId);

            int targetPid = ResolveTargetPid(def, deviceId);
            bool sent = SendReportToDevice(def, targetPid, tx);
            if (sent)
            {
                def.CurrentPollingRate = rateHz;
            }
            return sent;
        }

        public static bool ExecuteCustomActionForAdapter(FdasAdapterDefinition def, MouseBatteryInfo dev, string actionId, int value)
        {
            if (def == null || string.IsNullOrEmpty(actionId) || def.CustomActionDefs == null) return false;
            FdasCustomActionDef actDef = null;
            for (int i = 0; i < def.CustomActionDefs.Count; i++)
            {
                if (def.CustomActionDefs[i] != null && string.Equals(def.CustomActionDefs[i].ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    actDef = def.CustomActionDefs[i];
                    break;
                }
            }
            if (actDef == null || string.IsNullOrEmpty(actDef.TxTemplate)) return false;

            var context = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            context["value"] = value;
            context["val"] = value;
            context["boolBit"] = value != 0 ? 1 : 0;

            string tpl = ExpressionEvaluator.ExpandTemplate(actDef.TxTemplate, context, def.Variables);
            byte[] tx = ParseHexBytes(tpl);
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

            ApplyChecksum(tx, def.ChecksumType, def.VendorId);
            string devId = (dev != null && !string.IsNullOrEmpty(dev.DeviceId)) ? dev.DeviceId : "";
            int targetPid = ResolveTargetPid(def, devId);
            return SendReportToDevice(def, targetPid, tx);
        }

        private static int ResolveTargetPid(FdasAdapterDefinition def, string deviceId)
        {
            if (def == null) return 0;
            int vid, pid;
            if (!string.IsNullOrEmpty(deviceId) && HardwareDriverBase.TryExtractVidPidFromString(deviceId, out vid, out pid))
            {
                if (def.MatchesPid(pid)) return pid;
            }
            return def.ProductId;
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
                    int vid, pid;
                    bool hasVidPid = HardwareDriverBase.TryExtractVidPidFromString(deviceId, out vid, out pid);
                    for (int i = 0; i < loadedAdapters.Count; i++)
                    {
                        FdasAdapterDefinition def = loadedAdapters[i];
                        if (hasVidPid && def.VendorId == vid && def.MatchesPid(pid))
                        {
                            return def;
                        }
                        string id = string.Format("FDAS:{0:X4}:{1:X4}", def.VendorId, def.ProductId);
                        if (id.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            return def;
                        }
                        string suffix = string.Format("{0:X4}:{1:X4}", def.VendorId, def.ProductId);
                        if (deviceId.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return def;
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

        private static bool SendReportToDevice(FdasAdapterDefinition def, int targetPid, byte[] tx)
        {
            if (def == null || tx == null || tx.Length == 0) return false;
            int pidToUse = targetPid != 0 ? targetPid : def.ProductId;
            try
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(def.VendorId, pidToUse);
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

        private static void ApplyChecksum(byte[] buf, string checksumType, int vendorId)
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
                byte sum = 0;
                for (int i = 0; i < buf.Length - 1; i++)
                {
                    sum = (byte)(sum + buf[i]);
                }
                buf[buf.Length - 1] = (byte)((0x55 - sum) & 0xFF);
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

        private static List<FdasDeviceEntry> ExtractDeviceMatrix(string json)
        {
            List<FdasDeviceEntry> list = new List<FdasDeviceEntry>();
            try
            {
                Match arrMatch = Regex.Match(json, "\"devices\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (arrMatch.Success)
                {
                    string body = arrMatch.Groups[1].Value;
                    MatchCollection objs = Regex.Matches(body, "\\{([^\\}]*)\\}", RegexOptions.Singleline);
                    foreach (Match m in objs)
                    {
                        string objText = m.Groups[1].Value;
                        string pidStr = ExtractJsonString(objText, "productId");
                        int pid = ConvertHexToInt(pidStr);
                        if (pid != 0)
                        {
                            FdasDeviceEntry entry = new FdasDeviceEntry();
                            entry.ProductId = pid;
                            entry.Model = ExtractJsonString(objText, "model");
                            entry.Transport = ExtractJsonString(objText, "transport");
                            entry.MaxRate = ExtractJsonInt(objText, "maxRate", 0);
                            list.Add(entry);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private static List<FdasCustomActionDef> ExtractCustomActions(string json)
        {
            List<FdasCustomActionDef> list = new List<FdasCustomActionDef>();
            try
            {
                int idx = json.IndexOf("\"customActions\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return list;
                int openBracket = json.IndexOf('[', idx);
                if (openBracket < 0) return list;

                int depth = 0;
                int closeBracket = -1;
                for (int i = openBracket; i < json.Length; i++)
                {
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            closeBracket = i;
                            break;
                        }
                    }
                }
                if (closeBracket <= openBracket) return list;

                string arrayBody = json.Substring(openBracket + 1, closeBracket - openBracket - 1);
                int objStart = -1;
                int braceDepth = 0;
                for (int i = 0; i < arrayBody.Length; i++)
                {
                    if (arrayBody[i] == '{')
                    {
                        if (braceDepth == 0) objStart = i;
                        braceDepth++;
                    }
                    else if (arrayBody[i] == '}')
                    {
                        braceDepth--;
                        if (braceDepth == 0 && objStart >= 0)
                        {
                            string objText = arrayBody.Substring(objStart, i - objStart + 1);
                            string actId = ExtractJsonString(objText, "actionId");
                            if (!string.IsNullOrEmpty(actId))
                            {
                                FdasCustomActionDef ad = new FdasCustomActionDef();
                                ad.ActionId = actId;
                                ad.Title = ExtractJsonString(objText, "title");
                                string typeStr = ExtractJsonString(objText, "type");
                                ad.ActionType = CustomActionType.Toggle;
                                if (string.Equals(typeStr, "Slider", StringComparison.OrdinalIgnoreCase)) ad.ActionType = CustomActionType.Slider;
                                else if (string.Equals(typeStr, "Button", StringComparison.OrdinalIgnoreCase)) ad.ActionType = CustomActionType.Button;

                                ad.ActiveText = ExtractJsonString(objText, "activeText");
                                ad.InactiveText = ExtractJsonString(objText, "inactiveText");
                                ad.ValueSuffix = ExtractJsonString(objText, "valueSuffix");
                                if (string.IsNullOrEmpty(ad.ValueSuffix)) ad.ValueSuffix = "%";
                                ad.MinValue = ExtractJsonInt(objText, "minValue", 0);
                                ad.MaxValue = ExtractJsonInt(objText, "maxValue", 100);
                                ad.DefaultValue = ExtractJsonInt(objText, "defaultValue", 0);
                                string trayStr = ExtractJsonBoolOrString(objText, "showInTrayMenu");
                                ad.ShowInTrayMenu = !string.Equals(trayStr, "false", StringComparison.OrdinalIgnoreCase);
                                ad.TxTemplate = ExtractJsonString(objText, "txTemplate");
                                ad.QueryTxHex = ExtractJsonString(objText, "queryTxHex");
                                ad.RxByteOffset = ExtractJsonInt(objText, "rxByteOffset", -1);
                                string maskStr = ExtractJsonString(objText, "rxBitMask");
                                ad.RxBitMask = !string.IsNullOrEmpty(maskStr) ? (byte)ConvertHexToInt(maskStr) : (byte)0xFF;
                                list.Add(ad);
                            }
                            objStart = -1;
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private static string ExtractJsonString(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return string.Empty;
        }

        private static string ExtractJsonBoolOrString(string json, string key)
        {
            string s = ExtractJsonString(json, key);
            if (!string.IsNullOrEmpty(s)) return s;
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return string.Empty;
        }

        private static int ExtractJsonInt(string json, string key, int defaultValue)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9]+)", RegexOptions.IgnoreCase);
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
            return null;
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
                catch { }
            }
            return bytes.ToArray();
        }

        #endregion
    }
}
