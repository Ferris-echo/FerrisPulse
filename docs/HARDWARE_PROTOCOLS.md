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
  - **CompX 公版 `3554:FA09` 键盘族（`CompxFa09KeyboardHelper.cs`，详见 1.5.1）**：同样使用 `3554` 与 `FF02/0002`，但输入/输出严格为 **20/20 字节**！查询帧 `13 4A 00 ... 5D`（前 19 字节和低 8 位校验），回复 `byte[5]` 为电量（`0..100`），`byte[6]` 为状态（`0x10` 充电 / `0x01` 电池供电）。**该 PID 被 MCHOSE G75 PRO / KZZI K75 / LEOBOG Hi98 Pro 等多家共用，严禁按 PID 认定单一品牌。**
  - **隔离要求**：`VgnDeviceHelper.IsVgnProtocolEndpoint` 必须校验 `inputLength == 17 && outputLength == 17 && pid != 0xFA09`，严禁使用 `>= 17`。

#### 1.5.1 CompX 公版 `3554:FA09` 键盘族：品牌归属只能看 0x05 子型号字节

> 本机实测 2026-11-26（LEOBOG Hi98 Pro），修复「LEOBOG Hi98 Pro 被误标为 MCHOSE G75 PRO」的 BUG。

- **描述符完全同质，无任何品牌信息**：`iProduct = "2.4G Wireless Receiver"`、`iManufacturer = "CX"`、私有端点恒为 `UsagePage 0xFF02 / Usage 0x0002`、`InLen=20 / OutLen=20 / FeatLen=0`。因此 `VID + PID + UsagePage + 报告长度 + 0x4A 电量帧` 这一整套指纹**全部无法区分品牌**。
- **`0x4A` 电量帧同质（实测）**：
  ```
  TX 13 4A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 5D
  RX 13 4A 01 00 02 5E 01 00 00 00 00 00 00 00 00 00 00 00 00 BF
                   ↑ [5]=0x5E=94%      ↑ [6]=0x01 电池供电（0x10 = 充电）
  校验和 = 前 19 字节无符号和 & 0xFF
  ```
- **唯一可用的物理事实：`0x05` 状态帧的 `RX[5]` 子型号字节**：
  ```
  TX 13 05 01 00 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 1B
  RX 13 05 01 00 0A SS 00 00 00 01 21 01 00 00 00 00 00 00 00 CS
                   ↑ 结构魔数 0x0A  ↑ 子型号
  ```
  | 子型号 `RX[5]` | 机型 | 归属 |
  | --- | --- | --- |
  | `0x02` | KZZI K75 | `KzziDeviceHelper`（卡片标题与「2.4G 公版不提供可信电量」口径保持不变） |
  | `0x03` / 其它 / 无应答 | 多品牌共用 / 未收录 | 中性名 `CompX 2.4G 无线键盘`，品牌 `CompX`（实测 LEOBOG Hi98 Pro 与同代工系狼蛛 AULA 均回 `0x03`，**不猜测单一品牌**） |

  实测样本：`13 05 01 00 0A 03 00 00 00 01 21 01 ... 49`（LEOBOG，`[5]=0x03`）；`13 05 01 00 0A 02 ... 5D`（KZZI，`[5]=0x02`）。**结构断言必须包含 `[2]=0x01`、`[4]=0x0A` 与校验和**，否则会把噪声帧当成型号证据。
- **铁律：未实测的子型号宁可留空走中性名，严禁把 PID 指向某个品牌**。历史实现把 `0xFA09` 硬编码成 MCHOSE G75 PRO，导致换一台同方案键盘就整机型误标。
- **命名空间与卡片去重**：同一台接收器可能被记成 `LEOBOG:3554:FA09`（原生判定）/ `CompX:3554:FA09`（通用扫描或用户手动添加）/ `HID:3554:FA09`（旧通用记录）。`DeviceManager.GetDeviceIdVariants` 的前缀表必须包含 `CompX` 与 `LEOBOG`，否则同一把键盘在主界面裂成两张卡。
- **隐藏标记迁移**：用户隐藏过历史上那张错标卡片时，`HideOrDeleteDevice` 会把全部命名空间变体写成隐藏标记；身份修正后必须用 `DeviceManager.IsHiddenExplicitly` 的**精确键**判定该家族卡片，避免新卡片被旧错标卡片的标记连带隐藏（用户重新隐藏时写入的正是精确键，语义不受影响）。
- **驱动隔离**：`CompxFa09KeyboardDriver` 只看 `DriverId`/`VID:PID`+品牌，禁止按品牌名 `CompX` 泛化——`CompX:05AC:024F` 是另一套公版键盘（`CompxWirelessKeyboardHelper`，33 字节 `0xFF60/0x61`）。同时 `HardwareDriverRegistry.RequiresStrictIdentity` 必须把 `BuiltIn:KzziKeyboard` 与 `BuiltIn:CompxFa09Keyboard` 纳入严格身份校验，否则两者的 `MatchesVidPid(3554, FA09)` 会互相吞卡片。
- **ATK F1 Ultimate 2.0 (`373B:11D9`, `8k dongle-L`)（贡献者 “好的很”）**：
  - 仅暴露 `UsagePage 0xFF02 / Usage 0x0002` 的 **17/17 字节 Interrupt 端点**（`FeatureReportByteLength == 0`）。
  - **严禁在 `WriteFile` 前执行 `DrainReports`**，否则会打断 8K 接收器与鼠标的无线握手状态机。
  - 发送 `08 04 00 ... 49`（Beken `0x55` 校验和），读取 `08 04 00 00 00 02 <batt> <chg> <mv_hi> <mv_lo> ...`。
- **ATK A9 Ultimate (`373B:11D9`, PR #80, 贡献者 `@ertutio`)**:
  - `373B:11D9` 为共享接收器：先读取上述 `0x04` 电量，再发 `08 10 00 ... 3D` 查询 CID/MID；仅当 17 字节回复格式/状态/`0x55` 校验有效且 `CID=1, MID=3` 时显示为 `ATK A9 Ultimate`，否则保持 F1 Ultimate 2.0 路径。
- **PMO WAVE75 蓝牙 (PR #80, 贡献者 `@ertutio`)**: 按蓝牙设备名 `WAVE75` / `WAVE 75` 归类为键盘，沿用标准蓝牙电量读取路径。
- **ATK A87 PRO 三模键盘 (`373B:111F`, `AtkA87ProHelper.cs`, PR #93, 贡献者 `@gallopcsy`)**:
  - `UsagePage 0xFF60 / Usage 0x0061` 33 字节中断端点（禁在 `WriteReport` 前 `DrainReports`，写超时需 `400ms`）。
  - 查询 `00 02 96 24`，回复 `00 02 96 24 <pct> <status>`（`status & 0x01` 为充电标志；`pct==0 && status==0` 为休眠帧须丢弃）。`UniversalGamingDeviceHelper` 按 `pid_111f` 排除。
- **VXE 蜻蜓 R1 星闪版 (`373B:1216`, `VxeNearLinkDeviceHelper.cs`, 贡献者 “冻干咸鱼”)**:
  - `UsagePage 0xFF05 / Usage 0x0001` 64 字节厂商端点（`HidD_SetOutputReport` 下发 + OVERLAPPED `ReadFile` 读取），走 **BITMOUSE 63B 帧 + Report ID `0x08`**（逆向自 ATK HUB 3.2.30 `app.asar`）。
  - 报文布局：`[0]=0x08`, `[1]=sum([2..63])&0xFF`, `[2]=0x72`, `[3]=2`, `[4]=sn`, `[5]=target(0x01)`, `[6]=cmdId`, `[7]=1`；`cmd 7` 返回 `rx[1]=0x72, rx[5]=0x07, rx[7]=pct(0..100), rx[8..9]=mV(LE)`，`cmd 15 (0x0F)` 返回 `rx[7]!=0` 为充电标志；`SupportsDpi = false`、`SupportsPollingRate = false`，并在 `UniversalGamingDeviceHelper` 按 `pid_1216` 隔离。

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
    但**实测 `0x4028`（USB 有线直连接口）回包 `[18]` 为 `1`**，与旧固件的 `0` 不一致。
    因此连接形态的判定**必须以实际命中的接口 PID 为主判据**（`GetTransportNameByPid`），
    只在 PID 未知时才退回 `[18]` 的旧映射，否则会把有线形态误标成「2.4G 无线」。
  - **回报率索引**：协议内按「RATES 索引 + 1」存储，RATES = `125/500/1000/2000/4000/8000` Hz；索引 0 仍发 0。
  - **DPI 上限**：搭载 PAW3950 的 4D 机型上限 42,000 DPI、6 档板载；旧版 A5 V2 Ultra 保持 26,000 DPI / 5 档。
  - **XY 双轴能力必须以 `0x0003` 的 `[12]` (HasY) 为准，不可硬编码**：
    实测 K7 V2 Ultra+（PAW3950 单传感器）该位恒为 `0`，向 `0x0103` 写入 `1200x800` 后回读为 `1200x1200`，
    固件强制把 Y 轴同步为 X 轴；`0x0102` 的 XY 标志位写入后同样被忽略。因此
    `BuiltInDrivers.PopulateCapabilities` 对 4D 协议族取 `dev.IsXyIndependent`（即 HasY），
    仅对真实支持双轴的旧版 A5 V2 Ultra 保持 `true`，避免向用户暴露无效开关。
  - **与旧协议隔离**：`MchoseMouseHelper.FindControlPath` 遇到上述 PID 直接跳过，避免重复卡片；四个写入入口优先委派 4D 驱动，失败自动回退旧协议。
  - **USB 有线直连形态（PID `0x4028`）**：
    - 鼠标插 USB 线后**不再**走 2.4G 接收器，Windows 枚举出的 USB 接口 PID 变为 `0x4028`（接收器是 `0x1018`）。
      该 PID 必须加入 `SupportedPids` 白名单，否则有线形态完全不被识别。
    - **协议完全一致**：`0x0900` / `0x0002` / `0x0003` / `0x0102` / `0x0103` 全部可用，
      电量、充电状态、DPI 档位读写、回报率切换与 2.4G 形态**无任何差别**
      （实机验证：电量 `[20]`=98、充电 `[19]`=1、DPI 2200、回报率 1000 Hz）。
    - **关键坑：端点必须优先选 `0x4028`**。鼠标插线后 2.4G 接收器通常仍插在机器上，
      于是 `UsagePage 0xFF01` 会**同时**枚举出两个端点：`0x1018`（已断链）与 `0x4028`（在线）。
      若 `PickEndpoint()` 不优先返回 `0x4028`，所有写帧都会打进断链的接收器端点，
      表现为「句柄有效、写入返回 False、读无应答」，DPI 与回报率功能全哑。
    - **无接收器**：有线形态由线缆供电，`IsDonglePresent` 必须为 `false`；
      `BuildStaleCard` 也不得再把传输形态硬编码成「2.4G 无线」。
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
- **G87 V2 / K99 V3 (`3837:3033` 2.4G + `3837:2020` 有线, `MchoseDeviceHelper.cs`)**（社区贡献者 `@MattiaBinotto19` 贡献）：
  - 优先选 `UsagePage 1 / Usage 0` 的 65 字节集合（MI_01），发送未编号性能配置查询帧 `00 55 05 00 38 38 ...`（65 字节）。
  - 响应帧固定结构：`[0]=0x00` 前导 `[1]=0xAA` `[2]=命令回显` `[3]=0x00` `[4]=校验` `[5]=数据长度` `[9..]=数据`。
  - **电量与充电在同一字节 `[41]` 的位域里**：`bit7 = 充电标志`，`低 7 位 = 电量百分比 (0-100)`。例如 `0xC3` = 充电中 + 67%，`0x44` = 未充电 + 68%；`[41] = 0x80` 表示充电中但电量未初始化，不得显示 0%。
  - **传输形态在 `[43]`**：`0x01 = USB 有线`，`0x02 = 2.4G 无线`。双端点同时在线时**形态取 2.4G、充电取两端点并集**；**充电期间 `[41]` 的电量低 7 位冻结不可信**，充电中按电量未知处理（`if (charging) battery = -1;`）且不写入硬件缓存；`DeviceId` 固定为 `MCHOSE:3837:3033`。
- **K99 V3 蓝牙 BLE 形态（公版 `07D7:EFFF`，`MchoseDeviceHelper.cs`)**：
  - `BTHLEDEVICE\{0000180F-...}_DEV_VID&0107D7_PID&EFFF_REV&0120_<MAC>` 使用公版 VID/PID，必须叠加 `BTHLE\DEV_<MAC>` 父节点 FriendlyName 的 `MCHOSE` 前缀校验。
  - 展示前去掉末尾纯数字 `-N` BLE 槽位后缀（不影响 `Ultra+`）；两段式读取 `BTHLEDEVICE` MAC → `BTHLE` `DEVPKEY_Device_BatteryLevel` 单字节；BLE 在线时优先返回并跳过 USB 探测；三形态统一归至 `MCHOSE:3837:3033`。
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
8. **华硕 ROG AZOTH 96 HE / DELTA II 系列 (`AsusDeviceHelper.Rog.cs`)**（B站社区贡献者 **“工程机风格”** 贡献）:
   - **通道与四元组隔离 (`VID=0x0B05`, `UsagePage=0xFF00 / Usage=0x0001`)**:
     - `0x1AD0` (**ROG AZOTH 96 HE Wireless**, SpeedNova 8K 接收器 `mi_02&col02`): `64/64B`，Report ID `0x02`（注意避开接收器上的鼠标槽 `0xFF01/0x0001`）。
     - `0x1AFA` (**ROG DELTA II 2.4GHz** 耳机, `mi_03&col04`): `64/64B`，Report ID `0xCC`（复用 PELTA 同族 `{CC 12 07}` 电量、`{CC 12 08}` 充电查询）。
     - `0x1C10` (**ROG AZOTH 96 HE** USB 有线, `mi_01`): `65/65B`，Report ID `0x00`。
   - **Azoth 96 HE 报文**:
     - 就绪探测 `{rid 12 00}`：`RX[5] != 0xFA`；注意 `1AD0` 接收器在位时即使键盘已切至有线/蓝牙仍回 `RX[5]=0x01`，不可单凭就绪帧判在线。
     - 电量/充电 `{rid 12 01}`：`RX[5]==0 && RX[6]<=100 && RX[9]<=1`，电量在 `RX[6]`，充电位 `RX[9]==1`（`RX[12]` 为动态电流类漂移值，非电量）；休眠/切换待机帧为 `02 FF AA`。
   - **三通道在线仲裁**: 仅当 USB 有线通道读到真实电量/充电响应，或蓝牙键盘（`BT_`）在线且电量 `>=0` 时，按型号归一化包含匹配将同型号 2.4G 条目置为离线休眠（避免仅插线充电时误压制）。
9. **DS5Dongle LCT616-DS5 3.22H (`054C:0CE6`, `Ds5DongleDeviceHelper.cs`, PR #86, 贡献者 `@Go2Heart`)**:
   - 精确匹配 `054C:0CE6` / `UsagePage 1 / Usage 5` / `In=64 Out=48 Feat=128`。
   - 只读 `HidD_GetFeature` `0xF8` 严格校验固件串 `LCT616-DS5 3.22H` 后读取 `0xF9`：`byte[12]` 为 10% 硬件档位电量，`byte[13]` 为状态（0 放电 / 1 充电 / 2 满电），`0xFF` 判为未知，绝不发送写指令。
10. **HyperX Cloud III S Wireless (`03F0:02CC` / `06BE`, `HyperXCloud3SHelper.cs`, PR #83, 贡献者 `@afwfv`)**:
   - 精确锁定 `UsagePage 0x01C0 / Usage 0x0001 / 64B` 端点（隔离 `0xFF13/62B` 固件端点与 `0x170F/8B` 按键端点）。
   - 查询 `0C 02 03 01 00 06 00`，回复 `0C 02 03 01 00 06 <pct> <f1> <f2>`；充电事件 `0D 02 03 00 0A <state>` 带 10 分钟时效窗口，超时降级为未知。
11. **公版 CompX 2.4G 无线键盘 (`05AC:024F`, `CompxWirelessKeyboardHelper.cs`, PR #83, 贡献者 `@afwfv`)**:
   - 借用 Apple VID/PID，通过产品串 `"2.4G Wireless Receiver"` 与 `GanssDeviceHelper` 隔离；走 `UsagePage 0xFF60 / Usage 0x0061 / 33B`（避开 `65B` 配置通道），查询 `00 20 01 ... 21`，回复 `byte[4]` 为电量。
12. **北通 BTP-KP20D XINPUT 手柄 (`20BC:5159`, `BeitongDeviceHelper.cs`, PR #93, 贡献者 `@gallopcsy`)**:
   - 纯推送式 `UsagePage 0x00FF / Usage 0x0003 / 64B` 中断端点，帧头 `02 15`，`byte[2]` 为电量、`byte[4] & 0x01` 为充电位。
13. **迈从 V9 Turbo+ 无线耳机 (`3837:600A`, `MchoseHeadsetHelper.cs`, PR #93, 贡献者 `@gallopcsy`)**:
   - 请求-应答式 `UsagePage 0xFF90 / Usage 0x0001 / 64B`，查询 `55 65 01`，回复 `55 65 <pct> <status>`；`55 65 00 1A` 为链路降级帧而非充电位，必须整帧丢弃并回退缓存，不声明充电位。

## Logitech G PRO X SUPERLIGHT 2（MR0097）实机记录（`LogitechMr0097Helper.cs`, PR #90, 贡献者 `@GaiusC`）

内置驱动 `BuiltIn:LogitechMR0097`（`Priority 100`，允许 `External` `Priority 300` 热覆盖），专属接管接收器无其余设备时屏蔽 `LogitechDeviceHelper` 重复访问。2026-11-09 迁移实测首次 `ROOT/0003` 回复延迟 1059ms，后续查询 7ms；身份发现允许 2000ms 有界冷启动等待，其余命令保持 500ms 短超时，写指令不自动重发。多接收器命令端点按完整 HID 路径与槽位保存。

接收器为 `046D:C54D`（配对槽位 1/2），有线为 `046D:C09B`（直连地址 `FF`）。两种传输均使用 `FF00/0002`、Input/Output/Feature 长度 `20/20/0` 的 HID++ 长报告端点，通过特性 `0003` 读到型号标识 `40 A9 C0 9B 00 00`。无线写操作前重新检查此标识。以下为 `@GaiusC` 提供的社区实机采样（特性索引动态发现，省略末尾零填充）：

| 查询 | 实机响应与解释 |
|---|---|
| `1004` Unified Battery，函数 1 | 无线 `11 01 06 19 59 08 00 00` 为 89% 放电；有线 `11 FF 06 19 3C 04 01 01` 为 60% 充电；拔线后 `11 01 06 19 3E 04 00 00` 为 62% 放电。偏移 4 为百分比，5 为等级，6 为充电状态。 |
| `2202` 扩展 DPI，函数 5 | `11 01 09 59 00 06 40 03 20 06 40 03 20 02`。当前 X/Y 分别位于偏移 5 和 9（大端），均为 1600。偏移 7 和 11 均为默认 800。 |
| `8061` 回报率，函数 2 | 无线参数 01，有线参数 00。返回索引 03 表示 1000 Hz。无线尝试设置 500 Hz 收到 HID++ 错误 02；扩展 DPI 尝试设置 1600 × 1650 收到错误 05（只读能力 `SupportsDpiWrite = false`, `SupportsPollingRateWrite = false`）。 |
| `8100` 板载档位 | 模式 01、布局版本 07。函数 4 读当前扇区，函数 5 读偏移 0、16 的两块。档位表从偏移 4 起，每档 5 字节（小端 X、小端 Y、LOD），读到 800、1200、1600、2400、3200。函数 B 读零基索引，函数 C 选择已有档位，切换后双端回读核验索引和双轴 DPI。 |

驱动仅对已识别无线设备和已验证的 5 档板载布局开放切换（`SupportsDpiStages`），选择前重读档表并保持原表数值不变。协议对照见 [Solaar 同型号硬件记录](https://github.com/pwr-Solaar/Solaar/issues/2207) 与 [libratbag 板载档位命令](https://github.com/libratbag/libratbag/blob/master/src/hidpp20.c)。

## Keychron M6 via Link-KM / USB（`KeychronM6Helper.cs`, PR #90, 贡献者 `@GaiusC`）

接收器为 `3434:D026`（Keychron Link-KM，`MI_01/COL02`），有线为 `3434:D060`（`MI_04`），均限定 `FFC1/0001`、Input/Output/Feature `64/64/0` 专用控制端点。`UniversalGamingDeviceHelper` 显式排除这两个 PID，且仅接受完成 `B5 02` 身份握手的端点。协议源于官方 Keychron Launcher `main.281a047df556abfc.js`（Win32 完整帧含首字节 Report ID）：

| 操作 | TX | RX |
| --- | --- | --- |
| 配对鼠标身份（target 1） | `B5 02 01` | `B6 02 04 00 34 34 60 D0 03 01 11` |
| 接收器身份（target 0） | `B5 02 00` | `B6 02 02 00 34 34 26 D0 03 01 01` |
| USB 鼠标身份（target 0） | `B5 02 00` | `B6 02`，偏移 4–7 为 `34 34 60 D0` |
| 电量与状态 | `B3 06` | `B4 06 ...` 偏移 20：`56h` (86% 放电)、`D0h` (80% 充电)、`D6h` (86% 充电)、`5Ch` (92% 放电) |

低七位为百分比，最高位 (`0x80`) 为经 2026-11-09 实测确认的充电标志。读取超时回退同路径 `DriverStateStore` 缓存并标记休眠/充电未知；通过 `PhysicalUnitId` 与 `HKCU\Software\FerrisPulse\Keychron\WiredReceivers` 关联同实物有线与无线状态，实时优先于休眠，有线优先于接收器。能力仅开放电量 (`SupportsBattery = true`)，DPI/回报率保持禁用。

## 社区批次集成协议摘要 (PR #55 / #61 / #63 / #64 / #71 / #75 / #76 / #85)

1. **雷柏 VT 系列 2.4G (`24AE:1851`, `RapooVt1851DeviceHelper.cs`, PR #76, `@ysy41527624`)**：`UsagePage 0xFF00 / Usage 0x0E` 32 字节配置帧 `BA A5 <cmd>` + `HidD_GetInputReport` 回读，`BB B0 02` 周期状态推送；支持 7 档 DPI (`0x888`)、125–8000Hz (`0x880`) 及电量/充电状态。
2. **雷柏 MT760 办公系列 (`24AE:1870`, `RapooDeviceHelper.MT760.cs`, PR #75, `@yangziyu1230`)**：`UsagePage 0xFF00 / Usage 0x0E` 32B，`BA A5 AA` 读取电量与 `0x02` 充电位，支持 50–4000 DPI（7 档）及 125–1000Hz。
3. **VGN V108 2.4G 键盘 (`391D:2A02`, `VgnKeyboardHelper.cs`, PR #71, `@SnivyJ`)**：`UsagePage 0xFF02 / Usage 0x0002` 20B 中断通道，0x13 帧族（`0x4A` 电量与高半字节充电位、`0x05` 型号码 `0x30`），末字节 8 位累加和。
4. **美商海盗船 HS80 MAX (`1B1C:0A97/0A96`) 与 M75 AIR (`1B1C:1BDC/1BF2`, `CorsairDeviceHelper*.cs`, PR #85, `@limuxi10086`)**：Bragi 65B 协议（通用游戏引擎显式排除 `0A97/0A96/1BDC`），HS80 MAX 只读电量，M75 AIR 支持电量与 DPI/XY/回报率控制。
5. **迈从 G98V2 (`41E4:2004`) / G7 (`A8A5:2255`) 与水月雨 GM-01 PRO (`35D8:0133`, PR #64, `@xinyulan810`)**：G98V2 复用 20B SINOWEALTH `0x13/0x4A` 帧（型号字节 `0x18`）；G7 走 `00 55 30 A5` 65B WebHID 帧；GM-01 PRO 走 `0x0C/0x0001` 64B `4B 80 27 00` 并由 `GenericHidHelper` 按专属设备排除。
6. **VGN N75 PRO (`320F:5088`, PR #63, `@ZoveD`)、VGN V98 PRO (`320F:5088`, PR #61, `@747891392`) 与 LEOBOG GM5 PRO (`04F3:026F`, PR #63, `@ZoveD`)**：`320F:5088` 按产品串在 ATK V75X / VGN V98 PRO / VGN N75 PRO 间严格互斥；GM5 PRO 走 `0x000A/0x0000` 32B 推送帧（`[1]=awake`, `[4]=pct`）。
7. **Endgame Gear XM2we (`3367:1961`, `EndgameGearDeviceHelper.cs`, PR #61, `@747891392`)**：双端点 Beken（Feature `0x08` TX / Interrupt IN `0x09` RX，`0x55` 校验和），通用游戏引擎已移出 `VID_ENDGAME_GEAR`。
8. **因科特 Ironcat Zero (`25A7:FA7C`)、KZZI K75 (`3554:FA09` 指纹避让) 与 BLE Xbox 手柄 (`045E:0B13`, PR #55, `@Luoxia992`)**：`25A7:FA7C` 走 17B `0x09 0x04` 电量/毫伏（校验和 `0x55 - sum(0..15)`）；`3554:FA09` 先检测 KZZI `0x05` 指纹防误标为 G75 PRO；`BleDeviceHelper` 统一暴露 Xbox BLE (`0x180F`) 等蓝牙电量卡片。
