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
  - DPI 读写：Class `0x04`, Cmd `0x05` / `0x06`；多档位查询 Class `0x04`, Cmd `0x86`（载荷 38 字节 `0x26`），多档位写入 Class `0x04`, Cmd `0x06`。支持 Focus Pro 35K Gen-2、Focus Pro 45K Gen-2、Focus Pro 50K Gen-3 1-DPI 步进与 XY 独立配置。
  - 回报率：Class `0x00`, Cmd `0x40`（传统 125~1000Hz）与 HyperPolling `8000Hz` 协议。
- **雷云官方 Web 驱动 (`https://synapse.razer.com/`) 物理事实与 V4 Pro 矩阵**：
  - **毒蝰 V4 Pro (Viper V4 Pro)**: 有线 PID `0x00E5` (229), Dongle PID `0x00E6` (230)。Focus Pro 50K Gen-3 传感器，支持 50,000 DPI，1-DPI 步进，帧同步 (FrameSync)，Airoha AB1623 主控，8000Hz 超高回报率。
  - **炼狱蝰蛇 V4 Pro 碳纤维版 (DeathAdder V4 Pro Carbon)**: 官方专属独立有线 PID `0x00EF` (239), Dongle PID `0x00F0` (240)。Focus Pro 50K Gen-3 传感器，支持 50,000 DPI，1-DPI 步进，帧同步 (FrameSync)，8000Hz 超高回报率。
  - **炼狱蝰蛇 V4 Pro 标准版 (DeathAdder V4 Pro)**: 有线 PID `0x00BE` (190), Dongle PID `0x00BF` (191)。第二代 Focus Pro 45K 传感器，支持 45,000 DPI（绝非 35K 亦非 50K），1-DPI 步进，8000Hz 超高回报率。
  - **极速版系列鼠标 (HyperSpeed Mice)**: 
    - `Razer DeathAdder V4 HyperSpeed`: PID `0xB007` (45063)，Focus Pro 33K 传感器 (33,000 DPI)，1-DPI 步进，支持 HyperPolling。
    - `Razer Viper V4 HyperSpeed`: PID `0xB005` (45061)，Focus Pro 33K 传感器 (33,000 DPI)，1-DPI 步进，支持 HyperPolling。
  - **猎魂光蛛键盘系列 (Huntsman Keyboards)**:
    - 60% 基础版: `Huntsman Mini` (`0x0257`), `Huntsman Mini Japan` (`0x0269`), `Huntsman V3 Pro Mini` (`0x02B0`)。
    - 旗舰 8000Hz 系列: `Huntsman V3 Pro 8KHz` (`0x02CF`), `Huntsman V3 Pro TKL 8KHz` (`0x02D0`), `Huntsman V3 Pro Mini 8KHz` (`0x02D1`), `Huntsman Signature Edition` (`0x02D8`), `Huntsman V3 Tenkeyless 8KHz` (`0x02E5`), `Huntsman V3 Pro Low-profile Tenkeyless 8KHz` (`0x02E6`)。
    - 极速磁轴 (HE) 竞技系列: `Huntsman V3 HE Magnetic Mini 65% 8KHz` (`0x02E4`), `Huntsman V3 HE Magnetic TKL 8KHz` (`0x02EA`)。
  - **单手键盘与灵刃笔记本控制器 (Keypad & Blade Laptops)**:
    - `Tartarus V2 Pro` (`0x02EB`): 塔洛斯神蝎 V2 Pro 单手游戏键盘。
    - `Blade 16 (2026)` (`0x02E0`) 与 `Blade 18 (2026)` (`0x02E1`): 灵刃 16 / 18 内置系统控制器与键盘。
- **Airoha 主控通信时序与状态防破坏机制**：
  - Airoha AB1623 主控在 8KHz 帧同步模式下处理 Feature Report 时容易处于 BUSY (`status == 0x01`)，查询多档位时必须采用至少 3 次带 15ms 退避的重试机制。
  - 刷新时若单次偶发未获取到档位，必须平滑继承当前已知有效状态，严禁将内存档位清零（防止 UI 兜底跌落到第 1 档）。
  - 开机桌面校准仅同步硬件物理档位至内存记忆，严禁在未读取前向下发送第 1 档覆盖硬件板载 EEPROM。

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

#### 1.7.1 XCTECH 代工接收器为**多品牌共用**（瓦尔基里 VK M3 系列，实机验证）
- **背景**: `249A:5C2F` 并非宏碁独占 ID。该 ID 由 XCTECH 代工，同时供宏碁 OMR 系列与**瓦尔基里 (Valkyrie) VK M3 系列**使用，**协议完全一致**，仅 `dev_id` 不同。（依据：`C:\Program Files (x86)\VK M3 Series\config.xml` 中 `<mode value="1" desc="2.4G" vid="249A" pid="5C2F" />`。）
- **型号映射（`GetModelNameFromDevId`）**: `M220 → Valkyrie VK M3 Lite`、`E023 → Valkyrie VK M3`。
- **实机回帧（VK M3 Lite / `M220`，电量 75%、未充电）**:
  ```
  TX: 00 10 00 00 ...(33B)
  RX: 00 10 00 01 0B 4D 32 32 30 84 01 11 01 00 4B 00 ...
      [4]=0x0B 负载长度  [5..8]="M220"  [13]=0x00 未充电  [14]=0x4B=75%
  ```
- **DPI 档位与回报率控制协议**:
  - `0x02` 回报率下发：`00 02 00 01 01 <flag> 00...`（`0x08`=125, `0x04`=250, `0x02`=500, `0x01`=1000, `0x10`=2000）；
  - `0x03` DPI 六档下发：25 字节负载，`[0]=(activeStage << 4) | (header & 0x0F)`，接 6 组 `[Xlo, Xhi, Ylo, Yhi]`，`[32]` 为 8 位累加和。
  - 传感器映射：`0x11` 为 PAW3395/3311 系列，量程 50~18000 DPI，步进 50。
- **⚠️ 端点陷阱（改动前必读）**: 该接收器在部分主机上厂商集合枚举为 `UsagePage 0x0001 / Usage 0x0000`，**并非** `0xFF01 / 0x0010`。此时仅靠 `SelectChannel` 中「`OutputReportLength == 33`」的兜底分支才能命中，**该兜底分支不可删除**，否则 VK M3 系列会直接掉线。
- **工具**: `tools/probe_vk_m3.ps1` 可一键验证 249A/248A 家族的四条通道（Feature 全 RID + Feature/Output）并打印解码结果。

### 1.8 OpenMouse / All-in-One 通用电竞协议引擎 (`UniversalGamingDeviceHelper.cs`)
- **引擎一 (CompX / PixArt 64B Feature `0x83`)**: 发送 `00 00 00 02 02 00 83...`，轮询 `GetFeature` 跳过 `0xA0` 等待帧，匹配 `A1 00 02 02 00 83 [chg] [pct]`（覆盖 Lamzu, Pulsar, WLmouse, Attack Shark, Ajazz, Darmoshark, Glorious, Ninjutso, RAWM, Incott, Finalmouse, Keychron, Dareu 等）。
- **引擎二 (Beken / ATK / Endgame Gear 16/17B `0x08 0x04`)**: `0x55` 互补校验和，解析电量、充电与毫伏电压。
- **引擎三 (Vaxee 64B Feature `0x0E 0xA5`)**: Page `0x0B` 电量（`raw * 5%`）、Page `0x10` 充电状态。
- **引擎四 (怒喵 Angry Miao 62B RACE Feature `0x14`)**: 指令 `0x30CF` 读取 `AM Infinity .97` 电量与充放电状态。

---

### 1.9 赛诺韦尔 (SinoWealth) 多模键鼠接收器 `258A:010C` (`SinoWealthDeviceHelper.cs`)
- **定位**: `0x258A` 是 SinoWealth **芯片厂商** ID，被大量贴牌键鼠共用（达尔优 / 多彩 / 狼蛛 / 红龙等）。`010C` 是多模键鼠套装接收器：`MI_00` = 键盘、`MI_01` = 鼠标 + 厂商集合（COL03/05/06）。
- **厂商集合布局（实机 `Get-PnpDevice` + 探针枚举）**:
  | 集合 | 特征 | 用途 |
  |---|---|---|
  | COL03 | `In=4  Out=0  Feat=0`  | 4 字节输入，**无可用 Feature** |
  | COL05 | `In=0  Out=0  Feat=6`  | `0x05` 短命令（写入生效） |
  | COL06 | `In=8  Out=0  Feat=520`| `0x04/0x06` 配置 / 描述符导出 |
- **写入通道实测**: 仅 `0x05` 开头的 6 字节载荷被固件接受（`05 11 00 00 00 00` → `SetFeature = True`）；`00 00 02 02 00 83`（Glorious 258A 方案的电量查询）**被拒**。
- **读回实测（本机固件）**: 所有 `0x04/0x06` 读取返回的都是**上一次写入载荷的暂存回显 + 静态固件块**（含 `"BY Tech"` 字符串与键盘 Report Descriptor），**不是配置**：
  ```
  基线（未发任何命令）: 05 11 00 00 00 00 06 00 00 0A 03 30 00 30 00 ...
  发命令之后          : 05 11 00 00 00 00 06 00 00 0A 03 30 00 30 00 ...   （一字不差）
  ```
  故 `ParseConfig` **强制校验 `rx[1] == 0x11`**，否则会把描述符导出回包误当配置解析。
- **电量结论（重要，勿重复踩坑）**: 本机固件在 Feature 报告（全 RID `0x00~0x0F`、长度 `16~520`）与中断 IN 管道（静默 / 命令后 / 鼠标持续交互 12s）四条路径上**均不回电量**。已穷举验证的手段：MouseFlash `0x11` 配置命令、Glorious 258A `0x83` 电量命令（含 `0x05` 前缀变体）、裸 RID 触发中断 IN、`HidD_GetInputReport` 快照。**结论：该机型不对外暴露电量**。
- **诚实降级**: 因此本驱动 `BatteryPercent = -1` ⇒ `SupportsBattery / SupportsDpi / SupportsPollingRate` 全部 `false`，只做设备识别与在线占位，绝不虚标数值。
- **README/Issue 参考**: 若日后拿到可用抓包，只需在 `ParseConfig` 内补一个 `BatteryOffset` 常量即可点亮电量。

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

### 2.4 前行者 (EWEADN) X87 三模机械键盘 (`0C45:FEFE`, `EweadnDeviceHelper.cs`)
- **设备形态**: 松翰 (Sonix) 方案 2.4G **复合接收器**，`USB\VID_0C45&PID_FEFE`，`BusReportedDeviceDesc = "2.4G Dongle"`。
  同时枚举出键盘、鼠标、多媒体与两个厂商私有集合，共 9 个 HID 集合：

  | MI | UsagePage/Usage | In/Out | 用途 |
  |---|---|---|---|
  | MI_00 | `0x0001/0x0006` | 9 / 2 | 键盘标准集合（本 Helper 认定的键盘业务通道） |
  | MI_01 | `0x0001/0x0002` | 6 / 0 | 鼠标集合（接收器内另一设备） |
  | MI_02 | `0x0001` 多集合 + `0x000C/0x0001` + `0xFFFF/0x0001` | 2~16 / 0 | 多媒体、系统控制、厂商 4 字节集合 |
  | MI_03 | `0xFF60/0x0061` | 33 / 33 | VIA 风格配置通道（键位表 + 灯效矩阵） |
  | MI_04 | `0xFF59/0x0061` | 65 / 65 | 私有通道（仅发固定信标） |

- **精确四元组隔离**：该公版接收器与其他 `0C45` 设备共享 VID，识别必须联合校验
  `VID + PID + UsagePage/Usage + 精确 Input/Output 报告长度`，且键盘通道优先认 `\kbd` 后缀、MIS_00 兜底。

- **⚠️ 电量不可读（2026-11-07 本机穷举实测结论，勿再重复盲扫）**：
  1. `0xFF60/0x61` 33 字节通道：命令字 `0x00..0xFF` 全扫，仅应答两类分块数据——`type 0x12` 键位映射表
     （负载为 `29/3A/3B/3C...` 等 HID usage 码，即 ESC/F1/F2/F3），`type 0x33` RGB 灯效矩阵。
     分块头 `00 55 <type> <len> <offLo> <offMid> <offHi> 00 00 <payload>`，按 offset 可完整重组，**blob 内无电量字段**。
     注意此通道是「按需分块 dump」：一次触发后表传完即停，重复触发不会重发，容易误判为「无应答」。
  2. `0xFF59/0x61` 65 字节通道：只自发固定信标 `00 0F 01 01 11 00 00`，内容恒定、与按键无关，无电量语义。
  3. `0xFFFF/0x0001` 4 字节集合（report id 5）：12~20 秒被动监听零帧。
  4. 所有集合的 `FeatureReportByteLength = 0`，即接收器完全不暴露 Feature Report。
  5. 无任何 `UsagePage 0x06 / Usage 0x20`（Battery Strength）标准电量集合：
     对全部 9 个集合调用 `HidP_GetUsageValue(0/2, 0x06, 0, 0x20)` 均返回
     `0xC0110003`（`HIDP_STATUS_USAGE_NOT_FOUND`），即固件根本没有声明该用法。
     对照：雷蛇 Basilisk V3 Pro 的私有通道同样返回该错误（其电量走私有报文），
     说明此返回码不能反证「设备有电量」，只能证明标准 HID 路径不通。
  6. 官方驱动 EHUB (`resources/ry/iot_manager/*.exe`) 静态扫描：`percent` / `battery` / `Power`
     等关键字全部命中 Rust 依赖库噪声（`percent-encoding`、`is_power_of_two` 等），
     **无任何电量相关业务代码**；`app.asar` 内的 JS 也不含 HID 通讯层。
     即官方自身未实现 X87 电量显示，这与上述协议层结论一致。
- **本仓库的实现立场**：按「物理事实优先，严禁虚标能力」铁律，2.4G 形态下不填任何常量或估算电量值。

### ✅ 电量在**蓝牙 (BLE) 模式**下可得（`EweadnBleBatteryHelper.cs`）

X87 是三模键盘，电量只在蓝牙模式下由**标准 GATT** 提供，与厂商私有 HID 无关：

- **Service** `0x180F` Battery Service；**Characteristic** `0x2A19` Battery Level（1 字节 0~100）
- 配对记录（本机实测）：
  `HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\cc415e28335e`
  的 `LEName = "X87-5.0-BT2"`、`VID = 3141 (0x0C45)`、`PID = 65278 (0xFEFE)`、`REV = 0138`
  —— 与 USB 接收器完全一致；`LmpVersion = 12`（蓝牙 5.x）
- Windows 为其枚举出节点
  `BTHLEDEVICE\{0000180F-...}_DEV_VID&010C45_PID&FEFE_REV&0138_CC415E28335E`
- 实现：`EweadnBleBatteryHelper` 先读注册表定位已配对 MAC，再用 WinRT
  `BluetoothLEDevice.FromBluetoothAddressAsync` → `GetGattServicesForUuidAsync` →
  `GetCharacteristicsForUuidAsync` → `ReadValueAsync` 取 `0x2A19`，60 秒缓存。
  为保持零外部依赖与 .NET 4.0 兼容，WinRT 全程按名称反射调用，任何失败一律返回 `-1`。
- **行为**：`QueryBattery()` 返回 ≥0 时设备条目显示电量且 `SupportsBattery = true`、`Transport = "蓝牙"`；
  返回 `-1`（2.4G/有线模式，或蓝牙未连接）时 `SupportsBattery = false`、电量保持 `-1` 显示「不可读」。

### 形态与能力判定（与官方驱动对齐）

| 形态 | VID:PID | 端点特征 | 电量 |
|---|---|---|---|
| 2.4G 接收器 | `0C45:FEFE` | `0xFF59/0x61`(65B) + `0xFF60/0x61`(33B)，**无 `0xFF68`** | 不可读（固件不转发） |
| 键盘本体 USB 直连 | `0C45:8006` | 必须含 `0xFF68/0x61` 才支持官方驱动协议 | 需升级到支持驱动的固件后可读 |
| 蓝牙 BLE | `X87-5.0-BT2` (`cc415e28335e`) | 标准 GATT `0x180F` / `0x2A19` | **可直接读取** |

`EweadnDeviceHelper` 提供两个如实反映能力的方法：
- `IsKeyboardBodyPresent()` —— 键盘本体（`0x8006`）是否在线
- `HasDriverCapableInterface()` —— 是否具备官方驱动要求的 `0xFF68/0x61` 端点

### 官方驱动的排查结论（避免后人重复投入）

| 来源 | 结论 |
|---|---|
| **官方网页驱动 C `hub.eweadn.cn`（主驱动，决定性）** | 该驱动区分两种形态并自带能力判定：**键盘本体 USB 直连 = `VID 0x0C45 / PID 0x8006 / productName "X87"`**，且端点列表中必须存在 **`UsagePage 0xFF68 / Usage 0x0061`** 才算「支持驱动」；若找到 `0x8006` 却**缺**该端点，驱动直接弹窗「键盘和接收器的固件不支持驱动，请点击确定升级固件」（函数 `kSe`）。另有 `RSe`：同一 `0xFF60/0x61` 端点出现 **≥2 次**也会提示升级。**本机实测**：接收器 `0C45:FEFE` 的 9 个端点里只有 `0xFF59/0x61` 与 `0xFF60/0x61`，**无 `0xFF68/0x61`**；键盘本体 `0C45:8006` 当前不在线（正使用 2.4G）。⇒ 当前固件形态下官方驱动自己也会判定为「不支持・需升级」，与本仓库在 2.4G 上探不到电量的实测完全吻合。该驱动的电量字段位于鼠标协议 `decodeGetBasics`/`handleBatteryChange`（`byte[6]`：bit7=充电、低 7 位=电量；事件码 5），属 `0xFF68` 家族，本接收器取不到 |
| 官方网页驱动 A `eweadn.i-game.tech` | 设备表只含 `vendorId` `0x28E9`/`0x056D` 与 `0x31xx` 系列；检索 `0xFEFE`/`0x0C45` **零命中**，不覆盖本接收器。其 `getPoorNum` 电量协议（帧 `[cmd,00,00,0x38,crcLo,crcHi,00,00,00,payload]`，sum 累加校验；应答 `n[7]`=百分比、`n[8]`=0未充/1充电/2充满、`n[9]`=低电标志）适用于那些型号 |
| 官方网页驱动 B `eweadn.driveall.cn` | 设备表**含** `{vendorId:3141, productId:65278, usagePage:0xFF60, usage:97}`，但其组帧为 `buildPkt_Init → [0xAA, cmd, len, offLo, offHi]`（reportId 固定 0，主用 `0xFF68` 家族），初始化命令 16/17/19/20/23 解析字段只有版本/宏上限/N键/扫描率/稳定度/组合键/MAC/分区/回报率/自动校准/灯效，**无电量字段** |
| 官方桌面驱动 EHUB `iot_manager` | `percent`/`battery` 关键字全部命中 Rust 依赖库噪声，无电量业务代码 |
| 本接收器 `FF60/61` 通道 | 对**任意写入都只做逐字节回环回显**：`TX 00 55 8F 00 E4 … ⇒ RX 00 55 8F 00 E4 …`；换用 `0xAA` 协议、`getPoorNum` 的 `0x8F`/`0x1F`、`25~33`/`250~255` 候选命令码，无一产生真实数据应答。偶发非零数据块属主动上报的键位表(`type 0x12`)/灯效矩阵(`type 0x33`)，不随电量变化 |

## 3. 雷蛇 Chroma 扩展矩阵灯效 (`RazerChromaHelper.cs`)

适用于 Basilisk V3 Pro 系列（`1532:00AA` 有线 / `1532:00AB` 2.4G 接收器 / `00CC`·`00CD` 35K /
`00C4`·`00C5` Basilisk V3 35K / `0099` Basilisk V3）。与 OpenRazer `razerchromacommon.c` 双向印证并本机实测。

- **通道**: 91 字节 **Feature Report**（`UsagePage 0x0001 / Usage 0x0002`），必须走 `HidD_SetFeature` / `HidD_GetFeature`。
  该系列无 Interrupt 通道，切勿用 `WriteFile` 下发。
- **帧结构**: `[2]=事务ID 0x1F`，`[6]=dataSize`，`[7]=class`，`[8]=cmd`，`[9..]=args`，`[89]=CRC(XOR of [3..87])`。
- **LED 物理分区**（本机逐个 LED ID 实测）：`0x00` 整只联动 / `0x01` 滚轮 / `0x04` Logo / `0x0A` 底部环绕灯带。
  **注意**：OpenRazer 头文件的 `BACKLIGHT_LED = 0x05` 在此型号上回 `0x03 FAILURE`，底光实为 `0x0A`；
  `0x02/0x03/0x05..0x09/0x0B..` 均不存在。
- **灯效**（class `0x0F`，cmd `0x02`，`args[0]=VARSTORE 0x01`，`args[1]=LED ID`）：

  | 效果 | dataSize | args 布局 |
  |---|---|---|
  | 关闭 | `0x06` | `01 <led> 00 00 00 00` |
  | 静态 | `0x09` | `01 <led> 01 00 00 01 R G B` |
  | 呼吸 | `0x09` | `01 <led> 02 01 00 <speed 1-4> R G B` |
  | 光谱 | `0x06` | `01 <led> 03 00 00 00` |
  | 波浪 | `0x06` | `01 <led> 04 <dir 0-2> 28 00` |
  | 响应 | `0x09` | `01 <led> 05 00 <speed 1-4> 01 R G B` |

  **星光 `0x07` 在此型号回 `0x05 NOT_SUPPORTED`，故未实现。**
- **亮度**: 写 `class 0x0F cmd 0x04`，args `01 <led> <0-255>`。
- **滚轮物理机构**（class `0x02`，`args[0]=VARSTORE`）：
  - 模式：写 `cmd 0x14` args `01 <0=Tactile 段落 / 1=FreeSpin 无阻尼>`；读 `cmd 0x94`。
  - 滚轮加速：写 `cmd 0x16` args `01 <0/1>`；读 `cmd 0x96`。
  - 智能滚轮 Smart Reel：写 `cmd 0x17` args `01 <0/1>`；读 `cmd 0x97`。
- **⚠️ 应答状态机（本仓库实测踩坑，务必遵守）**：
  - `response[1]` 状态位：`0x00` New Command（已收下未完成）/ `0x01` Busy / `0x02` Successful /
    `0x03` Failure / `0x04` Timeout / `0x05` Not Supported。
  - **`0x00` 与 `0x01` 必须继续 `GetFeature` 轮询，禁止重发 `SetFeature`** —— 重发只会把同一份工作再排队一次，
    设备永远轮不到执行，表现为「直连脚本单发成功、循环连发全失败」。
  - **应答必须回显本次请求的 `[7]` class 与 `[8]` cmd**，否则是别的并发命令（如 DPI 轮询 `26 04 86`）留在
    同一条 Feature 响应队列里的陈旧应答，必须丢弃并继续轮询，不能当作本次结果。
  - Chroma 与 DPI 读写共用同一端点，必须共用 `HardwareCommandBus.GetDeviceLock`，否则并发会串包。
  - **`0x94`/`0x96`/`0x97`（滚轮三开关）回读载荷可信**：有线 `00AA` 模式实测「写 1 读 1、写 0 读 0」精确跟随，
    应答 `args[1]`（即 `response[10]`）就是当前值。故本仓库提供 `QueryScrollMode` / `QueryScrollAcceleration` /
    `QuerySmartReel` 与批量 `SyncScrollState`，对话框打开时读取设备真实状态而非显示默认值。
    > 踩坑记录：早期在 2.4G `00AB` 模式下误判「回读不可信」，真因是同一轮询里 DPI 查询的陈旧应答串进了
    > 同一条 Feature 响应队列。加上 class/cmd 回显校验后，两种模式下回读都恢复正常。
  - **`0x84`（亮度回读）不可用**：有线 `00AA` 模式下稳定回 `0x03 FAILURE`，故不提供亮度 Query，
    只在本地缓存最后写入值。
