# FerrisPulse · 灵脉 (RazerBatteryTray) 项目交接文档

> **交接日期**: 2026-10-21  
> **项目架构**: Windows Forms / Win32 原生通信 / GDI+ 自绘  
> **目标运行时**: .NET Framework 4.0 / C# 5.0 严格兼容规范  
> **源码编码规范**: 严格 **UTF-8 带签名（BOM）** 编码，Windows `\r\n` 换行符  
> **依赖限制**: **零外部 NuGet 依赖**，严禁引入任何第三方三方库  

---

## 一、 接棒 Agent 关键须知 (Must-Read Directives)

1. **环境与路径问题（重要修复前置）**：
   - 上一个会话中，智能体执行环境由于将工作空间目录识别为带前导斜杠和引号的 `/"D:/FerrisPulse · 灵脉"`，导致宿主终端在启动 `run_command` 时在底层报 `no such directory`，因此未能通过自动化终端执行 `dotnet build`。
   - 当用户修复路径配置后，**第一步操作**即执行编译命令验证代码：
     ```bash
     dotnet build
     # 或使用 MSBuild:
     msbuild "D:\FerrisPulse · 灵脉\FerrisPulse.sln" /p:Configuration=Release
     ```
2. **语言与编译约束**：
   - 严禁使用 C# 6.0+ 语法（❌ 严禁 `$"..."` 字符串插值、❌ 严禁 `?.` 空条件运算符、❌ 严禁 `=>` 表达式体属性/方法、❌ 严禁自动属性初始值 `public int A { get; set; } = 1;`）。
   - 全部使用 C# 5.0 标准：`string.Format(...)`、标准 null 检查、常规大括号函数体。
3. **编码规范**：
   - 所有源代码文件修改后必须保存为 **UTF-8 with BOM**（防止 Visual Studio 或 MSBuild 在编译带中文字符串时发生乱码或 CS1056 语法错）。

---

## 二、 本次已完成的 9 项重点改造与修复详情

所有业务逻辑与 UI 代码均已编写就绪，具体涉及 12 个核心文件：

### 1. 修复常规 DPI 与电竞 DPI 栏位跳转冲突
- **问题现象**：当设置桌面 DPI 与全屏电竞 DPI 不同时，一旦全屏游戏启动，主界面常规 DPI 的高亮按钮也跳到了电竞 DPI 位置。
- **根本原因**：
  1. `ApplyGameDpiStage` 在硬件下发成功后，直接调用了 `HighlightDpiStage(targetStage)`，将常规桌面行（`btnDpiStages`）强制高亮至电竞档位。
  2. 后台 50ms 轮询线程 `DpiMonitorWorker` 探测到鼠标进入新档位后，在 `OnDpiChanged` 中直接用 `newStage` 重置了 `btnDpiStages` 的高亮。
- **解决方案**：
  - 在 `src/UI/Forms/MainForm.Hardware.cs` 中：进入游戏时，常规 DPI 按钮行牢牢锁定在进入游戏前记忆的 `desktopDpiStage`，仅在电竞 DPI 栏更新选中档位（`UpdateGameDpiButtonSelection(targetStage)`）。
  - 在 `OnDpiChanged` 接收到硬件档位变化时，判断 `isGamingActive && autoGameDpiEnabled`，若是则更新电竞栏并保持常规栏桌面档位高亮。

---

### 2. 回报率管理页保存后，电竞回报率一栏未同步更新
- **问题现象**：在回报率管理对话框添加/编辑档位后，保存关闭，主界面的“电竞回报率”按钮行没有更新，导致上下两栏不同步。
- **根本原因**：`src/UI/Forms/MainForm.UI.cs` 中 `OpenPollingRateManagementDialog` 弹窗返回 OK 后，仅调用了 `RelayoutRateButtons()`（仅排版了常规桌面行），漏掉了 `btnGameRates` 的排版与同步。
- **解决方案**：改为统一调用 `RelayoutPerformanceControls()`，一次性完成桌面回报率行与电竞回报率行的布局计算与状态联动。

---

### 3. 回报率档位满 5 档后未选中档位灰色禁用提示
- **改动文件**：`src/UI/Forms/PollingRateManagementDialog.cs`
- **解决方案**：
  - 新增 `RefreshItemRowStates()` 动态计算激活项。
  - 勾选达到 5 个档位上限后，自动将未选中的所有 CheckBox 和文字项置灰（`Color.FromArgb(90, 100, 120)`）并禁用点击，底部同步切换为警示提示：`⚠️ 最多支持启用 5 个回报率档位，如需添加其他档位请先取消勾选现有档位`。取消勾选后即刻恢复可用状态。

---

### 4. 回报率档位页面底部按键对齐统一设计语言
- **改动文件**：`src/UI/Forms/PollingRateManagementDialog.cs`
- **解决方案**：
  - 遵循 Windows 标准与 FerrisPulse 设计语言：
    - **左侧**：**取消**按键（深灰背景 `#181E2A`，悬浮 `#252E3E`，边框 `#323E55`）。
    - **右侧**：**保存并使用**按键（品牌主色绿 `#00A064`，悬浮 `#00C853`）。

---

### 5. DPI 配置页 XY 调节交互改进、主界面双 DPI 显示与 OSD 防顶替
- **改动文件**：`src/UI/Forms/DpiManagementDialog.cs`、`src/UI/Controls/DpiStageRowControl.cs`、`src/UI/Forms/MainForm.UI.cs`、`src/UI/Forms/MainForm.Hardware.cs`
- **改进点**：
  1. **直观比例预设**：新增 `[🔗 联动等比]`、`[等比 1:1]`、`[⚡ 4:3拉伸补偿 (X*1.33)]`、`[⚡ 16:10补偿 (X*1.20)]`、`[⚡ 反向1.33x (Y*1.33)]` 胶囊按钮，并高亮标记当前比例，底部附带专业 CS2/瓦罗兰特拉伸补偿手感说明。
  2. **XY 独立标签**：行控件中增加醒目的天青色 **X** 与金黄色 **Y** 标签。
  3. **主界面双 DPI 呈现**：当 X 与 Y 轴数值不同时，桌面与电竞 DPI 按钮自动渲染为 `3200×2000` 格式，并智能缩小字号防止文本溢出。
  4. **彻底解决 OSD 菜单双 DPI 被顶替的问题**：
     - **根因**：原先 `DpiMonitorWorker` 轮询时调用的是不带 Y 轴的 `FastQueryDpi`，在随后的 50ms 帧触发 `OnDpiChanged` 时直接 `curDpiY = newDpi`，将双 DPI 强行抹平并弹出单 DPI 窗口。
     - **修复**：全链路雷蛇、瓦尔基里、雷柏、罗技的 `FastQueryDpi` 与 `OnDpiChanged` 均同步获取并透传 `curDpiY`，杜绝任何单 DPI 窗口回弹覆盖。

---

### 6. 进入游戏自动优化 OSD 联动与卡片重构
- **改动文件**：`src/UI/Forms/DpiOsdForm.cs`、`src/UI/Forms/MainForm.Hardware.cs`
- **解决方案**：
  - 新增 `ShowGameOptimization(bool isEnteringGame, bool showDpi, int dpiX, int dpiY, int stage, int stageCount, bool showRate, int hz, string brand = "Razer")`。
  - 在 `OnPaint` 中新增双开合并卡片（`isDualOptimizationMode`）：
    - **双开**（DPI + 回报率同时开启）：左半部呈现双轴/单轴 DPI 与档位标签，中间垂直微光分割线，右半部呈现 Hz 回报率，底部附带 `⚡ 全屏游戏优化已激活`（切回桌面附带 `🖥️ 已恢复常规桌面状态`）徽标。
    - **仅开 DPI**：仅展示 DPI 卡片。
    - **仅开回报率**：仅展示回报率卡片。
  - 在 `MainForm.Hardware.cs` 中实现 `ApplyGameOptimization(bool isEntering)` 统一调度，杜绝多个 OSD 窗体并发冲突。

---

### 7. 雷柏鼠标通信防假死与 2.4G 在线状态识别
- **改动文件**：`src/Hardware/RapooDeviceHelper.cs`、`src/UI/Forms/MainForm.Hardware.cs`、`src/UI/Forms/MainForm.cs`
- **通信防假死**：
  1. 彻底移除了 50ms 轮询循环中对回报率的 EEPROM 特性报文轮询（回报率无硬件按键，仅软件触发）。
  2. 新增 `lastRapooDpiPollTime`，将 DPI 硬件轮询节流限制为 300ms 一次，避免国产 MCU 报文堵塞。
  3. 写入 DPI 与回报率后引入 350ms 的静默保护期（`SetEepromQuietPeriod(350)`），防止写 EEPROM 时读取冲突导致假死。
- **2.4G 在线状态识别**：
  - 修正原先 `effPid >= 0x4000` 粗暴判定逻辑。增加物理 USB PID 判定与 `HidD_GetProductString` 字符串检索，只要包含 "Receiver"、"Dongle"、"2.4G"、"Wireless" 即准确识别为 2.4G 无线并剥除名字符号。

---

### 8. 瓦尔基里键盘电量检测失效修复
- **改动文件**：`src/Hardware/ValkyrieDeviceHelper.cs`、`src/Hardware/DeviceManager.cs`
- **解决方案**：
  - 补充新版固件使用的瓦尔基里键盘 `VID_347A` ("347a:") 识别过滤。
  - 在私有协议（0xF7/0xF5/0x04）无应答时，自动回退探测 `GenericHidHelper.QueryHidBattery` 标准 HID 电池报文。
  - 在 `DeviceManager` 设备去重合并逻辑中，保留已有设备实体的 `CustomName`，防止自定义键盘别名被覆盖丢失。

---

### 9. 罗技 GPW 电量与 2.4G/有线状态错误修复
- **改动文件**：`src/Hardware/LogitechDeviceHelper.cs`
- **根本原因**：
  - 罗技无线接收器（C539/C547/C548等）在 `0xFF` 索引处会响应 HID++ Ping。原代码优先测试 `0xFF`，判定成功后直接标记为 `"USB 有线"` 并执行 `break` 跳出，导致插在槽位 `0x01` 的真正无线鼠标被直接丢弃。
- **解决方案**：
  - 针对无线接收器，`targetIndices` 严格只轮询无线配对槽位 `{ 0x01, 0x02, 0x03, 0x04 }`，传输方式固定标记为 `"Lightspeed 无线"`。
  - 仅纯有线直连设备才测试 `0xFF`。
  - 增加 Feature `0x1001`（电池电压 mV 转换）回退机制，确保 0x1000 统一电池状态读取失败时仍能准确计算电量。
  - 将 `SendAndReceive` 的写缓冲区长度与设备的 `caps.OutputReportByteLength` 严格对齐，彻底消除 Win32 `ERROR_INVALID_PARAMETER (87)` 报错。

---

## 三、 本次修改的文件索引表

| 序号 | 文件路径 | 主要修改内容 |
| :--- | :--- | :--- |
| 1 | `src/Hardware/RapooDeviceHelper.cs` | 增加产品字符串 P/Invoke、EEPROM 静默期、300ms 节流、2.4G 接收器判定 |
| 2 | `src/Hardware/RazerDeviceHelper.cs` | 增加带 `dpiY` 的 5 参数 `FastQueryDpi` 重载（同时保留了另一 Agent 的巴蛇 35K PID 修复） |
| 3 | `src/Hardware/ValkyrieDeviceHelper.cs` | 补充 `VID_347A`、带 `dpiY` 的 `FastQueryDpi`、键盘标准 HID 电池探测回退 |
| 4 | `src/Hardware/DeviceManager.cs` | 瓦尔基里设备回退标准 HID 电池探测、保留设备 `CustomName` 防止去重丢别名 |
| 5 | `src/Hardware/LogitechDeviceHelper.cs` | 接收器排除 `0xFF` 槽位避免误判有线、Feature 0x1001 电压回退、对齐写缓冲区长度 |
| 6 | `src/UI/Forms/PollingRateManagementDialog.cs` | 满 5 档置灰禁用、取消在左灰底、保存并使用在右绿底 |
| 7 | `src/UI/Controls/DpiStageRowControl.cs` | 增加天青色 X 与金黄色 Y 胶囊徽标，优化双轴数值输入体验 |
| 8 | `src/UI/Forms/DpiManagementDialog.cs` | 增加 1:1 / 4:3 / 16:10 / 1.33x 快捷预设胶囊、CS2 电竞手感说明 |
| 9 | `src/UI/Forms/DpiOsdForm.cs` | 增加 `ShowGameOptimization` 及双开合并电竞 OSD 卡片自绘渲染 |
| 10 | `src/UI/Forms/MainForm.cs` | 新增 `lastRapooDpiPollTime` 字段 |
| 11 | `src/UI/Forms/MainForm.UI.cs` | 回报率保存后全量更新电竞行、双 DPI `valX×valY` 主界面排版与字体自适应缩放 |
| 12 | `src/UI/Forms/MainForm.Hardware.cs` | 电竞/常规 DPI 按钮状态锁定防跳动、雷柏 300ms 节流与移除回报率轮询、全品牌 OSD 透传 Y 轴防顶替、`ApplyGameOptimization` 联动 |

---

## 四、 接棒 Agent 的执行清单 (Next Steps for Incoming Agent)

1. **环境与路径校验**：
   - 确认工作目录为有效 Windows 路径（如 `D:\FerrisPulse · 灵脉`，无前导 `/`）。
2. **执行编译验证**：
   ```powershell
   dotnet build
   ```
   - 若出现任何警告或编译报错，直接定位至对应行调整。注意维持 C# 5.0 语法规范。
3. **功能性验证验证点**：
   - [ ] 启动程序，选择雷蛇/罗技/雷柏/瓦尔基里鼠标。
   - [ ] 打开 DPI 配置页，点击 `[⚡ 4:3拉伸补偿]`，确认 X 与 Y 数值变为 `X*1.33`，主界面按钮正确显示为 `XXXX×YYYY` 格式。
   - [ ] 按下鼠标物理 DPI 切换键，确认屏幕弹出 OSD 显示 `XXXX×YYYY` 且**不会**被单 DPI 顶掉。
   - [ ] 勾选“全屏游戏自动优化总开关”，启动全屏游戏：
     - 同时勾选电竞 DPI 与回报率：确认弹出双开合并卡片（左侧 DPI，右侧 Hz）。
     - 确认进入游戏后，主界面“常规 DPI”栏位保持高亮在原本的桌面档位，**不**跳动到游戏档位。
     - 切回桌面，确认平滑恢复常规桌面 DPI 与回报率。
   - [ ] 打开回报率管理页，勾选 5 个档位，确认剩余未勾选项变灰且不可选，底部提示超限；点击左侧“取消”或右侧“保存并使用”。
   - [ ] 连接罗技 GPW 与无线接收器，确认正确显示为“Lightspeed 无线”，电量正常读取。
   - [ ] 连接瓦尔基里键盘，确认电量百分比正常探测展示。
   - [ ] 调节雷柏鼠标 DPI，确认软件与鼠标丝滑响应，无卡死或无响应现象。

---

## 五、 2026-10-30 新增：三台外设电量适配（MOONDROP / MCHOSE G98V2 / MCHOSE G7）

本节记录在本机（Windows，.NET 4.0 / csc.exe 4.8）实测完成的三台设备适配结论，
三台均已拿到真实电量报文（水月雨那台最初判定「无电量通道」，后由官方网页驱动 hub.moondroplab.tech 的 WebHID 协议打通）。

### 5.1 设备与协议一览

| 设备 | VID:PID | 端点（UsagePage:Usage） | 帧长 | 电量来源 | 状态 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| MCHOSE G98V2 键盘 2.4G 接收器 | `0x41E4:0x2004` | `FF02:0002` | 20B In/Out | SINOWEALTH `0x13 0x4A` 报文 | ✅ 实测 98% |
| MCHOSE G7 鼠标 2.4G 接收器 | `0xA8A5:0x2255` | `FF01:0010` | 65B In/Out | 官方 `/gmouse` WebHID 协议 | ✅ 实测 100% |
| MOONDROP GM-01 PRO 耳机 | `0x35D8:0x0133` | `0C:0001` | 64B In/Out | 官方 hub WebHID `4B 80 27` | ✅ 实测 90% |

### 5.2 MCHOSE G98V2（`0x41E4:0x2004`，SINOWEALTH 方案）

与仓库已有的 Z75S（`0x258A:0x01BB`）完全同构，复用 `MchoseDeviceHelper` 的 20 字节帧：

- 电量查询：`13 4A 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 5E`
- 型号查询：`13 05 01 00 … 00 19`
- 应答解析：`[1]` 命令回显，`[5]` 电量百分比，`[6]` 高半字节=充电 / 低半字节=充满，`[19]` = `sum([0..18]) & 0xFF`
- 实机应答：`13 4A 01 00 02 62 00 … C2` → 98%，未充电
- 型号 token `0A 00 00 00 00 18`（G98V2），与 Z75S 的 `…09` 区分，靠 token 而非 VID 判型号

### 5.3 MCHOSE G7（`0xA8A5:0x2255`，YJX 主控）

协议取自官方网页驱动 <https://www.mchose.com.cn/gmouse> 的 `getBatteryInfo()`：

- 65 字节缓冲区，`[0]` 为 Report ID（0），其后为 payload
- 电量查询：`00 55 30 A5 0B 2E 01 01 01 00 …`
- 应答判定：`buf[0]==0x00 && buf[1]==0xAA && buf[2]==0x30 && buf[3]==0xA5`
- `buf[9]` = 电量 0..100，`buf[10]` = 充电标志（`0x01` 表示已接线）
- 辅助命令：版本 `00 55 03`（ASCII 版本在 `[24..26]`）、在线 `00 55 ED`
- 实机应答：`00 AA 30 A5 0B 0A 01 01 01 64 00 …` → 100%，未充电
- 该系列 DPI / 回报率只有设备主动上报帧（`AA FA`），无查询写指令，故不声明 `SupportsDpi` / `SupportsPollingRate`

### 5.4 MOONDROP GM-01 PRO（`0x35D8:0x0133`）——官方 WebHID 协议

协议取自官方网页驱动 <https://hub.moondroplab.tech/>（Vue SPA + WebHID），不是猜测：

- 命令通道是 **`UsagePage 0x0C / Usage 0x0001`，In/Out 均 64 字节**，不是 `FF02` 私有页；
- 帧格式：`[0]=0x4B`（Report ID 75，官方 `config.reportId`）、`[1]=OP`、`[2]=CMD`、`[3]=payload 长度`，其余补零；
- 电量：`OP=0x80 / CMD=0x27`，应答 `[2]=0x27`、`[3]=6`、`[8]=有效标志(1)`、`[9]=0..100`；
- 实机应答：`4B 80 27 06 00 00 00 00 01 5A` → 90%；
- 辅助命令：`0x0C` 固件版本（ASCII `1.5.1`）、`0x31` 在线状态（`[4]=1`）、`0x2C` SKU/SN；
- 官方协议没有充电字段，USB 直连即持续供电，故 `isCharging = false` 不臆造。

> 曾经踩的坑：`FF02:0002` 的 64/65 字节镜像端点写入永远 OK 但零回帧，
> 在这里穷举了首字节 `0x00..0x3F` 都没用——真正的通道藏在 Consumer 页的 64 字节集合里。
> 所以 `IsProtocolEndpoint` 必须按 UsagePage/Usage/In/Out 四元组精确匹配，绝不能"取报告长度最大的集合"。

`GenericHidHelper` 仍排除该 VID/PID，避免同一条耳机出现两条记录。
### 5.5 本次改动文件索引

| 序号 | 文件路径 | 修改内容 |
| :--- | :--- | :--- |
| 1 | `src/Hardware/MchoseGMouseHelper.cs` | **新增**：G7/G3/A7e 系列 65 字节协议、电量/版本/在线查询、注册表缓存、5s 节流 |
| 2 | `src/Hardware/MoondropDeviceHelper.cs` | **新增**：GM-01 PRO 官方 WebHID 协议（`0x4B` / `0x80` / `0x27`）电量读取、端点四元组判定、`IsManagedDevice` 供通用扫描排除 |
| 3 | `src/Hardware/MchoseDeviceHelper.cs` | 扩展 `0x41E4:0x2004`（G98V2）：型号 token 化、`ParseSinowealthIdentityResponse`、双 VID 枚举 |
| 4 | `src/Hardware/GenericHidHelper.cs` | 通用扫描跳过 MOONDROP，避免重复条目 |
| 5 | `src/Hardware/UniversalGamingDeviceHelper.cs` | `IsSupportedGamingDevice` 排除 `pid_2004` / `pid_2255`，防止误发通用鼠标探测 |
| 6 | `src/Hardware/DeviceManager.cs` | 新增 2.966（G7）与 2.967（MOONDROP）两段注册 |
| 7 | `tests/test_mchose_g7_gmouse.ps1` | **新增**：26 条断言，锁定帧格式、偏移与拒绝条件 |
| 8 | `tests/test_moondrop_gm01pro.ps1` | **新增**：43 条断言，锁定协议常量、帧格式、应答解析与端点四元组 |

### 5.6 复现步骤

```powershell
cd D:\code\FerrisPulse
& cmd.exe /c "build.bat"                     # 必须用 pwsh 调 cmd，直接 ops_bash 跑 cmd 不生效
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run_all_tests.ps1
```

实机核对（反射加载 `FerrisPulse.exe`，只读不写）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\scratch\list_all_devices.ps1
# [Keyboard ] MCHOSE G98V2        98% -  2.4G 无线  MCHOSE:41E4:2004
# [Mouse    ] MCHOSE G7          100% -  2.4G 无线  MCHOSE:A8A5:2255
# [Headset  ] MOONDROP GM01 Pro   90% -  USB 有线    MOONDROP:35D8:0133
```

> ⚠️ 新踩的坑：`edit` / `write` 工具保存 .cs 或 .ps1 后会丢失 UTF-8 BOM，
> 每次改完必须用 `UTF8Encoding($true)` 重写并通过 `tools/ensure_boms_safe.py` 复查。
