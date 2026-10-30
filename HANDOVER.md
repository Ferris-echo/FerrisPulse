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

## 五、 已知问题：游戏运行期间进程被终止（2026-10-28）

### 现象

游戏（英雄联盟）运行期间，FerrisPulse 进程在 `DpiMonitor: 运行中 ... gaming=1` 之后
**数十秒内静默消失**。用户感知为「软件掉了」。

| 观测项 | 结果 |
|---|---|
| .NET 异常日志 | ❌ 无（`AppDomain.UnhandledException` 未触发） |
| `MainForm 关闭` 日志 | ❌ 无（未走正常关闭流程） |
| WER 崩溃转储（DumpType=2） | ❌ 未生成 |
| Application 事件日志 | ❌ 无记录 |
| Defender 检测事件（1116/1117） | ❌ 无 |
| 游戏不运行时 | ✅ 可稳定运行 15 分钟以上 |

**无 dump + 无异常 + 无事件日志 ⇒ 进程是被 `TerminateProcess` 强制终止，而非崩溃。**

### 已排除的假设

| 假设 | 排除依据 |
|---|---|
| `CancelIo` 后无界等待导致持锁假死 | 已改为静态 OVERLAPPED + 500ms 有界等待（5e073ad），无游戏时可稳定 15 分钟 |
| 单实例健康检查误杀 | 已修复托盘态误判（3262e88），且终止时并无第二个实例 |
| Windows Defender 拦截 | 已加路径排除项，且无检测事件 |
| MSI Center 游戏加速 | 其「游戏助手 / 场景设置」组件均未安装 |
| Process Monitor 等调试工具 | 未运行、无驱动加载 |

### 当前结论（未最终确认）

**最可能的原因：游戏反作弊主动终止了本进程。**

支持证据：

1. 只在游戏进程活跃时发生，且总在 `gaming=1` 之后
2. 反作弊的终止是内核级 `TerminateProcess`，不产生 dump、不写事件日志
3. 当日游戏曾弹出「检测到黑客工具。请关闭不必要的程序，重启机器后再试。(1-7000)」
4. 同机的官方外设驱动（Razer Synapse 等）不受影响

### 为什么本程序会被判定为可疑

| 行为 | 反作弊关注度 |
|---|---|
| 全局 RawInput 钩子（`RIDEV_INPUTSINK`） | 高 —— 宏/脚本常用同样手段 |
| 每秒枚举全量进程表（`Process.GetProcesses()`） | 中 —— 用于全屏游戏检测 |
| 全屏 OSD 覆盖层（顶层窗口绘制在游戏上） | 中 —— 可能被判为 overlay |
| HID 厂商报文读写 | 低 —— G HUB / Synapse 同样如此 |

与商业外设软件的关键差异：**无代码签名、不在反作弊厂商白名单、无 WHQL 驱动**。

### 规避建议

玩带内核反作弊的游戏（Vanguard / BattlEye / EAC）前，关闭以下两项，
使程序退化为纯读取工具，行为面缩到最小：

- **全屏游戏自动优化**（不再枚举游戏进程、不写设备）
- **DPI 切换屏幕提示 (OSD)**（不再创建覆盖层）

### 后续排查方向（如需彻底定位）

1. 安装 Sysinternals Process Monitor，过滤器设为
   `Process Name is FerrisPulse.exe` + `Operation is Process Exit`，记录退出状态码
2. 关闭游戏进程后启动本程序，做 A/B 对照确认是否与游戏相关
3. 若确认为反作弊所致，可考虑：降低行为面（默认关闭游戏检测）、申请厂商白名单、或代码签名

---

## 六、 已知限制：回报率显示不跟手（2026-10-30）

### 现象

用官方驱动改回报率后，本程序界面要等一段时间才更新，比 DPI 明显慢。

### 原因

| 路径 | 触发频率 | 覆盖内容 |
|---|---|---|
| 常规轮询 | 用户设定（默认 30 秒）/ 省电模式 2 分钟 | 全部状态 |
| 唤醒刷新 | 普通 15 秒 / 省电 60 秒 | 全部状态 |
| DPI 快速监听 | 3 秒（独立线程） | 仅雷蛇 DPI |

回报率没有独立的快速监听，只能搭上面两条路径的顺风车，
所以最快也要等一次刷新周期。

### 为什么不做成 3 秒

每次查询都会把鼠标从 LP 低功耗推到 HP 高性能并打断其休眠计时。
DPI 监听已经是这个代价，再把回报率并进去等于翻倍唤醒频率，
会让省电模式失去意义。

> **前提限定（2026-10-30 用户实测）**：LP/HP 只在 **≤1000Hz 无线**时存在。
> 回报率设为 2000Hz 或 4000Hz 时，鼠标恒处于官方驱动标注的 **Corded** 高性能态，
> 无论有线还是无线都不再降频——此时查询不会打断任何降频，
> 省电模式的收益只剩「减少 USB 总线占用」和「减少唤醒次数」。

### 当前处理

- 唤醒刷新节流：普通模式 60 秒 → **15 秒**（`dbf8c7f`）
- 省电模式保持 60 秒，避免架空 2 分钟轮询上限
- 窗口隐藏/最小化时完全不查询，两种模式一致

### 如果还要更快

可选方案与代价：

| 方案 | 延迟 | 代价 |
|---|---|---|
| 节流降到 5 秒 | ≤5 秒 | 设备查询 ×3，耗电上升 |
| 并入 DPI 快速监听（3 秒） | ≤3 秒 | 唤醒频率翻倍，省电模式失效 |
| 只在打开性能页时高频读 | 页面内 3 秒 | 实现复杂，需管理页面生命周期 |

**结论：15 秒是当前功耗与跟手性的折中，不再优化。**
