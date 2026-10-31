# 迈从 MCHOSE G7 / G98V2 与水月雨 MOONDROP GM-01 PRO 适配

适配日期：2026-10-30。基线：`b1a730b`（v2.2.0）。本机实测环境：Windows 10/11 x64，`csc.exe` 4.8（C# 5）。

## 支持范围

| 设备 | VID / PID | 能力 | 状态 |
| --- | --- | --- | --- |
| MCHOSE G7 鼠标（2.4G 接收器） | `A8A5:2255` | 电量 0–100%、充电标志、版本号、在线状态 | 实机读取 100% |
| MCHOSE G98V2 键盘（2.4G 接收器） | `41E4:2004` | 电量 0–100%、充电/充满半字节、型号 token | 实机读取 98% |
| MOONDROP GM-01 PRO 耳机 | `35D8:0133` | 电量 0–100%、固件版本、在线状态 | 实机读取 90% |

G 系列鼠标（G3 / G3 V2 / A7e / G7）共用同一套 65 字节协议，本次只对 `A8A5:2255` 做了实机验证；
其余型号沿用同一帧格式，但未逐台实测，故不额外声明支持。

## 设备识别

| 项目 | G7 | G98V2 | GM-01 PRO |
| --- | --- | --- | --- |
| USB 制造商字符串 | `YJX-CHIP` | `SINOWEALTH` | `MOONDROP` |
| USB 产品字符串 | `MCHOSE G7` | `MCHOSE 2.4G Wireless` | `MOONDROP GM01 Pro` |
| 通信集合 | UsagePage `FF01` / Usage `0010` | UsagePage `FF02` / Usage `0002` | UsagePage `0C` / Usage `0001` |
| In / Out / Feature 长度 | `65 / 65 / 0` | `20 / 20 / 0` | `64 / 64 / 0` |

制造商字符串只反映主控方案（`YJX-CHIP`、`SINOWEALTH`），**不能**作为品牌判据；
识别一律使用 `VID + PID + UsagePage/Usage + In/Out 报告长度` 四元组。

## 报文：MCHOSE G7 系列（65 字节）

帧为完整 Windows HID 缓冲区，`[0]` 是 Report ID（本机型为 `00`），其后为 payload，
与官方 `/gmouse` WebHID 实现的 `sendReport(id, data)` 视图相差 1 字节。

```text
电量 TX: 00 55 30 A5 0B 2E 01 01 01 00 00 ... 00      (65B)
电量 RX: 00 AA 30 A5 0B 0A 01 01 01 64 00 ... 00      (65B，实机样本 = 100% 未充电)
版本 TX: 00 55 03 00 ... 00
版本 RX: 00 AA 03 00 36 38 00 00 00 "YJX.co.mouse" ... 32 32 33 ...
在线 TX: 00 55 ED 00 ... 00
在线 RX: 00 AA ED 00 ... 00
```

| RX 偏移（含 Report ID） | 含义 |
| --- | --- |
| 0 | `00` Report ID |
| 1 | `AA` 应答标识 |
| 2 | 命令回显：`30` 电量 / `03` 版本 / `ED` 在线 |
| 3–4 | `A5 0B` 固定标识（电量应答） |
| 9 | 电量百分比 `0..100` |
| 10 | 充电标志：`01` 已接线 / `00` 电池供电 |
| 24–26 | 版本号 ASCII 三位（实机 `"223"`） |

分辨率注意：`ParseBatteryResponse` 同时兼容「带 Report ID 的 65 字节 Windows 缓冲区」
与「已剥离 Report ID 的 64 字节 WebHID 视图」，两者偏移差 1 字节。

## 报文：MCHOSE G98V2（20 字节，SINOWEALTH 方案）

与仓库既有 Z75S（`258A:01BB`）同构，直接复用 `MchoseDeviceHelper` 的通用帧。

```text
电量 TX: 13 4A 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 5E
电量 RX: 13 4A 01 00 02 62 00 00 00 00 00 00 00 00 00 00 00 00 00 C2
型号 TX: 13 05 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 19
型号 RX: 13 05 01 00 06 0A 00 00 00 00 18 00 00 00 00 00 00 00 00 41
```

| RX 偏移 | 含义 |
| --- | --- |
| 1 | 命令回显：`05` 型号 / `4A` 电量 |
| 4 | 有效载荷长度 |
| 5 | 电量百分比 |
| 6 | 高半字节 = 充电 / 低半字节 = 充满 |
| 19 | 校验和 = `sum([0..18]) & 0xFF` |

型号靠 token 区分：Z75S 为 `0A 00 00 00 00 09`，G98V2 为 `0A 00 00 00 00 18`，
因此同 VID/PID 的不同型号不会被互相顶替。`41E4:2004` 的接收器只接受 Report ID `0x13`，
其余 ID 会返回 `87 (ERROR_INVALID_PARAMETER)` 或 `225`。

## 报文：MOONDROP GM-01 PRO（64 字节）

协议来自官方网页驱动 <https://hub.moondroplab.tech/>（WebHID）。命令通道是
**`UsagePage 0x0C / Usage 0x0001` 的 64 字节集合**——不是 `FF02` 私有页。

帧结构（完整 64 字节缓冲区）：

```
[0] = 0x4B   Report ID（官方 config.reportId = 75）
[1] = OP     0x80 读 / 0x01 写
[2] = CMD    0x27 电量 / 0x0C 固件 / 0x31 在线 / 0x2C SKU-SN
[3] = payload 长度（读命令为 0）
[4..63] = payload，其余补零
```

电量查询与应答：

```
电量 TX: 4B 80 27 00 00 00 … （补零到 64 字节）
电量 RX: 4B 80 27 06 00 00 00 00 01 5A 00 …
```

| RX 偏移 | 含义 |
| --- | --- |
| 1 | 操作码回显 `0x80` |
| 2 | 命令回显 `0x27` |
| 3 | payload 长度（电量应答为 6） |
| 8 | 有效标志（`1` = 本次读数有效） |
| 9 | 电量百分比 0–100 |

辅助命令实测：`4B 80 0C 00` → `4B 80 0C 05 31 2E 35 2E 31`（ASCII `1.5.1`）；
`4B 80 31 00` → `4B 80 31 01 01`（`[4]=1` 表示在线）。

## 已知限制

1. **G7 不提供 DPI / 回报率控制**：官方协议里 `AA FA` 帧是设备主动上报，
   没有查询或写指令，因此 `SupportsDpi` 与 `SupportsPollingRate` 均为 `false`。
2. **GM-01 PRO 不提供充电状态**：官方 WebHID 协议只有电量命令，没有充电字段；
   USB 直连即持续供电，因此 `IsCharging` 恒为 `false`，不渲染闪电图标。
3. **端点选择是最大的坑**：`FF02:0002` 还有 64/65 字节的镜像端点，写入永远返回成功
   却零回帧（首字节 `0x00..0x3F` 全量探测、迈从两套已知帧都试过，均无响应）。
   真正的通道藏在 Consumer 页的 64 字节集合里，因此端点判定必须按
   `UsagePage / Usage / In / Out` 四元组精确匹配，绝不能"取报告长度最大的集合"。

## 验证

```powershell
cd D:\code\FerrisPulse
& cmd.exe /c "build.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_all_tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\scratch\list_all_devices.ps1
```

离线单测（不产生任何物理 HID 写操作）：

- `tests/test_mchose_g7_gmouse.ps1`：26 条断言，锁定帧字节、偏移、64/65 双视图与全部拒绝条件。
- `tests/test_moondrop_gm01pro.ps1`：43 条断言，锁定协议常量、帧格式、应答解析、端点四元组与去重链路。

## 改动文件

| 文件 | 说明 |
| --- | --- |
| `src/Hardware/MchoseGMouseHelper.cs` | 新增，G 系列 65 字节协议、缓存与 5s 节流 |
| `src/Hardware/MoondropDeviceHelper.cs` | 新增，GM-01 PRO 官方 WebHID 协议（`0x4B`/`0x80`/`0x27`）电量读取与端点四元组判定 |
| `src/Hardware/MchoseDeviceHelper.cs` | 扩展 `41E4:2004`（G98V2）型号 token 与解析 |
| `src/Hardware/GenericHidHelper.cs` | 通用扫描跳过 MOONDROP，避免重复条目 |
| `src/Hardware/UniversalGamingDeviceHelper.cs` | 排除 `pid_2004` / `pid_2255` |
| `src/Hardware/DeviceManager.cs` | 新增 2.966 / 2.967 注册段 |
