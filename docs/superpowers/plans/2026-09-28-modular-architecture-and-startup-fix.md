# FerrisPulse 模块化解耦重构与开机自启动彻底修复执行计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `D:\电量管家\` 中近 20,000 行的单文件 `RazerBatteryTray.cs` 完全解耦重构为模块化架构（`src/` 结构），消除乱码与深层缺陷，补全 `WM_TASKBARCREATED` 托盘重建与自启动引擎，并为每个源文件嵌入标准 AI 维护指令。

**Architecture:** 采用“高内聚、低耦合”的纯净分层体系：`Core/` 负责入口/单实例 Mutex/注册表与自启动引擎；`Models/` 负责归一化数据契约；`Hardware/` 独立承载各厂商 HID 协议与 `DeviceManager`；`Services/` 承载游戏检测与音频合成；`UI/` 拆分为 `Theme/`（纯矢量算法）、`Controls/`（21 个独立控件）与 `Forms/`（独立弹窗及 5 个 `partial class MainForm` 分部切片）。使用系统自带 `csc.exe /recurse:src\*.cs` 保持零第三方依赖极速编译。

**Tech Stack:** C# 5 / .NET 4.0 (`csc.exe 4.8.9221.0`), WinForms, GDI+ 矢量渲染, Win32 P/Invoke, Raw Input, USB HID, Windows Registry.

**Spec:** [modular_architecture_and_startup_fix_plan.md](file:///C:/Users/LEGION%20Y9000X/.gemini/antigravity/brain/e0557259-c592-4be6-97a5-3ae3c207c5fe/modular_architecture_and_startup_fix_plan.md)

---

## Global Constraints

- **工作空间锁定**: 全部代码生成、编译与验证严格限制在 `D:\电量管家\` 内，严禁修改或引用 `proud-hertz`。
- **C# 5 / .NET 4.0 语法铁律**: 严禁使用 C# 6+ 语法（❌ 禁止 `$"..."`、❌ 禁止 `?.`、❌ 禁止 `nameof`、❌ 禁止 `out var`、❌ 禁止表达式主体方法体 `=>`）。
- **编码规范**: 所有新建和修改的 `.cs` 文件必须强制使用 **UTF-8 with BOM** 编码。
- **高分屏规范**: 所有坐标、尺寸、圆角半径必须包裹在 `S(...)` 缩放函数中，严禁硬编码物理像素。
- **AI 维护契约**: 所有 `.cs` 文件第一行必须嵌入标准 `[AI MAINTENANCE DIRECTIVE]` 提示词模板。
- **零破坏封装**: 主窗体必须使用 `partial class MainForm` 进行无损拆分，保持所有私有字段在同类编译期作用域内。

---

## Review Focus

1. **开机静默启动时托盘隐形**: 必须在 `WndProc` 中注册并响应 `RegisterWindowMessage("TaskbarCreated")`，确保 Explorer 后置加载或重启时自动重建托盘图标。
2. **时序倒挂自杀式删除自启动**: 在 `StartupManager` 中，必须先检查并平滑迁移现有自启动配置，最后才清理历史无用键名，杜绝误注销。
3. **工作目录被重置为 System32**: 在 `Program.Main` 启动首行必须调用 `Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory)`。
4. **乱码与脏字符**: 彻底修复 `text.Replace("?, "")`、`"开机自动启?`、`"巴塞利斯?` 等截断字符。
5. **单实例杀进程隐患**: 使用 `Mutex("Global\\FerrisPulse_SingleInstance_Mutex")` 取代遍历进程暴力 `Kill()`。

---

### Task 1: 基础设施搭建与核心数据模型、Win32 契约生成

**Files:**
- Create: `D:\电量管家\ARCHITECTURE.md`
- Create: `D:\电量管家\src\Models\DeviceCategory.cs`
- Create: `D:\电量管家\src\Models\CustomDeviceConfig.cs`
- Create: `D:\电量管家\src\Models\MouseBatteryInfo.cs`
- Create: `D:\电量管家\src\Models\PriorityItem.cs`
- Create: `D:\电量管家\src\Models\RunningProcessItem.cs`
- Create: `D:\电量管家\src\Models\RazerDeviceHardwareState.cs`
- Create: `D:\电量管家\src\Core\NativeMethods.cs`

**Interfaces:**
- Consumes: 无（基础定义）
- Produces: `DeviceCategory`, `CustomDeviceConfig`, `MouseBatteryInfo`, `PriorityItem`, `RunningProcessItem`, `RazerDeviceHardwareState`, `NativeMethods` (包含 Win32 P/Invoke 与 COM 接口 `IShellLinkW`, `IPropertyStore`, 以及 `WM_TASKBARCREATED`)

- [ ] **Step 1: 创建架构全局路由索引文档 `ARCHITECTURE.md`**
  写入完整的目录认知地图、功能路由表以及全域 AI 维护守则。

- [ ] **Step 2: 创建 `src\Models\` 下的 6 个实体类**
  包含标准 AI 维护指令头部，提取原代码中的枚举与数据契约，保存为 UTF-8 with BOM。

- [ ] **Step 3: 创建 `src\Core\NativeMethods.cs`**
  集中封装 `SetProcessDPIAware`, `RegisterWindowMessage("TaskbarCreated")`, `GetTickCount64`, `RAWINPUTDEVICE`, `SHQueryUserNotificationState`, ShellLink COM 接口等。

- [ ] **Step 4: 语法与编码自检**
  检查无 C# 6 语法，文件带 BOM。

---

### Task 2: 核心子系统：自启动引擎、配置管理与应用入口重构

**Files:**
- Create: `D:\电量管家\src\Core\StartupManager.cs`
- Create: `D:\电量管家\src\Core\ConfigManager.cs`
- Create: `D:\电量管家\src\Core\Program.cs`

**Interfaces:**
- Consumes: `NativeMethods`, `ConfigManager`
- Produces: `StartupManager.IsAutoStartConfigured()`, `StartupManager.SetAutoStart(bool, bool)`, `StartupManager.EnsureAppShortcutWithAumid()`, `ConfigManager.LoadConfig()`, `ConfigManager.SaveConfig()`

- [ ] **Step 1: 编写 `StartupManager.cs`**
  实现自启动状态检测（先查新名称再查旧名称）、注册表 `Run` 与 `StartupApproved\Run` 双键同步写入、平滑垃圾清理，以及 StartMenu AUMID 快捷方式创建。头部嵌入 AI 指令。

- [ ] **Step 2: 编写 `ConfigManager.cs`**
  集中管理注册表 `Software\FerrisPulse` 配置读写（DPI OSD、回报率、刷新间隔、游戏列表等），提供静态存取接口。

- [ ] **Step 3: 编写 `Program.cs`**
  第一行锁定工作目录 `Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory)`；使用系统级 `Mutex` 实现安全的单实例互斥与唤醒；处理 `--autostart` 启动参数。

- [ ] **Step 4: 检查异常防护边界**
  确保所有注册表和快捷方式操作均包含完整的 `try-catch` 降级逻辑。

---

### Task 3: 硬件通信驱动层独立解耦与统一设备调度

**Files:**
- Create: `D:\电量管家\src\Hardware\UniversalWinLockHelper.cs`
- Create: `D:\电量管家\src\Hardware\AudioDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\XInputHelper.cs`
- Create: `D:\电量管家\src\Hardware\RazerDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\RapooDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\VgnDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\LogitechDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\NuphyDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\GenericHidHelper.cs`
- Create: `D:\电量管家\src\Hardware\BluetoothDeviceHelper.cs`
- Create: `D:\电量管家\src\Hardware\DeviceManager.cs`

**Interfaces:**
- Consumes: `MouseBatteryInfo`, `NativeMethods`, `DeviceCategory`
- Produces: 各品牌独立硬件探测方法，以及 `DeviceManager.QueryAllDevices()`、优先级排序与状态仲裁。

- [ ] **Step 1: 提取系统/通用助手**
  创建 `UniversalWinLockHelper.cs`、`AudioDeviceHelper.cs`、`XInputHelper.cs`。

- [ ] **Step 2: 独立封装各大品牌私有 HID 通信协议**
  分别创建 `RazerDeviceHelper.cs`（含 90 字节私有报文与 8K 轮询率设置）、`RapooDeviceHelper.cs`、`VgnDeviceHelper.cs`、`LogitechDeviceHelper.cs`、`NuphyDeviceHelper.cs`、`GenericHidHelper.cs`、`BluetoothDeviceHelper.cs`。修复乱码（如 `"巴塞利斯蛇"`）。

- [ ] **Step 3: 实现统一设备调度器 `DeviceManager.cs`**
  聚合所有品牌助手，提供统一轮询入口、设备优先级排序与缓存更新。

- [ ] **Step 4: 线程安全性检查**
  确保所有底层 USB HID 数据传输均带异常隔离，单设备失败不影响整体设备轮询。

---

### Task 4: 后台感知服务层：游戏探测与声学合成器

**Files:**
- Create: `D:\电量管家\src\Services\GameDetector.cs`
- Create: `D:\电量管家\src\Services\ChimeSynthesizer.cs`

**Interfaces:**
- Consumes: `NativeMethods`
- Produces: `GameDetector.IsGameRunning(out string)`, `ChimeSynthesizer.PlayGameModeChime(bool isEnter)`

- [ ] **Step 1: 创建 `GameDetector.cs`**
  封装 `SHQueryUserNotificationState` 全屏检测与前台窗口进程遍历、深度系统进程与浏览器/播放器白名单过滤，以及 2 周期防抖仲裁。

- [ ] **Step 2: 创建 `ChimeSynthesizer.cs`**
  实现 44.1kHz 16-bit PCM 柔和纯音合成器（进入游戏：G5->C6 上升双音；退出游戏：C6->G5 下降双音），零外部音频文件依赖，异步非阻塞播放。

- [ ] **Step 3: 自测验证**
  验证合成算法无死锁，非阻塞主线程。

---

### Task 5: 界面设计系统与 21 个原子矢量自定义控件

**Files:**
- Create: `D:\电量管家\src\UI\Theme\ModernThemeHelper.cs`
- Create: `D:\电量管家\src\UI\Theme\ModernDarkMenuRenderer.cs`
- Create: 21 个自定义控件 `.cs` 文件于 `D:\电量管家\src\UI\Controls\`

**Interfaces:**
- Consumes: GDI+ (Drawing, Drawing2D), `NativeMethods`
- Produces: 现代暗黑主题画刷、抗黑角圆角绘制、闪电矢量图，以及 21 个自绘制双缓冲自定义控件。

- [ ] **Step 1: 创建 `ModernThemeHelper.cs` 与 `ModernDarkMenuRenderer.cs`**
  修复 `DrawVectorLightning` 与状态颜色计算，杜绝 `text.Replace("?, "")` 类的脏字符。

- [ ] **Step 2: 拆分基础原子控件（卡片、按钮、滑块、徽章）**
  创建 `RoundedCard.cs`, `ModernButton.cs`, `ModernBadge.cs`, `ModernCheckBox.cs`, `ModernSegmentButton.cs`, `SmoothSegmentedTabBar.cs`, `SubtleDivider.cs`, `ModernProgressBar.cs`, `StatusPill.cs`, `TactileSmoothSliderControl.cs`。

- [ ] **Step 3: 拆分高级功能与复合控件**
  创建 `AnimatedContextMenuStrip.cs`, `ModernDarkDropdown.cs`, `ModernInputBox.cs`, `WizardRadarBadge.cs`, `ThreeLocksHudControl.cs`, `ScanningBeamControl.cs`, `HeadsetStatusTile.cs`, `FocusDeviceSelector.cs`, `PriorityReorderControl.cs`, `DpiStageRowControl.cs`, `DpiActionButton.cs`。

- [ ] **Step 4: 检查双缓冲与高分屏适配**
  确保所有控件均开启 `OptimizedDoubleBuffer` 且使用 `DpiScale` 计算物理渲染位置。

---

### Task 6: 独立业务弹窗与二级对话框

**Files:**
- Create: `D:\电量管家\src\UI\Forms\DpiOsdForm.cs`
- Create: `D:\电量管家\src\UI\Forms\KeyRolloverSpeedForm.cs`
- Create: `D:\电量管家\src\UI\Forms\MouseRateTestDialog.cs`
- Create: `D:\电量管家\src\UI\Forms\ModernDeviceSwitcherFlyout.cs`
- Create: `D:\电量管家\src\UI\Forms\DevicePriorityForm.cs`
- Create: `D:\电量管家\src\UI\Forms\AddDeviceWizardForm.cs`
- Create: `D:\电量管家\src\UI\Forms\GameProcessManagerForm.cs`
- Create: `D:\电量管家\src\UI\Forms\PromptDialog.cs`
- Create: `D:\电量管家\src\UI\Forms\DpiManagementDialog.cs`

**Interfaces:**
- Consumes: `UI.Controls`, `UI.Theme`, `Hardware.DeviceManager`, `Models`
- Produces: 各独立功能弹出窗体。

- [ ] **Step 1: 提取独立工具与向导窗体**
  实现按键测速仪、回报率测试、设备优先级管理、设备添加向导、游戏进程捕获窗口等。

- [ ] **Step 2: 提取并完善 `DpiOsdForm.cs`**
  确保具备零焦点 (`WS_EX_NOACTIVATE | WS_EX_TOPMOST`) 特性，且支持居中胶囊、能量阶梯、微型指示段三套风格。

---

### Task 7: 主窗体解耦切片 (`partial class MainForm`) 与开机自启动托盘重建

**Files:**
- Create: `D:\电量管家\src\UI\Forms\MainForm.cs`
- Create: `D:\电量管家\src\UI\Forms\MainForm.UI.cs`
- Create: `D:\电量管家\src\UI\Forms\MainForm.Tray.cs`
- Create: `D:\电量管家\src\UI\Forms\MainForm.Hardware.cs`
- Create: `D:\电量管家\src\UI\Forms\MainForm.Events.cs`

**Interfaces:**
- Consumes: 所有底层模块、`StartupManager`、`DeviceManager`、`GameDetector`
- Produces: `MainForm` 主窗体完整交互。

- [ ] **Step 1: 编写 `MainForm.cs` (核心中枢)**
  构造函数、DPI 缩放初始化、配置加载、定时器调度、窗体显示/隐藏控制。

- [ ] **Step 2: 编写 `MainForm.UI.cs` (界面布局)**
  构建卡片容器、按钮事件绑定、60FPS 平滑缓动风琴折叠动画。

- [ ] **Step 3: 编写 `MainForm.Tray.cs` (托盘中枢)**
  NotifyIcon 实例、动态多设备右键菜单生成、单击切换与双击还原。

- [ ] **Step 4: 编写 `MainForm.Hardware.cs` (外设分发)**
  后台电量轮询回调、UI 状态更新、DPI/回报率硬件状态同步。消除在 Handle 未创建时调用 `BeginInvoke` 的隐患。

- [ ] **Step 5: 编写 `MainForm.Events.cs` (系统消息泵)**
  重写 `WndProc`，**核心重点实现 `WM_TASKBARCREATED` 处理：当收到该消息时立即调用 `ReinitializeTrayIcon()` 自动重新注册托盘图标**；处理 `WM_DEVICECHANGE` 和 Raw Input。

---

### Task 8: 构建系统升级与全局规则固化

**Files:**
- Modify: `D:\电量管家\build.bat`
- Modify: `D:\电量管家\AGENT_RULES.md`
- Modify: `D:\电量管家\.cursorrules`

- [ ] **Step 1: 更新 `D:\电量管家\build.bat`**
  将构建命令更新为：
  `"%CSC%" /target:winexe /optimize+ /codepage:65001 /win32icon:app.ico /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:FerrisPulse.exe /recurse:src\*.cs`

- [ ] **Step 2: 更新 `AGENT_RULES.md` 与 `.cursorrules`**
  正式录入架构地图、AI 维护契约与纯 D 盘构建规范。

- [ ] **Step 3: 执行首次模块化联合编译**
  运行 `cmd /c build.bat`，验证 `csc.exe` 在 C# 5 语法下顺利输出 `FerrisPulse.exe`，达到 0 报错、0 警告。

---

### Task 9: 全场景运行验证与交付闭环

- [ ] **Step 1: 验证常态运行**
  双击或终端运行 `FerrisPulse.exe`，主窗体正常展现，设备电量正常刷新。

- [ ] **Step 2: 验证开机自启动模拟**
  命令行运行 `FerrisPulse.exe --autostart`，主窗体保持静默隐藏，右下角托盘图标正常点亮。

- [ ] **Step 3: 验证托盘重启自愈机制 (`WM_TASKBARCREATED`)**
  通过 PowerShell 杀死并重启 `explorer.exe`：
  `Stop-Process -Name explorer -Force`
  等待任务栏重建完毕，验证 `FerrisPulse` 托盘图标是否 100% 自动重新恢复。

- [ ] **Step 4: 验证注册表与单实例互斥**
  检查 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 注册表项；二次启动程序，验证单实例互斥唤醒现有窗口而不会多开。
