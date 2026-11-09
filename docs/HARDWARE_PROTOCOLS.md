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

## Logitech G PRO X SUPERLIGHT 2（MR0097）实机记录

独立实现位于 `packages/logitech_mr0097.fp-driver.cs`（DriverId `External:LogitechMR0097`，Priority 300），不再依赖本地修改的 LogitechDeviceHelper。2026-11-09 迁移实测首次 ROOT/0003 回复延迟 1059ms，后续查询 7ms；身份发现需允许有界冷启动等待，其余命令保持短超时，写指令不自动重发。多接收器命令端点按完整路径与槽位保存。

接收器为 `046D:C54D`，本机配对槽位 1。有线为 `046D:C09B`，直连地址 FF。两种传输均使用 `FF00/0002`、Input/Output/Feature 长度 `20/20/0` 的 HID++ 长报告端点。无线设备名称为 `PRO X 2`，通过特性 0003 读到型号标识 `40 A9 C0 9B 00 00`。无线写操作重新检查此标识。以下为 GaiusC 提供的社区实机采样，省略末尾零填充。特性索引须动态发现。

| 查询 | 实机响应与解释 |
|---|---|
| 1004 Unified Battery，函数 1 | 无线 `11 01 06 19 59 08 00 00` 为 89%、放电。有线 `11 FF 06 19 3C 04 01 01` 为 60%、充电。拔线后 `11 01 06 19 3E 04 00 00` 为 62%、放电。偏移 4 为百分比，5 为等级，6 为状态。这些是固件报告值。 |
| 2202 扩展 DPI，函数 5 | `11 01 09 59 00 06 40 03 20 06 40 03 20 02`。当前 X/Y 分别位于偏移 5 和 9（大端），均为 1600。偏移 7 和 11 均为默认 800。 |
| 8061 回报率，函数 2 | 无线参数 01，有线参数 00。返回索引 03 表示 1000 Hz。无线尝试设置 500 Hz 收到 HID++ 错误 02；扩展 DPI 尝试设置 1600 × 1650 收到错误 05。 |
| 8100 板载档位 | 模式 01、布局版本 07。函数 4 读当前扇区，再用函数 5 读偏移 0、16 的两块。档位表从偏移 4 起，每档 5 字节（小端 X、小端 Y、LOD）。本机读到 800、1200、1600、2400、3200。函数 B 读取零基索引，函数 C 选择已有档位。选择第 2 档后读回索引 01 和双轴 1200，恢复第 3 档后读回 02 和双轴 1600。 |

驱动只对已识别的无线设备和已验证的板载布局开放切档，选择前重新读取档表，随后核验索引和双轴 DPI。保留板载档表原值。协议对照见 [Solaar 的同型号硬件记录](https://github.com/pwr-Solaar/Solaar/issues/2207) 和 [libratbag 的板载档位命令](https://github.com/libratbag/libratbag/blob/master/src/hidpp20.c)。

### Keychron M6 via Link-KM / USB（2026-11-08、11-09 GaiusC 实机贡献）

独立实现位于 `packages/keychron_m6.fp-driver.cs`（DriverId `External:KeychronM6`，Priority 300）。源码不调用本地 KeychronDeviceHelper，支持官方公共驱动接口；主程序的物理身份增强字段通过可选反射设置。

接收器为 3434:D026，产品字符串 Keychron Link-KM。专用端点 FFC1/0001，Input/Output/Feature 长度为 64/64/0。精确端点在 MI_01/COL02。有线 M6 为 3434:D060，控制端点位于 MI_04，同样为 FFC1/0001、64/64/0。共享接收器的键盘、游戏控制器和桥接集合均不参与鼠标查询，通用 CompX 探测同时排除这两个 PID。

采用官方 Launcher 的鼠标协议，来源为 https://launcher.keychron.com/main.281a047df556abfc.js 的 98502、5497、61892 模块。WebHID 参数不含 Report ID，Win32 缓冲区首字节包含 Report ID。以下帧省略尾部填充至 64 字节的零值。

| 操作 | TX | RX |
| --- | --- | --- |
| 配对鼠标身份，target 1 | B5 02 01 | B6 02 04 00 34 34 60 D0 03 01 11 |
| 接收器身份，target 0 | B5 02 00 | B6 02 02 00 34 34 26 D0 03 01 01 |
| USB 鼠标身份，target 0 | B5 02 00 | B6 02，偏移 4–7 为 34 34 60 D0 |
| 电量与状态 | B3 06 | B4 06 00 22 22 02 90 01 20 03 40 06 80 0C 88 13 15 05 04 0A 56 |

身份响应偏移 4–7 为 VID/PID 小端字节，必须确认 3434:D060 后才接受为 M6。电量为 Win32 完整帧偏移 20 的低七位，56h 为 86%，用户已核对。最高位为充电标志，对应官方 JS 不含 Report ID 的 payload[19]。2026-11-08 尚未完成充电实测时采用 IsChargingUnknown；2026-11-09 用户确认插线后实测 D0h（80% 充电）和 D6h（86% 充电），随后确认拔线并移动鼠标，接收器读到 5Ch（92% 未充电）。实时读数现采用该已验证标志。

此协议的这两条读命令没有附加校验和，严格校验 64 字节长度、Report ID、命令号、身份和百分比范围。禁止套用其他 Keychron 协议的 A1 校验和。读取超时只允许回退同一端点路径的已验证 M6 缓存，保留原时间戳并标记休眠、充电未知，不沿用旧充电标志。能力为电量，DPI/回报率写入保持禁用。

插线后鼠标切到 USB，接收器可能返回 B6 E4 02 02 等离线错误，不能把接收器的休眠缓存当作当前充电状态。保留实际传输 DeviceId（Keychron:3434:D026:MS / Keychron:3434:D060:MS），通过包含接收器完整路径的 PhysicalUnitId 关联用户已确认的同一实物；合并时实时读数优先于缓存，两路均实时则优先 USB。实测端点没有序列号，不按型号自动合并所有同款设备。本机确认关系存放在 HKCU\\Software\\FerrisPulse\\Keychron\\WiredReceivers，以 USB 完整 HID 路径为值名、`Keychron:ReceiverPath:<完整 RF 控制路径>` 为 REG_SZ 值；旧 DeviceId 格式仅在恰好一个已验证接收器时兼容。未确认关联的 USB 保持独立；更换 USB 端口后如路径变化，需重新确认关联。源码和共享补丁不包含本机路径。
