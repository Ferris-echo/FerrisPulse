# FerrisPulse 外设适配器规范标准 (FDAS v2.0)
## FerrisPulse Device Adapter Specification (Version 2.0)

> 本规范定义了开源软件「FerrisPulse (灵脉)」与外设逆向成果之间的标准化契约接口。
> 社区贡献者或外部 AI Agent（如 ChatGPT / Claude / DeepSeek）逆向得出的硬件通信指令，只需遵循本规范输出单一 `*.fp-adapter.json` 规范包，即可通过官方自动化融合引擎 `tools/integrate_adaptation.ps1` 实现秒级免手动接入。
>
> **版本兼容**：v2.0 完全向下兼容 v1.0。`fdasVersion` 填写 `"1.0"` 或 `"2.0"` 均可加载；
> v2.0 新增字段（`brandColor`、`devices`、`variables`、`rateCodeMap` 等）在 v1.0 包中缺省时按默认行为处理。

---

## 1. 设计哲学与核心原则

1. **硬件指纹优先 (Hardware Fingerprint First)**：以不可篡改的 USB `VID:PID`、`UsagePage`、`Usage` 及报告长度为基准，不依赖用户输入的模糊商业名称。
2. **多模态特性正交解耦 (Orthogonal Capabilities)**：
   - 鼠标具备专属性能矩阵（DPI 档位、XY 轴分离、回报率频段与写入模板）。
   - 键盘/耳机/手柄保持极简纯粹，直取物理电量、充电感知及多节点电池（如 TWS 耳塞）。
3. **抓包样本强制闭环 (Proof by Sample Hex)**：
   - 规范包中必须携带真实的抓包输入/输出 Hex 样本（`sampleRxHex` / `sampleTxHex`）。
   - 自动化融合引擎在将配置编译进系统前，必须在内存中回放抓包样本并通过单元测试断言，从根源上杜绝 AI 凭空幻觉捏造协议。
4. **C# 5.0 / .NET 4.0 零依赖原生兼容**：生成的驱动代码保证能够在 Windows 自带的 `csc.exe` 下零报错秒级编译，杜绝引入任何第三方包。

---

## 2. 规范文件结构定义 (JSON Schema)

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

> **解析行为说明（重要）**
> 运行时解析器按 **字段名全局匹配**，不区分 JSON 嵌套层级。因此把 `minDpi` 放在
> `capabilities.mouse.dpi` 内或直接放在 `capabilities.mouse` 内，效果相同。
> **未识别的字段一律静默忽略，不报错**——写错字段名不会有任何提示，请对照本文档核对。

### 2.1 元数据分区 (`metadata`)
描述设备的人类可读信息与贡献者署名：
- `brand` (string, 必填): 品牌名称（如 `"MCHOSE"`, `"Sony"`, `"VGN"`, `"AJAZZ"`）。
- `model` (string, 必填): 设备商业型号（如 `"A5 V2 Ultra"`, `"INZONE Buds"`, `"AJ199 4K"`）。
- `category` (string, 必填): 设备分类，必须为以下之一：
  - `"Mouse"` (鼠标)
  - `"Keyboard"` (键盘)
  - `"Headset"` (头戴式耳机)
  - `"Earbuds"` (入耳式耳机 / TWS)
  - `"Gamepad"` (游戏手柄)
  - `"Dongle"` (接收器 / 拓展坞)
  - `"Generic"` (通用外设)
- `author` (string, 必填): 贡献者昵称（将自动登入 `CHANGELOG.md` 及致谢榜）。
- `description` (string, 可选): 设备特性说明与逆向要点备注。
  **强烈建议把协议细节写进这里**（字节偏移、编码公式、校验方式、写命令与地址），
  这样后来者无需重新逆向即可维护。参考 `examples/adapters/zaopin_z2.fp-adapter.json`。
- `brandColor` (string, 可选, **v2.0 新增**): 品牌主题色，格式 `#RRGGBB`。
  声明后优先于内置的品牌配色表；格式非法时回退内置配色，不会显示为黑色。
- `sensorDescription` (string, 可选, **v2.0 新增**): 传感器型号描述（如 `"PAW3395 · 50 DPI 步进"`）。

### 2.2 硬件拓扑分区 (`hardware`)
描述操作系统枚举该设备的核心物理指标：
- `vendorId` (string, 必填): 16 进制字符串（如 `"0x3837"`）。
- `productId` (string, 必填): 16 进制字符串（如 `"0x1101"`）。
- `devices` (array of object, 可选, **v2.0 新增**): 多 PID 矩阵。
  同一型号存在多个 PID（有线 / 2.4G 接收器 / 蓝牙）时，用此字段把它们归并为**同一个设备身份**，
  避免切换连接方式后被认成两台设备、导致选中状态与自定义配置丢失。
  - `productId` (string, 必填): 该形态的 PID。
  - `model` (string, 必填): 该形态的显示名称。
  - `transport` (string, 可选): 连接方式描述（如 `"USB 有线"` / `"2.4G 无线"`）。
  - `maxRate` (int, 可选): 该形态支持的最大回报率。
  - **缺省行为**：不声明 `devices` 时，设备身份为 `FDAS:{VID}:{PID}`（v1.0 行为，保持不变）；
    声明后统一为 `FDAS:{VID}:{型号slug}`。
  - **注意**：不同形态的 PID 必须来自实测。把「有线直插」误写成「1K 接收器」会导致界面显示错误的连接方式。
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
  - 声明后，若 Tx 报文首字节不等于该 ID，运行时会自动前插一位（查询与写入口径一致）。
  - **CompX 方案注意**：`VgnCompxSum55` 校验会**跳过前导报告 ID**，
    从 cmd 字节开始累加。校验公式为 `尾字节 = 0x55 - sum(不含ReportId) - 8`。
- `batteryPercentByteOffset` (int, 必填): 电量百分比在有效载荷中的字节索引（基于 0）。
- `chargingByteOffset` (int, 可选): 充电状态指示字节索引。
- `chargingBitMask` (string, 可选, 默认 `"0x01"`): 充电标志位掩码。
- `batteryFormula` (string, 可选, 默认 `"DirectPercent"`): 电量解析算子：
  - `"DirectPercent"`: 字节值直接代表 0~100。
  - `"Max10Scale"`: 字节为 0~10 离散档位，自动乘以 10 转换为百分比。
  - `"XorMask"`: 需要按位取反（如 `val ^ 0xFF`）。
  - `"VoltageAdc"`: 字节为毫伏电压，需由内置曲线表转换。
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
  - `dpiXLoByteOffset` (int, 可选): X 轴 DPI 低字节偏移。
  - `dpiXHiByteOffset` (int, 可选): X 轴 DPI 高字节偏移。
  - `dpiYLoByteOffset` (int, 可选): Y 轴 DPI 低字节偏移（若支持 XY 独立）。
  - `dpiYHiByteOffset` (int, 可选): Y 轴 DPI 高字节偏移。
  - `sampleDpiRxHex` (string, 可选): 读取 DPI 状态的真实抓包 Hex 样本。
- `pollingRate` (object):
  - `supportedRates` (array of int, 必填): 硬件支持的全部回报率数组（如 `[125, 250, 500, 1000, 2000, 4000, 8000]`）。
  - `defaultRate` (int, 必填): 推荐默认回报率（如 `1000`）。
  - `maxRate` (int, 必填): 硬件最大极限回报率（如 `8000`）。
  - `rateIndexMap` (object, 可选): 速率到固件协议索引的映射（如 `{"125": 0, "500": 1, "1000": 2, "2000": 3, "4000": 4, "8000": 5}`）。

### 2.4 指令字典与报文模板 (`protocol`)
定义与硬件双向交互的 Tx 发送模板：
- `queryBatteryTxHex` (string, 可选): 查询电量主动发送的 Hex 报文。
- `queryBatteryPipeline` (array of string, 可选, **v2.0 新增**): 多步时序握手管线。
  需要先唤醒 / 握手 / 解锁才能读到电量的设备，按顺序发送数组中每条报文。
  声明后优先于 `queryBatteryTxHex`。
- `queryDpiTxHex` (string, 可选): 读取当前 DPI 的报文。
- `setDpiTxTemplate` (string, 鼠标必填): **切换当前激活档位**的报文模板（不是写 DPI 数值）。
  支持宏变量替换：
  - `{stage}`: 当前激活档位 (1-based 或 0-based 由 `stageBaseZero` 决定)
  - `{dpiX_lo}`, `{dpiX_hi}`: X 轴 DPI 低位/高位十六进制
  - `{dpiY_lo}`, `{dpiY_hi}`: Y 轴 DPI 低位/高位十六进制
- `setDpiValueTxTemplate` (string, 可选, **v2.0 新增**): **写入某个档位的 DPI 数值**的报文模板。
  与 `setDpiTxTemplate` 是两件不同的事：前者改数值，后者切档位。
  可用变量：`{stage}`、`{dpi}`、以及 `variables` 中自定义的派生量。
- `setPollingRateTxTemplate` (string, 鼠标必填): 切换回报率报文模板，支持 `{rateIndex}` 或 `{rateHz}`。
- `variables` (object, 可选, **v2.0 新增**): 动态派生变量字典。
  模板中可用 `{变量名}` 引用，变量之间可相互引用（`$变量名`）。
  支持四则运算、位运算（`&` `|` `^` `<<` `>>`）与括号。
  示例：
  ```json
  "variables": {
    "rateCode": "rateCodeMap[$rateHz]",
    "rateParity": "(0x55 - $rateCode) & 0xFF",
    "dpiEncoded": "($dpi / 50) - 1",
    "dpiLow": "$dpiEncoded & 0xFF"
  }
  ```
  > **注意**：模板里引用的每个 `{占位符}` 都必须在 `variables` 中定义，
  > 或属于内置变量（`stage` / `dpi` / `rateHz` / `rateIndex`）。缺失时会被替换为空，报文错乱。
- `rateCodeMap` (object, 可选, **v2.0 新增**): 回报率 → 设备私有编码的映射（字符串形式）。
  ```json
  "rateCodeMap": { "125": "0x08", "1000": "0x01", "4000": "0x20" }
  ```
  > 与 `rateIndexMap` 的区别：`rateIndexMap` 是**数组下标序号**（0-based），
  > `rateCodeMap` 是**设备真实编码**。两者语义不同，按设备协议二选一，不要混用。
- `checksumType` (string, 可选, 默认 `"None"`):
  - `"None"`: 不计算，尾字节保持模板原值
  - `"SumMod256"`: 所有字节累加和低 8 位
  - `"XorSum"`: 所有字节异或和
  - `"VgnCompxSum55"`: CompX 公版方案。`尾字节 = 0x55 - sum(不含ReportId) - 8`。
    **未声明 `reportId` 时不排除首字节**（保持旧包行为）。
  - `"SonyInzone"`: 索尼专用累加序列校验 —— **FDAS 适配包暂不支持**，
    索尼设备走内置专用通道，无需编写适配包。

---

## 3. 标准化示例索引

1. 旗舰三模游戏鼠标（支持 XY 轴独立 DPI + 8K 回报率）：
   👉 [examples/adapters/mchose_a5_v2_ultra.fp-adapter.json](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/examples/adapters/mchose_a5_v2_ultra.fp-adapter.json)
2. 多节点 2.4G 游戏耳塞（左耳 / 右耳 / 充电盒独立电量）：
   👉 [examples/adapters/sony_inzone_buds.fp-adapter.json](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/examples/adapters/sony_inzone_buds.fp-adapter.json)
3. CompX 公版方案 2.4G 鼠标（17 字节 0x08 帧 + 0x55 互补校验 + 多步握手管线）：
   👉 [examples/adapters/zaopin_z2.fp-adapter.json](file:///D:/workbuddy2/FerrisPulse/examples/adapters/zaopin_z2.fp-adapter.json)

---

## 4. v2.0 变更记录

| 字段 | 分区 | 说明 |
|---|---|---|
| `metadata.brandColor` | metadata | 品牌主题色 `#RRGGBB`，优先于内置配色表 |
| `metadata.sensorDescription` | metadata | 传感器型号描述 |
| `hardware.devices` | hardware | 多 PID 矩阵，归并有线 / 无线等形态 |
| `protocol.variables` | protocol | 动态派生变量，模板可引用 |
| `protocol.rateCodeMap` | protocol | 回报率 → 设备私有编码 |
| `protocol.queryBatteryPipeline` | protocol | 多步握手管线 |
| `protocol.setDpiValueTxTemplate` | protocol | 写入 DPI 数值（区别于切档位） |
| `checksumType: VgnCompxSum55` | protocol | 补充 CompX 校验，排除前导 Report ID |

**v2.0 修复的实现缺陷**：此前 CompX 校验把前导 `0x08` 也计入求和，
导致所有含报告 ID 的报文校验位偏低 8、设备拒收。现已按 `reportId` 声明自动排除。

### 已知限制

| 项 | 状态 |
|---|---|
| `capabilities.customActions` | 未实现（Toggle / Slider / Button） |
| `protocol.setStagesTxTemplate` | 未实现（五档批量下发） |
| `.fp-driver.cs` 动态 C# 驱动 | 未实现（无热加载机制） |
| `capabilities.battery.sampleTxHex` | 仅解析 `sampleRxHex` |
