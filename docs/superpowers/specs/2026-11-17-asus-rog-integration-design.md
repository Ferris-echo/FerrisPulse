# 华硕 ROG 外设生态扩容与三通道仲裁融合设计 (ASUS ROG Integration Design)

> **贡献者致谢**：B站网友 **“工程机风格”**

## 1. 背景与目标

将社区贡献者“工程机风格”针对华硕 ROG 外设的协议适配与多通道体验增强合入主线：
1. 新增 3 款华硕 ROG 设备（VID `0x0B05`）支持：
   - **ROG AZOTH 96 HE Wireless (`0x1AD0`)**：2.4G 无线键盘（SpeedNova 8K 接收器，`mi_02&col02`）
   - **ROG DELTA II (`0x1AFA`)**：2.4GHz 无线游戏耳机（`mi_03&col04`，与 PELTA `0x1B84` 同族）
   - **ROG AZOTH 96 HE (`0x1C10`)**：USB 有线键盘（`mi_01`）
2. 补齐 `UniversalGamingDeviceHelper` 隔离边界、`BuiltInDrivers` 内置驱动注册及 `DriverStateStore` 离线/休眠持久化缓存。
3. 在 `DeviceManager` 引入 **2.4G / USB 有线 / 蓝牙三通道在线仲裁**，消除同一物理键盘切至有线或蓝牙后 2.4G 接收器条目仍伪在线的多卡片分身。
4. 升级 `DeviceManager.BuildTrayBatterySummary` 为「鼠标 | 键盘 | 耳机」三段摘要，并对键盘实行择优选择（活跃/有线/真实电量优先于休眠键盘）。
5. 固化全内存反射单元测试 `tests/test_asus_rog_devices.ps1`，并在代码与文档贡献榜署名致谢。

## 2. 约束与非目标

- **C# 5.0 / .NET 4.0**：严禁插值字符串、`?.`、`nameof`、自动属性初始化器或外部依赖。
- **UTF-8 with BOM**：所有 `.cs`、`.ps1`、`.md` 文件均保持 UTF-8 BOM 并通过 `tools/ensure_boms_safe.py` 校验。
- **能力位宁缺毋滥**：仅开启 `SupportsBattery`（且仅在读到真实电量或已证实电池能力时），`SupportsDpi`、`SupportsDpiStages`、`SupportsXyIndependence`、`SupportsPollingRate`、`SupportsHardwareDpiMonitor` 一律保持 `false`。
- **不引入重复外部包**：既已融入主线，不在仓库 `adapters/` 重复放置过渡脚本 `asus_rog_ext.fp-driver.cs`。
- **托盘文本上限兼容**：`MainForm.Tray.cs` 已有 `trayIcon.Text` 的 63 字符裁剪，保持现状不破坏。

## 3. 架构方案

### 3.1 协议与端点隔离 (`src/Hardware/AsusDeviceHelper.cs` + `AsusDeviceHelper.Rog.cs`)
- 将 `AsusDeviceHelper` 改为 `public static partial class`。
- 新增 `AsusDeviceHelper.Rog.cs`，定义三个新 PID 常量及精确四元组白名单 `IsRogEndpoint(d, pid, packetSize)`：`VID == 0x0B05 && PID == pid && UsagePage == 0xFF00 && Usage == 0x0001 && InputReportLength == packetSize && OutputReportLength == packetSize`。
- **协议解析**：
  - `1AD0`（64B，Report ID `0x02`）与 `1C10`（65B，Report ID `0x00`）：
    - 就绪探测 `{rid 12 00}`：`rx.Length == packetSize && rx[0] == rid && rx[1] == 0x12 && rx[2] == 0x00 && rx[5] != 0xFA`
    - 电量/充电 `{rid 12 01}`：`rx.Length == packetSize && rx[0] == rid && rx[1] == 0x12 && rx[2] == 0x01 && rx[5] == 0 && rx[6] <= 100 && rx[9] <= 1`
    - 休眠/NAK 帧：`rx.Length >= 3 && rx[1] == 0xFF && rx[2] == 0xAA` 立即中止读取，保留最后真实电量并清除 `Ready` 与 `Charging`。
  - `1AFA`（64B，Report ID `0xCC`）：复用同族 `TryParseBatteryReport` (`{CC 12 07}`, `RX[6]`) 与 `TryParseChargingReport` (`{CC 12 08}`, `RX[5]`)。
- **冷启动/休眠状态缓存**：读取成功后写入 `DriverStateStore.SaveBatteryState`，状态对象初始化时通过 `DriverStateStore.TryLoadBatteryState` 恢复最近真实电量（`Ready=false`, `Charging=false`）。

### 3.2 通用引擎隔离与内置驱动注册 (`UniversalGamingDeviceHelper.cs` & `BuiltInDrivers.cs`)
- `UniversalGamingDeviceHelper.IsSupportedGamingDevice` 增加屏蔽 `pid_1ad0`、`pid_1afa`、`pid_1c10`，防止通用鼠标探测误发指令。
- `BuiltInDrivers.cs` 新增 `AsusDeviceDriver : HardwareDriverBase`（使用 `CanHandle` 匹配 `Brand == "ASUS"` 或 `ASUS:0B05:`，不重写 `MatchesVidPid` 以避免将键鼠误认为鼠标 RawInput 路径），显式锁死所有 DPI/回报率能力位为 `false`。

### 3.3 三通道在线仲裁与托盘摘要 (`DeviceManager.cs`)
- 在 `DeviceManager.QueryAllDevices` 2.13d 段调用 `AsusDeviceHelper.QueryAllRogDevices()`（1AFA 耳机）与 `AsusDeviceHelper.QueryAllRogKeyboards()`（1AD0 / 1C10 键盘）。
- 在 `QueryAllRogKeyboards()` 返回前与 `DeviceManager.QueryAllDevices()` 合并蓝牙自定义设备之后、排序与主设备选择之前，执行 `ApplyCrossTransportKeyboardArbitration(List<MouseBatteryInfo> devices)`：
  - 仅当存在在线有线键盘且 `BatteryPercent >= 0 || IsCharging` 时，或存在在线蓝牙键盘（`DeviceId` 以 `BT_` 开头）且 `BatteryPercent >= 0` 时，按非空 `DeviceName`（缺失回退 `CustomName`）去空格转大写包含匹配同型号 2.4G 键盘，将 2.4G 条目置为 `IsConnected = false; IsSleeping = true; IsCharging = false;` 并设置提示语。
  - 若仅仅是“开关在 2.4G 时插线充电”且有线通道无响应，则不触发压制。
- `BuildTrayBatterySummary`：
  - 分别选出最优在线鼠标、最优在线键盘、最优在线耳机/耳塞（未隐藏）。
  - 键盘择优规则：有线键盘 > 非休眠且有真实电量 > 非休眠 > 休眠。
  - 耳机/耳塞择优规则：非休眠且有真实电量 > 非休眠 > 休眠。
  - 输出以 `"  |  "` 拼接 `"鼠标 ..."`、`"键盘 ..."`、`"耳机 ..."`（兼容现有仅含鼠标+键盘时的 `"鼠标 X%  |  键盘 Y%"` 格式）。

### 3.4 署名与文档归档
- 更新 `AsusDeviceHelper.cs`、`AsusDeviceHelper.Rog.cs` 头部注释；
- 更新 `README.md` 极客殿堂贡献榜及第一梯队 ASUS ROG 支持型号表；
- 更新 `docs/HARDWARE_PROTOCOLS.md` 归档 `1AD0`、`1AFA`、`1C10` 报文与三通道仲裁规则；
- 更新 `CHANGELOG.md` 记录本次贡献融合。
