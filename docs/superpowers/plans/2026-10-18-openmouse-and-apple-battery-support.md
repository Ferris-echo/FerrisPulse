# 全面移植 OpenMouse / All-in-One 外设功能与 Apple/Beats 全系电量适配技术方案

本文档详细阐述将开源项目 `OpenMouse-Project/openmouse` (含 `mouse-protocol` 与 `keyboard-protocol`) 与 `ShuiJu/All-in-One-Mouse-Driver` 支持的所有外设协议，以及针对 Apple / Beats 全系设备（AirPods、Beats、Magic 外设）在 Windows 平台上的电量读取方案。

---

## 一、技术调研与背景分析

### 1. 瓦尔基里 (Valkyrie) 键盘无法识别电量的根因分析
在排查当前 `src/Hardware/ValkyrieDeviceHelper.cs` 代码后，发现以下三个核心缺陷导致键盘电量无法识别：
1. **Windows 键盘设备独占锁定与 CreateFile 权限问题**：
   - 当前代码中打开 HID 设备使用的是 `CreateFile(path, 0, ...)`（访问权限为 0）。
   - Windows 下 `HidD_GetAttributes` 允许 0 权限，但发送私有控制指令 `HidD_SetFeature`（如瓦尔基里的 0xF7 报文）底层使用 `IOCTL_HID_SET_FEATURE`，**必须要求 `GENERIC_WRITE` 权限**，否则直接报 `ERROR_ACCESS_DENIED (5)`。
   - 同时，Windows 键盘驱动子系统默认对键盘输入端持独占 `GENERIC_READ` 锁。如果直接请求 `GENERIC_READ | GENERIC_WRITE` 会失败；但 Windows 明确允许应用程序以 `GENERIC_WRITE` + `FILE_SHARE_READ | FILE_SHARE_WRITE` 打开键盘控制接口发送 Feature 报文。
2. **设备白名单与匹配范围过于狭窄**：
   - 现存 `IsValkyrieDevice` 在 `VID_3151` 下仅硬编码了 5 个 PID (`5007`, `5020`, `503f`, `5040`, `504b`)，而大量的瓦尔基里无线键盘（如 VK99 Max、VK87、VK75、Mecha 75、Cyber 68、MAG68 等）采用其他 PID 或代工固件，被直接漏过。
3. **固件回退报文机制缺失**：
   - 部分瓦尔基里键盘固件使用 0xF5 或 0x04 命令字进行状态查询，单一 0xF7 命令在部分新版固件上无响应。

### 2. Apple / Beats 系列设备在 Windows 下电量读取方案
Apple 体系的音频设备（AirPods、Beats）和输入外设（Magic Mouse、Magic Keyboard、Magic Trackpad）在 Windows 下无法原生查看电量，是因为其并不采用普通的标准 BLE 单一服务，而是采用 Apple 专有机制：

1. **AirPods 与 Beats 真无线耳机的 BLE 连续广播（Apple Continuity Protocols）**：
   - 包含设备：AirPods 1/2/3/4/Pro/Pro 2/Max，Beats Studio Buds/Buds+、Fit Pro、Solo Buds、Solo 4、Solo3、Powerbeats Pro 等。
   - 机制：设备会持续发射带有 Apple 厂商标识 `Company ID = 0x004C` (76) 的 BLE 广播包。
   - 广播类型：`0x07` (Proximity Pairing) 或 `0x10` (Nearby Info)。
   - 报文格式（27字节）：
     - `data[0]`: `0x07`
     - `data[1]`: `0x19` (长度 25)
     - `data[3..4]`: `modelId`（如 0x200E 为 AirPods Pro，0x2014/0x2024 为 Pro 2，0x2012 为 Beats Fit Pro，0x2017 为 Beats Studio Buds+ 等）
     - `data[5]`: 状态位（包含了当前由哪侧耳机发射广播 `broadcastFrom`，1 为左耳，0 为右耳）
     - `data[6..7]`: 电池数据。四位半字节：
       - `curr`：当前发射侧电量（0~10，代表 0%~100%，15 为未连接/不可用）
       - `anot`：另一侧耳机电量（0~10）
       - `caseBox`：充电仓电量（0~10）
       - `currCharging`, `anotCharging`, `caseCharging`：三部分各自的充电状态标志位
   - **技术突破（已实测验证通过）**：
     - 在纯 .NET Framework 4.0 / C# 5.0 环境下，无需任何第三方 NuGet 包或外部 C++ DLL，通过 `Type.GetType("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher, Windows.Devices, ContentType=WindowsRuntime")` 和 `add_Received` 即可动态激活 Windows 10/11 原生 WinRT BLE 广播监听器，毫秒级捕获实机附近 AirPods / Beats 广播！
2. **Windows PnP 蓝牙设备遥测聚合 (`DEVPKEY_Device_BatteryLevel`)**：
   - Windows 10 (1809+) 在通过 HFP 协议或标准蓝牙音频通道握手时，会将电量缓存到统一设备属性 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`。
   - 当 Beats 头戴式耳机（如 Beats Studio3 / Solo3 / Studio Pro）处于连接状态时，通过结合 MAC 匹配与 FriendlyName 过滤，可以精准拉取系统缓存的电量。
3. **Apple Magic 系列外设（Magic Mouse 1/2, Magic Keyboard, Magic Trackpad）**：
   - 作为 Bluetooth HID 复合设备连接，支持通过 Windows HID 驱动直接查询 Feature Report `0x47` 或 `0xD1`，其第 1 字节即为精确电量百分比（0~100）。

### 3. OpenMouse 与 All-in-One-Mouse-Driver 全量设备协议归纳
通过深入解析两大开源项目的源代码与协议库，其涵盖的主流协议引擎可归纳为三大核心类别：

| 协议体系 | 对应品牌与外设 | 核心报文与通信格式 |
|---|---|---|
| **CompX / PixArt 64字节 Feature 引擎** | **Lamzu** (Inca 8K, Paro, Atlantis, Thorn, Maya), **Pulsar** (X2, Xlite), **WLmouse** (Beast X 全系, Sword X), **Attack Shark** (X3, R1, X6, X11), **Ajazz** (AJ199, AJ139, AJ159), **Darmoshark** (M3, M3s), **Glorious** (Model O/D/I 系列), **Ninjutso** (Sora, Origin One X), **RAWM** (SA-MH01), **Incott** (GHero) | 65 字节 Feature Report (Report ID 0x00)。查询电量指令：`00 00 00 02 02 00 83 ...`；回复帧：`A1 00 02 02 00 83 [充电标志] [电量百分比]` |
| **Beken / ATK / Endgame Gear 16字节 EEPROM 引擎** | **ATK** (A9, F1, X1, VXE R1 全系, 蜻蜓 F1/F2), **Endgame Gear** (OP1we, XM2we) | 16 字节 Output/Input 报文 (Report ID 0x08)。校验和规则 `(0x55 - (0x08 + sum(data[0..14]))) & 0xFF`。电量指令 `0x04`，回复包 `data[5]` 为电量，`data[6]` 为充电标志，`data[7..8]` 为毫伏电压 |
| **Vaxee 64字节专用控制引擎** | **Vaxee** (NP-01S, NP-01S v2, XE, AX, OUTSET AX, E1) | 64 字节 Feature Report (Report ID 0x0E)。发送 `0E A5 [page] 01 01 00 ...`。Page `0x0B` 为电量（`buf[5] * 5` 为百分比），Page `0x10` 为充电状态（`buf[5] == 0x01`） |
| **Keychron & Wooting 自定义 HID 引擎** | **Keychron** (M1, M2, M3, M6 等), **Wooting** (60HE, 80HE, Two HE) | 厂商自定义 UsagePage (Wooting: `0xFF55:0x01`, Keychron: `0xFFC1:0x01`, `0xFF0A:0x01`) |

---

## 二、模块架构设计

```
FerrisPulse 硬件驱动分层架构
 ├── Hardware/
 │    ├── RazerDeviceHelper.cs       (雷蛇全系支持，含独立 X/Y 灵敏度)
 │    ├── ValkyrieDeviceHelper.cs    (瓦尔基里全系键盘/鼠标增强版，修正权限与白名单)
 │    ├── AppleDeviceHelper.cs       (【新增】Apple/Beats 生态全系电量支持：BLE广播+PnP+HID)
 │    ├── UniversalGamingDeviceHelper.cs (【新增】OpenMouse & All-in-One 全量外设引擎)
 │    │    ├── CompX/PixArt 驱动分支 (Lamzu, Pulsar, WLmouse, Darmoshark, Glorious, Ajazz, etc.)
 │    │    ├── Beken/ATK 驱动分支    (ATK, VXE, Endgame Gear)
 │    │    └── Vaxee 驱动分支        (Vaxee 全系)
 │    ├── VgnDeviceHelper.cs         (VGN 专用固件适配)
 │    ├── RapooDeviceHelper.cs       (雷柏专用适配)
 │    ├── LogitechDeviceHelper.cs    (罗技 HID++ 适配)
 │    └── DeviceManager.cs           (统一设备调度中心、去重与缓存管理)
 └── UI/
      ├── MainForm.Hardware.cs       (UI 与托盘电量渲染，支持耳机双耳及仓电量展示)
      └── ...
```

---

## 三、实施计划

1. **第 1 步：修复并增强瓦尔基里全系外设驱动 (`ValkyrieDeviceHelper.cs`)**
   - 调整 `CreateFile` 打开模式为 `GENERIC_WRITE` 优先，规避 Windows 键盘独占锁导致权限被拒的问题。
   - 放宽匹配逻辑，自动识别所有包含 `Valkyrie`、`VK` 关键字及 `VID_374A`、`VID_3151` 的所有键盘与鼠标。
   - 增加 0xF7 / 0xF5 容错机制。

2. **第 2 步：实现全能游戏外设驱动引擎 (`UniversalGamingDeviceHelper.cs`)**
   - 实现三大协议族（CompX 64字节、Beken/ATK 16字节、Vaxee 64字节）。
   - 内置完整的 VID/PID 静态产品数据库，自动解析准确型号名称（如 `Lamzu Inca 8K`、`Vaxee NP-01S`、`Pulsar X2V2`、`WLmouse Beast X`、`ATK F1 Pro Max` 等）。

3. **第 3 步：实现 Apple & Beats 生态全系电量引擎 (`AppleDeviceHelper.cs`)**
   - 编写 `AppleDeviceHelper`，集成动态反射启动的 `BluetoothLEAdvertisementWatcher`。
   - 实现 Apple Continuity 协议解码（支持左耳、右耳、充电仓三路独立电量与充电状态）。
   - 实现 Apple Magic 键鼠外设 HID Feature 0x47 报文直读。
   - 结合 PnP 属性 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`。

4. **第 4 步：统一调度中心与 UI 展现对接**
   - 在 `DeviceManager.cs` 中增加 `AppleDeviceHelper.QueryAllAppleDevices()` 与 `UniversalGamingDeviceHelper.QueryAllDevices()`。
   - 在 `MainForm.Hardware.cs` 中完善耳机类外设的展示（支持单耳/双耳/充电盒电量聚合显示与对应图标切换）。

5. **第 5 步：编写单元与集成测试用例，全量回归验证**
   - 编写协议数据包编解码测试，包含真实的 Lamzu、Vaxee、ATK、Apple Continuity 样例包。
   - 运行 `build.bat` 验证原生 C# 5.0 编译通过。
   - 运行 `tools/run_all_tests.ps1` 确保全量 30+ 测试套件 100% 绿色通过。
