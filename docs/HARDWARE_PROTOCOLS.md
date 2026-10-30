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
- **G87 V2 / K99 V3 (`3837:3033` 2.4G + `3837:2020` 有线, `MchoseDeviceHelper.cs`)**：
  - 优先选 `UsagePage 1 / Usage 0` 的 65 字节集合（MI_01），发送未编号性能配置查询帧 `00 55 05 00 38 38 ...`（65 字节）。
  - 响应帧固定结构：`[0]=0x00` 前导 `[1]=0xAA` `[2]=命令回显` `[3]=0x00` `[4]=校验` `[5]=数据长度` `[9..]=数据`。
  - **电量与充电在同一字节 `[41]` 的位域里**：`bit7 = 充电标志`，`低 7 位 = 电量百分比 (0-100)`。例如 `0xC3` = 充电中 + 67%，`0x44` = 未充电 + 68%。
    - 旧版解析 `battery = report[offset+40]`、`charging = report[offset+41] != 0` 存在**整体错位一字节 + 未掩盖 bit7** 的缺陷：`0xC3 = 195 > 100` 会被 0-100 校验拒绝，导致回退到 `0x4A`/`0x87` 帧；而这两帧在 K99 V3 上全部无应答，最终电量与充电都读不到。
    - `[41] = 0x80` 表示**充电中但电量未初始化**，必须展示「充电中」+ 电量未知，不得显示 0%。
  - **传输形态在 `[43]`**：`0x01 = USB 有线`，`0x02 = 2.4G 无线`。其余值视为未知，不得猜测。
  - **双端点同时在线时两者数据会不一致**：键盘保持 2.4G 档位但插着 USB 线充电时，`0x2020` 回 `[41]=0xC3`（充电）、`[43]=0x01`（有线），而 `0x3033` 回 `[41]=0x44`（未充电）、`[43]=0x02`（2.4G）。此时应**形态取 2.4G、充电取两端点并集**，展示为「2.4G 无线 + USB 充电」。
  - `DeviceId` 固定为 `MCHOSE:3837:3033`（即使仅有线在线），保证跨形态的聚焦记忆不会失效。
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
