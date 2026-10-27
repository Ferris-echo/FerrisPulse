# FerrisPulse 外设扩展包规范标准 (FDAS v2.0 & C# 5.0 Script Driver)
## FerrisPulse Device Adapter & Driver Package Specification (v2.0)

> 本规范定义了开源软件「FerrisPulse (灵脉)」的双模硬件扩展包体系：
> 1. **声明式 JSON 适配包 (`*.fp-adapter.json`, FDAS v1.0 / v2.0)**：零编译风险，覆盖绝大多数标准/半私有 HID 外设（支持单设备或一厂多型号 `devices` PID 矩阵、DPI 档位表写入 `setStagesTxTemplate`、以及自定义动作指令 `customActions`）。
> 2. **动态 C# 5.0 驱动脚本包 (`*.fp-driver.cs`)**：针对多帧握手、动态挑战响应、复杂状态机或需要修复内置驱动的深度外设，运行时由 `.NET 4.0 CSharpCodeProvider` 全内存编译为原生 `IHardwareDriver` 实例，具备与内置驱动 100% 对等的能力，且优先级（Priority = 300）高于 JSON 包（200）和内置包（100），支持直接热覆盖同名或同 `VID:PID` 的内置驱动。

---

## 1. 设计哲学与核心原则

1. **硬件指纹优先 (Hardware Fingerprint First)**：以不可篡改的 USB `VID:PID`、`UsagePage`、`Usage` 及报告长度为基准，不依赖用户输入的模糊商业名称。
2. **UI 与硬件协议 100% 零硬编码解耦 (Zero-Hardcoding UI Decoupling)**：
   - 主程序 UI（`MainForm.*`）绝不判断具体品牌名称，仅读取每个 `MouseBatteryInfo` 挂载的 `DeviceCapabilities` 能力契约，并通过 `HardwareCommandBus` 统一总线与按设备 `VID:PID` 细粒度互斥锁调度指令。
3. **抓包样本强制闭环 (Proof by Sample Hex)**：
   - 规范包中必须携带真实的抓包输入/输出 Hex 样本（`sampleRxHex` / `sampleTxHex`）。
   - 自动化融合引擎在将配置加载进系统前，必须在内存中回放抓包样本并通过断言，从根源上杜绝 AI 凭空幻觉捏造协议。
4. **C# 5.0 / .NET 4.0 零依赖原生兼容**：所有内置与动态脚本驱动均保证能够在 Windows 自带的 `.NET 4.0` 下零报错秒级编译，杜绝引入任何第三方包。

---

## 2. 模式 A：FDAS v2.0 声明式扩展包 (`*.fp-adapter.json`)

每个适配器文件扩展名统一为：`<brand>_<model_slug>.fp-adapter.json`。根节点包含四大顶层分区：

```json
{
  "$schema": "https://raw.githubusercontent.com/Ferris-echo/FerrisPulse/main/schemas/fdas-v2.0.json",
  "fdasVersion": "2.0",
  "metadata": { ... },
  "hardware": { ... },
  "capabilities": { ... },
  "protocol": { ... }
}
```
> 向下兼容：`"fdasVersion": "1.0"` 的全部历史扩展包无需修改即可无缝加载。

### 2.1 元数据分区 (`metadata`)
描述设备的人类可读信息、UI 主题色与贡献者署名：
- `brand` (string, 必填): 品牌名称（如 `"Razer"`, `"Logitech"`, `"Sony"`, `"VGN"`）。
- `model` (string, 必填): 设备商业型号（如 `"DeathAdder V4 Pro"`, `"INZONE Buds"`）。
- `category` (string, 必填): 设备分类，必须为以下之一：
  - `"Mouse"` (鼠标)
  - `"Keyboard"` (键盘)
  - `"Headset"` (头戴式耳机)
  - `"Earbuds"` (入耳式耳机 / TWS)
  - `"Gamepad"` (游戏手柄)
  - `"Dongle"` (接收器 / 拓展坞)
  - `"Generic"` (通用外设)
- `brandColor` (string, v2.0 可选): 品牌卡片强调色 Hex（如 `"#00FF00"`）。
- `sensorDescription` (string, v2.0 可选): 传感器或主控芯片型号描述（如 `"Focus Pro 35K Gen-2 · 1-DPI 精准步进"`）。
- `author` (string, 必填): 贡献者昵称（将自动登入 `CHANGELOG.md` 及致谢榜）。
- `description` (string, 可选): 设备特性说明与逆向要点备注。

### 2.2 硬件拓扑分区 (`hardware`)
描述操作系统枚举该设备的核心物理指标：
- `vendorId` (string, 必填): 16 进制字符串（如 `"0x1532"`）。
- `productId` (string, 必填): 主型号 16 进制字符串（如 `"0x00BE"`）。
- `devices` (array of object, v2.0 可选): 同厂同协议多型号 PID 矩阵，允许一个扩展包同时覆盖有线/2.4G/8K接收器或多个衍生型号：
  ```json
  "devices": [
    { "productId": "0x00BE", "model": "Razer DeathAdder V4 Pro (2.4G)", "transport": "2.4G 无线", "maxRate": 8000 },
    { "productId": "0x00BF", "model": "Razer DeathAdder V4 Pro (Wired)", "transport": "USB 有线", "maxRate": 8000 }
  ]
  ```
- `productString` (string, 可选): 芯片 ROM 内置产品字符串。
- `manufacturerString` (string, 可选): 芯片 ROM 内置制造商字符串。
- `endpointTopology` (object, 必填): 专用于通信的 HID 端点拓扑：
  - `usagePage` (string, 必填): 通信 UsagePage 16 进制（如 `"0xFFA0"`）。
  - `usage` (string, 必填): 通信 Usage 16 进制（如 `"0x0001"`）。
  - `reportType` (string, 必填): 通信报告形态，`"Interrupt"` 或 `"Feature"`。
  - `reportLength` (int, 必填): 报告总字节数（如 `64` 或 `20`）。
  - `readTimeoutMs` (int, 可选, 默认 500): HID I/O 读取超时毫秒数。

### 2.3 功能能力分区 (`capabilities`)

#### A. 电池与充电能力 (`capabilities.battery`)
所有具备电池的外设均需声明：
- `reportId` (string, 必填): 报告 ID（如 `"0x11"`, `"0x02"`，若无则为 `"0x00"`）。
- `batteryPercentByteOffset` (int, 必填): 电量百分比在有效载荷中的字节索引（基于 0）。
- `chargingByteOffset` (int, 可选): 充电状态指示字节索引。
- `chargingBitMask` (string, 可选, 默认 `"0x01"`): 充电标志位掩码。
- `batteryFormula` (string, 可选, 默认 `"DirectPercent"`): 电量解析算子：
  - `"DirectPercent"`: 字节值直接代表 0~100。
  - `"Max10Scale"`: 字节为 0~10 离散档位，自动乘以 10 转换为百分比。
  - `"XorMask"`: 需要按位取反（如 `val ^ 0xFF`）。
  - `"VoltageAdc"`: 字节为毫伏电压，需由内置曲线表转换。
  - 数学算式：如 `"(b[6] * 256 + b[5]) / 10"`，由内置安全表达式求值引擎 (`FdasExpressionEvaluator`) 实时计算。
- `multiEndpoints` (array of string, 针对 Earbuds 可选): 多耳塞独立电量声明（如 `["Left", "Right", "Case"]`）。
- `sampleRxHex` (string, 必填): 真实抓包接收报文 Hex 字符串（包含 ReportId），自动化测试将直接回放此报文验证电量断言。

#### B. 鼠标专属性能矩阵 (`capabilities.mouse`)
仅当 `category == "Mouse"` 时必填：
- `dpi` (object):
  - `minDpi` (int, 必填): 硬件最低 DPI（如 `50` 或 `100`）。
  - `maxDpi` (int, 必填): 硬件最高 DPI（如 `26000`, `30000`, `42000`）。
  - `step` (int, 必填): 调节步进（通常为 `50`）。
  - `defaultStages` (array of int, 必填): 默认档位数组（如 `[400, 800, 1600, 3200, 6400]`）。
  - `supportsXyIndependence` (bool, 必填): 是否支持 XY 轴独立灵敏度调节。
  - `ratioPresets` (array of string, 可选): 常用屏幕比例预设（如 `["1:1", "4:3", "16:10", "1.33"]`）。
  - `activeStageByteOffset` (int, 可选): 当前激活档位字节索引（0-based）。
  - `dpiXLoByteOffset` / `dpiXHiByteOffset` / `dpiYLoByteOffset` / `dpiYHiByteOffset` (int, 可选): X/Y 轴 DPI 低/高字节偏移。
  - `sampleDpiRxHex` (string, 可选): 读取 DPI 状态的真实抓包 Hex 样本。
- `pollingRate` (object):
  - `supportedRates` (array of int, 必填): 硬件支持的全部回报率数组（如 `[125, 250, 500, 1000, 2000, 4000, 8000]`）。
  - `defaultRate` (int, 必填): 推荐默认回报率（如 `1000`）。
  - `maxRate` (int, 必填): 硬件最大极限回报率（如 `8000`）。
  - `rateIndexMap` (object, 可选): 速率到固件协议索引的映射（如 `{"125": 0, "500": 1, "1000": 2, "2000": 3, "4000": 4, "8000": 5}`）。

#### C. 声明式自定义硬件动作 (`capabilities.customActions`, v2.0 新增)
允许为键盘、耳机、手柄或鼠标声明专属控制动作（如背光模式、侧灯开关、休眠时间、灯效亮度等）：
```json
"customActions": [
  {
    "actionId": "backlight_mode",
    "displayName": "背光模式",
    "actionType": "Enum",
    "options": ["常亮", "呼吸", "霓虹"],
    "txTemplate": "07 02 {val:X2} 00 00 00 00 00"
  }
]
```

### 2.4 指令字典与报文模板 (`protocol`)
定义与硬件双向交互的 Tx 发送模板：
- `queryBatteryTxHex` (string, 可选): 查询电量主动发送的 Hex 报文。
- `setDpiTxTemplate` (string, 鼠标必填): 下发设置 DPI 的报文模板，支持宏变量替换（`{stage}`, `{dpiX_lo}`, `{dpiX_hi}`, `{dpiY_lo}`, `{dpiY_hi}`）。
- `setStagesTxTemplate` (string, v2.0 可选): 板载多档位表批量下发报文模板（支持 `{count}`, `{active}`, `{s1_lo}`, `{s1_hi}` 等占位符）。
- `setPollingRateTxTemplate` (string, 鼠标必填): 切换回报率报文模板，支持 `{rateIndex}` 或 `{rateHz}`。
- `checksumType` (string, 可选, 默认 `"None"`): `"None"`, `"SumMod256"`, `"XorSum"`, `"RazerCrc"`, `"SonyInzone"`。

---

## 3. 模式 B：动态 C# 5.0 驱动脚本扩展包 (`*.fp-driver.cs`)

当外设协议存在多包挑战响应、复杂状态机或需要深度自定义逻辑时，可编写继承自 `HardwareDriverBase` 的 `.fp-driver.cs` 文件并放入 `adapters/` 目录（或通过软件导入窗口直接粘贴）。

### 3.1 核心契约与基类 (`HardwareDriverBase`)
- **必须兼容 C# 5.0 语法**（严禁使用 `$"..."` 字符串插值、`?.`、`nameof`、`out var`、`=>` 表达式属性）。
- 继承 `HardwareDriverBase` 后可直接调用：
  - `SaveCachedBattery(deviceKey, percent)` / `LoadCachedBattery(deviceKey)`（统一注册表冷启动与休眠状态缓存 `DriverStateStore`）
  - `SaveStateInt` / `LoadStateInt` / `SaveStateString` / `LoadStateString`
- 若将 `DriverId` 设置为与内置驱动一致（例如 `"builtin.mchose.mouse"`）或覆盖相同 `VID:PID`，由于脚本包默认 `Priority = 300` 高于内置包 `Priority = 100`，系统会自动以外部扩展包无缝接管该硬件。

---

## 4. 标准化示例索引

1. 旗舰双模 8K 游戏鼠标（Razer DeathAdder V4 Pro，支持 Focus Pro 35K Gen-2 XY 轴独立 1-DPI 步进 + 8000Hz 回报率 + 多 PID 矩阵）：
   👉 [examples/adapters/razer_deathadder_v4_pro.fp-adapter.json](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/examples/adapters/razer_deathadder_v4_pro.fp-adapter.json)
2. 多节点 2.4G 游戏耳塞（左耳 / 右耳 / 充电盒独立电量）：
   👉 [examples/adapters/sony_inzone_buds.fp-adapter.json](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/examples/adapters/sony_inzone_buds.fp-adapter.json)
