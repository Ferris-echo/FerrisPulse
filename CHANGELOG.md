# 📝 FerrisPulse · 灵脉 更新日志 (Changelog)

所有关于 **FerrisPulse · 灵脉 (多品牌外设电量管家与独占感知引擎)** 的重要升级与功能特性均记录于此。

## 🚧 [Unreleased] - 社区多设备深度适配 (索尼 / 罗技 / 怒喵 / ATK / 元派 / 飞智 / 迈从)

### 🎧 音频与耳机设备接入
- **索尼 (Sony) INZONE Buds 真无线降噪电竞耳机 (WF-G700N, `054C:0EC2`)**（感谢 [@jacky50403](https://github.com/jacky50403) 逆向贡献 #22）：
  - 新增 `SonyInzoneHelper`，通过 64 字节 Output Report `0x02` 查询帧发送自增序列号与校验和请求。
  - 解析 64 字节 Input Report `0x02` 报文：独立读取并校验左右耳塞及充电盒电量百分比（精确到 1%）与充电中状态。
  - 智能电量降级呈现：当单只耳机入盒断开（返回 `0xFF`）时自动降级显示在耳耳机电量；仅充电盒在位时展示盒子电量；耳机与充电盒均入盒合盖休眠时优雅展示休眠占位状态。
- **索尼 (Sony) INZONE H5 头戴式无线游戏耳机 (WH-G500, `054C:0EBF`)**（感谢 [@3155679](https://github.com/3155679) / Aemeath_win 实机逆向适配）：
  - 复用同一 `0xFF04 / 0x0001` 厂商控制通道与 64 字节 Output Report `0x02` 查询帧（`96 C3` key id、`0x41` PC→接收器寻址），无需新增传输层。
  - **电池槽按载荷宽度自适应分派**：头戴型号回传 2 字节 `[charger, percent]`，入耳型号回传 6 字节三组状态对；`ParseBatteryResponseAuto` 依 `hci_len - 12` 判宽，避免为每个新型号维护 PID 表。
  - 充电识别：状态字节非 `0x00` 即充电中，UI 与 Tooltip 同步点亮闪电标记；接收器在线但耳机未开机时保留在线占位并显示「耳机未连接」，绝不伪造电量。
- **索尼 (Sony) PULSE Explore TWS 无线电竞入耳式耳机 (`054C:0ECC` PS Link / `054C:0ED0` 充电盒 USB)**（感谢 [@Q-LL](https://github.com/Q-LL) 深度逆向贡献 #31）：
  - 新增 `SonyDeviceHelper.PulseExplore` 与 `SonyPulseExploreState`：支持 PS Link（0011）无线接收器与 USB 充电盒（0111）双通道状态读取；
  - 精准解析 Feature Report `0xB0` 报文：独立读取左右双耳 16 档精细电量（如 87%）、单耳在位与在盒充电状态；
  - 遵循物理事实原则：明确标注盒自身电量未知，杜绝伪造数据；在设备切换与断开时实现优雅自愈。
- **漫步者 (EDIFIER) 花再 Halo Space 2.4G 无线头戴式耳机 (`2D99:E049`)**：
  - 新增 `EdifierDeviceHelper`，直连 `UsagePage 0xFF14 / Usage 0x0001` 的 64 字节中断端点，按 EC 私有协议组帧（`2E AA EC` 帧头 + 大端长度 + `XOR 0xA5` 载荷掩码 + 自下标 1 起算的 8 位累加和）。
  - 实机验证并接入 `0xD0` 电量查询（`0x86 ^ 0xA5 = 35%`）、`0xC9` 设备名称（UTF-8 “花再 Halo Space”）、`0xC6` 固件版本（1.0.2）与 `0xC8` 蓝牙 MAC 查询；名称/版本/MAC 首次成功后缓存，轮询仅保留单条电量命令并节流 1 秒。
  - **充电状态诚实缺省**：2.4G 无线模式下接入充电器会触发设备侧强制关机，叠加固件对 `0xF2` 设备状态查询在任何载荷长度下均不回包，无线模式本就没有可读的充电态；故不声明充电位、不渲染充电闪电图标，休眠/未连接时保留未知电量（`-1`）而非伪造 0%。
  - 新增 `docs/EDIFIER_HALO_SPACE_PROTOCOL.md`（完整逆向报告：帧布局、校验和手算验证、命令表、缓冲区残渣陷阱、安全边界）与 FDAS 存档 `examples/adapters/edifier_halo_space.fp-adapter.json`。

### 🖱️ 旗舰外设协议深度修复与扩展
- **雷柏 (Rapoo) VT3 系列双模电竞鼠标 (`24AE:1412` 等)**（感谢 [@lbyisme](https://github.com/lbyisme) 协议逆向与实机适配贡献 #38，底层协议参考/移植自 Iris `rapoo-tray` MIT 许可项目）：
  - 新增 `RapooProtocol` 纯协议解析与 `RapooDeviceHelper.Status.cs` 实时遥测管道；
  - 严谨解析 19 字节遥测报文（Report ID 0x07），支持 6 档自定义 DPI 阶段（上限由 5 档扩至 6 档）、独立 XY 轴 DPI 调节与硬件端点实时同步；
  - 接入 Win32 可取消异步重叠 I/O，杜绝 EEPROM 写入与多端点枚举阻塞主线程；完善物理父接口端点智能隔离与休眠断连安全自愈。
- **高斯 (GANSS) GS3108T-PRO 三模机械键盘 (`0C45:8006` 有线 / `0C45:8008` 2.4G)**（感谢 [@lbyisme](https://github.com/lbyisme) 适配贡献 #38）：
  - 新增 `GanssDeviceHelper`，通过 `0x0C45` 通道与 `0x05 / 0x11` 协议帧精准读取无线电量；
  - 物理传输模式隔离：精准识别有线（8006）与 2.4G（8008）模式，有线接入时直接判定常供电状态并隐藏过时电量注脚；
  - 隔离 Apple/Magic Keyboard 泛用发现池，杜绝同类 HID 接口伪造与重名识别冲突。
- **盖世小鸡 (GameSir) 启明星 2 / Nova 2 双模游戏手柄 (`3537:1011` / `3537:1012`)**（感谢 [@lbyisme](https://github.com/lbyisme) 适配贡献 #38）：
  - 新增 `GameSirDeviceHelper`，直连 `UsagePage 0xFFA0` 专用手柄端点读取实时状态；
  - 精确多控制器匹配与 MAC 地址持久化指纹绑定，杜绝多手柄混淆与同类控制器碰撞；
  - 状态栏与托盘电量精准同步，完美识别充电中与满电状态。
- **ATK V75X 三模客制化机械键盘 (`320F:5088` 2.4G)**（感谢 [@Q-LL](https://github.com/Q-LL) 架构与实机逆向贡献 #31）：
  - 新增 `AtkDeviceHelper`，通过 `UsagePage 0xFF1C / Usage 0x0092` 的 64 字节 Output/Input 厂商集合发送非零递增序号请求；
  - 严谨解析 `0x1A` 状态帧读取实时电量（87% 实测）与真实充电位，杜绝短包与越界电量；
  - 新增 `AtkLightingDialog` 独立键盘灯效调控台与 `AtkDeviceHelper.Lighting`：支持 20 种官方灯效、5 档亮度、RGB 调色与速度调节，引入 56 字节分块事务读取、写入与全快照校验回滚保护。
- **索尼 (Sony) DualSense Edge 旗舰无线手柄 (`054C:0DF2` USB)**（感谢 [@Q-LL](https://github.com/Q-LL) 适配贡献 #31）：
  - 新增 `SonyDeviceHelper`，通过 64 字节 Input Report `0x01` 解析 `rx[53]` 电池档位与充电状态；
  - 严格映射 10% 档位中点估值，归类为 Gamepad 专属设备卡片。
- **雷蛇 (Razer) Viper V2 Pro 毒蝰 V2 专业版 (`1532:00A6` 接收器 / `1532:00A5` 有线)**（感谢 [@Q-LL](https://github.com/Q-LL) 根因排查与修复 #31）：
  - 纠正旧版本将 `00A6/00A5` 误识别为 Cobra Pro 的偏差，精准映射为 Viper V2 Pro；
  - 限制普通接收器回报率上限为 1000Hz；
  - 修复全量硬件查询对 Y 轴档位与当前 Y 轴 DPI 的同步丢失问题。
- **Win32 HID 底层总线生命周期加固 (`HidTransport.cs`)**（感谢 [@Q-LL](https://github.com/Q-LL) 架构贡献 #31）：
  - 引入 `OpenRead`、`OpenFeature` 与 `ReadFeatureReport`，扩展只读/零权限句柄支持；
  - 重构 `TransferReport`：异步 I/O 缓冲区使用 `GCHandle` 显式固定，OVERLAPPED 超时取消后等待完成才释放，私有事件绑定 CLR 避让标志，杜绝崩溃与句柄悬挂。
- **雷蛇 (Razer) Viper V3 HyperSpeed 板载按键映射硬件通信层 (`1532:00B8`)**（感谢 [@lililixxx1](https://github.com/lililixxx1) 逆向与架构贡献 #13，关联 Issue #7 / #16）：
  - 新增 `RazerButtonMapping`，通过 `0x02/0x8C`（读）与 `0x02/0x0C`（写）实现板载 8 个物理按键功能重定义与事务重试；
  - 引入批量写入保护机制：完整 pre-apply 快照备份、逐键写入回读语义校验与失败自动回滚；
  - 零依赖自研 JSON 快照解析与注册表持久化（`HKCU\Software\FerrisPulse\ButtonMappingBackup`）；
  - 配套编写专属单元测试 `tests/test_razer_viper_v3_button_mapping.ps1`（4/4 PASS）。
- **VGN 蜻蜓 3 Pro 超轻量游戏鼠标 (`391D:1A03`)**（感谢 [@REDTEA3](https://github.com/REDTEA3) 适配贡献 #43）：
  - 扩充 `VgnDeviceHelper`：深度接入 Yjx（易捷翔）私有协议通道（`UsagePage 0xFF01 / Usage 0x0010`，65 字节中断 OUT），支持 `0x30` 电量查询、`0x0E`/`0x0F` 档位与 8K（8000Hz）回报率读写；
  - 挂载至 `VgnMouseDriver` 与 `HardwareCommandBus`，实现全屏游戏自动优化与板载性能同步。
- **VGN Neon75 三模客制化机械键盘 (`3151:5038` 2.4G / `3151:502F` 有线)**（感谢 [@Bestkilling](https://github.com/Bestkilling) 实机深度适配与架构贡献 #42）：
  - 新增 `VgnKeyboardHelper`，通过 65 字节中断端点发送 `0xF7` 状态帧与 Bit7 校验和，精准解析 `payload[9]` 真实硬件充电位，消除“键盘 100% 满电误判为正在充电”的问题；
  - 精准隔离同厂公版 VID（`0x3151`），将 Neon75 从 Valkyrie 泛 3151 识别池中彻底剥离；
  - 新增 `VgnKeyboardForm` 窗体与 `0x87`/`0x07` 键盘灯效调控，并在状态栏与托盘上线双设备摘要（`DeviceManager.BuildTrayBatterySummary`，如“鼠标 51% | 键盘 100%”）。
- **皂品 (Zaopin) Z2 三模无线游戏鼠标 (`3554:F526` / `3554:F527`)**（感谢 [@WrongMemory](https://github.com/WrongMemory) 逆向贡献与实机适配 #41）：
  - **CompX 协议识别与双 PID 覆盖**：基于 `cid == 7 && mid == 2` 与 PID `0xF526`（Nano 1K 接收器）/ `0xF527`（4K 底座接收器）精准识别，动态派生 Zaopin 品牌与官方品牌色（`#FF9100`）。
  - **锂电池电压精细换算与官方口径对齐**：复刻官方驱动 21 阈值锂电放电表并做线性插值，引入 12mV 负载压降补偿校准，解决固件电量计中间区间滞后问题；支持电压与百分比联动展示。
  - **DPI 6 档自定义与回报率（4K 上限）调控**：接入 `SetVgnDpiStage`、`SetVgnDpiValue`、`UpdateVgnHardwareStages` 与 `SetVgnPollingRate`，支持 125~4000Hz 6 档回报率；接入 `FastQueryVgnDpiStage` 实时监听实体 DPI 键事件联动。
- **虚拟 HID 框架过滤与宏碁 (Acer) OMR331 充电状态/哨兵值修复**（感谢 [@Qingyushan](https://github.com/Qingyushan) 架构贡献 #33）：
  - **VHF 虚拟 HID 结构化过滤**：新增 `VirtualHidFilter`，深度拦截网易 UU 远程（uuycinput.sys）等工具注入系统的虚拟键鼠（`HID_DEVICE_SYSTEM_VHF`），消除“设备向导将远程虚拟设备识别为 2.4G 无线”的误判。
  - **Acer OMR331 充电标志纠正**：纠正旧实现将状态位 `[15]` 误当充电标志的偏差，通过 A/B 实测对齐真实充电标志位 `[13]`。
  - **充电 100% 哨兵值智能降级**：解决宏碁鼠标插线瞬间上报 100% 哨兵值的伪满电问题，引入 `ResolveDisplayedBattery` 优雅回退到最近一次非充电实测值；状态栏供电文案动态跟随 `info.IsCharging`。
- **罗技 (Logitech) G304 / C53F DPI 误读修正、档位防跳变与回报率三态只读能力位**（感谢 [@FeranyDev](https://github.com/FeranyDev) 架构设计与实机逆向 #34 / #35）：
  - **Feature 0x2201 实测 DPI 读取修正**：改用 Func 2（`0x28 getSensorDpi`）直读硬件当前 DPI，杜绝旧版误调 Func 1 导致读取到支持列表首项（200 DPI）的严重偏差，新增 `ParseAdjustableDpiReply` 严格校验错误帧与越界帧。
  - **实测 DPI 反推档位与防回跳**：实现 `MatchDpiStage(dpi, stages)`，优先匹配 ±50 容差并自动跳过板载末尾空槽，彻底解决“点击立即刷新后高亮回跳至 800 DPI”的顽疾。
  - **回报率写入三态能力位 (`SupportsPollingRateWrite`)**：引入一次性 no-op 写入自检与注册表持久化缓存，对明确不支持修改回报率的设备（如 G304 固件拒绝 0x8060 Func2）在 UI 呈现专属只读态（`IsReadOnly` 保持气泡提示）、全屏电竞优化自动锁定关闭回报率并仅开电竞 DPI。
  - **右上角徽标实测优先**：右上角「● 硬件生效」徽标与桌面档位高亮改为实测回报率优先，彻底杜绝下发失败时的期望值谎报。
- **罗技 (Logitech) G502 X PLUS 旗舰无线鼠标与 Lightspeed 接收器重构修复**（感谢 [@TonySen1](https://github.com/TonySen1) 根因排查 #15）：
  - **端点智能优选**：枚举全量 HID 集合，智能识别并优先绑定 20 字节通信端点（`UsagePage 0xFF00, Usage 0x0002`），解决 Windows 下向 7 字节短集合发送报文导致 `WriteFile` 失败的顽疾。
  - **HID++ 2.0 握手修复**：对齐 HID++ 2.0 规范，将 Ping 命令调整为根特性函数 1（`getProtocolVersion`，`txPing[3] = 0x18`）及特征 Token，彻底解决 G502 X 握手无响应。
  - **设备名称偏移纠正**：修复 `0x0005` 特性读取设备名时的字节偏移错位，完整恢复首字母 `G`（`G502 X PLUS`）。
  - **统一电量特性 `0x1004` 精确解析**：规范解析第 6 字节充电状态（`1..4` 为充电中），消除旧版将第 5 字节电量等级误判为充电中布尔值的缺陷。
  - **G HUB 并发保护与多端点解耦**：引入 `matchDevIdx, matchFeatIdx, matchSwid` 三元组响应截止时间匹配，接收器端点解耦，杜绝同类 Lightspeed 设备名称被硬编码覆盖。
- **ATK A9 AIR 8K & X1 V2 8K 超轻量电竞鼠标**（感谢 [@leviswhite527](https://github.com/leviswhite527) 报文贡献 #25）：
  - 接入 `373B:1279` (A9 AIR 8K) 与 `373B:12E9` (X1 V2 8K) 硬件白名单与 8000Hz 档位支持。
  - 升级 `QueryBekenWeBattery`：增加 Output/Input Report 回退机制（通过 `WriteFile` 与 `ReadFile` 异步通信），解决 8K 接收器下 `HidD_SetFeature` 返回失败的问题。
- **元派 (Metapanda) P1 1K / 4K 无线电竞鼠标**（感谢 [@Afang555](https://github.com/Afang555) 源码贡献 #21）：
  - 接入 `3554:F5A9` (1K 接收器)、`3554:F5AB` (有线)、`3554:F5AC` (4K 接收器) 硬件白名单与 4000Hz 档位，复用通用 WE 17 字节电量协议。
- **怒喵 (Angry Miao) AM Infinity .97 客制化无线鼠标**（感谢 [@leviswhite527](https://github.com/leviswhite527) 协议贡献 #25）：
  - 接入 `0E8D:0703` (2.4G 接收器)、`0E8D:0880` / `35A1:0035` (有线) 硬件白名单与官方品牌色。
  - 实现 62 字节 RACE 协议引擎，发送 `0x14` Feature 报文与 `0x30CF` 查询指令，精准获取电量与充电状态。

- **迈从 (MCHOSE) A5 V2 Ultra 三模无线游戏鼠标**（特别鸣谢社区群友“冰城”逆向贡献）：
  - 新增 `MchoseMouseHelper`，实现 0x11/0x06 短报文（20 字节，异或 0xFF 掩码）身份握手，高精度提取真实物理电量百分比（1% 精度）、充电状态与连接形态（2.4G/有线/蓝牙）；
  - 深度支持 0x12/0x67 与 0x12/0x57 报文引擎，读取并配置 6 档板载 DPI，且原生支持 **X 轴与 Y 轴独立分离调校** 与回读校验；
  - 原生接入 0x11/0x41 档位指令，覆盖 125Hz、500Hz、1000Hz、2000Hz、4000Hz（2.4G）与 8000Hz（有线）超高回报率切换，全面打通主界面交互与全屏游戏自动优化。

- **迈从 (MCHOSE) K7 V2 Ultra+ 三模无线游戏鼠标（新一代 4D 帧协议族，`3837:1018` 等）**：
  - 新增 `MchoseMouse4DHelper`，面向与 A5 V2 Ultra 完全不兼容的 **64 字节 `0x4D` 帧**协议族（K7 Ultra / K7 V2 Ultra+ / A5 V3 Ultra+ / A7 V3 等，PID 白名单 `1001 / 1014 / 1016 / 1018 / 101C / 101D`）。
  - 帧结构：`[0]=0x4D` 帧头、`[1]=0x01` 版本、`[2]=flags`、`[3]=data 长度`、`[4..5]=命令字（小端）`、`[6]=bizCode`、`[7]=sequence`、`[8..]=data`、`[8+len]`=从 `[2]` 到 `[7+len]` 逐字节 XOR 校验和；payload **不做 `^0xFF` 反转**（旧 A5 协议才反转）。
  - **必须走 `WriteFile`/`ReadFile` 中断管道**（`GENERIC_READ|GENERIC_WRITE`），`HidD_SetFeature`/`HidD_GetFeature` 对该协议族一律无效；设备刚唤醒或落盘配置时回包首字节为 `0xFF`（忙标志），按 `BusyRetryCount × BusyRetryDelayMs` 退避重发而非当作有效应答。
  - `0x0900` 读设备信息：`[8..9]` VID、`[10..11]` PID、`[12]` 配置档数、`[18]` 连接状态（`0`=有线 / `2`=2.4G 接收器）、`[19]` 充电状态、`[20]` 电量百分比、`[21]` 游戏模式；实机 K7 V2 Ultra+ 读得 77%。
  - `0x0002` 读性能配置：`[9]` 高 4 位 USB 回报率索引 / 低 4 位 USB DPI 档，`[10]` 高 4 位 2.4G 回报率索引 / 低 4 位 2.4G DPI 档；回报率索引为「RATES 索引 + 1」存储（`DecodeRateIndex`/`EncodeRateIndex` 往返），支持 125/500/1000/2000/4000/8000 Hz 六档。
  - `0x0003` 读 DPI：`[10]` 档位总数、`[11]` 当前档位、`[12]` 是否 XY 独立、`[13..24]` 6×uint16 小端 DPI；`0x0103` 整表回写 17 字节、`0x0102` 全量回写 19 字节性能配置（只改回报率半字节，其余字段原样回写，避免误改休眠/传感器/消抖）。
  - 与旧版协议**按 PID 物理隔离**：`MchoseMouseHelper.FindControlPath` 遇到 4D 白名单 PID 直接跳过，避免生成电量未知的重复卡片；四个写入入口（查询 / DPI 档位 / 双轴 DPI / 回报率）优先委派 4D 驱动，失败自动回退旧协议，既有 A5 V2 设备零回归。
  - 能力契约按 PID 分流：4D 机型 50~42,000 DPI（PAW3950）、旧 A5 V2 保持 50~26,000 DPI；两者均声明回报率调节。
  - **XY 双轴能力修正（实机证伪）**：早期实现把 4D 机型的 `SupportsXyIndependence` 硬编码为 `true`。实机验证发现 K7 V2 Ultra+（PAW3950 单传感器）`0x0003` 回包 `[12]` HasY 恒为 `0`，向 `0x0103` 写入 `1200x800` 后回读为 `1200x1200`（固件强制 Y 轴跟随 X 轴），`0x0102` 的 XY 标志位写入亦被忽略。现改为对 4D 协议族直接取设备回包 `dev.IsXyIndependent`，仅对真实支持双轴的旧版 A5 V2 Ultra 保持 `true`，不再向用户暴露无效开关。
  - 新增 `test_mchose_mouse_4d_support.ps1` 六项契约测试（帧结构与 XOR 校验、`0x0900` 电量/充电/异常拒绝、回报率编解码往返、六档 DPI 解析、PID 白名单与双能力档分流、XY 能力跟随 HasY 标志的正反用例）。
  - **设备聚焦记忆修复（用户反馈：“在 exe 里没法调整 DPI”）**：反射与真实点击双路验证发现，驱动与硬件层写入均 100% 正确（`SetDpi`/`SetPollingRate` 均回读一致），真正缺陷在**启动默认聚焦**：主窗口每次启动都回落到托盘主设备（本机为 MCHOSE K99 V3 键盘），而键盘视图下 DPI/回报率控件整块不渲染，用户不手动切换设备就“看不到也调不了”。
    - 新增 `MainForm.lastSelectedDeviceId` 与注册表键 `HKCU\Software\FerrisPulse\LastSelectedDevice`：`SelectDeviceView` 在用户（或托盘菜单）选中设备时记住 `DeviceId`；`LoadConfig`/`SaveConfig` 负责读写。
    - 新增 `MainForm.GetPreferredStartupDevice()` 与 `FindRememberedDevice()`，`ShowWindow` 改用前者。启动展示优先级：① 上次主动选中且仍可用的设备 → ② 主设备本身可调节性能 → ③ 任一真正在线且可调节的鼠标 → ④ 任一休眠占位但接收器在场的可调节鼠标 → ⑤ 托盘主设备兜底。
    - `ApplyDeviceStatus` 首次刷新时也优先重建记忆设备，避免启动竞态导致回落。
    - **无线鼠标休眠占位修正**：2.4G 鼠标刚启动时常处于休眠状态，此时 `IsConnected=false` 但 `IsDonglePresent=true`。早期规则要求必须 `IsConnected`，导致优选永远跳过鼠标、回落到键盘，修复即为失效。现补充一轮「接收器在场即可选」的源，并同步放宽 `FindRememberedDevice` 的可用性判定。
    - **用户固定标记**：新增 `MainForm.deviceSelectionPinnedByUser`。只有用户主动选择过设备（`SelectDeviceView`）后才锁定；否则启动期间每轮刷新都重新评估优选，避免鼠标尚未枚举出来时被一次性锁死在键盘上。
    - **严格隔离**：本修复只影响主窗口内容区，`GetPrimaryDevice()`、托盘图标、托盘菜单与 OSD 展示的主设备行为完全不变（已测试验证）。
    - 新增 `tests/test_device_focus_memory.ps1` 六项回归验证：字段与方法存在性、冷启动无记忆时选中休眠占位但可调节的鼠标、记忆的休眠设备在接收器在场时仍可定位、记忆键盘时恢复键盘、记忆设备不存在时安全回退、`GetPrimaryDevice` 行为不变全部通过。
  - **严格按板载实际档位渲染（用户反馈：“给我多加了 1600 和 3200 的 DPI 选项出来”）**：实机排查发现，`SetDpi` / `SetDpiStage` 原先在目标档位超出当前档数时会执行 `if (target >= stageCount) stageCount = target + 1;`，即**自动扩档并永久写入板载**。先前调试写入第 2/3 档时，把设备真实的 1 档永久改写成 3 档（`2200 / 1600 / 3200`），这才是 UI 多出档位的直接原因，而非 UI 层伪造。
    - `MchoseMouse4DHelper.SetDpi` / `SetDpiStage` 改为严格保守：`if (target < 0 || target >= stageCount) return false;`，越界直接拒绝，**增删档位只能显式走 `SetDpiStages`**。
    - 新增板载真实快照 `CachedDpiStages` / `CachedDpiStagesY` / `CachedDpiStage` / `CachedDpiX` / `CachedDpiY` / `CachedPollingRate`，随 `MchoseMouse4DCache` 持久化。休眠占位卡片 `BuildStaleCard` 改用这份真实快照渲染，**不再硬编码 `PollingRate = 1000`、不再给出全量 125~8000 Hz、不再留空档位让 UI 回落默认值**；无快照时如实留空（`DpiStages = null`、`PollingRate = 0`）。
    - `MainForm` 移除全部“凭空造选项”的路径：`dpiStageList` 默认值由 `{400, 800, 1600, 3000, 6400}` 改为空列表、`dpiStageValues` 与 `pollingRateValues` 改为空数组；`LoadConfig` 不再把注册表 `CustomDpiStages` 当作 UI 选项来源（仅作历史记录）。
    - `UpdatePerformanceSection` 兜底三层化：设备上报档位表 → 用档位表；只上报当前 DPI → 只呈现这一个值；两者都没有 → 保持空档位。回报率兜底同样改为「设备上报表 → 自报单一值 → 空」，并对 `LoadCustomPollingRates` 的结果按设备能力表过滤去重。
    - `RelayoutDpiButtons` / `RelayoutRateButtons` / 电竞 DPI / 电竞回报率 / `SetGameRateControlsVisibility` 中的 `Math.Max(1, ...)` 全部去除，按钮数量严格等于 `dpiStageList.Count` 与 `pollingRateValues.Length`（空时数量为 0，不再强行渲染一个假档位）。
    - 写入与 OSD 文案中的兜底值同步收紧：`targetDpi` 不再回落 `800`，改为设备自报的当前 DPI，取不到就不写；OSD/通知中的档位总数不再回落 `5`。
    - 新增 `tests/test_mchose_onboard_truthfulness.ps1`（37 项）：源码层禁止自动扩档、禁止伪造默认档位表；UI 层断言各按钮数量表达式严格等于板载数量、电竞区块同样不得 `Math.Max(1, ...)`、注册表不再作选项源；运行时断言 `DpiStageCount == DpiStages.Length`、越界 `SetDpi` 被拒且板载档数不变。
  - **清除残留的硬编码兜底值（同一原则的延伸）**：实机语义探针逐场景验证后，把最后几处“凭空造数”的兜底一并去掉。
    - `MainForm.Hardware.cs` 中 `RestoreDesktopSettingsSynchronous` / `RestoreDesktopDpiStage` 的 `targetDpi` 不再回落 `1600`，改为设备自报的 `curDev.Dpi`，取不到就不下发；档位上限由写死的 `5` 改为 `dpiStageValues.Length`，设备板载槽位数变化时不再被截断。
    - `ApplyGameOptimization` / `ApplyGameDpiStage` / `SetGameDpiStageFromUI` 的档位上限一律改为按当前设备实际档位数收敛，不再假定固定 5 档。
    - Rapoo 托盘菜单在 `info.Dpi <= 0` 时显示「当前 DPI: 未知」，不再伪造 `1600 DPI`。
    - `MainForm.LoadConfig` 中 `gameDpiStage` 的上界不再写死 `5`（下界仍为 1），上界在运行时按设备真实档位数收敛。
    - `OpenPollingRateManagementDialog` 在设备未上报可用回报率时直接返回，不再补一张 `125 ~ 8000 Hz` 全量表；`curHz` 也不再凭空回落 `1000`，改取设备上报表中的实际值。
  - **电竞 DPI / 电竞回报率的实机语义确认（用户问“是不是只在游戏里生效”）**：用反射状态机逐场景验证，行为与上游一致且符合预期。
    - 非游戏状态修改电竞 DPI / 电竞回报率：**不写硬件**，只记配置（实测改电竞回报率 8000 Hz 后硬件仍为 1000 Hz）。
    - 进入游戏（`isGamingActive`）：自动下发电竞值（实测回报率 1000 → 2000 Hz 生效），并记住进入前的日用档位 `desktopDpiStage`。
    - 游戏运行中修改**日用** DPI / 回报率：**不写硬件**，仅作为退出后的恢复预设，避免打断游戏中的手感。
    - 游戏运行中修改**电竞**值且对应自动切换开关已开启：立即下发（实测 2000 → 4000 → 8000 Hz 连续生效）；开关关闭时按设计不下发。
    - 退出游戏：恢复日用值与日用回报率，并清空 `desktopDpiStage` 记忆。
  - `tests/test_mchose_onboard_truthfulness.ps1` 由 37 项扩到 45 项，新增断言：无残留 `1600` DPI 兜底、无写死的档位上限 `5`、回报率对话框不再补全量表、不再凭空回落 `1000 Hz`。

  - **电竞回报率 / 电竞 DPI 双开关独立生效（用户反馈：“电竞回报率没勾选，游戏场景却改了回报率”）**：用户指出电竞回报率复选框未勾选时，进入游戏仍然改写了回报率。排查确认为真实缺陷：`ApplyGameOptimization(bool isEntering)` 在“进/退游戏”路径上**不看两个子开关**，只按 `applyRate && applyDpi` 的组合调用关系推断，于是组合模式（两个都开）下能正确下发，但只要走到 `ApplyGameOptimization` 就一定同时写回报率与 DPI；而 `StartGameMonitor`、`PerformStartupDesktopCalibration` 又都优先调用它，导致未勾选的通道被一并改写。
    - `ApplyGameOptimization` 内置闸门：`bool applyRate = autoGameRateEnabled;` / `bool applyDpi = autoGameDpiEnabled;`，两者皆假时直接 `return`，回报率写入被 `if (applyRate)` 包裹、DPI 写入被 `if (applyDpi)` 包裹，**不再依赖调用方事先判断**，任何新增调用点都无法绕过。
    - `ApplyGamePollingRate()` 增加 `if (!autoGameRateEnabled) return;`、`ApplyGameDpiStage()` 增加 `if (!autoGameDpiEnabled) return;`，单通道入口同样自带闸门。
    - OSD 与通知气泡按实际生效通道展示：仅回报率生效时文案不再出现 DPI 数值，仅 DPI 生效时不再出现 Hz 数值，避免虚报未勾选的项目。
    - `RestoreDesktopSettingsSynchronous()` 写回报率前检查 `autoGameRateEnabled`、写 DPI 前检查 `autoGameDpiEnabled`（原有逻辑保留并加注释），启动同步校准同样不会越权改写。
    - 新增 `tests/test_game_toggle_independence.ps1`（23 项）：源码契约层断言双闸门存在、`ApplyGameOptimization` 内两个写入点各自被对应 `if` 包裹且写入点唯一、`ApplyGamePollingRate`/`ApplyGameDpiStage` 各自带闸门、`StartGameMonitor` 与 `PerformStartupDesktopCalibration` 内不存在绕过闸门的直接硬件写入、同步校准的两处开关前置判断、以及三个文件的 UTF-8 BOM 与无 CRCRLF 腐败。
    - 实机四场景验证（反射状态机，硬件回读）：① 电竞回报率未勾选 + 电竞 DPI 已勾选 → 进游戏回报率保持 `1000 Hz` 不变、退出亦不变；② 两者均勾选 → 进游戏回报率 `1000 → 8000 Hz`、退出恢复 `1000 Hz`；③ 两者均未勾选 → 完全不触碰硬件；④ 直接调用 `ApplyGamePollingRate()` 在未勾选时被闸门拦住。

  - **设备品类权威纠错（用户反馈：“为什么我的鼠标被识别为键盘了？”）**：实机排查确认为真实缺陷——手工添加设备时一旦把复合外设记成键盘，错误会被永久固化，且原逻辑只做「键盘单向迁移」，没有反向纠错。
    - 根因链条：`HKCU\Software\RazerBatteryTray\CustomDevices\HID:3837:1018` 的 `Category = 3`（Keyboard）→ `DeviceManager.LoadCustomDevices()` 直接采用注册表值 → `1782` 行 `devInfo.Category = cfg.Category;` 用错误品类造卡 → `MchoseMouseDriver.CanHandle` 因 `dev.Category != DeviceCategory.Mouse` 拒绝接管 → `PopulateCapabilities` 不执行 → DPI / 回报率 / 档位能力全部为 `false`，UI 控件整块消失，用户无法自行恢复。
    - 错误首因：`3837:1018` 是复合外设，`MI_00` 为 HID 鼠标、`MI_01` 为 HID 键盘、`MI_02` 为厂商接口；`AddDeviceWizardForm` 的 `resolveCategory` 会无条件返回注册表中已有的旧 `Category`，把错值继承并固化。
    - 新增 `MchoseDeviceHelper.TryGetAuthoritativeCategory(deviceId, hardwareFingerprint, out DeviceCategory)`：按 VID/PID 反推权威品类——`0x3837` 且 PID 命中 4D 白名单（`1001 / 1014 / 1016 / 1018 / 101C / 101D`）判为 `Mouse`，`0x3837:0x3033`（G87 V2）、CompX `0x3554:0xFA09`（G75 PRO）、`0x0E8D:0x01BB`（Z75s）判为 `Keyboard`，其余返回 `false` 不干预。
    - `DeviceManager.LoadCustomDevices()` 读盘时立即用该 API 纠正并 `SaveCustomDevice(cfg)` 落盘；自定义配置合并相位改为硬件事实优先：`mergeTruthKnown && mergeTruthCat != Generic` 时覆盖历史值，否则才沿用注册表值。
    - `AddDeviceWizardForm.resolveCategory` 改为硬件事实优先：先查 `TryGetAuthoritativeCategory(devId, devId)`，再查已有配置的权威品类，最后才回退 `match.Category` / `defCat`，从源头阻止错误被继承。
    - 实机验证：无需手工改注册表，仅调用 `QueryAllDevices()` 即自动自愈 —— `HID:3837:1018` 的 `Category` 由 `3` 改写为 `0`，卡片变为 `cat=Mouse` / `driver=BuiltIn:MchoseMouse` / `dpi=2200` / `stages=1` / `caps(dpi=True rate=True)`；键盘 `3837:3033` 保持 `Keyboard` 不受影响。
    - 新增 `tests/test_device_category_truth.ps1`（39 项）：三文件 BOM/CRCRLF 合规、权威 API 覆盖两族、`LoadCustomDevices` 改写并落盘、向导不再盲目继承、运行时 `3837:1018` 必须为 `Mouse` 且具备 DPI/回报率/档位能力、注册表无残留错值。

  - **性能卡片左列宽度自适应（用户反馈：“电竞回报率的‘率’字被 1000Hz 框框遮住”）**：实测确认属上游原版既有布局缺陷，与本次适配无关。
    - 根因：`MainForm.UI.cs` 四处 `int lblW = (int)(82 * dpiScale);` 写死左列宽度，但 `ModernCheckBox.OnPaint` 的文字从控件内 `x = 25` 起绘（1px 留白 + 16px 方框 + 8px 间距），`电竞回报率` 实测宽 68px，实际需要 97px 控件宽度；而右侧首个回报率按钮正好从 `x = 96` 紧贴该栏右边缘起排，于是文字区（到 `x = 107`）与按钮重叠 11px，压掉了“率”字右半。
    - 新增 `MainForm.MeasurePerfLabelColumnW()`：用 `TextRenderer.MeasureText` 实测左列全部文案（`lblDpiTitle` / `lblRateTitle` / `lblNuphyBrightTitle` / `chkAutoGameDpi` / `chkAutoGameRate`）的最宽值，加上 `boxAndGap = 25`（与 `ModernCheckBox` 绘制偏移保持一致）与 `4 * dpiScale` 留白，与原 `82 * dpiScale` 取大值作为实际宽度；控件尚未创建时回退 68px 文本宽。
    - 四处 `int lblW = (int)(82 * dpiScale);` 全部改为 `int lblW = MeasurePerfLabelColumnW();`，区域包括 `BuildUI` 控件创建、`RelayoutDpiButtons`、`RelayoutRateButtons`、`RelayoutPerformanceControls`，任意新增文案都不会再被遮挡。
    - 实机验证：`lblW` 由 82px 变为 97px，`chkAutoGameRate` 文字占据 `X = 39..107`、控件右边缘 111，首个回报率按钮左边缘 111，完全不再重叠；屏幕截图像素分析确认文字最后亮列与按钮首列不相交。
    - 新增 `tests/test_perf_label_layout.ps1`（27 项）：源码层禁止写死 82px、要求 4 处均使用实测宽度；运行时在 1.0/1.25/1.5/1.75/2.0 五个缩放比下逐档断言文字不溢出自身控件且不越过首个回报率按钮左边缘。


### 🎮 手柄与键盘设备接入
- **飞智 (Flydigi) APEX 6 / APEX 6 Pro / Vader 5 Pro** (#24)：
  - 新增 `FlydigiDeviceHelper`，通过 `0xFFA0` Vendor HID 集合发送 GPA6 `5A A5 01 02` 设备信息查询，解析 `0..10` 档电量、充电标志、连接形态与型号；实测 APEX 6 Pro 返回 100% 电量。
- **迈从 (MCHOSE) G87 V2 三模机械键盘** (#24)：
  - 新增 `MchoseDeviceHelper`，通过官方 SDK 的 `55 05 00 38 38` 性能配置查询读取 `rf_battery` 与 `isCharging`；实机 2.4G 读到 21%，通用电竞鼠标协议显式隔离 `3837:3033`。

### 🛠️ 底层总线与自动化测试
- **通用 HID 总线架构**：新增 `HidTransport`，统一 SetupAPI 枚举、HidD 属性/能力查询与重叠 I/O 超时读写，杜绝同步 `ReadFile` 阻塞 UI 轮询线程。
- **全量契约测试**：新增 `test_edifier_halo_space.ps1`、`test_sony_inzone_support.ps1`、`test_logitech_g502x_support.ps1`、`test_atk_angrymiao_metapanda.ps1` 与 `test_flydigi_mchose_support.ps1`，全量测试套件 51/51 100% 通过（新增 test_mchose_mouse_support.ps1）。

---

## 🚀 [v2.2.0] - 2026-10-21 (AI 智能体专属外设适配 · 9大重点体验与稳定性修复)

### 🤖 一、 AI 智能体专属外设适配（做自己的开发者）
- **外设向导开发者微光卡片**：添加设备向导页（`AddDeviceWizardForm`）重磅上线“自己做自己的开发者”极客卡片，支持一键复制完整定制版 AI Agent 硬件逆向提示词；
- **工程经验飞轮传承**：提示词第 1 步强制前置通读 `docs/AGENT_HANDBOOK.md` 与 `AGENT_RULES.md`，使外部智能体全盘继承 11 大底层铁律（严格 C# 5.0、Win32 键盘 HID 0 权限握手、DWM 抗白框状态机、测试全内存反射防锁死等），杜绝踩坑；
- **独立推荐 AI 工具弹窗**：新增 `AiToolsDialog`，清晰提供 DeepSeek Harness、Tencent Workbuddy、Google Antigravity、OpenAI Codex 官网直达与科学上网指引；
- **全工程中英双语指南**：正式发布 `docs/AI_DEVICE_ADAPTATION_GUIDE.md` 并在 README 中建立索引，助力全球社区开发者与智能体共建外设生态。

### 🎯 二、 9 大重点体验问题与稳定性修复
1. **修复常规 DPI 与电竞 DPI 栏位跳转冲突**：全屏游戏优化激活时锁定常规桌面 DPI 档位高亮，彻底杜绝电竞档位下发时常规栏位发生漂移错位；
2. **修复回报率管理页保存不同步**：编辑回报率档位保存后，主界面桌面行与电竞行回报率按钮同步全量重排（`RelayoutPerformanceControls`）；
3. **回报率满 5 档未选项目置灰与上限警示**：勾选达到 5 档上限后，未选中的档位 CheckBox 与文字自动置灰禁用，底部动态显示上限指引；
4. **回报率档位管理弹窗底部按键设计统一**：严格对齐规范设计语言，左侧取消、右侧品牌主色保存；
5. **DPI 配置页 XY 轴分离调节深度进化**：新增 1:1、4:3、16:10 等比联动锁定与 CS2 控枪预设胶囊，行控件加入醒目 X/Y 标签，主界面与右上角硬件生效徽标双轴自适应呈现（如 `● 硬件生效: 4000 Hz · 3000×2250 DPI`）；
6. **全屏游戏自动优化 OSD 防顶替机制**：建立 OSD 窗体层熔断、业务静默期与轮询状态预对齐三层立体保护，彻底杜绝硬件轮询截断覆盖全屏优化双开卡片；
7. **全屏游戏双开 OSD 卡片排版与视觉规范重塑**：DPI 与回报率数值字体大幅放大至 18.5pt / 13.5pt，顶部去 Emoji 采用典雅钛银字体，底部战术胶囊徽章彻底移除“第几档”冗余前缀，尺寸微调至 248×76 呈现呼吸感对称；
8. **雷蛇巴塞利斯蛇 V3 专业版 35K 协议深度适配**：完美支持第 3 代 35K 传感器型号识别、90 字节双向报文电量解析与多模通信；
9. **变量作用域冲突与设备列表冷启动布局修复**：修复 DeviceManager 中 CS0136 变量命名冲突，优化高 DPI 缩放下按键自适应防溢出测算。

---

## 🚀 [v2.1.1] - 2026-10-20 (体验与生态重大升级 · 稳定性优化)

### 🎯 一、 功能与体验革新

#### 【XY轴DPI独立调节】
- **支持横纵独立调校**：鼠标现已支持 X 轴与 Y 轴灵敏度分开独立调节，满足不同屏幕比例与 FPS 垂直控枪、压枪的手感需求；
- **FPS 电竞比例联动预设**：内置一键比例联动胶囊（`1:1`、`4:3 75%` 纵向控枪优化、`16:10 90%`、`1.33x` 等），开启锁定后调节任意轴自动按比例换算；
- **自适应双轴悬浮 HUD**：屏幕悬浮 OSD 全面升级支持双轴排版显示（如 `1600 × 1200 DPI`），清晰直观无遮挡。

#### 【游戏自动优化加入DPI】
- **进游戏自动调档**：全屏游戏自动优化功能现已加入专属 DPI 切换，进入游戏自动拉满电竞 DPI 与超高回报率；
- **退游戏秒级复原**：切回桌面或退出游戏自动平滑恢复为日常办公档位，兼顾电竞竞技手感与日常办公续航；
- **防抢焦点盲操反馈**：悬浮 HUD 与清脆电子提示音无感提醒，不抢占游戏操作焦点、绝不吞枪。

#### 【正式支持低回报率选项】
- **多档位自由选择**：正式加入 125Hz、250Hz、500Hz 等低回报率与全档位选项；
- **兼顾节能与复古手感**：日常办公更省电、降低低配电脑 CPU 负载，同时满足特定经典电竞游戏对低回报率手感稳定性的需求。

#### 【Windows原生展开动画】
- **每次呼出均有原生弹窗动效**：重塑窗口弹出体验，点击托盘呼出软件时，每一次均能完美重现与首次开启软件一致的 Windows 原生居中缩放微光展开动效；
- **原地展开与位置记忆**：窗口呼出严格在上次关闭的原位就地展开，告别位置漂移，彻底消除从底部任务栏飞出变形的不适感；
- **彻底杜绝白框闪烁**：全面解决深色模式下窗口呼出瞬间偶现白框闪烁的问题，界面展开纯净顺滑。

---

### 🎮 二、 品牌协议深度融入与外设适配

#### 【罗技、雷柏等协议深度融入】
- **罗技 (Logitech)**：原生支持 Lightspeed 与优联无线协议，免开官方后台直接调节物理 DPI、回报率与游戏自动优化；
- **雷柏 (Rapoo)**：原生支持雷柏无线电竞协议，直接调节 DPI 档位与 125Hz~8000Hz 全阶梯回报率，支持进游戏自动调节；
- **瓦尔基里 (Valkyrie)**：全面支持 VK 电竞鼠标物理 DPI 切换与 8K 回报率调节，深度融入游戏自动优化。

#### 【Apple & Beats 家族全系适配】
- **全系耳机电量感知**：原生支持 AirPods 全系（AirPods 1/2/3/4、Pro 1/2、Max）与 Beats 全家族耳机，实时读取左右耳独立电量与充电状态；
- **真配对白名单防骚扰**：仅允许已在本机蓝牙设置中配对的外设入库，彻底杜绝周围陌生路人设备被误扫自动入库的困扰；
- **锁定真实名称**：完美支持用户自定义蓝牙重命名，杜绝耳机显示为底层代号（如 Wireless iAP），始终锁定真实耳机名称。

#### 【HyperX 飓风三原生支持】
- **新增飓风三无线版支持**：原生支持极度未知 HyperX 飓风三 (Cloud III Wireless) 无线电竞耳机，免开后台即可实时查看电量、无线连接状态与麦克风静音状态；
- **特别鸣谢**：感谢群友 **“rm -rf /*”** 对本功能提供的宝贵代码贡献！

#### 【瓦尔基里键盘电量深度修复】
- **机械键盘电量恢复**：彻底修复 VK 全系机械键盘电量读取问题，告别“免驱”状态，实时物理百分比电量读取全面恢复正常；
- **键鼠同接收器独立分类**：同一接收器下的键盘与鼠标实现独立识别与分别配置，设备状态互不干扰。

---

### 🛠️ 三、 稳定性与问题修复

#### 【修复已知问题】
- **已知问题修复**：修复已知问题。

---

## 🚀 [v2.0.4] - 2026-10-18

### ⚔️ 1. 瓦尔基里 (Valkyrie) 全系外设驱动原生支持
- **全系机械键盘适配**：深度逆向解析瓦尔基里私有 HID `0xF7` 握手报文，无驱动原生支持 VK 99、VK 87、VK 75 等全系机械键盘的 2.4G/有线双模真实电量采集、充电状态与连接感知；
- **全系电竞鼠标原生适配**：支持 VK M5、VK M7 等全系电竞鼠标的精准电量、8K 极限回报率（125Hz~8000Hz）实时调节与物理 DPI 档位联动；
- **键鼠双模多设备二合一接收器**：支持瓦尔基里全系多模接收器协议，同一接收器下的键盘与鼠标可同时独立显示电量并分别配置硬件性能。

### 🎯 2. 雷蛇与瓦尔基里 XY 轴灵敏度独立比例调节
- **XY 轴独立调节引擎**：支持雷蛇（Razer 0x1532）与瓦尔基里（Valkyrie 0x093A/0x248A）鼠标的 X/Y 轴灵敏度独立调校与板载 EEPROM 固化存储；
- **FPS 电竞比例联动预设**：DPI 管理窗口内置快捷比例切换胶囊（`1:1` 等比、`4:3 75%` 纵向控枪优化、`16:10 90%`、`1.33x`），锁定状态下修改 X 轴自动按比例换算 Y 轴；
- **双文本框对称微调**：青色 `X` 轴与金色 `Y` 轴双微调框像素级对齐，独立支持 100~35000 DPI 高精微步进。

### 🖥️ 3. 屏幕悬浮 OSD 双轴排版与品牌配色
- **XY 独立双轴 HUD 渲染**：当 $X \ne Y$ 时，OSD 自动拓宽并自适应字号显示 `1600 × 1200 DPI`，居中胶囊、能量阶梯与微型指示段三套风格全面消除文字边缘裁切；
- **瓦尔基里专属品牌霓虹配色**：新增瓦尔基里专属电竞粉红高亮主色，与雷蛇荧光绿、雷柏天青蓝保持高度视觉识别度。

### 🎨 4. 性能面板 9px 均匀行间距精修
- **统一整齐的行间距体系**：重构“鼠标性能与档位调节”卡片内 5 行设置间距，彻底解决 DPI 档位与常规回报率过于松散（14px）、下半部分开关与提醒紧贴拥挤（3~4px）的问题，全部校准为统一舒适的 9px 间距；
- **卡片高度与底边对齐**：折叠状态（148px）与展开状态（218px）高度自适应动态对齐，内外留白自然协调。

## 🚀 [v2.0.3] - 2026-10-18

### 🔋 1. 托盘样式更新
- **新增「纯数字极简」电量形态**：全新推出第 4 种托盘电量显示形态，完全剔除电池外框与能量条，直接以超大纯净数字呈现剩余电量；针对 100%（紧凑微调）、两位数与个位数进行了像素级居中微调；
- **重构现代电竞断连图标**：重新绘制现代电竞风格断连状态标识，替换原有的简陋问号与割裂图标，辨识度大幅提升；
- **托盘菜单即时热切换**：右键托盘菜单无缝支持「居中胶囊」、「左侧胶囊」、「数字能量表」与「纯数字」四大形态实时热切换。

### ⚡ 2. 修复性能卡死问题
- **硬件轮询全面异步化**：彻底剥离底层 HID 硬件探测与通信时的同步阻塞循环，耗时操作全面转由后台线程池异步处理，彻底解决跨屏拖拽、系统设置变更与 DPI 切换时的窗口泛白卡顿与“未响应”假死现象；
- **根绝渲染黑边条**：彻底消除跨多显示器、多 DPI 拖拽窗口时边缘产生的黑色残留边框（Black Bar），重绘与尺寸变更精准同步。

### 🖥️ 3. 窗口比例与高分屏优化
- **极限缩放智能防溢出**：引入屏幕工作区高度自适应夹紧算法（`ClampScaleToScreen`）。在 300% 等大缩放下自动智能收敛缩放上限，**彻底解决主窗口底部“立即刷新/最小化到托盘”控制区域被截断且无法复原的问题**；
- **恢复黄金舒适比例**：恢复 4K 150% 缩放下的 705px 黄金宽度与原生自然比例，界面视觉舒展协调；
- **移除粗白滚动条**：彻底移除影响视觉体验的原生 WinForms 白色粗滚动条，保持精致纯净的暗黑控制台整体感；
- **全窗口通用高 DPI 自适应**：覆盖全软件 12 个二级窗体与对话框，并精修关于页二维码排版防遮挡与 Windows 11 原生弹窗动效。

## 🚀 [v2.0.2] - 2026-10-17
- **【动效与渲染】全屏游戏回报率折叠动画修复**：彻底解决展开/收回时出现的假死、画面撕裂、双排按钮重叠残影问题，升级为基于 160ms Cubic Ease-Out 平滑减速缓动的**卷帘门式自然遮罩动效**，引入 `WS_CLIPCHILDREN` 硬件剪裁与动态脏矩形增量擦除；
- **【安全与防误报】尝试解决杀软误杀拦截**：停用代码混淆器，发布干净透明的标准开源 Raw Binary；自动更新彻底移除后台隐式 PowerShell 脚本调用，全面升级为纯 C# 进程内原生文件安全热替换，规避主动防御启发式误报；
- **【托盘图标精修】断联状态图标重绘**：胶囊电池断联状态补齐闭合四角边框，消除四角缝隙，圆润紧凑；数字能量表断联状态中间破折号严格居中对齐；
- **【快捷方式体验优化】**：彻底去除每次开机启动时自动在桌面静默创建快捷方式的行为，改为仅支持在托盘右键菜单中由用户手动主动创建。

## 🌟 [v2.0.0] - 2026-10-05 (里程碑重大更新)

### 🖱️ 1. 多品牌外设全生态原生支持
- **罗技 (Logitech)**：原生支持 Lightspeed 与优联无线键鼠（GPW、GPX 系列等），免安装 G HUB 即可读取精确电量与硬件全称；
- **雷柏 (Rapoo)**：原生支持 VT9 系列等无线外设协议，支持 DPI 与回报率直接调节；
- **NuPhy (努斐)**：支持全系机械键盘休眠调节、WinLock 防误触锁定与背光控制；
- **国产电竞品牌**：支持 ATK、VGN、VXE、迈从 (MCHOSE)、狼蛛 (AULA) 等主流无线键鼠外设多通道识别与平滑切换；
- **蓝牙音频设备**：支持常见蓝牙耳机（如小米 Buds 4 Pro 等）实时电量读取与断联即时重连刷新；
- **雷蛇 (Razer) 全系增强**：支持黑寡妇、猎魂光蛛键盘专属状态指示；支持有线与 2.4G 无线模式智能识别与无缝切换。

### 🎮 2. 全屏游戏智能调度与性能控制
- **全屏游戏自动切换回报率**：进入游戏自动拉满电竞级高回报率（如 4000Hz / 8000Hz），退出游戏自动平滑切回常规桌面节能模式，兼顾竞技手感与无线续航；
- **电竞游戏进程管理器**：支持一键捕获当前运行的游戏进程或手动添加游戏主程序，100% 绿色反作弊安全；
- **全屏无感游戏 OSD 与提示音**：调节性能或切屏时提供零焦点置顶 OSD 与清脆电子提示音盲操反馈，绝不抢占游戏焦点、绝不吞枪；
- **独占全键无冲测速仪 (NKRO)**：内置键盘按键测速面板，测试期间屏蔽系统快捷键干扰，毫秒级统计击键并发与延迟。

### 🚀 3. 全自动在线更新与原地热替换
- **一键检查更新**：支持在线检测最新版本，发现新版一键升级；
- **无感自动替换**：升级包下载完毕后自动完成原地更新并重启，彻底告别手动解压与文件覆盖；
- **低干扰静默提醒**：开机低优先级静默探测，界面支持平滑热重载与轻柔微光提示。

### 🎨 4. Windows 11 原生暗黑美学与交互体验
- **原生暗黑质感**：深度契合 Windows 11 暗黑圆角与微光质感，提供居中胶囊等三大任务栏托盘图标风格自由切换；
- **主界面卡片直改名称**：主界面设备名称支持直接点击修改为个性化中英文昵称，多设备标签页与托盘同步联动刷新；
- **精修关于与致谢卡片**：高质感暗黑微晶浮岛卡底，赞赏码双向居中排版，舒适克制；
- **纯净右键托盘菜单**：全面去 Emoji 与多余图标，保持极简原生美学。

### ⚡ 5. 智能休眠待机与系统稳定性
- **独家智能休眠算法**：准确区分“物理拔出断联”与“静置待机休眠”，休眠时保留电量记忆，轻微位移毫秒级瞬间唤醒；
- **开机自启与自愈机制**：优化开机自启稳定性，资源管理器崩溃重启后托盘图标自动重建自愈；
- **极致轻量纯净**：常驻仅约 15~20MB 内存，0.1 秒秒速冷启，单文件免安装，纯净无遥测。

---

## 🚀 [v1.0.0] - 2026-09-10 (初代版本发布)
- **初始版本发布**：专为雷蛇无线鼠标打造，直接绕过臃肿雷云读取原生 HID 电池报告，单文件极速轻量。
