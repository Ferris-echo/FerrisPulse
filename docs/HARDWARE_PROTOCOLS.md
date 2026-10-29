# 🔌 FerrisPulse · 灵脉 硬件私有协议字节字典 (On-Demand Reference)

> **⚠️ 注意力管理提示 (Attention Budget Notice)**：  
> 本文档为**按需查阅字典 (On-Demand Dictionary)**，专门归档各品牌外设的底层 HID 报文结构、命令字与字节偏移。  
> **AI Agent 在日常 UI 修改、架构重构或通用调试时【无需】通读本文档**；仅当需要排查或扩展某一特定品牌的底层通信报文时，再按品牌检索对应小节。

---

## 1. 鼠标与键鼠双模协议

### 1.1 雷蛇 (Razer) 90/91 字节私有协议 (`RazerDeviceHelper.cs`)
- **VID**: `0x1532`，90 字节标准 Razer USB 控制帧（带前导 Report ID `0x00` 时为 91 字节）。
- **核心指令**：
  - 电量查询：Class `0x07`, Cmd `0x80`（`byte[10]` 为 `0..255` 原始电量，换算 `(raw * 100) / 255`）。
  - 充电状态：Class `0x07`, Cmd `0x84`（`byte[10]` 为 `0/1`）。
  - DPI 读写：Class `0x04`, Cmd `0x05` / `0x06`（支持 Focus Pro 35K Gen-2 1-DPI 步进与 XY 轴独立设置）。
  - 回报率：Class `0x00`, Cmd `0x40`（传统 125~1000Hz）与 HyperPolling `8000Hz` 协议。

### 1.2 罗技 (Logitech) HID++ 2.0 / 4.2 协议 (`LogitechDeviceHelper.cs`)
- **VID**: `0x046D`。Lightspeed `C547` 接收器必须优先锁定 20 字节长集合（`0xFF00/0x0002`），避开 7 字节短集合。
- **Ping 握手**：`txPing[3] = 0x18`（Root Feature 函数 1 `getProtocolVersion` + `swid = 8`），Token `0x5A`。
- **常用 Feature**：
  - `0x0005` (`DeviceName`): 实际 ASCII 名称从 `byte[4]` 开始读取。
  - `0x1004` (`UnifiedBattery`): `byte[4]` 为百分比（`0..100`），`byte[5]` 为离散档位，`byte[6]` 为充电状态（`1`=充电中, `2`=充满, `3`=放电）。
  - `0x2201` / `0x2202` (`AdjustableDPI`): 板载 DPI 读取与设置。
  - `0x8060` / `0x8061` (`ReportRate`): 支持最高 8000Hz（HERO 2 传感器系列）。

### 1.3 雷柏 (Rapoo) AHUB 2.0 遥测流与 EEPROM 协议 (`RapooDeviceHelper.cs`)
- **VID**: `0x24AE`
- **Report ID `0x07` (`col09`) 19 字节异步遥测流**：
  - 端点：`UsagePage 0xFF00 / Usage 0x0002, InputLen=19`，使用 `FILE_FLAG_OVERLAPPED` 非阻塞读取。
  - 偏移：`[0]=0x07`, `[2]=当前档位(0-based)`, `[3..4]=DpiX(LE)`, `[5..6]=DpiY(LE)`, `[7]=充电状态(0x01放电/0x02充电)`, `[8]=电量百分比(0..100)`, `[10]=回报率码`, `[13..14]=PID`, `[17]=档位总数`。
- **EEPROM 读写避让**：特征端点用 `0` 权限打开，EEPROM 写入后保持 800ms 静默期，轮询恒定推移时间戳防 50ms 高频死循环。

### 1.4 瓦尔基里 (Valkyrie) 键鼠协议 (`ValkyrieDeviceHelper.cs`)
- **VID**: `0x374A`（及代工 `0x3151`, `0x347A`）。
- **0xF7 状态与电量查询**：
  - 标准响应：`rx[0]=0x00, rx[1]=0x01` 时 `baseOffset = 1`；无前导 `rx[0]=0x01` 时 `baseOffset = 0`。
  - **VK 87 Max V2 (`374A:A272`) 特例（贡献者 “Zero”）**：2.4G 接收器返回 `00 00 <batt> 00 00 00 01 01`（次字节为 `0x00`），此时 `rx[0] == 0x00` 必须设 `baseOffset = 1`，使 `kbBatt = rx[2]`、`devType = rx[7]` 准确命中。
- **鼠标配置**：`0xD3` 读取回报率、`0xD4` 读取多档位 XY 独立 DPI、`0x54` 写入多档位 DPI、`0x04` 写入回报率（最高 8000Hz）。

### 1.5 CompX (`0x3554`) / VGN / VXE / ATK / 迈从 G75 PRO 协议族
- **同 VID (`0x3554`) 键鼠端点物理隔离铁律（贡献者 “yaphets”）**：
  - **VGN 蜻蜓 F1 Pro (`3554:F503`) 等鼠标**：`UsagePage 0xFF02 / Usage 0x0002`，输入/输出严格为 **17/17 字节**，Report ID `0x08`。通信顺序：`Cmd 1` 握手 -> `Cmd 2` 驱动激活 -> `Cmd 3` 在线查询 -> `Cmd 4` 电量查询（回复 `byte[6]` 电量，`byte[7]==1` 充电，校验和为 `85 - (sum(b[1..15]) & 0xFF) - 8`）。
  - **迈从 MCHOSE G75 PRO (`3554:FA09`) 键盘**：同样使用 `3554` 与 `FF02/0002`，但输入/输出严格为 **20/20 字节**！查询帧 `13 4A 00 ... 5D`（前 19 字节和低 8 位校验），回复 `byte[5]` 为电量（`0..100`），`byte[6]` 为状态（`0x10` 充电 / `0x01` 电池供电）。
  - **隔离要求**：`VgnDeviceHelper.IsVgnProtocolEndpoint` 必须校验 `inputLength == 17 && outputLength == 17 && pid != 0xFA09`，严禁使用 `>= 17`。
- **ATK F1 Ultimate 2.0 (`373B:11D9`, `8k dongle-L`)（贡献者 “好的很”）**：
  - 仅暴露 `UsagePage 0xFF02 / Usage 0x0002` 的 **17/17 字节 Interrupt 端点**（`FeatureReportByteLength == 0`）。
  - **严禁在 `WriteFile` 前执行 `DrainReports`**，否则会打断 8K 接收器与鼠标的无线握手状态机。
  - 发送 `08 04 00 ... 49`（Beken `0x55` 校验和），读取 `08 04 00 00 00 02 <batt> <chg> <mv_hi> <mv_lo> ...`。

### 1.6 迈从 (MCHOSE) A5 V2 Ultra 鼠标与 G87 V2 键盘
- **A5 V2 Ultra (`MchoseMouseHelper.cs`)**：
  - `UsagePage 0xFF01 / 0xFF0B`：短报文 `0x11` 发送 `CMD_IDENTITY (0x06)`（载荷异或 `0xFF`），回复 `[11] ^ 0xFF` 为电量，`[12] ^ 0xFF` 为充电；长报文 `0x12` 发送 `0x67/0x57/0x41` 读写 6 档 XY DPI 与 8K 回报率。
- **K7 V2 Ultra+ 等新一代 4D 帧协议族 (`MchoseMouse4DHelper.cs`)**：
  - **支持 PID 白名单**：`0x1001`（K7 Ultra）、`0x1014`、`0x1016`、`0x1018`（K7 V2 Ultra+，实机验证）、`0x101C`、`0x101D`，VID 固定 `0x3837`。
  - **帧结构（64 字节，首字节即 Report ID `0x4D`）**：
    ```
    [0]=0x4D  [1]=0x01  [2]=flags(1)  [3]=data 长度
    [4]=cmd 低字节  [5]=cmd 高字节  [6]=bizCode  [7]=sequence
    [8..]=data  [8+len]=校验和
    ```
    校验和为 `frame[2]` 到 `frame[7+len]` 逐字节 XOR。**payload 不做 `^0xFF` 反转**（这是与旧 A5 协议最关键的区别）。
  - **传输要求**：必须 `CreateFile` 带 `GENERIC_READ|GENERIC_WRITE` 打开，用 `WriteFile`/`ReadFile` 收发中断报告；`HidD_SetFeature`/`HidD_GetFeature` 对该协议族**一律无效**。
  - **忙标志**：设备刚唤醒或正在落盘配置时，回包数据区首字节为 `0xFF`（无有效数据），必须退避重发（`BusyRetryCount × BusyRetryDelayMs`），不能当作有效应答。
  - **命令表**：
    | 命令 | 方向 | 用途与关键偏移 |
    | --- | --- | --- |
    | `0x0900` | 读 | 设备信息：`[8..9]`=VID(LE)、`[10..11]`=PID(LE)、`[12]`=配置档数、`[13]`=宏存储类型、`[14..15]`=共享宏区、`[16..17]`=宏单元、`[18]`=连接状态、`[19]`=充电状态、`[20]`=电量%、`[21]`=游戏模式 |
    | `0x0002` | 读 | 性能配置：`[8]`=配置档、`[9]`=高4位 USB 回报率索引/低4位 USB DPI 档、`[10]`=高4位 2.4G 回报率索引/低4位 2.4G DPI 档、`[11]`=休眠分钟、`[12]`=休眠模式、`[13]`=传感器、`[14]`=角度、`[15]`=左键消抖、`[16]`=右键消抖 |
    | `0x0003` | 读 | DPI：`[8]`=配置档、`[9]`=方向、`[10]`=档位总数、`[11]`=当前档位、`[12]`=是否 XY 独立、`[13..24]`=6×uint16 LE DPI |
    | `0x0103` | 写 | DPI 整表回写，data 17 字节 `[档位, 方向, XY独立, 档位总数, 当前档位, 6×uint16]` |
    | `0x0102` | 写 | 性能配置全量回写，data 19 字节（只改回报率半字节，其余字段原样回写） |
    | `0x0901` | 读 | 固件版本，data `[0]`=鼠标 / `[1]`=接收器 |
  - **连接状态**：`[18]` 为 `0` 表示有线直连，`2` 表示 2.4G 接收器链路。
  - **回报率索引**：协议内按「RATES 索引 + 1」存储，RATES = `125/500/1000/2000/4000/8000` Hz；索引 0 仍发 0。
  - **DPI 上限**：搭载 PAW3950 的 4D 机型上限 42,000 DPI、6 档板载；旧版 A5 V2 Ultra 保持 26,000 DPI / 5 档。
  - **XY 双轴能力必须以 `0x0003` 的 `[12]` (HasY) 为准，不可硬编码**：
    实测 K7 V2 Ultra+（PAW3950 单传感器）该位恒为 `0`，向 `0x0103` 写入 `1200x800` 后回读为 `1200x1200`，
    固件强制把 Y 轴同步为 X 轴；`0x0102` 的 XY 标志位写入后同样被忽略。因此
    `BuiltInDrivers.PopulateCapabilities` 对 4D 协议族取 `dev.IsXyIndependent`（即 HasY），
    仅对真实支持双轴的旧版 A5 V2 Ultra 保持 `true`，避免向用户暴露无效开关。
  - **与旧协议隔离**：`MchoseMouseHelper.FindControlPath` 遇到上述 PID 直接跳过，避免重复卡片；四个写入入口优先委派 4D 驱动，失败自动回退旧协议。
  - **蓝牙 BLE 形态（HID over GATT，PID `0x4026`）**：
    - 同一台 K7 V2 Ultra+ 切到蓝牙后**不再**以 USB 厂商接口出现，而是成为 BLE HID over GATT 节点，Windows 在该节点上报的 PID 为 `0x4026`（与 USB 接口 PID `0x1018` 不同，是固件另编的身份，不是笔误）。
    - **无厂商通道**：BLE 形态只暴露标准鼠标/键盘/多媒体集合，`0x4D` 帧协议在此形态下完全不可达。因此 DPI 档位、X/Y 双轴、回报率一律**不可读写**，必须由 `MchoseMouseDriver.PopulateCapabilities` 按 `Transport` 含「蓝牙」如实降级为 `SupportsDpi = false` / `SupportsDpiStages = false` / `SupportsPollingRate = false` / `SupportsXyIndependence = false` / `SupportsHardwareDpiMonitor = false`，UI 落入 Generic Mouse 分支（与上游对「能力缺失鼠标」的既有处理一致）。
    - **电量走 Windows 原生 GATT 电池服务**：读 `DEVPKEY_Device_BatteryLevel`（`DEVPROP_TYPE_BYTE`，单字节 0-100）。**不能**按 `ToInt32` 读，否则会把相邻字节一并算进来得到荒谬数值。
    - **关键坑：VID/PID 与电量分属两个不同节点**，两步缺一不可：
      ```
      BTHLEDEVICE\{GATT 服务 GUID}_DEV_VID&023837_PID&4026_REV&0100_<MAC>\...   ← 有 VID/PID，无电量
      BTHLE\DEV_<MAC>\...                                                      ← 有电量与 FriendlyName，无 VID/PID
      ```
      必须先枚举 `BTHLEDEVICE`（`SetupDiGetClassDevs(..., "BTHLEDEVICE", ...)`）拿到 MAC 与 PID，再回到 `BTHLE` 按 MAC 找父节点读电量。
    - **VID/PID 前缀约定**：实例 ID 里写作 `VID&023837` / `PID&4026`，前两位是 Windows 的序号前缀，解析时必须只取**末 4 位**作为真实 VID/PID。
    - **MAC 解析不能用 `BluetoothDeviceHelper.ExtractMac`**：它按 `DEV_` 后取 12 位，对带 `VID&/PID&` 前缀的 BLE 格式会截成 `VID&023837_P`。正确做法是取**最后一个反斜杠之前**、且该段中**最后一个下划线之后**的 12 位十六进制（不能取第一个反斜杠，`BTHLEDEVICE` 之后就有一个）。
    - **充电状态不可读**：标准 GATT 电池服务只给百分比，没有充电标志位，`IsCharging` 恒为 `false`，**绝不猜测**。
    - **性能铁律**：BLE 在线时必须**先**返回并跳过 `0x4D` 查询。鼠标切蓝牙后 2.4G 接收器通常仍插在机上但已断链，此时 0x4D 通道每次查询都要把 `BusyRetryCount(8) × BusyRetryDelayMs(90)` 全部耗完才放弃，**单次约 4.2 秒**；若不跳过，每轮 UI 刷新都会白卡 4 秒以上（实测由 4.2 s 降至 55 ms）。
    - **卡片去重**：旧版通用 HID 扫描曾在 `HKCU\Software\RazerBatteryTray\CustomDevices` 下写入 `HID:3837:1018` 通用记录，与原生 `MCHOSE:` 记录并存会让主界面出现两张卡（通用卡永远 `-1%`）。`DeviceManager.CleanupDuplicateMchoseDevices()` 只删 `HID:3837:` 前缀且 PID 命中 4D 白名单的记录，原生记录保持不动。
- **G87 V2 (`3837:3033`, `MchoseDeviceHelper.cs`)**：
  - 优先选 `UsagePage 1 / Usage 0` 的 65 字节集合（MI_01），发送未编号性能配置查询帧 `00 55 05 00 38 38 ...`（65 字节），回复 `AA 05 ...` 中 `offset 40` 为电量，`offset 41` 为充电标志。
- **Z75S 三模机械键盘 (`258A:01BB`, `MchoseDeviceHelper.cs`)**（社区贡献者 `@WaterGauge` 贡献）：
  - 专属 `UsagePage 0xFF02 / Usage 0x0002` 20 字节厂商集合（避开按键端点）。
  - 先以命令 `0x05` 查询并校验型号 Token `0A0000000009`，实现同公版 VID 方案间的精确物理隔离。
  - 再以命令 `0x4A` 查询电量，回复 `rx[5]` 为电量百分比，`rx[6]` 高半字节为充电状态；第 19 字节为前 19 字节累加和。

### 1.7 宏碁 (Acer) OMR331 旗舰版及 OMR 全系 30+ 款无线鼠标 (`AcerDeviceHelper.cs`)
- **贡献者**: “Zero”
- **端点**: 无线接收器 `249A:5C2F`（及有线 `248A:5D2E/5E2E`），`UsagePage 0xFF01 / Usage 0x0010`，33/33 字节 Interrupt。
- **协议**: 发送 33 字节 `00 10 00...`，回复 `00 10 00 01 0B <4字节 ASCII dev_id 如 "R332"> <fw_lo> <fw_hi> ... [14]=battery(0..100)`。
- **注意**: 无线接收器下 `byte[15]` 会在未插线时随唤醒/休眠在 `0x01/0x00` 间漂移，无线通道下强制 `isCharging = false`。

### 1.8 OpenMouse / All-in-One 通用电竞协议引擎 (`UniversalGamingDeviceHelper.cs`)
- **引擎一 (CompX / PixArt 64B Feature `0x83`)**: 发送 `00 00 00 02 02 00 83...`，轮询 `GetFeature` 跳过 `0xA0` 等待帧，匹配 `A1 00 02 02 00 83 [chg] [pct]`（覆盖 Lamzu, Pulsar, WLmouse, Attack Shark, Ajazz, Darmoshark, Glorious, Ninjutso, RAWM, Incott, Finalmouse, Keychron, Dareu 等）。
- **引擎二 (Beken / ATK / Endgame Gear 16/17B `0x08 0x04`)**: `0x55` 互补校验和，解析电量、充电与毫伏电压。
- **引擎三 (Vaxee 64B Feature `0x0E 0xA5`)**: Page `0x0B` 电量（`raw * 5%`）、Page `0x10` 充电状态。
- **引擎四 (怒喵 Angry Miao 62B RACE Feature `0x14`)**: 指令 `0x30CF` 读取 `AM Infinity .97` 电量与充放电状态。

---

## 2. 键盘、手柄与无线耳机协议

### 2.1 NuPhy 矮轴机械键盘 (`NuphyDeviceHelper.cs`)
- **支持系列**: Air60 / Air75 / Air96 / Halo65 / Halo75 / Halo96 / Field75 / WH80 等。
- **能力**: 0 权限打开私有 Feature 通道读取电量，支持自动休眠开关、Win 键锁定、背光开关/亮度与侧灯控制。

### 2.2 飞智 (Flydigi) 手柄 GPA6 协议 (`FlydigiDeviceHelper.cs`)
- **型号**: APEX 6 / APEX 6 Pro (`37D7:2502`)、Vader 5 Pro (`37D7:2401`)。
- **协议**: `UsagePage 0xFFA0`，33 字节中断报告 `00 5A A5 01 02 ...`（命令 `0x01`），回复 `payload[6]` 低 4 位为电量档位（`<=5 -> *20%`, `<=10 -> *10%`），高 4 位为充电标志。

### 2.3 无线电竞耳机与音频设备
1. **雷蛇旋风黑鲨 V2 Pro 2.4 (`1532:0555`, `RazerHeadsetHelper.cs`)**:
   - `FeatureReportByteLength == 0`，走 `UsagePage 0xFF00` 64 字节中断 OUT/IN，`PA`/`PI` 帧命令 `0x21` 电量、`0x2A` 充电、`0x20` 在线。
2. **赛睿 Arctis Nova 5 / 5X (`1038:2232` / `2253`, `SteelSeriesDeviceHelper.cs`)**:
   - `UsagePage 0xFFC0` 65 字节中断报告发送 `00 B0`；回复 `rx[2]==0x02` 为离线，`rx[4]` 为电量，`rx[5]==0x01` 为充电。
3. **索尼 INZONE 耳机系列 (`SonyInzoneHelper.cs`)**（社区贡献者 `@3155679` 贡献 H5 支持）:
   - **通道**: 2.4G 接收器（入耳 Buds `054C:0EC2` / 头戴 H5 `054C:0EBF` 等），`UsagePage 0xFF04 / Usage 0x0001`，64 字节 In/Out 中断端点（`FeatureReportByteLength == 0`）。
   - **协议**: Report ID `0x02`，查询命令 `02 0C 01 00 FC 08 96 C3 41 04 01 <tid_lo> <tid_hi> <checksum>`。
   - **型号自适应宽判法**: 头戴型号（H5/H7/H9）载荷为 2 字节 `[status(=charger), percent]`，`buffer[15]` 为校验和；入耳型号（Buds）载荷为 6 字节 `[st_L, pct_L, st_R, pct_R, st_Case, pct_Case]`，`buffer[19]` 为校验和。自适应按 `hci_len` 宽度派发，拒绝短包伪造。
4. **罗技 G PRO X Wireless (`046D:0ABA`, `LogitechDeviceHelper.cs`)**:
   - `UsagePage 0xFF43` 20 字节通道查询 HID++ 4.2 `0x1F20 ADC_MEASUREMENT` 毫伏电压，按 12 阶锂电曲线插值换算百分比。
5. **极度未知 HyperX Cloud III Wireless (`HyperXDeviceHelper.cs`)**:
   - 社区贡献者 **“rm -rf /*”** 贡献，支持 HP/Kingston 双代工版本无线电量与充电检测。
6. **Apple AirPods / Beats / Magic 系列 (`AppleDeviceHelper.cs`)**:
   - 强制比对 Windows 系统经典蓝牙配对白名单（防路人 BLE 广播误扫），剔除 `Wireless iAP` 虚拟端点伪名称。
7. **漫步者 (EDIFIER) 花再 Halo Space 2.4G 无线头戴耳机 (`2D99:E049`, `EdifierDeviceHelper.cs`)**（社区贡献者 `@Aztand` 贡献）:
   - **通道**: `UsagePage 0xFF14 / Usage 0x0001`，64 字节中断端点（避开 `0xFF13` 62 字节固件通道）。
   - **协议**: 帧头 `2E AA EC <cmd> <len_hi> <len_lo>`，载荷逐字节 `XOR 0xA5` 掩码，校验和为从下标 1 起算的 8 位累加和。
   - **指令**: `0xD0` 读电量（`0x86 ^ 0xA5 = 35%`）、`0xC9` 读 UTF-8 名称、`0xC6` 固件版本。
   - **注意**: 2.4G 无线模式下一旦接充电线会被设备端硬件强制关机，诚实缺省 `isCharging = false`，不臆造充电标志。
