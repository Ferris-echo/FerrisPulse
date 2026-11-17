# 🎧 海盗船 HS80 MAX Wireless · FerrisPulse 适配报告

> 适配日期：2026-11-07  
> 适配环境：Windows 11 (26200) · 本机 USB 实机抓包  
> 目标设备：**Corsair HS80 MAX Wireless**（2.4G 接收器 `1B1C:0A97`）

---

## 1. 设备识别结果

| 角色 | VID:PID | 产品字符串 | 物理接口 | 结论 |
| :--- | :--- | :--- | :--- | :--- |
| HS80 MAX 2.4G 接收器 | `1B1C:0A97` | `CORSAIR HS80 MAX WIRELESS Gaming Receiver` | `MI_04` / `Col01`，UsagePage `0xFF42` | ✅ 本次适配，电量/供电状态可读 |
| HS80 MAX 耳机子设备 | `1B1C:0A96` | `CORSAIR HS80 MAX ...`（有线直连时出现） | 同一 Bragi 控制集合 | ✅ 预留识别，PID 握手 `0x12 == 0x0A96` |
| CORSAIR VANGUARD PRO 96 键盘 | `1B1C:2B0E` | `CORSAIR VANGUARD PRO 96 Hall Effect Gaming Keyboard` | `MI_03`（无可写控制端点，`WriteFile` 返回 `ERROR_INVALID_FUNCTION`） | ❌ 有线键盘，无电池，不虚标 |
| CORSAIR SLIPSTREAM 接收器 | `1B1C:1BDC` | `CORSAIR SLIPSTREAM WIRELESS USB Receiver` | `MI_01` 可写但报 `ERROR_INVALID_PARAMETER (87)`，`MI_02/04/05` 为 `ERROR_INVALID_FUNCTION` | ❌ 无法确认配对型号与协议，不虚标 |
| CORSAIR 虚拟 HID | `1B1C:1BF0` | （空） | `HID\VIRTUALDEVICE&10` | ❌ 虚拟设备，过滤 |

> 关键排查：本机标准 `HidP_GetCaps` / `HidD_GetFeature` 通道均返回 `INVALID_PREPARSED_DATA` / `ERROR_INVALID_FUNCTION`，因此适配**不依赖 Report Descriptor**，改用 `MI_04 + Col01` 路径 + Bragi PID 握手确权，兼容受限 HID 栈。

---

## 2. Bragi HID 协议（实机验证）

- **传输**：64 字节 HID Report；OUT Report ID `0x02`，IN 响应 Report ID `0x01`，异步通知 `0x03`。
- **帧格式**：
  - TX：`02 <target> <cmd> <prop> 00 ...`，`target 0x08` = 接收器，`0x09` = 耳机子设备（child_id=1）。
  - RX：`01 <child> <cmd> <err> <payload...>`，`err == 0x00` 表示成功。
- **固件约束**：两次写入间隔必须 ≥ 50ms，否则丢帧。

### 实测报文

| 用途 | TX | RX | 解析 |
| :--- | :--- | :--- | :--- |
| 读电量 | `02 09 02 0F 00...` | `01 01 02 00 EA 01...` | `0x01EA = 490` → **49.0%** |
| 读电量 | `02 09 02 0F 00...` | `01 01 02 00 72 01...` | `0x0172 = 370` → **37.0%** |
| 读供电状态 | `02 09 02 10 00...` | `01 01 02 00 02...` | 状态 `2` |
| 读子设备 PID | `02 09 02 12 00...` | `01 01 02 00 96 0A...` | `0x0A96` = HS80 MAX 耳机 |

### 供电状态语义修正（物理事实优先）

社区文档（ToastKiste21 / BrokeSkill）将 `0x10 == 2` 记为“已接电源/充满”，但本机实测：

- 电量从 **49% → 41% → 37% → 36%** 持续下降期间，状态恒为 `2`；
- 系统未枚举 `1B1C:0A96`（耳机未接 USB 线）；
- 因此 `2` 应为 **电池供电（放电）**，`1` 才是充电中。

最终映射：`0` 未知 / `1` 充电中 / `2` 电池供电 / `3` 已接电源但未充电或已充满。FerrisPulse 仅在 `1` 时点亮充电闪电，避免图标反复横跳。

---

## 2.5 M75 AIR 鼠标（SLIPSTREAM 接收器扩展）

同一 `CorsairDeviceHelper` 新增分部类 `CorsairDeviceHelper.Mouse.cs`，接管 SLIPSTREAM 接收器：

- 接收器 `1B1C:1BDC`，控制集合 `MI_01`，65 字节 Report（Report ID `0x02`），子设备 target `0x09`。
- 子设备 PID `0x12` 返回 `0x1BF2`（M75 AIR）。
- 电量 `0x0F` 返回 `00 01 02 00 76 02` → 630 → **63.0%**；状态 `0x10` 返回 `2`（电池供电）。
- DPI X/Y `0x21/0x22` 返回 `1600/1600`；回报率 `0x01` 返回 `4`（1000Hz）。
- 写入验证：SET `0x21=800`、`0x22=1200` 后回读 800/1200，恢复 1600/1600；SET 回报率 3/4 返回 `err=0`，重开句柄后读回 4。
- 鼠标输入帧 `00 00 ...` 会被严格过滤，仅接受 `00 01 <cmd> ...` 的同步响应。
## 3. 代码变更清单

| 文件 | 变更 |
| :--- | :--- |
| `src/Hardware/CorsairDeviceHelper.cs` | **新增**。HS80 Bragi 会话、PID 握手、电量/供电解析、50ms 节流、注册表离线缓存、休眠态保活。 |
| `src/Hardware/CorsairDeviceHelper.Mouse.cs` | **新增**。M75 AIR 65 字节 Bragi、电量/DPI/回报率读、DPI 双轴与回报率写、鼠标输入帧过滤。 |
| `src/Hardware/DeviceManager.cs` | 接入 `QueryCorsairDeviceInfo()`；新增 `CleanupDuplicateCorsairDevices()`：自动解除旧版隐藏标记，并用 `CorsairUserHidden` 尊重用户主动隐藏；设备 ID 前缀表加入 `Corsair`。 |
| `src/Hardware/BuiltInDrivers.cs` | 注册 `CorsairHeadsetDriver`（Priority 100，`BuiltIn:CorsairHeadset`）。 |
| `src/Hardware/UniversalGamingDeviceHelper.cs` | 增加 `0A97/0A96` 端点隔离，禁止通用 CompX 引擎向耳机控制集合盲发异构报文。 |
| `src/UI/Forms/MainForm.cs` | 启动时预加载 `CorsairDeviceHelper.LoadHardwareCache()`。 |
| `tests/test_corsair_hs80max.ps1` | **新增**。HS80 BOM/C# 5.0 契约、VID/PID 隔离、真实帧回放、错误帧拒绝、生命周期接入、实机/休眠分支。 |
| `tests/test_corsair_m75air.ps1` | **新增**。M75 AIR 65 字节帧、DPI/回报率解析、输入帧过滤与实机探测。 |
| `docs/HARDWARE_PROTOCOLS.md` | 新增 §2.3.8 协议字节字典。 |
| `README.md` / `CHANGELOG.md` | 支持列表与更新日志。 |

---

## 4. 验证结果

```text
cmd.exe /c "build.bat"
[SUCCESS] Build successful (Pure Open-Source Raw Binary): FerrisPulse.exe

powershell -ExecutionPolicy Bypass -File tests\test_corsair_hs80max.ps1
ALL 6/6 PASSED
实机读取：HS80 MAX Wireless / 36% / 充电:False
```

- 全量回归：`tests/run_all_tests.ps1` 共 129 项，本次改动相关项全部通过。
- 4 项环境性失败（`D:\FerrisPulse · 灵脉` 硬编码路径、`Assembly.LoadFrom/LoadFile` 远程加载限制、既有离线设备优先级断言）已在官方 pristine 源码上复现，确认与本次改动无关。
- 端到端：`DeviceManager.QueryAllDevices()` 返回 `Corsair:1B1C:0A97`，`Category = Headset`，`IsDonglePresent = true`；耳机休眠时保留缓存 36% 并标记 `IsSleeping`。

---

## 5. 提交给作者的三种方式

### 方式 A：GitHub Pull Request / Issue（首选）
1. Fork https://github.com/Ferris-echo/FerrisPulse
2. 将本目录变更提交到新分支（如 `feat/corsair-hs80-max`）
3. 发起 Pull Request，或在 Issues 附上本报告与抓包帧

### 方式 B：Gitee Pull Request / Issue（国内镜像）
- https://gitee.com/Ferris-echo/ferris-pulse

### 方式 C：Patch / 文本共享
- 使用配套生成的 `Corsair-HS80MAX-FerrisPulse.patch`（工作区根目录），通过邮件或 Issue 附件发送给作者。

> 建议在 PR 描述中注明：实机 VID/PID、`02 09 02 0F` 帧、`0x0A96` 握手、状态 `2 = 电池供电` 的修正依据，方便作者复核。

---

## 6. 参考与致谢

- [ToastKiste21/corsair-hs80-max-re](https://github.com/ToastKiste21/corsair-hs80-max-re)（MIT）—— Bragi 协议文档与固件反编译。
- [BrokeSkill/hs80-max-control](https://github.com/BrokeSkill/hs80-max-control)（MIT）—— C# Bragi 会话与上报布局参考。
- [ST1CKEL/HS80_Control](https://github.com/ST1CKEL/HS80_Control)（GPL-3.0）—— 电量属性与事件帧格式交叉验证。
- [ckb-next/ckb-next](https://github.com/ckb-next/ckb-next)（GPL-2.0）—— `BRAGI_BATTERY_LEVEL/STATUS` 常量交叉验证。

本次实现仅复用协议事实与常量，未复制上述项目的源代码；所有 C# 代码按 FerrisPulse 的 C# 5.0 / .NET 4.0 / 零依赖规范独立编写。
