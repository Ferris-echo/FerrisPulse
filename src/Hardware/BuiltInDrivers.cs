// ============================================================================

// 模块名称: BuiltInDrivers.cs

// 所属区域: Hardware (内置驱动包统一插件化封装层)

// 核心职责:

//   将软件内置的所有硬件协议 Helper (Razer, Logitech, Rapoo, Valkyrie, MCHOSE,

//   NuPhy, VGN, Flydigi, Sony, Apple, HyperX, SteelSeries, UniversalGaming)

//   封装为标准 IHardwareDriver 插件实例 (Priority = 100)，声明真实的

//   DeviceCapabilities，支持被外部扩展包 (Priority 200/300) 按 VID:PID 无缝热覆盖。

// 依赖关系: IHardwareDriver.cs, HardwareDriverBase.cs, HardwareDriverRegistry.cs

//

// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:

// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑时，【必须】同步更新上方说明。

// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。

// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。

// ============================================================================



using System;

using System.Collections.Generic;



namespace RazerBatteryTray

{

    public static class BuiltInDrivers

    {

        private static bool isRegistered = false;

        private static readonly object initLock = new object();



        public static void EnsureRegistered()

        {

            if (isRegistered) return;

            lock (initLock)

            {

                if (isRegistered) return;

                isRegistered = true;



                HardwareDriverRegistry.RegisterDriver(new RazerMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new RazerHeadsetDriver());

                HardwareDriverRegistry.RegisterDriver(new LogitechMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new RapooMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new ValkyrieMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new ValkyrieKeyboardDriver());

                HardwareDriverRegistry.RegisterDriver(new MchoseMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new MchoseKeyboardDriver());

                HardwareDriverRegistry.RegisterDriver(new NuphyKeyboardDriver());

                HardwareDriverRegistry.RegisterDriver(new VgnMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new FlydigiGamepadDriver());

                HardwareDriverRegistry.RegisterDriver(new SonyInzoneDriver());

                HardwareDriverRegistry.RegisterDriver(new AppleDeviceDriver());

                HardwareDriverRegistry.RegisterDriver(new HyperXHeadsetDriver());

                HardwareDriverRegistry.RegisterDriver(new SteelSeriesHeadsetDriver());

                HardwareDriverRegistry.RegisterDriver(new AcerMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new IroncatMouseDriver());

                HardwareDriverRegistry.RegisterDriver(new BlePeripheralDriver());

                HardwareDriverRegistry.RegisterDriver(new KzziKeyboardDriver());

                HardwareDriverRegistry.RegisterDriver(new UniversalGamingDriver());

            }

        }

    }



    public sealed class RazerMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:RazerMouse"; } }

        public override string Brand { get { return "Razer"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null || dev.Category != DeviceCategory.Mouse) return false;

            if (string.Equals(dev.Brand, "Razer", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(dev.Brand, "雷蛇", StringComparison.OrdinalIgnoreCase))

            {

                return true;

            }

            return dev.DeviceId != null && dev.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;



            int vid, pid;

            if (!TryExtractDeviceVidPid(dev, out vid, out pid)) pid = 0;

            RazerDeviceHelper.RazerDpiCapability rzCap = RazerDeviceHelper.GetDpiCapability(dev.DisplayName, pid);



            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = true;

            dev.Capabilities.SupportsDpiStages = true;

            dev.Capabilities.SupportsPollingRate = true;
            dev.Capabilities.SupportsFastDpiMonitor = true;

            dev.Capabilities.SupportsXyIndependence = true;

            dev.Capabilities.SupportsHardwareDpiMonitor = true;

            dev.Capabilities.MinDpi = rzCap != null && rzCap.MinDpi > 0 ? rzCap.MinDpi : 100;

            dev.Capabilities.MaxDpi = rzCap != null && rzCap.MaxDpi > 0 ? rzCap.MaxDpi : 30000;

            dev.Capabilities.DpiStep = rzCap != null && rzCap.Step > 0 ? rzCap.Step : 50;

            dev.Capabilities.SensorDescription = rzCap != null && !string.IsNullOrEmpty(rzCap.Description)

                ? rzCap.Description

                : "Razer Focus Pro 高精度光学传感器";

            if (dev.SupportedPollingRates != null && dev.SupportedPollingRates.Length > 0)

            {

                dev.Capabilities.SupportedPollingRates = dev.SupportedPollingRates;

            }

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = RazerDeviceHelper.QueryAllRazerDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }



        public override bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)

        {

            string devId = dev != null ? dev.DeviceId : null;

            return RazerDeviceHelper.FastQueryDpi(out dpiX, out dpiY, out stage, out count, devId);

        }



        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)

        {

            string devId = dev != null ? dev.DeviceId : null;

            if (stage > 0)

            {

                return RazerDeviceHelper.SetRazerDpiStage(stage, devId);

            }

            return RazerDeviceHelper.SetRazerDpi(dpiX, dpiY > 0 ? dpiY : dpiX, devId);

        }



        public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)

        {

            string devId = dev != null ? dev.DeviceId : null;

            return RazerDeviceHelper.UpdateRazerHardwareStages(activeStage, stagesX, stagesY, devId);

        }



        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)

        {

            string devId = dev != null ? dev.DeviceId : null;

            return RazerDeviceHelper.SetRazerPollingRate(rateHz, devId);

        }

    }



    public sealed class RazerHeadsetDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:RazerHeadset"; } }

        public override string Brand { get { return "Razer"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            if (dev.Category != DeviceCategory.Headset && dev.Category != DeviceCategory.Earbuds) return false;

            return string.Equals(dev.Brand, "Razer", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "雷蛇", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = RazerHeadsetHelper.QueryAllRazerHeadsets();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }



    public sealed class LogitechMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:LogitechMouse"; } }

        public override string Brand { get { return "Logitech"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "Logitech", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "罗技", StringComparison.OrdinalIgnoreCase) ||

                   (dev.DeviceId != null && dev.DeviceId.StartsWith("Logitech:046D:", StringComparison.OrdinalIgnoreCase));

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;



            bool isMouse = (dev.Category == DeviceCategory.Mouse);

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = isMouse;

            dev.Capabilities.SupportsDpiStages = isMouse;

            dev.Capabilities.SupportsPollingRate = isMouse;

            dev.Capabilities.SupportsXyIndependence = false;

            dev.Capabilities.SupportsHardwareDpiMonitor = isMouse;

            dev.Capabilities.MinDpi = 100;

            dev.Capabilities.DpiStep = 50;



            int maxDpi = 25600;

            string sensorDesc = "罗技 HERO 25K / HERO 2 高精度光学传感器";

            string nameLower = (dev.DisplayName ?? "").ToLowerInvariant();

            if (nameLower.Contains("superlight 2 dex") || nameLower.Contains("dex") || nameLower.Contains("gpw 5") || nameLower.Contains("gpw5"))

            {

                maxDpi = 48000;

                sensorDesc = "罗技 HERO 2 旗舰级光学传感器 (最高 48,000 DPI / 888 IPS)";

            }

            else if (nameLower.Contains("superlight 2") || nameLower.Contains("gpx 2") || nameLower.Contains("gpx2") || nameLower.Contains("gpw 4") || nameLower.Contains("gpw4") || nameLower.Contains("2c"))

            {

                maxDpi = 44000;

                sensorDesc = "罗技 HERO 2 高精度光学传感器 (最高 44,000 DPI / 888 IPS)";

            }

            dev.Capabilities.MaxDpi = maxDpi;

            dev.Capabilities.SensorDescription = sensorDesc;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = LogitechDeviceHelper.QueryAllLogitechDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }



        public override bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)

        {

            return LogitechDeviceHelper.FastQueryDpi(out dpiX, out dpiY, out stage, out count);

        }



        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)

        {

            string devId = dev != null ? dev.DeviceId : null;

            return LogitechDeviceHelper.SetLogitechDpi(dpiX, dpiY > 0 ? dpiY : dpiX, devId);

        }



        public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)

        {

            if (stagesX == null || stagesX.Length == 0) return false;

            int idx = Math.Max(0, Math.Min(stagesX.Length - 1, activeStage - 1));

            int dx = stagesX[idx];

            int dy = (stagesY != null && stagesY.Length > idx) ? stagesY[idx] : dx;

            string devId = dev != null ? dev.DeviceId : null;

            return LogitechDeviceHelper.SetLogitechDpi(dx, dy, devId);

        }



        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)

        {

            string devId = dev != null ? dev.DeviceId : null;

            return LogitechDeviceHelper.SetLogitechPollingRate(rateHz, devId);

        }

    }



    public sealed class RapooMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:RapooMouse"; } }

        public override string Brand { get { return "Rapoo"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "Rapoo", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "雷柏", StringComparison.OrdinalIgnoreCase) ||

                   (dev.DeviceId != null && dev.DeviceId.StartsWith("Rapoo:24AE:", StringComparison.OrdinalIgnoreCase));

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;



            bool isMouse = (dev.Category == DeviceCategory.Mouse);

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = isMouse;

            dev.Capabilities.SupportsDpiStages = isMouse;

            dev.Capabilities.SupportsPollingRate = isMouse;

            dev.Capabilities.SupportsXyIndependence = isMouse;

            dev.Capabilities.SupportsHardwareDpiMonitor = isMouse;

            dev.Capabilities.MinDpi = 50;

            dev.Capabilities.MaxDpi = 26000;

            dev.Capabilities.DpiStep = 10;

            dev.Capabilities.SensorDescription = "雷柏原相高精度电竞光学传感器 (PAW3398/3395/3950，最高 26,000 DPI)";

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            MouseBatteryInfo info = RapooDeviceHelper.QueryRapooDeviceInfo();

            if (info != null && (info.IsConnected || info.IsDonglePresent))

            {

                info.Category = DeviceCategory.Mouse;

                PopulateCapabilities(info);

                list.Add(info);

            }

            return list;

        }



        public override bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)

        {

            return RapooDeviceHelper.FastQueryDpi(out dpiX, out dpiY, out stage, out count);

        }



        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)

        {

            if (stage > 0)

            {

                return RapooDeviceHelper.SetRapooDpiStage(stage);

            }

            return RapooDeviceHelper.SetRapooXyDpi(dpiX, dpiY > 0 ? dpiY : dpiX, 1);

        }



        public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)

        {

            return RapooDeviceHelper.UpdateRapooHardwareStages(activeStage, stagesX, stagesY);

        }



        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)

        {

            return RapooDeviceHelper.SetRapooPollingRate(rateHz);

        }

    }



    public sealed class ValkyrieMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:ValkyrieMouse"; } }

        public override string Brand { get { return "Valkyrie"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null || dev.Category != DeviceCategory.Mouse) return false;

            return string.Equals(dev.Brand, "Valkyrie", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "瓦尔基里", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;



            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = true;

            dev.Capabilities.SupportsDpiStages = true;

            dev.Capabilities.SupportsPollingRate = true;

            dev.Capabilities.SupportsXyIndependence = true;

            dev.Capabilities.SupportsHardwareDpiMonitor = true;

            dev.Capabilities.MinDpi = 100;

            dev.Capabilities.MaxDpi = 26000;

            dev.Capabilities.DpiStep = 50;

            dev.Capabilities.SensorDescription = "瓦尔基里高精度电竞光学传感器 (PAW3395/PAW3950)";

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> all = ValkyrieDeviceHelper.QueryAllValkyrieDevices();

            List<MouseBatteryInfo> mice = new List<MouseBatteryInfo>();

            if (all != null)

            {

                for (int i = 0; i < all.Count; i++)

                {

                    if (all[i] != null && all[i].Category == DeviceCategory.Mouse)

                    {

                        PopulateCapabilities(all[i]);

                        mice.Add(all[i]);

                    }

                }

            }

            return mice;

        }



        public override bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)

        {

            return ValkyrieDeviceHelper.FastQueryDpi(out dpiX, out dpiY, out stage, out count);

        }



        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)

        {

            return ValkyrieDeviceHelper.SetValkyrieDpi(dpiX, dpiY > 0 ? dpiY : dpiX);

        }



        public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)

        {

            return ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(activeStage, stagesX, stagesY);

        }



        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)

        {

            return ValkyrieDeviceHelper.SetValkyriePollingRate(rateHz);

        }

    }



    public sealed class ValkyrieKeyboardDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:ValkyrieKeyboard"; } }

        public override string Brand { get { return "Valkyrie"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null || dev.Category != DeviceCategory.Keyboard) return false;

            return string.Equals(dev.Brand, "Valkyrie", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "瓦尔基里", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            return new List<MouseBatteryInfo>();

        }

    }



    public sealed class MchoseMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:MchoseMouse"; } }

        public override string Brand { get { return "MCHOSE"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null || dev.Category != DeviceCategory.Mouse) return false;

            return string.Equals(dev.Brand, "MCHOSE", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "Mchose", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "迈从", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;



            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = true;

            dev.Capabilities.SupportsDpiStages = true;

            dev.Capabilities.SupportsPollingRate = true;

            dev.Capabilities.SupportsXyIndependence = true;

            dev.Capabilities.SupportsHardwareDpiMonitor = false;

            dev.Capabilities.MinDpi = 50;

            dev.Capabilities.MaxDpi = 26000;

            dev.Capabilities.DpiStep = 50;

            dev.Capabilities.SensorDescription = "迈从原相高精度电竞光学传感器 (PAW3395/PAW3950，最高 26,000 DPI)";

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = MchoseMouseHelper.QueryAllMchoseMice();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }



        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)

        {

            if (stage > 0)

            {

                return MchoseMouseHelper.SetMchoseDpiStage(stage);

            }

            return MchoseMouseHelper.SetMchoseDpi(dpiX, dpiY > 0 ? dpiY : dpiX, 1);

        }



        public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)

        {

            return MchoseMouseHelper.UpdateMchoseHardwareStages(activeStage, stagesX, stagesY);

        }



        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)

        {

            return MchoseMouseHelper.SetMchosePollingRate(rateHz);

        }

    }



    public sealed class MchoseKeyboardDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:MchoseKeyboard"; } }

        public override string Brand { get { return "MCHOSE"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null || dev.Category != DeviceCategory.Keyboard) return false;

            return string.Equals(dev.Brand, "MCHOSE", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "Mchose", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "迈从", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = MchoseDeviceHelper.QueryAllMchoseDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }



    public sealed class NuphyKeyboardDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:NuPhyKeyboard"; } }

        public override string Brand { get { return "NuPhy"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "NuPhy", StringComparison.OrdinalIgnoreCase) ||

                   (dev.DeviceId != null && dev.DeviceId.StartsWith("HID:19F5:", StringComparison.OrdinalIgnoreCase));

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;



            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;



            NuphyDeviceHelper.NuphyHardwareSettings s = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();

            dev.Capabilities.CustomActions.Clear();

            dev.Capabilities.CustomActions.Add(new CustomDeviceAction

            {

                ActionId = "auto_sleep",

                Title = "自动休眠",

                ActionType = CustomActionType.Toggle,

                ActiveText = "🌙 自动休眠: 开启",

                InactiveText = "🌙 自动休眠: 关闭",

                BoolValue = s.SleepEnabled,

                IntValue = s.SleepEnabled ? 1 : 0,

                ShowInTrayMenu = true

            });

            dev.Capabilities.CustomActions.Add(new CustomDeviceAction

            {

                ActionId = "win_lock",

                Title = "Win键锁定",

                ActionType = CustomActionType.Toggle,

                ActiveText = "🔒 Win键锁定: 开启",

                InactiveText = "🔓 Win键锁定: 关闭",

                BoolValue = s.WinLock,

                IntValue = s.WinLock ? 1 : 0,

                ShowInTrayMenu = true

            });

            bool backOn = s.BacklightEnabled && s.BacklightBrightness > 0;

            dev.Capabilities.CustomActions.Add(new CustomDeviceAction

            {

                ActionId = "backlight_toggle",

                Title = "键盘背光",

                ActionType = CustomActionType.Toggle,

                ActiveText = "💡 键盘背光: 开",

                InactiveText = "💡 键盘背光: 关",

                BoolValue = backOn,

                IntValue = backOn ? 1 : 0,

                ShowInTrayMenu = false

            });

            bool sideOn = s.SidelightEnabled && s.SidelightBrightness > 0;

            dev.Capabilities.CustomActions.Add(new CustomDeviceAction

            {

                ActionId = "sidelight_toggle",

                Title = "前置灯带",

                ActionType = CustomActionType.Toggle,

                ActiveText = "✨ 前置灯带: 开",

                InactiveText = "✨ 前置灯带: 关",

                BoolValue = sideOn,

                IntValue = sideOn ? 1 : 0,

                ShowInTrayMenu = false

            });

            dev.Capabilities.CustomActions.Add(new CustomDeviceAction

            {

                ActionId = "backlight_brightness",

                Title = "背光亮度",

                ActionType = CustomActionType.Slider,

                IntValue = s.BacklightEnabled ? s.BacklightBrightness : 0,

                MinValue = 0,

                MaxValue = 100,

                ValueSuffix = "%",

                ShowInTrayMenu = false

            });

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            MouseBatteryInfo nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();

            if (nuphy != null && (nuphy.IsConnected || nuphy.IsDonglePresent))

            {

                nuphy.Category = DeviceCategory.Keyboard;

                PopulateCapabilities(nuphy);

                list.Add(nuphy);

            }

            return list;

        }



        public override bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value)

        {

            if (string.IsNullOrEmpty(actionId)) return false;

            bool ok = false;

            if (string.Equals(actionId, "auto_sleep", StringComparison.OrdinalIgnoreCase))

            {

                ok = NuphyDeviceHelper.SetAutoSleep(value != 0);

            }

            else if (string.Equals(actionId, "win_lock", StringComparison.OrdinalIgnoreCase))

            {

                ok = NuphyDeviceHelper.SetWinLock(value != 0);

            }

            else if (string.Equals(actionId, "backlight_toggle", StringComparison.OrdinalIgnoreCase))

            {

                ok = NuphyDeviceHelper.SetBacklightEnabled(value != 0);

            }

            else if (string.Equals(actionId, "sidelight_toggle", StringComparison.OrdinalIgnoreCase))

            {

                ok = NuphyDeviceHelper.SetSidelightEnabled(value != 0);

            }

            else if (string.Equals(actionId, "backlight_brightness", StringComparison.OrdinalIgnoreCase))

            {

                ok = NuphyDeviceHelper.SetBacklightBrightness(value);

            }

            if (dev != null) PopulateCapabilities(dev);

            return ok;

        }



        public override bool SyncCustomActions(MouseBatteryInfo dev)

        {

            NuphyDeviceHelper.NuphyHardwareSettings hwSettings;

            bool ok = NuphyDeviceHelper.GetHardwareSettings(out hwSettings);

            if (dev != null) PopulateCapabilities(dev);

            return ok;

        }

    }



    public sealed class VgnMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:VgnMouse"; } }

        public override string Brand { get { return "VGN"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            if (FdasRuntimeHelper.IsFdasDevice(dev.DeviceId)) return false;

            return string.Equals(dev.Brand, "VGN", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "Zaopin", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "皂品", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = true;

            dev.Capabilities.SupportsDpiStages = true;

            dev.Capabilities.SupportsPollingRate = true;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            MouseBatteryInfo vgn = VgnDeviceHelper.QueryVgnDeviceInfo();

            if (vgn != null && (vgn.IsConnected || vgn.IsDonglePresent))

            {

                vgn.Category = DeviceCategory.Mouse;

                PopulateCapabilities(vgn);

                list.Add(vgn);

            }

            return list;

        }



        public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stageIndex)

        {

            if (stageIndex > 0)

            {

                return VgnDeviceHelper.SetVgnDpiStage(stageIndex);

            }

            int activeStage = (dev != null && dev.DpiStage > 0) ? dev.DpiStage : 1;

            return VgnDeviceHelper.SetVgnDpiValue(dpiX, activeStage - 1);

        }



        public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)

        {

            return VgnDeviceHelper.SetVgnPollingRate(rateHz);

        }



        public override bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stageIndex, out int stageCount)

        {

            dpiX = 0;

            dpiY = 0;

            stageIndex = 0;

            stageCount = 0;

            int st = VgnDeviceHelper.FastQueryVgnDpiStage();

            if (st > 0)

            {

                stageIndex = st;

                if (dev != null && dev.DpiStages != null && dev.DpiStages.Length >= st)

                {

                    dpiX = dev.DpiStages[st - 1];

                    dpiY = dpiX;

                    stageCount = dev.DpiStages.Length;

                    return true;

                }

            }

            return false;

        }

    }



    public sealed class FlydigiGamepadDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:FlydigiGamepad"; } }

        public override string Brand { get { return "Flydigi"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "Flydigi", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "飞智", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = FlydigiDeviceHelper.QueryAllFlydigiDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }



    public sealed class SonyInzoneDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:SonyInzone"; } }

        public override string Brand { get { return "Sony"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "Sony", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "索尼", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = SonyInzoneHelper.QueryAllSonyInzoneDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }



    public sealed class AppleDeviceDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:AppleDevice"; } }

        public override string Brand { get { return "Apple"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "Apple", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "Beats", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = AppleDeviceHelper.QueryAllAppleDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }



    public sealed class HyperXHeadsetDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:HyperXHeadset"; } }

        public override string Brand { get { return "HyperX"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "HyperX", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "极度未知", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            MouseBatteryInfo hp = HyperXDeviceHelper.QueryHyperXDeviceInfo();

            if (hp != null && (hp.IsConnected || hp.IsDonglePresent))

            {

                PopulateCapabilities(hp);

                list.Add(hp);

            }

            return list;

        }

    }



    public sealed class SteelSeriesHeadsetDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:SteelSeriesHeadset"; } }

        public override string Brand { get { return "SteelSeries"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            return string.Equals(dev.Brand, "SteelSeries", StringComparison.OrdinalIgnoreCase) ||

                   string.Equals(dev.Brand, "赛睿", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            MouseBatteryInfo ss = SteelSeriesDeviceHelper.QuerySteelSeriesDeviceInfo();

            if (ss != null && (ss.IsConnected || ss.IsDonglePresent))

            {

                PopulateCapabilities(ss);

                list.Add(ss);

            }

            return list;

        }

    }



    public sealed class UniversalGamingDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:UniversalGaming"; } }

        public override string Brand { get { return "UniversalGaming"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null) return false;

            if (string.Equals(dev.DriverId, DriverId, StringComparison.OrdinalIgnoreCase)) return true;

            if (dev.DeviceId != null && dev.DeviceId.StartsWith("Gaming:", StringComparison.OrdinalIgnoreCase)) return true;

            return false;

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

            dev.Capabilities.SupportsXyIndependence = false;

            dev.Capabilities.SupportsHardwareDpiMonitor = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = UniversalGamingDeviceHelper.QueryAllGamingDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }



    public sealed class AcerMouseDriver : HardwareDriverBase

    {

        public override string DriverId { get { return "BuiltIn:AcerMouse"; } }

        public override string Brand { get { return "Acer"; } }

        public override int Priority { get { return 100; } }

        public override bool IsExternalPackage { get { return false; } }



        public override bool CanHandle(MouseBatteryInfo dev)

        {

            if (dev == null || dev.Category != DeviceCategory.Mouse) return false;

            if (string.Equals(dev.DriverId, DriverId, StringComparison.OrdinalIgnoreCase)) return true;

            if (string.Equals(dev.Brand, "Acer", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(dev.Brand, "宏碁", StringComparison.OrdinalIgnoreCase)) return true;

            return dev.DeviceId != null && dev.DeviceId.StartsWith("Acer:", StringComparison.OrdinalIgnoreCase);

        }



        public override void PopulateCapabilities(MouseBatteryInfo dev)

        {

            if (dev == null) return;

            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;

            dev.Capabilities.SupportsDpi = false;

            dev.Capabilities.SupportsDpiStages = false;

            dev.Capabilities.SupportsPollingRate = false;

            dev.Capabilities.SupportsXyIndependence = false;

            dev.Capabilities.SupportsHardwareDpiMonitor = false;

        }



        public override List<MouseBatteryInfo> QueryDevices()

        {

            List<MouseBatteryInfo> list = AcerDeviceHelper.QueryAllAcerDevices();

            if (list != null)

            {

                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);

            }

            return list ?? new List<MouseBatteryInfo>();

        }

    }

}

