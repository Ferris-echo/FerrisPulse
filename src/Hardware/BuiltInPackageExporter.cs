// ============================================================================
// 模块名称: BuiltInPackageExporter.cs
// 所属区域: Hardware (内置扩展包模板导出器层)
// 核心职责:
//   提供将官方内置厂商的标准化 FDAS v2.0 (.fp-adapter.json) 与动态脚本 (.fp-driver.cs)
//   参考扩展包一键导出至指定目录（或本地 adapters/ 目录）的能力，方便用户查看、修改或热覆盖。
// 依赖关系: FdasRuntimeHelper.cs, System.IO
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace RazerBatteryTray
{
    public static class BuiltInPackageExporter
    {
        public static int ExportDefaultPackages(string targetDir, bool overwrite)
        {
            if (string.IsNullOrEmpty(targetDir))
            {
                targetDir = FdasRuntimeHelper.GetAdaptersDirectory();
            }
            if (string.IsNullOrEmpty(targetDir)) return 0;

            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                int count = 0;
                UTF8Encoding utf8Bom = new UTF8Encoding(true);

                string razerPath = Path.Combine(targetDir, "template_razer_deathadder_v4_pro.fp-adapter.json.template");
                if (overwrite || !File.Exists(razerPath))
                {
                    File.WriteAllText(razerPath, GetRazerDeathAdderV4ProTemplateJson(), utf8Bom);
                    count++;
                }

                string mchosePath = Path.Combine(targetDir, "template_mchose_a5_series.fp-adapter.json.template");
                if (overwrite || !File.Exists(mchosePath))
                {
                    File.WriteAllText(mchosePath, GetMchoseV2TemplateJson(), utf8Bom);
                    count++;
                }

                string vgnPath = Path.Combine(targetDir, "template_vgn_compx_series.fp-adapter.json.template");
                if (overwrite || !File.Exists(vgnPath))
                {
                    File.WriteAllText(vgnPath, GetVgnCompxV2TemplateJson(), utf8Bom);
                    count++;
                }

                string kbPath = Path.Combine(targetDir, "template_keyboard_custom_actions.fp-adapter.json.template");
                if (overwrite || !File.Exists(kbPath))
                {
                    File.WriteAllText(kbPath, GetKeyboardCustomActionsTemplateJson(), utf8Bom);
                    count++;
                }

                string scriptPath = Path.Combine(targetDir, "template_custom_driver.fp-driver.cs.template");
                if (overwrite || !File.Exists(scriptPath))
                {
                    File.WriteAllText(scriptPath, GetCustomScriptDriverTemplateCs(), utf8Bom);
                    count++;
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetRazerDeathAdderV4ProTemplateJson()
        {
            return "{\r\n" +
                   "  \"fdasVersion\": \"2.0\",\r\n" +
                   "  \"metadata\": {\r\n" +
                   "    \"brand\": \"Razer\",\r\n" +
                   "    \"model\": \"DeathAdder V4 Pro (Template)\",\r\n" +
                   "    \"category\": \"Mouse\",\r\n" +
                   "    \"brandColor\": \"#00FF00\",\r\n" +
                   "    \"author\": \"FerrisPulse Official\",\r\n" +
                   "    \"description\": \"将文件名末尾的 .template 去掉即可作为外部扩展包生效并覆盖内置同 VID:PID 驱动\"\r\n" +
                   "  },\r\n" +
                   "  \"hardware\": {\r\n" +
                   "    \"vendorId\": \"0x1532\",\r\n" +
                   "    \"productId\": \"0x00BE\",\r\n" +
                   "    \"devices\": [\r\n" +
                   "      { \"productId\": \"0x00BE\", \"model\": \"Razer DeathAdder V4 Pro (2.4G)\", \"transport\": \"2.4G 无线\", \"maxRate\": 8000 },\r\n" +
                   "      { \"productId\": \"0x00BF\", \"model\": \"Razer DeathAdder V4 Pro (Wired)\", \"transport\": \"USB 有线\", \"maxRate\": 8000 }\r\n" +
                   "    ],\r\n" +
                   "    \"endpointTopology\": {\r\n" +
                   "      \"usagePage\": \"0x0001\",\r\n" +
                   "      \"usage\": \"0x0002\",\r\n" +
                   "      \"reportType\": \"Feature\",\r\n" +
                   "      \"reportLength\": 91\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"capabilities\": {\r\n" +
                   "    \"battery\": {\r\n" +
                   "      \"reportId\": \"0x00\",\r\n" +
                   "      \"batteryPercentByteOffset\": 10,\r\n" +
                   "      \"chargingByteOffset\": 9,\r\n" +
                   "      \"chargingBitMask\": \"0x01\",\r\n" +
                   "      \"batteryFormula\": \"(b[10] * 100) / 255\",\r\n" +
                   "      \"sampleRxHex\": \"00 02 1F 00 00 00 02 07 80 01 CC 00 00 00 00 00 00 00 00 00\"\r\n" +
                   "    },\r\n" +
                   "    \"mouse\": {\r\n" +
                   "      \"dpi\": {\r\n" +
                   "        \"minDpi\": 100,\r\n" +
                   "        \"maxDpi\": 35000,\r\n" +
                   "        \"step\": 1,\r\n" +
                   "        \"sensorDescription\": \"Focus Pro 35K Gen-2 · 1-DPI 精准步进 (100 ~ 35,000 DPI)\",\r\n" +
                   "        \"defaultStages\": [400, 800, 1600, 3200, 6400],\r\n" +
                   "        \"supportsXyIndependence\": true\r\n" +
                   "      },\r\n" +
                   "      \"pollingRate\": {\r\n" +
                   "        \"supportedRates\": [125, 250, 500, 1000, 2000, 4000, 8000],\r\n" +
                   "        \"defaultRate\": 1000,\r\n" +
                   "        \"maxRate\": 8000\r\n" +
                   "      }\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"protocol\": {\r\n" +
                   "    \"queryBatteryTxHex\": \"00 00 1F 00 00 00 02 07 80 00 00 00 00 00 00 00 00 00 00 00\",\r\n" +
                   "    \"setDpiTxTemplate\": \"00 00 1F 00 00 00 07 04 05 01 {stage} {dpiX_hi} {dpiX_lo} {dpiY_hi} {dpiY_lo} 00 00\",\r\n" +
                   "    \"setPollingRateTxTemplate\": \"00 00 1F 00 00 00 02 00 40 01 {rateIndex} 00 00 00 00 00\",\r\n" +
                   "    \"checksumType\": \"XorSum\"\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        private static string GetMchoseV2TemplateJson()
        {
            return "{\r\n" +
                   "  \"fdasVersion\": \"2.0\",\r\n" +
                   "  \"metadata\": {\r\n" +
                   "    \"brand\": \"MCHOSE\",\r\n" +
                   "    \"model\": \"A5 Series (Template)\",\r\n" +
                   "    \"category\": \"Mouse\",\r\n" +
                   "    \"brandColor\": \"#7E57C2\",\r\n" +
                   "    \"author\": \"FerrisPulse Official\",\r\n" +
                   "    \"description\": \"将文件名末尾的 .template 去掉即可作为外部扩展包生效并覆盖内置同 VID:PID 驱动\"\r\n" +
                   "  },\r\n" +
                   "  \"hardware\": {\r\n" +
                   "    \"vendorId\": \"0x3837\",\r\n" +
                   "    \"productId\": \"0x1101\",\r\n" +
                   "    \"devices\": [\r\n" +
                   "      { \"productId\": \"0x1101\", \"model\": \"A5 V2 Ultra (2.4G)\", \"transport\": \"2.4G 无线\", \"maxRate\": 8000 }\r\n" +
                   "    ],\r\n" +
                   "    \"endpointTopology\": {\r\n" +
                   "      \"usagePage\": \"0xFFA0\",\r\n" +
                   "      \"usage\": \"0x0001\",\r\n" +
                   "      \"reportType\": \"Interrupt\",\r\n" +
                   "      \"reportLength\": 64\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"capabilities\": {\r\n" +
                   "    \"battery\": {\r\n" +
                   "      \"reportId\": \"0x11\",\r\n" +
                   "      \"batteryPercentByteOffset\": 4,\r\n" +
                   "      \"chargingByteOffset\": 5,\r\n" +
                   "      \"chargingBitMask\": \"0x01\",\r\n" +
                   "      \"batteryFormula\": \"DirectPercent\",\r\n" +
                   "      \"sampleRxHex\": \"11 02 04 01 58 00\"\r\n" +
                   "    },\r\n" +
                   "    \"mouse\": {\r\n" +
                   "      \"dpi\": {\r\n" +
                   "        \"minDpi\": 50,\r\n" +
                   "        \"maxDpi\": 26000,\r\n" +
                   "        \"step\": 50,\r\n" +
                   "        \"sensorDescription\": \"PAW3395 旗舰光学引擎 (50 ~ 26,000 DPI)\",\r\n" +
                   "        \"defaultStages\": [400, 800, 1600, 3200, 6400],\r\n" +
                   "        \"supportsXyIndependence\": true\r\n" +
                   "      },\r\n" +
                   "      \"pollingRate\": {\r\n" +
                   "        \"supportedRates\": [125, 250, 500, 1000, 2000, 4000, 8000],\r\n" +
                   "        \"defaultRate\": 1000,\r\n" +
                   "        \"maxRate\": 8000\r\n" +
                   "      }\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"protocol\": {\r\n" +
                   "    \"queryBatteryTxHex\": \"11 02 04 00 00 00\",\r\n" +
                   "    \"setDpiTxTemplate\": \"11 02 10 {stage} {dpiX_lo} {dpiX_hi} {dpiY_lo} {dpiY_hi}\",\r\n" +
                   "    \"setPollingRateTxTemplate\": \"11 02 20 {rateIndex} 00\",\r\n" +
                   "    \"checksumType\": \"SumMod256\"\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        private static string GetVgnCompxV2TemplateJson()
        {
            return "{\r\n" +
                   "  \"fdasVersion\": \"2.0\",\r\n" +
                   "  \"metadata\": {\r\n" +
                   "    \"brand\": \"VGN\",\r\n" +
                   "    \"model\": \"Dragonfly Series (Compx Template)\",\r\n" +
                   "    \"category\": \"Mouse\",\r\n" +
                   "    \"brandColor\": \"#FF7800\",\r\n" +
                   "    \"author\": \"FerrisPulse Official\"\r\n" +
                   "  },\r\n" +
                   "  \"hardware\": {\r\n" +
                   "    \"vendorId\": \"0x3554\",\r\n" +
                   "    \"productId\": \"0xFB3E\",\r\n" +
                   "    \"devices\": [\r\n" +
                   "      { \"productId\": \"0xFB3E\", \"model\": \"Dragonfly F2 Pro Max\", \"transport\": \"2.4G 无线\", \"maxRate\": 8000 }\r\n" +
                   "    ],\r\n" +
                   "    \"endpointTopology\": {\r\n" +
                   "      \"usagePage\": \"0xFF02\",\r\n" +
                   "      \"usage\": \"0x0002\",\r\n" +
                   "      \"reportType\": \"Interrupt\",\r\n" +
                   "      \"reportLength\": 17\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"capabilities\": {\r\n" +
                   "    \"battery\": {\r\n" +
                   "      \"reportId\": \"0x08\",\r\n" +
                   "      \"batteryPercentByteOffset\": 6,\r\n" +
                   "      \"chargingByteOffset\": 7,\r\n" +
                   "      \"chargingBitMask\": \"0x01\",\r\n" +
                   "      \"batteryFormula\": \"DirectPercent\",\r\n" +
                   "      \"sampleRxHex\": \"08 04 00 00 00 02 64 00\"\r\n" +
                   "    },\r\n" +
                   "    \"mouse\": {\r\n" +
                   "      \"dpi\": {\r\n" +
                   "        \"minDpi\": 50,\r\n" +
                   "        \"maxDpi\": 30000,\r\n" +
                   "        \"step\": 50,\r\n" +
                   "        \"sensorDescription\": \"PAW3950 / PAW3395 (50 ~ 30,000 DPI)\",\r\n" +
                   "        \"defaultStages\": [400, 800, 1600, 3200],\r\n" +
                   "        \"supportsXyIndependence\": false\r\n" +
                   "      },\r\n" +
                   "      \"pollingRate\": {\r\n" +
                   "        \"supportedRates\": [125, 250, 500, 1000, 2000, 4000, 8000],\r\n" +
                   "        \"defaultRate\": 1000,\r\n" +
                   "        \"maxRate\": 8000\r\n" +
                   "      }\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"protocol\": {\r\n" +
                   "    \"queryBatteryTxHex\": \"08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49\",\r\n" +
                   "    \"setDpiTxTemplate\": \"08 07 00 00 04 02 {stage0} {stage0Parity} 00 00 00 00 00 00 00 00 00\",\r\n" +
                   "    \"setPollingRateTxTemplate\": \"08 07 00 00 00 02 {rateCode} {rateParity} 00 00 00 00 00 00 00 00 00\",\r\n" +
                   "    \"checksumType\": \"VgnCompxSum55\"\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        private static string GetKeyboardCustomActionsTemplateJson()
        {
            return "{\r\n" +
                   "  \"fdasVersion\": \"2.0\",\r\n" +
                   "  \"metadata\": {\r\n" +
                   "    \"brand\": \"CustomKeyboard\",\r\n" +
                   "    \"model\": \"Pro 75 Wireless (Template)\",\r\n" +
                   "    \"category\": \"Keyboard\",\r\n" +
                   "    \"brandColor\": \"#00E5FF\",\r\n" +
                   "    \"author\": \"FerrisPulse Official\"\r\n" +
                   "  },\r\n" +
                   "  \"hardware\": {\r\n" +
                   "    \"vendorId\": \"0x2E8A\",\r\n" +
                   "    \"productId\": \"0xFEA0\",\r\n" +
                   "    \"endpointTopology\": {\r\n" +
                   "      \"usagePage\": \"0xFF00\",\r\n" +
                   "      \"usage\": \"0x0001\",\r\n" +
                   "      \"reportType\": \"Feature\",\r\n" +
                   "      \"reportLength\": 64\r\n" +
                   "    }\r\n" +
                   "  },\r\n" +
                   "  \"capabilities\": {\r\n" +
                   "    \"battery\": {\r\n" +
                   "      \"reportId\": \"0x05\",\r\n" +
                   "      \"batteryPercentByteOffset\": 4,\r\n" +
                   "      \"chargingByteOffset\": 5,\r\n" +
                   "      \"chargingBitMask\": \"0x01\",\r\n" +
                   "      \"batteryFormula\": \"DirectPercent\",\r\n" +
                   "      \"sampleRxHex\": \"05 81 00 00 64 01\"\r\n" +
                   "    },\r\n" +
                   "    \"customActions\": [\r\n" +
                   "      {\r\n" +
                   "        \"actionId\": \"auto_sleep\",\r\n" +
                   "        \"title\": \"自动休眠\",\r\n" +
                   "        \"type\": \"Toggle\",\r\n" +
                   "        \"activeText\": \"🌙 自动休眠 (6分钟): 已开启\",\r\n" +
                   "        \"inactiveText\": \"☀️ 自动休眠: 已关闭 (常亮)\",\r\n" +
                   "        \"defaultValue\": 1,\r\n" +
                   "        \"showInTrayMenu\": true,\r\n" +
                   "        \"txTemplate\": \"05 11 00 {boolBit}\"\r\n" +
                   "      },\r\n" +
                   "      {\r\n" +
                   "        \"actionId\": \"win_lock\",\r\n" +
                   "        \"title\": \"Win 键锁定\",\r\n" +
                   "        \"type\": \"Toggle\",\r\n" +
                   "        \"activeText\": \"🔒 Win 键锁定 (防误触): 已开启\",\r\n" +
                   "        \"inactiveText\": \"🔓 Win 键锁定: 已解锁 (正常)\",\r\n" +
                   "        \"defaultValue\": 0,\r\n" +
                   "        \"showInTrayMenu\": true,\r\n" +
                   "        \"txTemplate\": \"05 12 00 {boolBit}\"\r\n" +
                   "      },\r\n" +
                   "      {\r\n" +
                   "        \"actionId\": \"backlight_brightness\",\r\n" +
                   "        \"title\": \"主背光亮度\",\r\n" +
                   "        \"type\": \"Slider\",\r\n" +
                   "        \"minValue\": 0,\r\n" +
                   "        \"maxValue\": 100,\r\n" +
                   "        \"defaultValue\": 75,\r\n" +
                   "        \"valueSuffix\": \"%\",\r\n" +
                   "        \"showInTrayMenu\": true,\r\n" +
                   "        \"txTemplate\": \"05 13 00 {value}\"\r\n" +
                   "      }\r\n" +
                   "    ]\r\n" +
                   "  },\r\n" +
                   "  \"protocol\": {\r\n" +
                   "    \"queryBatteryTxHex\": \"05 81 00 00\",\r\n" +
                   "    \"checksumType\": \"None\"\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        private static string GetCustomScriptDriverTemplateCs()
        {
            return "// ============================================================================\r\n" +
                   "// 扩展包模板: template_custom_driver.fp-driver.cs.template\r\n" +
                   "// 使用说明: 将末尾的 .template 去掉（保留 .fp-driver.cs 后缀）放入 adapters/ 目录，\r\n" +
                   "//           FerrisPulse 将在内存中自动编译并按最高优先级 (Priority=300) 热挂载。\r\n" +
                   "// 语法要求: 严格遵循 C# 5.0 语法（使用 string.Format 或 + 拼接字符串）。\r\n" +
                   "// ============================================================================\r\n\r\n" +
                   "using System;\r\n" +
                   "using System.Collections.Generic;\r\n" +
                   "using RazerBatteryTray;\r\n\r\n" +
                   "public class SampleCustomHardwareDriver : HardwareDriverBase\r\n" +
                   "{\r\n" +
                   "    public override string DriverId { get { return \"ext.sample.custom\"; } }\r\n" +
                   "    public override string Brand { get { return \"SampleBrand\"; } }\r\n" +
                   "    public override bool IsExternalPackage { get { return true; } }\r\n\r\n" +
                   "    public override bool MatchesVidPid(int vid, int pid)\r\n" +
                   "    {\r\n" +
                   "        return vid == 0xFFFF && pid == 0x9999;\r\n" +
                   "    }\r\n\r\n" +
                   "    public override void PopulateCapabilities(MouseBatteryInfo dev)\r\n" +
                   "    {\r\n" +
                   "        base.PopulateCapabilities(dev);\r\n" +
                   "        dev.Capabilities.SupportsDpi = true;\r\n" +
                   "        dev.Capabilities.SupportsDpiStages = true;\r\n" +
                   "        dev.Capabilities.SupportsPollingRate = true;\r\n" +
                   "        dev.Capabilities.ThemeColorHex = \"#00E676\";\r\n" +
                   "        dev.Capabilities.SensorDescription = \"Custom Optical Sensor (50 ~ 30,000 DPI)\";\r\n" +
                   "    }\r\n\r\n" +
                   "    public override List<MouseBatteryInfo> QueryDevices()\r\n" +
                   "    {\r\n" +
                   "        return new List<MouseBatteryInfo>();\r\n" +
                   "    }\r\n" +
                   "}\r\n";
        }
    }
}
