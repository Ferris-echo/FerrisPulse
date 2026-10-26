# 漫步者 花再 Halo Space (EDIFIER HALO SPACE) EC 协议逆向报告

> 目标设备：**漫步者 花再 Halo Space 2.4GHz 无线头戴式耳机**
> HID 标识：`VID_2D99 & PID_E049`，产品字符串 `漫步者 Halo Space D1`，制造商 `EDIFIER`
> 对应驱动：`src/Hardware/EdifierDeviceHelper.cs`
> 对应契约测试：`tests/test_edifier_halo_space.ps1`
> 对应 FDAS 存档：`examples/adapters/edifier_halo_space.fp-adapter.json`

本报告中的所有字节序列均来自**真实设备在线抓包**，未使用任何推测值；未经验证的结论已明确标注。

---

## 1. 设备枚举与端点选择

官方管理器 `EDIFIER TempoHub`（PyInstaller 打包的 PyQt6 程序，安装于 `C:\Program Files (x86)\EDIFIER TempoHub`）通过 Python `hid` 模块与设备通信。枚举结果如下（SetupAPI + `hid.dll` 直接读取）：

| 集合 | UsagePage | Usage | Input | Output | Feature | 用途判定 |
| :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| `mi_03` | `0x000C` | `0x0001` | 2 | 0 | 0 | 消费类多媒体键 |
| `mi_04&col01` | `0xFF13` | `0x0001` | 62 | 62 | 0 | 厂商私有（疑似 OTA / 日志） |
| **`mi_04&col02`** | **`0xFF14`** | **`0x0001`** | **64** | **64** | 0 | **EC 控制通道（本适配目标）** |

**结论 E1**：耳机主控私有通道为 `UsagePage 0xFF14 / Usage 0x0001`，64 字节中断双向报文，无 Feature Report。
这与 TempoHub 的发现逻辑一致：`vendor_id == 11673 (0x2D99) and usage == 1 and usage_page == 65300 (0xFF14)` 即判定为 `EDIFIER` 设备。
另有 `37D7:1002 (UsagePage 0xFFA0)` 为蓝牙 HID-over-GATT 通道，2.4G 接收器场景不涉及。

---

## 2. 帧格式

TempoHub 的 `driver/common.pyc` 常量：`COMMON = 46 (0x2E)`、`HEADER = 170 (0xAA)`、`DEVICE_TYPES = [0xEC, 0xED]`、`MAX_READ_LENGTH = MAX_SEND_LENGTH = 64`。
组装函数 `add_len(n)` 输出大端两字节长度，`add_crc(buffer)` 输出 `sum(buffer[1:]) & 0xFF`（**自下标 1 起算，不含首字节**）。

### 2.1 主机 → 设备（Tx）

```
[0x2E][0xAA][DEVICE_TYPE][CMD_INDEX][LEN_HI][LEN_LO][PAYLOAD ^ 0xA5 ...][CRC][0x00 填充至 64]
```

* `DEVICE_TYPE`：`0xEC`（主设备通道）/ `0xED`（扩展通道，命令索引落在 0x100+ 时使用）
* `LEN`：**明文**载荷长度（大端）
* `CRC`：`sum(frame[1 .. 6+LEN-1]) & 0xFF`，即从帧头 `0xAA` 累加至载荷末字节，**不含** `0x2E`
* 不足 64 字节部分以 `0x00` 补齐后一次性 `WriteFile` 64 字节（**不做 ReportId 前置，不带 0x00 报告号**）

电量查询实测发送帧（64 字节）：

```
2E AA EC D0 00 00 66 00 00 00 ... 00
```

校验和验证：`0xAA + 0xEC + 0xD0 + 0x00 + 0x00 = 0x266`，`0x266 & 0xFF = 0x66` ✔

### 2.2 设备 → 主机（Rx）

```
[0x2F][0xBB][DEVICE_TYPE][CMD_INDEX][LEN_HI][LEN_LO][PAYLOAD ^ 0xA5 ...][CRC][... 缓冲区残渣 ...]
```

* 首字节 `0x2F` = Tx 的 `0x2E + 1`；帧头 `0xBB` = Tx 的 `0xAA + 0x11`（同一累加偏移规律）
* 校验和同样自下标 1 起算：`sum(frame[1 .. 6+LEN-1]) & 0xFF`
* **关键陷阱**：设备回传的 64 字节缓冲区**尾部残留历史报文**（实测固定以 `5A A3 A1 A5 A5 A5 C1 5A 3B 62 03 43 4C 3A 16 43 30 2D 94 97` 收尾）。
  解析时必须只截取 `[6, 6+LEN)` 区间，**绝不能**把整包当作有效载荷。

电量应答实测（有效区间 8 字节）：

```
2F BB EC D0 00 01 86 FE
```

校验和验证：`0xBB + 0xEC + 0xD0 + 0x00 + 0x01 + 0x86 = 0x2FE`，`0x2FE & 0xFF = 0xFE` ✔

---

## 3. 载荷加密：XOR 0xA5

TempoHub 的 `encrypt()` 依据 `device_encryption_map[path]` 分支处理：第 2 字节 `== 16` → 逐字节 `XOR 0xA5`；`== 48` → 按位取反；`== 49` → 变长加密封装；否则明文。Halo Space 走的是 **XOR 0xA5** 分支。

* **仅 PAYLOAD 参与掩码**，长度字段、校验和、填充字节全为明文（因此 CRC 必须按**线上字节**计算，不能先解掩码再算）。
* 加密与解密是同一运算（对称），驱动里 `MaskPayload()` 双向复用。

电量解码：`0x86 ^ 0xA5 = 0x23 = 35%` ✔

设备名称解码（17 字节）：`4D 2F 14 40 23 28 85 ED C4 C9 CA 85 F6 D5 C4 C6 C0` 逐字节 `^ 0xA5` 得
`E8 8A B1 E5 86 8D 20 48 61 6C 6F 20 53 70 61 63 65` = UTF-8 **“花再 Halo Space”** ✔

该结果本身就是掩码假设成立的强证据：任意错误掩码都不可能还原出合法 UTF-8 中文。

---

## 4. 命令索引表（实测）

| 命令 | 名称 | 载荷长度 | 实测解码结果 | 状态 |
| :---: | :--- | :---: | :--- | :---: |
| `0xD0` | 电量查询 | 1 | `0x86 ^ 0xA5 = 0x23` → **35%** | ✅ 已验证 |
| `0xC9` | 设备名称 | 17 | `花再 Halo Space` | ✅ 已验证 |
| `0xC6` | 固件版本 | 3 | `01 00 02` → **1.0.2** | ✅ 已验证 |
| `0xC8` | 蓝牙 MAC | 6 | `B4:E7:B3:31:52:D3` | ✅ 已验证 |
| `0x05` | 版本/协议号 | 1 | `0x07` | ✅ 已验证 |
| `0xD8` | 功能集位图 | 24 | `01 01 01 00 01 01 00 00 00 00 00 01 00 07 0A 00 1F 70 00 80 04 10 10 02` | ✅ 已验证 |
| `0xD5` | 当前 EQ | 1 | `0x00` | ✅ 已验证 |
| `0x08` | 场景/降噪模式 | 2 | `00 04` | ✅ 已验证 |
| `0xB3` | 麦克风状态 | 1 | `0x01` | ✅ 已验证 |
| `0xB4` | 音量/增益 | 1 | `0x4A` | ✅ 已验证 |
| `0xD3` | 自动关机查询 | 2 | `67 FE` | ✅ 已验证 |
| `0xF2` | TWS 设备状态（充放电） | — | **任何载荷长度均无回包** | ❌ 不支持 |

### 4.1 关于 0xF2（必须记录的负面结论）

TempoHub 的耳机主页依赖 `CM_F2 → device_status_query_parse` 返回 `('Peer','LB','RB','DB','DC','ED')` 六字节状态（对应充电值 + 充电态）。
对 Halo Space 分别以 0 字节、1 字节 `00`、2 字节 `00 00` … 6 字节载荷发送 `0xF2`，**全部超时无响应**。

**设备侧佐证**：Halo Space 在 2.4G 无线模式下**一旦接入充电器就会被设备强制关机**（厂商既定行为，非协议缺陷）。也就是说 2.4G 模式下根本不存在“既要保持连接、又要读到充电中状态”的窗口，即便 `0xF2` 有回包也无从验证。

**因此本适配不声明、不渲染充电状态**（`IsCharging` 恒为 `false`）。
这是刻意的“宁缺毋滥”：把充电位伪造成 `payload[1] & 0x01` 会在真实设备上显示错误的闪电图标。

---

## 5. 安全边界

* 本适配**只做只读查询**（`0xD0 / 0xC9 / 0xC6 / 0xC8`），不下发任何写指令。
* 不触碰固件升级路径（TempoHub 内 `resource/utils/dfu_bes.zip` 为 BES 平台 DFU 包）：错误的 DFU 握手有**变砖**风险，不在本次适配范围内。
* 每次轮询前 `DrainReports` 清空陈旧队列，单次轮询仅 1 条电量命令（名称/版本/MAC 首次成功后缓存），避免长期占用 2.4G 接收器的 HID 通道。
* 设备休眠/未连接时不返回 0，而是保留 `BatteryPercent = -1` 并标记 `IsSleeping`，由上层 UI 显示“休眠或未连接”。

---

## 6. 驱动实现映射

| 协议要素 | 实现位置（`src/Hardware/EdifierDeviceHelper.cs`） |
| :--- | :--- |
| 端点识别 `0xFF14 / 0x0001 / 64B` | `TargetUsagePage` / `TargetUsage` / `ReportLength`，`QueryAllEdifierDevices()` 枚举匹配 |
| Tx 建帧 | `BuildEcFrame(byte cmdIndex, byte[] payload)` |
| 累加和（自下标 1） | `CalculateChecksum(byte[] buffer, int offset, int count)` |
| XOR 0xA5 | `MaskPayload(byte[] payload)` |
| Rx 解析 + 越界/校验和防护 | `TryParseEcReport(byte[] report, byte expectedIndex, out byte[] payload)` |
| 命令收发（写 64B → 读 64B，400ms 超时，3 次重试） | `QueryDevice(IntPtr handle, byte cmdIndex, byte[] requestPayload, out byte[] responsePayload)` |
| 电量 `0xD0` | `ParseBatteryPayload` |
| 名称 `0xC9` / 版本 `0xC6` / MAC `0xC8` | `ParseNamePayload` / `ParseVersionPayload` / `ParseMacPayload` |

---

## 7. 复现方法

1. 设备以 2.4G 接收器连接，确认 `VID_2D99&PID_E049` 出现在系统中。
2. 编译后运行 `tests/test_edifier_halo_space.ps1`：内部通过内存反射校验建帧与解析（不依赖真机，纯协议契约）。
3. 真机验证可直接运行本仓库外的探针脚本（ctypes 直连 `hid.dll` + `CreateFileW` / `WriteFile` / `ReadFile` OVERLAPPED），发送第 2.1 节的 64 字节帧并读回 64 字节。

> 备注：Windows 上 `ctypes.wintypes` **不导出** `OVERLAPPED`，需自行定义结构体（`Internal`、`InternalHigh`、匿名 `Offset/OffsetHigh` 联合、`hEvent`），否则会得到 `AttributeError: module 'ctypes.wintypes' has no attribute 'OVERLAPPED'`。
