# 📘 FerrisPulse · 灵脉 智能体实战操作与接力维护手册 (Agent Operational Handbook)

> **致所有接手本项目的 AI Agent 伙伴**：  
> 本手册凝聚了前序 Agent 在开发、重构、调试、测试与发布全生命周期中积累的全部实战经验与踩坑教训。  
> 请在动工前完整研读本手册；并在完成新功能、解决复杂疑难 Bug 或建立新规范后，**主动反哺更新本文档**，实现工程经验的代际传承与可持续演进。

---

## 目录
1. [项目定位与核心架构](#1-项目定位与核心架构)
2. [环境锚定与开发终端指令集](#2-环境锚定与开发终端指令集)
3. [C# 5.0 语法铁律与架构避坑宝典](#3-c-50-语法铁律与架构避坑宝典)
   - 3.1 编译器与语言等级严格锁定
   - 3.2 文件编码安全（UTF-8 with BOM）
   - 3.3 UI 状态同步与布局避坑（血泪教训）
   - 3.4 Windows 原生 DWM 弹窗动效、抗白框与坐标绝对记忆（核心避坑）
   - 3.5 Win32 键盘 HID 独占锁与权限陷阱（0 权限握手与双通道隔离）
   - 3.6 蓝牙外设与 Apple/Beats 生态避坑（真实名称与配对白名单）
   - 3.7 Windows 行终止符腐败陷阱（`\r\r\n` Double-CR Bug）
   - 3.8 社区开源贡献与代码署名礼仪
   - 3.9 新品牌 HID 适配实战（飞智 GPA6 / 迈从 G87 V2）
   - 3.10 社区硬件协议逆向实战（索尼 INZONE / 罗技 G502X / 怒喵 RACE / ATK 8K / 元派 P1）
   - 3.13 迈从 Z75S 共享 VID/PID 型号确认与 20 字节中断协议
4. [版本发布标准作业程序 (Release SOP)](#4-版本发布标准作业程序-release-sop)
   - 4.1 云端标准流水线发布流程
   - 4.2 “发布后本地留旧测新”实战法则 (Local Test vs Remote Release SOP)
5. [杀毒软件误报与数字签名防御体系](#5-杀毒软件误报与数字签名防御体系)
6. [测试驱动与自动化回归体系](#6-测试驱动与自动化回归体系)
   - 6.1 单元测试与 Mocking 规范
   - 6.2 自动化测试防锁死铁律（全内存反射加载，杜绝 Add-Type 句柄独占）
7. [智能体接力公约与知识飞轮机制](#7-智能体接力公约与知识飞轮机制)

---

## 1. 项目定位与核心架构

### 1.1 项目愿景
**FerrisPulse · 灵脉** 诞生于纯粹的极客初衷：**彻底干掉官方动辄 1GB、常驻 10+ 后台守护服务的臃肿驱动全家桶**（如 Razer Synapse 雷云、Logitech G HUB 等）。
提供纯原生、免驱级、常驻内存仅约 12MB 的跨品牌外设统一调度中心（原生支持雷蛇、罗技、雷柏、瓦尔基里、Apple/Beats、HyperX 等）。

### 1.2 模块分层结构
严禁单文件巨石化，所有代码严格遵守分层：
- `src/Core/`: 底层 Win32 P/Invoke (`NativeMethods.cs`)、注册表持久化 (`ConfigManager.cs`)、自启动管理 (`StartupManager.cs`)、程序入口 (`Program.cs`)。
- `src/Hardware/`: 品牌私有 HID 报文逆向解析与通信总线（Razer、Logitech、Rapoo、Valkyrie、Apple/Beats、HyperX 等）。
- `src/Models/`: 强类型数据模型与枚举 (`DeviceCategory`, `MouseBatteryInfo` 等)。
- `src/Services/`: 后台服务（全屏游戏探测 `AutoGameManager.cs`、自更新 `UpdateManager.cs`、提示音 `SoundFeedbackService.cs`）。
- `src/UI/`: 
  - `Forms/MainForm.*.cs`: 主窗体分部类体系（`MainForm.cs` 生命周期、`MainForm.UI.cs` 控件排版、`MainForm.Hardware.cs` 硬件轮询与事件响应、`MainForm.Tray.cs` 托盘渲染、`MainForm.Events.cs` 系统事件）。
  - `Forms/UpdateNoticeForm.cs`: 自更新弹窗。
  - `Forms/GameManagerForm.cs`: 游戏进程管理。
  - `Controls/`: 现代暗黑圆角发光控件体系（`SegmentedBar`, `CyberToggle`, `CyberDpiCard` 等）。

---

## 2. 环境锚定与开发终端指令集

### 2.1 工作区物理路径（唯一合法）
- **根目录**：`D:\FerrisPulse · 灵脉\`
- ⚠️ **路径陷阱注意**：
  - Windows 环境下路径含有中文字符与中点符号（`·`），在 PowerShell 中执行命令时，必须先设置控制台输出编码：
    ```powershell
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8;
    Set-Location -LiteralPath 'D:\FerrisPulse · 灵脉'
    ```
  - .NET 静态方法（如 `[System.IO.File]::ReadAllText`）默认依赖进程当前目录，在编写维护脚本时，必须使用绝对路径或 `(Resolve-Path "$PSScriptRoot\..").Path` 解析，严禁使用裸相对路径。

### 2.2 核心指令速查表

| 操作需求 | 执行命令 | 说明 |
| :--- | :--- | :--- |
| **纯净原生编译** | `cmd.exe /c "build.bat"` | 调用 C# 5.0 `csc.exe` 纯源码直编，产出 `FerrisPulse.exe` |
| **全量自动化测试** | `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1` | 执行 40+ 项测试用例，包含看门狗超时机制 |
| **BOM 编码强制校准** | `python tools/ensure_boms_safe.py` | 递归校准所有 `.cs` 和 `.ps1` 包含 UTF-8 BOM |
| **便携版压缩包打包** | `powershell -ExecutionPolicy Bypass -File tools/build_release_zip.ps1 -Version "2.1.1"` | 自动打包 `FerrisPulse_v2.1.1_Portable.zip` |
| **全自动双平台发布** | `powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version "2.1.1"` | 读取凭据、编译、校验、打 Tag、推 GitHub & Gitee |

### 2.3 Antigravity 平台终端路径注入陷阱与应对方案 (Platform Path Injection Bug & Workaround)
- **底层成因与报错特征**：
  在 Windows 操作系统中，若项目根目录包含中文、空格或特殊标点（如 Unicode 中点 `·`），Antigravity 2.0 平台在长对话上下文压缩（Compaction）或会话状态恢复时，注入给底层终端运行器（Backend Runner）的工作目录变量偶发被格式化为非法形态：
  ```text
  Command Working Directory: /"D:/FerrisPulse · 灵脉"
  ```
  （开头带有 POSIX 风格的前导斜杠 `/`，且两端包裹了双引号 `"`）。
- **导致后果**：
  当 Agent 调用 `run_command` 执行任何终端命令时，平台底层的宿主进程（Go/Node）在执行 `os.Chdir("/\"D:/FerrisPulse · 灵脉\"")` 时直接被 Windows 操作系统拦截，报错：
  ```text
  Command failed to run: /"D:/FerrisPulse · 灵脉": no such directory.
  ```
  **注意：命令在进入 PowerShell / CMD 之前就被拦截抛出，并非代码或脚本本身错误！**
- **智能体应对铁律**：
  1. ❌ **严禁盲目挂起与无休止重试**：一旦发现 `run_command` 抛出上述 `no such directory`，严禁通过循环或启动后台长效子 Agent 去重复执行该命令，否则子 Agent 会永久处于 Block / 卡死状态导致用户困惑。
  2. 💡 **透明沟通与指引本地构建**：直接向用户说明该平台底层路径注入限制，并引导用户在 IDE 终端执行 `cmd.exe /c "build.bat"` 或在文件资源管理器中直接双击根目录下的 `build.bat`。因为本地环境直接运行 `build.bat` 100% 走原生 Windows 环境，1 秒内即可完成编译生成最新的 `FerrisPulse.exe`。

---

## 3. C# 5.0 语法铁律与架构避坑宝典

### 3.1 编译器与语言等级严格锁定
- **编译器**：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- **语言标准**：严格 **C# 5.0**（严禁任何 C# 6.0+ 特性，否则编译立即报错）：
  - ❌ 禁止字符串插值：`$"Name: {name}"` ➡️ 必须使用 `string.Format("Name: {0}", name)` 或 `+` 拼接。
  - ❌ 禁止空条件运算符：`action?.Invoke()` ➡️ 必须使用 `if (action != null) action();`。
  - ❌ 禁止 `nameof(...)` ➡️ 必须使用硬编码字面量 `"propertyName"`。
  - ❌ 禁止表达式主体成员：`int Count => list.Count;` ➡️ 必须使用完整的 `{ get { return list.Count; } }`。
  - ❌ 禁止自动属性赋初值：`public int V { get; set; } = 1;` ➡️ 必须在构造函数中赋值。

### 3.2 文件编码安全（UTF-8 with BOM）
- 所有 `.cs` 文件**必须带有 UTF-8 BOM 签名**（`EF BB BF`），编译时 `build.bat` 严格携带 `/codepage:65001`。
- 修改代码后，如果不确定编辑器是否破坏了 BOM，运行 `python tools/ensure_boms_safe.py` 自动补全。

### 3.3 UI 状态同步与布局避坑（血泪教训）
1. **冷启动 0 尺寸防范**：
   - 动态卡片（如电竞 DPI 档位、电竞回报率按钮）在应用启动或设备切换时，必须显式调用 `RelayoutPerformanceControls()`。
   - 严禁让按钮保持在 `{Width=0, Height=0}` 状态，必须在展开或赋值后立即完成绝对坐标与尺寸计算。
2. **设备切换残留清理**：
   - 在 `UpdatePerformanceSection` 中切换设备时，必须最优先更新 `selectedDevice = info`。
   - 必须先彻底清空或隐藏非当前设备支持的控件，再重新挂载新设备的性能控件。
3. **音频播放防截断**：
   - 提示音播放不能依赖可能被垃圾回收或过早 Dispose 的短生命周期对象。
   - 多次连续播放需做好状态同步，避免高频切换时音频被强制中断。
4. **独立 X/Y 轴 DPI 布局与硬件生效徽标同步 (XY Independent DPI & Live Badge Sync)**：
   - **按钮呼吸感与自适应排版**：采用独立双轴后按钮文字由单值变为 `XXXX×YYYY` 格式（如 `3000×2250`）。主窗体基准宽度微调至 486px，性能卡片内部水平内边距设为 14px，标签列宽精简至 82px，档位间距设为 5px，使得 5 档模式下单按钮有效宽度由 52px 扩大至 57px+。
   - **`ModernSegmentButton` 强化防溢出绘制**：使用 `TextRenderer.MeasureText` 配合 `TextFormatFlags.NoPadding` 实时测算，当文字超长（如 11 字符 `16000×12000`）时，自动线性按比例等比缩放字号，严防碰撞按钮圆角边框。
   - **右上角硬件生效徽标 (`lblLiveRateBadge`) 同步**：在 `UpdatePollingRateDisplayStates` 中同步读取设备当前激活档位的 X 与 Y 轴数值，当处于双轴独立模式且 X≠Y 时，准确输出为 `● 硬件生效: XXXX Hz · XXXX×YYYY DPI`，与主界面及 OSD 保持全链路严格对齐。
5. **全屏游戏自动优化 OSD 防顶替机制 (Anti-Clobbering Game Optimization OSD)**：
   - **痛点根因**：进入游戏（或切回桌面）触发 `ApplyGameOptimization` 时，系统弹出包含双轴 DPI、回报率与游戏加速徽标的完整电竞 OSD（`ShowGameOptimization`）。但随后鼠标硬件完成 DPI 档位下发，后台 50ms 轮询线程 `DpiMonitorWorker` 探测到硬件数值变动，误认为是用户物理按键切换，触发 `OnDpiChanged` 调用单值 `ShowDpi`，直接将双开电竞卡片截断覆盖。
   - **三层立体防顶替方案**：
     1. **OSD 窗体层熔断**：`DpiOsdForm` 中，只要 `isDualOptimizationMode == true` 且处于展示计时周期内，`ShowDpi` 与 `ShowRate` 严格静默拦截返回，绝不破坏双开卡片。
     2. **业务分发层静默期**：引入 `lastGameOptOsdTime`，在游戏优化调度后 2.5 秒内，`OnDpiChanged` 自动跳过 `osdForm.ShowDpi` 的调用。
     3. **轮询状态预先对齐**：在 `ApplyGameOptimization`、`ApplyGameDpiStage` 与 `RestoreDesktopDpiStage` 发送硬件报文前，通过 `SyncMonitoredDpiState` 同步更新所有品牌的 `lastMonitored*Dpi` 与 `lastMonitored*Stage`，从源头杜绝 `DpiMonitorWorker` 产生虚假的“物理按键切换”硬件变更事件。

6. **全屏游戏双开 OSD 排版美化与视觉规范 (Dual Game Optimization OSD Visual Hierarchy)**：
   - **顶部标题无 Emoji 化与风格对齐**：原有的 `⚡`、`🍃` 在 GDI+ `Microsoft YaHei UI` 字体下会渲染为刺眼的乱码方块 `□`。参考单独回报率切换卡片（`isRateMode`）设计规范，去除 Emoji 符号，采用纯文字设计（进入游戏：“全屏电竞性能优化”；离开游戏：“常规桌面状态恢复”），字体为 YaHei Bold 9.2pt，色彩为典雅钛银色 `Color.FromArgb(195, 205, 225)`，居中对齐。
   - **数值与单位排版大字号强化**：原先双开卡片数值字号过小。将标准单值 DPI 与回报率字号提升至 `18.5f * dpiScale`，独立 XY 双轴字号提升至 `13.5f * dpiScale`（如 `3000×2250`），单位（`DPI`、`Hz`）放大至 `8.5f * dpiScale` 并沿数字底部基线对齐（`unitY = numY + h - uh - 1.2f`）。
   - **底部战术胶囊徽章去除冗余档位**：根据极简 HUD 规范，彻底移除`第几档`（如 `第 4 档 · `）冗余前缀，仅保留状态核心提示：“极速超低延迟已激活”与“桌面节能平稳模式”，并装载于深色战术胶囊微光徽章内（深色圆角背景、微妙边框与发光微点）。
   - **窗体尺度微调**：基准尺寸由 230×74 扩增为 248×76，保证 100%~200% Windows 系统缩放下双侧数值与底栏均具备呼吸感与充足留白。

### 3.4 Windows 原生 DWM 弹窗动效、抗白框与坐标绝对记忆（核心避坑）
这是经过多次迭代与用户深度对齐后沉淀的**黄金窗口状态机规范**：
1. **❌ 严禁使用 `AnimateWindow` 软件模拟动画**：
   - `AnimateWindow`（如 `AW_BLEND`）是 Windows 98 时代的 GDI 传统函数。在 Windows 10/11 的 DWM 硬件加速合成架构下，它会强行触发离屏内存 DC 的 GDI blit，在界面完全就绪前用系统默认白色笔刷擦除，**必然导致深色模式下闪现刺眼的白框（White Flash）**，且动画生硬卡顿，与系统原生动效割裂。
2. **❌ 严禁使用 `WindowState = Minimized` 作为隐藏状态**：
   - 如果窗口在托盘隐藏时被设为 `Minimized`，下一次调用 `ShowWindow(SW_RESTORE)` 时，Windows 会默认播放从**屏幕底部的任务栏 / Dock 栏飞出变形**的还原动画，造成严重的视觉拉扯感并导致窗口坐标乱飘。
3. **❌ 为什么单纯的 `this.Hide()` + `this.Show()` 没有弹窗动画？**：
   - 当同一个 Win32 窗口句柄（`HWND`）已经被创建并展示过一次后，后续的 `Show()` 仅被 DWM 视为已有窗口的可见性切换，DWM 会直接静态绘制，跳过系统的“Window Open（窗口开启）”居中缩放微光动画。
4. **✅ 终极解决方案：`this.RecreateHandle()` 窗口生命周期重塑**：
   - 在主窗体 [`MainForm.cs`](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/src/UI/Forms/MainForm.cs) 的 `ShowWindow()` 唤醒逻辑中：
     ```csharp
     if (!this.Visible)
     {
         try
         {
             this.RecreateHandle(); // 重建 Win32 原生句柄，重置 DWM 呈现状态机
             RestoreWindowPosition(); // 锁死上次关闭的原位坐标
         }
         catch { }
         this.WindowState = FormWindowState.Normal;
         this.Show(); // 触发全新窗口首次呈现，100% 播放与首次启动一致的原生 DWM 展开动画！
     }
     ```
   - **原理**：`RecreateHandle()` 销毁旧句柄并重新调用 `CreateWindowEx`。Windows DWM 将其判定为全新的顶级窗口诞生，在 `Show()` 瞬间**完美重现与软件首次冷启动完全一致的原生居中平滑缩放展开微光动画**！
   - **坐标绝对记忆**：`StartPosition` 固定为 `Manual`，句柄重建前后锁定 `savedWindowX` 与 `savedWindowY`，窗口严格在用户上次关闭它的原地展开，绝不从底栏乱飞。
   - **彻底阻断白框**：在 `MainForm.Events.cs` 中重写 `OnPaintBackground` 并拦截 `WM_ERASEBKGND`（直接返回 `(IntPtr)1`），系统浅色底色彻底无法介入绘制，全程暗黑纯色抗闪烁。
   - **句柄生命周期重绑定规范**：
     - 在 `OnHandleCreated` 中重新挂载 UIPI 消息豁免（`WM_SHOWME`, `WM_TASKBARCREATED`）、应用暗黑云角主题 `ModernThemeHelper.ApplyModernWin11Theme(this.Handle)`、注册硬件通知 `RegisterUsbNotification()` 以及重连鼠标休眠唤醒探测。
     - 在 `OnHandleDestroyed` 中妥善调用 `UnregisterDeviceNotification(hDevNotify)`，杜绝句柄泄露。

### 3.5 Win32 键盘 HID 独占锁与权限陷阱（瓦尔基里等键盘变“免驱”根因）
1. **Windows 内核 Keyboard 独占保护**：
   - Windows 底层 Raw Input 子系统为了安全防范键盘监听木马，对所有属于 Keyboard 集合的 HID 设备句柄强制实行独占锁。
   - 如果使用 `CreateFile` 打开键盘 HID 句柄时传入了 `GENERIC_READ (0x80000000)`，Windows 内核必定拒绝访问，返回错误代码 5 (`ERROR_ACCESS_DENIED`)！这会导致 `HidD_GetFeature` 报文握手中断，使原本支持读电量的键盘退化为“免驱在线”状态。
2. **黄金解法：0 访问权限握手**：
   - 打开键盘类 HID 节点时，首选访问权限必须设为 **`0`（即 `FILE_READ_ATTRIBUTES`）**！
   - 0 权限既不会触发 Windows 内核对键盘按键输入的独占冲突，又能自由收发 `HidD_GetFeature` / `HidD_SetFeature` 报文，键盘物理电量采集即刻恢复正常。
3. **键鼠同接收器独立通道隔离**：
   - 许多品牌（如瓦尔基里、雷蛇多模等）支持一个 2.4G 接收器同时连接键盘与鼠标。在 `DeviceManager` 中必须严格按 `Category`（`DeviceCategory.Mouse` 与 `DeviceCategory.Keyboard`）实行双通道隔离管理，严禁键鼠缓存互相覆盖。

### 3.6 蓝牙外设与 Apple/Beats 生态避坑（真实名称与防路人误扫）
1. **系统配对真白名单机制（杜绝路人陌生设备误扫）**：
   - Apple 设备（AirPods、Beats 等）通过 BLE 广播电池信息。如果仅按 BLE 信号扫描，会将周围几十米内所有路人的耳机全部扫描进库且无法删除。
   - 必须引入系统级经典蓝牙配对白名单（通过 `bthprops.cpl` 注册表与系统配对缓存验证），**仅允许本机 Windows 蓝牙设置中已正式配对的外设入库**，未配对的 BLE 广播一律无感过滤。
2. **过滤底层驱动 `Wireless iAP` 假名称**：
   - Windows 为 Apple/Beats 设备配对时，会在耳机物理 MAC 下枚举出一个名为 `Wireless iAP` 的底层协议子端点。如果直接读取子节点名称，耳机会变成 `Wireless iAP`。
   - 必须建立子端点黑名单，严密过滤 `Wireless iAP`、`iAP`、`Avrcp`、`Hands-Free` 等内部协议名，严格优先采用用户自定义的蓝牙重命名或型号官方全称，并在启动时自动清洗注册表历史脏数据。

### 3.7 Windows 行终止符腐败陷阱（`\r\r\n` Double-CR Bug）
1. **成因**：
   - 在 Windows 环境下使用 Python 脚本、PowerShell 字符串或跨平台工具进行正则替换与文件写入时，若换行符处理不当，容易将原本的 `\r\n` 误扩展为 `\r\r\n`。
   - 这会导致文件行数翻倍（每行之间多出一个空行），后续基于行号或精确字符块的替换工具彻底失效，甚至出现“AI 以为改了但实际上没改掉”的诡异现象！
2. **防范与清洗法宝**：
   - 发现代码行数异常激增时，第一时间用二进制检查是否存在 `b'\r\r\n'`。
   - 编写或运行 `python tools/ensure_boms_safe.py` 进行标准化清洗：`data.replace(b'\r\r\n', b'\r\n')`，并始终保持带签名的 `UTF-8 with BOM`。

### 3.8 社区开源贡献与代码署名礼仪
- 本项目致力于建立温暖、开放的极客开源生态。对于外部群友或社区开发者贡献的驱动代码或逆向协议（如 HyperX 飓风三无线耳机由群友 **“rm -rf /*”** 贡献），必须在代码注释、更新日志 `CHANGELOG.md` 及发布公告中妥善鸣谢，尊重每一位参与者的付出。

### 3.9 新品牌 HID 适配实战（飞智 GPA6 / 迈从 G87 V2）
1. **飞智 (Flydigi) APEX 6 / APEX 6 Pro / Vader 5 Pro**：
   - VID/PID：`37D7:2502`（APEX 6 系列）、`37D7:2401`（Vader 5 Pro）；配置通道为 UsagePage `0xFFA0` 的 Vendor 集合，输入/输出均为 33 字节（前导 0 Report ID + 32 字节 GPA6 私有帧）。
   - 帧格式：`5A A5 01 02 ...`，命令 `0x01` 读取设备信息；帧内 `[31]` 为 `[2..30]` 的 8 位和。回复从报告第 5 字节开始为 payload，`payload[6]` 低 4 位是电量档位、高 4 位是充电标志。
   - **电量刻度不可照搬 APEX 5**：APEX 6 实测 `0..10` 档（10 = 100%），APEX 5 为 `0..5` 档。`FlydigiDeviceHelper.ParseInfo` 采用 `<=5 → ×20`、`<=10 → ×10` 的兼容映射，避免把满电 10 误显示为 10%。
   - 必须用重叠 I/O + 超时取消读取；直接同步 `ReadFile` 在无回复时会永久阻塞 UI 轮询线程。
2. **迈从 (MCHOSE) G87 V2 三模机械键盘**：
   - VID/PID：`3837:3033`；配置通道优先选择 UsagePage `1` / Usage `0` 的 65 字节集合（MI_01），不要误开键盘按键集合（`\kbd`，写 LED 会被 Windows 拒绝访问 5）。
   - **正确通道（实机确认）**：G87 V2 固件不响应 Web 端的 `0x4A/0x87` 电池查询；官方 M HUB 通过 **性能配置查询** 读取电量。MI_01（UsagePage `1`/Usage `0`）为未编号报告，Windows 写入缓冲区必须为 `[0x00] + 64 字节`。
   - 查询帧：`55 05 00 38 38 00...`（64 字节，补前导 Report ID 0 后共 65 字节）。回复：`AA 05 ...`；**offset 40 = rf_battery（0..100），offset 41 = isCharging（0/1）**。本机实测 offset 40=`0x15`=21%，与官方 M HUB 显示 20%（UI 按 10% 精度向下取整）一致。
   - 官方 M HUB 的 `getBattery` 命令表（`13 4A...`）只适用于部分旧固件/鼠标；G87 V2 必须用 `0x55` 性能帧。不要因为 Web 端存在 `getBattery` 就假设键盘可用。
   - 未查通时**严禁把设备标成离线**：接收器与键盘在线就应显示在线、电量未知（有缓存才显示缓存值）；更不能伪造 `-1%` 为“已识别电量”。`MchoseDeviceHelper` 现已使用 `0x55` 性能帧并实机读到 21%。
   - `3837:3033` 必须在 `UniversalGamingDeviceHelper` 中显式排除，否则会被通用鼠标协议误当成 64 字节 Feature 设备探测。
3. **通用基础设施**：新品牌统一复用 `HidTransport.cs`（SetupAPI 枚举 + HidD 属性/能力 + 重叠 I/O），不要在每个 Helper 中重复声明 P/Invoke；所有 64/65 字节报告写入前必须按 `HidP_GetCaps` 的 `OutputReportByteLength` 补齐，否则 `WriteFile` 返回 `ERROR_INVALID_PARAMETER (87)`。

---

### 3.10 社区硬件协议逆向实战（索尼 INZONE / 罗技 G502X / 怒喵 RACE / ATK 8K / 元派 P1）

#### 1. 索尼 (Sony) INZONE Buds 真无线降噪电竞耳机 (WF-G700N, `054C:0EC2`)
- **硬件拓扑与端点发现**：
  - INZONE Buds 通过 2.4GHz USB-C 适配器与 PC 通信，在 Windows 下枚举多个 HID 集合。必须优先绑定 UsagePage `0xFF04`（Vendor-Defined Audio/Dongle 配置通道），输入/输出报文长度均为 64 字节。
- **64 字节 Output Report `0x02` 查询帧**：
  - 构造 `[0x02, 0x96, 0xC3, 0x41, 0x04, 0x01, SeqLo, SeqHi, 0x00...0x00, Checksum]`。
  - 序列号为自增 16 位整型；校验和计算公式为 `(0x96 + 0xC3 + 0x41 + 0x04 + 0x01 + SeqLo + SeqHi) & 0xFF`，写入尾字节 `[63]`。
- **64 字节 Input Report `0x02` 应答解析**：
  - 校验有效性：`sum(rx[6..18]) & 0xFF == rx[19]`。
  - 左耳塞电量：`rx[7]` 为百分比（`0..100`），`rx[8]` 为充电标志（非零为充电中）；若为 `0xFF` 则代表左耳在盒中断开。
  - 右耳塞电量：`rx[11]` 为百分比（`0..100`），`rx[12]` 为充电标志；`0xFF` 为在盒中断开。
  - 充电盒电量：`rx[15]` 为百分比（`0..100`），`rx[16]` 为充电标志。
  - **三态智能降级机制**：双耳在耳时优先取最低电量保障预警；单耳入盒时优雅降级显示在耳耳塞电量；仅充电盒在位时显示盒子电量；全部入盒合盖休眠且收发器静默时，UI 显示休眠占位状态，杜绝误报设备丢失。

#### 2. 罗技 (Logitech) G502 X PLUS 与 Lightspeed (C547) 接收器深度重构
- **多端点优先级锁定（解决 WriteFile 报错）**：
  - 罗技 Lightspeed `C547` 接收器在 Windows 下同时暴露 7 字节短集合（`0xFF00/0x0001`）与 20 字节长集合（`0xFF00/0x0002`）。向 7 字节短集合写入 HID++ 报文必然触发 `WriteFile` 错误。必须枚举并优先选定 20 字节通信端点。
- **HID++ 2.0 Ping 命令规范化**：
  - 在 HID++ 2.0 中，`byte 3 = (function << 4) | swid`。根特性函数 0 是 `getFeature`，根特性函数 1 才是 `getProtocolVersion`。标准 Ping 命令必须设 `txPing[3] = 0x18`（函数 1 + swid 8），并携带特征 Token `0x5A`，否则 G502 X 等新固件直接无响应。
- **设备名称偏移纠正**：
  - `0x0005`（DeviceNameType）特性的 `getDeviceName`，实际字符从 `byte 4` 开始存储（而非 `byte 5`）。若从第 5 字节读取会导致首字母丢失（如变成 `502 X PLUS`）。从第 4 字节读取完整恢复 `G502 X PLUS`。
- **统一电量特性 `0x1004` 解析**：
  - `byte 4` 为精确电量百分比（0..100）；`byte 5` 为离散电量级别（1..4）；`byte 6` 才是充电状态（1: 充电中, 2: 充满, 3: 未充电/放电）。严禁把第 5 字节误当成布尔值。
- **并发保护与接收器解耦**：
  - 采用 `(matchDevIdx, matchFeatIdx, matchSwid)` 三元组与截止时间匹配机制，杜绝 G HUB 后台并发通信干扰；解耦接收器硬编码，避免通用 Lightspeed 设备名称被覆盖。

#### 3. 怒喵 (Angry Miao) AM Infinity .97 客制化无线协议
- **RACE 62 字节协议**：
  - 2.4G 接收器 (`0E8D:0703`) 与有线模式 (`0E8D:0880` / `35A1:0035`) 采用 62 字节 Feature 报文。
  - 发送 Report ID `0x14`，前置包头 `0x05, 0x5A, 0x01, 0x00, 0x02`，查询指令 `0x30CF`。
  - 应答读取当前电量值与充放电状态，配合官方暗金品牌色呈现。

#### 4. ATK 8K NANO 与元派 (Metapanda) P1 WE 协议升级
- **8K 接收器 Output/Input Report 异步回退**：
  - 部分超高回报率（4000Hz/8000Hz）接收器（如 ATK A9 AIR / X1 V2 的 8K NANO 接收器 `373B:1279` / `12E9`）禁用了 64 字节 `HidD_SetFeature`，调用必然失败。
  - 必须升级 `QueryBekenWeBattery`：当 Feature 方式失败时，自动回退到 17 字节 Output Report（`WriteFile`）发送 `0x05, 0x11, 0x00, 0x00, 0x08, 0x01` 并通过重叠 I/O `ReadFile` 读取 17 字节 Input 应答，解析 `rx[7]` 获得真实电量。元派 P1（`3554:F5A9` / `F5AB` / `F5AC`）全模组完美兼容此逻辑。

#### 5. 迈从 (MCHOSE) A5 V2 Ultra 三模鼠标私有 WebHID 协议
- **0x11 短报文身份与电量逆向（异或 0xFF 掩码通信）**：
  - 设备过滤：UsagePage `0xFF01`（首选）或 `0xFF0B`（备用），FeatureReportByteLength >= 21。
  - 向 Report ID `0x11` 发送 `CMD_IDENTITY (0x06)`，所有 payload 字节均做 `^ 0xFF` 掩码反转；
  - 设备回复 Report ID `0x11`，状态码 `0xF9`：`[11] ^ 0xFF` 为物理电量百分比（`0..100`），`[12] ^ 0xFF` 为充电中标志（`1` 为充电中），`[10] ^ 0xFF` 低 3 位为模式（`0: 有线, 1: 2.4G, 2: 蓝牙`），第 3 位为连接在线状态。
- **0x12 长报文 64 字节板载 DPI 与回报率引擎**：
  - 发送 `CMD_CONFIG_READ (0x67)` 读取 63 字节配置：提取当前生效档位、6 组 X 轴与 Y 轴独立 DPI（`DpiX[i]` 位于 offset 4，`DpiY[i]` 位于 offset 51）；
  - 修改后发送 `CMD_CONFIG_WRITE (0x57)` 写入并回读验证；通过 `CMD_RATE (0x41)` 实现 125~8000Hz 超高回报率切换。
  - 若 0x12 长通道偶发休眠，发送 `CMD_PROFILE_SELECT (0x58) + 0x00` 唤醒重试。

---

### 3.11 无线电竞耳机协议突破（赛睿 Nova 5 / 雷蛇旋风黑鲨 / 罗技 G PRO X）

无线游戏耳机（Headset）在 Windows HID 栈中常具备与普通鼠标截然不同的拓扑特征，接手维护的 Agent 必须警惕以下三大典型陷阱：

1. **雷蛇旋风黑鲨 V2 Pro (BlackShark V2 Pro 2.4, `1532:0555`) 零 Feature Report 陷阱**：
   - 该机型的 `FeatureReportByteLength == 0`！直接调用 `HidD_SetFeature` 或走现有 90 字节 Feature Report 链路必定返回 `ERROR_INVALID_PARAMETER`。
   - **解法**：必须通过 `UsagePage == 0xFF00` 的中断集合，使用 `WriteFile` 异步写入 64 字节命令帧（格式：`02 80 08 00 00 'P' 'A' 08 00 03 <CMD> 00 ...`），随后通过 `ReadFile` 异步读取 `02 <len> 'P' 'I' ...` 应答帧。命令字包括 `0x21` 电量、`0x2A` 充电、`0x20` 在线状态。
2. **赛睿 Arctis Nova 5 / 5X (`1038:2232` / `1038:2253`) 专属 0x00B0 报文状态机**：
   - 该耳机采用 Vendor UsagePage `0xFFC0`，向 Dongle 发送 65 字节中断报文 `00 B0`，无线基站回传状态。
   - `rx[2]` 为离线标志（`0x02` 代表耳机未开机/离线，此时接收器在位但需优雅回退最后已知电量，禁止抹零），`rx[4]` 物理电量（0~100），`rx[5]` 为充电标志（`0x01` 充电中）。
3. **罗技 G PRO X Wireless (`046D:0ABA`) 非标准电量通道（0x1F20 ADC 毫伏放电曲线）**：
   - 该耳机不支持标准 HID++ `0x1000` / `0x1004` 特性，电量来自 HID++ 4.2 特性 `0x1F20 ADC_MEASUREMENT`。
   - 通信位于 `UsagePage 0xFF43`（20 字节短报文通道），先 Ping 根节点激活，再查询 `0x1F20` 特性索引，发送读取 ADC 指令，得到原始毫伏读数（`ra[4] << 8 | ra[5]`，范围在 2500mV ~ 4300mV 之间）。
   - 通过 12 阶高精度锂电池开路放电电压曲线（3500mV 对应 0%，4200mV 对应 100%）实施分段线性插值计算，精准映射出 0%~100% 百分比。

---

### 3.12 FDAS 全品牌通用硬件适配与现场探针逆向机制

为了让 FerrisPulse 能够驱动全球任意品牌、任意芯片主控（Compx、PixArt、SinoWealth、Nordic、Beken、Realtek、WCH 等）且**不改动一行 C# 代码**，FDAS v1.0 架构确立了以下核心工程范式：

1. **现场探针工具 (`tools/probe_hid_device.ps1`)**：
   - **零依赖与全内存反射**：采用 `[System.IO.File]::ReadAllBytes` 装配 `FerrisPulse.exe`，严禁 `Add-Type -Path` 产生文件句柄互斥锁死。
   - **三级权限降级与端点自适应**：自动按 `GENERIC_READ | GENERIC_WRITE` -> `GENERIC_WRITE` -> `0 (FILE_READ_ATTRIBUTES)` 尝试打开设备句柄，键盘端点通过 0 权限避开 Windows 内核防监听独占锁。
   - **双模通信测试**：支持 `-ReportType Interrupt`（WriteReport/ReadReport）与 `-ReportType Feature`（HidD_SetFeature/HidD_GetFeature），自动为出入缓冲区做长度对齐与排空。
   - **快速探测命令**：
     - 全局扫描：`powershell -ExecutionPolicy Bypass -File tools/probe_hid_device.ps1 -ScanAll`
     - 单设备下发测试：`powershell -ExecutionPolicy Bypass -File tools/probe_hid_device.ps1 -VendorId 0xXXXX -ProductId 0xXXXX -TxHex "08 02 ..."`

2. **任意 $N$ 字节长度与“静态穿透”解耦哲学**：
   - **端点长度解耦**：不再对 17/32/64 字节设限，Win32 物理通道严格按硬件端点声明的物理报告长度（任意 $N$ 字节）开辟缓冲区。
   - **静态穿透无需算校验和**：市面上 90% 外设的电量查询报文是固定握手字节。Agent 在探针中实测有效的完整 Hex 直接填入 `queryBatteryTxHex`，并将 `checksumType` 指定为 `"None"`，引擎原样透传下发，杜绝因未登记校验算法而阻断驱动。
   - **动态调节模板占位符**：仅在调节 DPI / 回报率时才动态生成指令，引擎提供 `{stage}`, `{stage0}`, `{dpiX_lo}`, `{dpiX_hi}`, `{rateIndex}`, `{rateCode}` 等宏占位符及常见校验和（`VgnCompxSum55`, `SumMod256`, `XorSum`）计算。

3. **解除一切电源状态偏见（客观物理状态）**：
   - 严禁在提示词或代码中假定“2.4G 模式下绝不可能充电”。
   - 现代无线外设处于 2.4G 连接的同时，完全可能放于无线充电座、磁吸座或插着 USB 充电线。充电判定必须严格由 `chargingByteOffset` 与 `chargingBitMask` 的物理实测位为准。

4. **CompX (0x3554) / VGN / ATK / VXE 无线接收器会话唤醒与 EEPROM 读写体系（血泪避坑）**：
   - **接收器 RF 网关状态门禁（Driver Active Handshake）**：
     CompX 2.4G 接收器（VID `0x3554`，如 VGN Dragonfly F2 Pro Max / ATK 系列）内部有驱动状态机。在 PC 驱动未向其下发 `Cmd 0x02`（`08 02 00 00 00 01 01 00 00 00 00 00 00 00 00 00 49`）前，无线接收器拒绝将 RF 报文转发给无线鼠标！下发后接收器建立空中会话，电量查询 `Cmd 0x04` 方可成功回显。
   - **DPI 与回报率调节采用 EEPROM 内存映射写入（Cmd 0x07）**：
     CompX 固件不支持孤立的虚拟命令 ID。所有配置调节均为 EEPROM 内存映射读写：
     - 写入命令：`0x07`，Report ID: `0x08`。
     - 回报率：地址 `0x0000`，长度 2 字节，数据为 `[rateCode, (0x55 - rateCode) & 0xFF]`。
       - 编码：125Hz=`0x08`, 250Hz=`0x04`, 500Hz=`0x02`, 1000Hz=`0x01`, 2000Hz=`0x10`, 4000Hz=`0x20`, 8000Hz=`0x40`。
     - 激活档位：地址 `0x0004`，长度 2 字节，数据为 `[stage0, (0x55 - stage0) & 0xFF]`。
     - 档位 DPI 数值：地址 `0x000C + stage0 * 4`，长度 4 字节，数据为 `[low, low, flags, (0x55 - low - low - flags) & 0xFF]`，其中 `encoded = (dpi / 50) - 1`，`low = encoded & 0xFF`，`high = (encoded >> 8) & 0x03`，`flags = (high << 2) | (high << 6)`。
   - **FDAS 运行时防电量虚标铁律（杜绝回退假样本）**：
     在 2.4G 模式下，设备休眠或超时未回显时，引擎必须保持最后真实读数 `LastBattery`，若从未读取过则置为 `-1`（标记未连接/休眠），**严禁直接回退到 `sampleBattery`**，否则会导致界面永久显示虚假固定的 85% 电量。

5. **FDAS 自适配鼠标在 UI 动效与布局中的统一门禁 (`IsSelectedDeviceSupportingAutoGameRate`)**：
   - 过去性能卡片高度调节 `UpdatePerformanceCardHeightForCurrentDevice`、控件排版 `RelayoutPerformanceControls`、回报率状态点亮 `UpdatePollingRateDisplayStates` 等处散落着硬编码品牌列表（如 `Brand == "Razer" || "Logitech" ...`）。当用户导入第三方 FDAS 鼠标（如 VGN 蜻蜓）后，点击“全屏游戏自动优化”时，卡片高度控制器误判设备不支持回报率，从而跳过重排与动画，导致卡片卡死在展开状态、无法平滑拉伸或收起。
   - 现已收敛为统一判定 `IsSelectedDeviceSupportingAutoGameRate()`，强制纳入 `FdasRuntimeHelper.IsFdasMouse(dev)`，确保所有自适配鼠标在性能卡片展开 (255px) 与收回 (148px) 时拥有完整原生动效与子控件精准隐藏。

6. **动态微表达式引擎 (`ExpressionEvaluator.cs`) 与开源社区生态飞轮**：
   - **彻底废弃宏观硬编码**：严禁将某类芯片或品牌在 C# 中硬编码为所谓“芯片族大一统驱动”，硬件固件千差万别，强行做宏观归类依然会冲毁其他设备的配置扇区。
   - **微表达式动态求值**：FDAS v1.0 架构内置纯内存微表达式求值器，支持在 JSON 的 `protocol.variables` 中声明派生计算（如 `"(0x55 - $rateCode) & 0xFF"`、`"($dpi / 50) - 1"`、`"$dpiHigh << 2"` 等）或直接在模板中使用内联表达式 `{ (0x55 - rateCode) & 0xFF }`，实现零编译、纯配置热加载驱动任意奇葩算法。
   - **硬件安全第一与探针写保护锁**：严禁在下位机真机上盲测写指令（如 `Cmd 0x07` 写未知地址），以防冲毁 2.4G 接收器 RF 配对表导致变砖。探针工具 `tools/probe_hid_device.ps1` 内置物理安全锁，默认拦截未经授权的高危 EEPROM 写入。
   - **首选官方 WebHID 源码逆向**：国内 90% 厂商均提供基于 Chrome WebHID 的网页驱动，按 F12 检索 `sendReport`、`write` 即可直接提取官方原厂验证过的 100% 安全算法。
   - **双轨制逃生舱与生态反哺**：若遇到极特殊协议超出 JSON 表达能力，授权 Agent 直接修改本地 C# 源码并 `build.bat` 验证；并引导用户将成果通过 GitHub/Gitee Issue 或直连作者邮箱（`2949225840@qq.com` / `2949225840zpf@gmail.com`）反哺，收录为官方原生预设。

---

### 3.13 迈从 Z75S：共享 VID/PID 型号确认与 20 字节中断协议

- **只适配实测组合**：Z75S 2.4G 接收器为 `258A:01BB`，厂商字符串可能是 SINOWEALTH，产品字符串可能乱码。VID/PID 被 Z75 等型号复用，不能凭白名单或泛化芯片家族协议直接命名。
- **型号握手先行**：配置集合为 UsagePage `FF02` / Usage `0002`，Input/Output 均为 20 字节，Feature 为 0。`13 05 01 00 ... 19` 查询，回复 payload 必须为 `0A 00 00 00 00 09` 才是 Z75S；Z75 的末字节为 `01`，应拒绝。初次无型号回显时不猜测设备；仅已验证的接收器可用其缓存作休眠占位。
- **正确报告长度与权限**：Z75S 缓冲区的第 0 字节已是 Report ID `13`，总长 20；不能照搬 G87 V2 的 65 字节路径或额外补 0。SetupAPI 元数据探测保持访问权限 `0`；只有厂商配置集合通过中断 R/W 通信，不打开 Usage `1/6` 的按键输入集合。访问权限 `0` 与 Win32 `FILE_READ_ATTRIBUTES` (`0x80`) 是不同的值，现有文档中的括注不能当作数值定义。
- **电量与充电分离**：TX `13 4A 01 00 ... 5E`；RX 字节 5 是 `0..100` 百分比，字节 6 **高半字节是充电，低半字节是充满**（官方 little-endian 位解析）。实测插线为 `0x10`，拔线满电为 `0x01`，所以满电不能推导充电中。
- **验包与限频**：中断读取使用 `HidTransport.ReadReportExact`，先要求 Win32 实际收到完整 20 字节，再验证 Report ID、命令、总包数 1、序号 0、载荷长度、19 字节累加校验和及电量范围；一次握手+电量查询共用 320ms 截止时间，最多读取三帧以过滤无关响应；成功、失败均推进 5 秒节流，句柄在 finally 关闭。
- **短包补零陷阱**：旧 `ReadReport` 返回分配的整块缓冲区，实际短读会被补零；例如 7 字节 `13 4A 01 00 02 00 A0` 补零后会伪造合法校验和，误报 0%/充电。固定长度协议应使用严格 API，保留旧调用者语义。测试用本地双向管道覆盖真实 Win32 I/O 边界，不能只测试直接传短数组的 parser。
- **缓存隔离**：Z75S 使用 `MchoseCache\Z75S\<实例标识编码>`，与 G87 根缓存隔离；保存型号 token、端点、电量和原始采样时间。超时保留最后真实读数并标记休眠，未知为 `-1`，禁止使用 G87 缓存或制造 100%。通用鼠标探测必须排除 `258A:01BB`。
- **界面也须验证边界**：选择器与托盘的未知判断应为 `<0`，真实 `0%` 仍须显示；非 NuPhy 键盘的 `100%` 不应套用其充电推断。键盘充电位需传给托盘图标；休眠缓存停止充电动画，并保留旧采样时间。NuPhy 专属硬件写菜单只能向 NuPhy 展示。
- **编译产物与现用副本一致性**：工程根目录编译成功不代表用户现用 EXE 已升级。遇到“测试能识别、界面找不到”时，用 `Get-CimInstance Win32_Process` 核对实际 `ExecutablePath/CommandLine`、新旧 SHA256 与快捷方式目标；全局单实例可能只唤醒仍在运行的旧版。便携版可在退出旧进程后备份并替换原启动入口，保留配置。用户自定义名称可能仍是“迈从键盘”，需从顶部设备名称区域切换查看。
- **测试与状态保护**：读取 EXE 必须使用字节数组反射。全量测试包含修改真实自启动注册表/快捷方式的历史测试，运行前先将相关值类型、内容和文件字节完整落盘备份；恢复 Binary/MultiString 时显式转回 `byte[]`/`string[]`，不能把 PowerShell 展开的 `object[]` 传给 Registry.SetValue。某项恢复失败后仍需继续恢复其余项并保留备份。

完整协议、实测字节和贡献说明见 [MCHOSE_Z75S_ADAPTATION.md](MCHOSE_Z75S_ADAPTATION.md)。

---

## 4. 版本发布标准作业程序 (Release SOP)

发布新版本是一项严密的工程操作，任何 Agent 在执行发布时必须严格遵循以下标准程序：

### 4.1 云端标准流水线发布流程 (Standard Release Pipeline)

#### 步骤 1：版本契约四要素对齐
1. **`src/Properties/AssemblyInfo.cs`**：
   ```csharp
   [assembly: AssemblyVersion("2.1.1.0")]
   [assembly: AssemblyFileVersion("2.1.1.0")]
   [assembly: AssemblyInformationalVersion("2.1.1-Release")]
   ```
2. **`version.json`**：
   - 更新 `latestVersion`（如 `"2.1.1"`）。
   - 更新 `releaseDate`（当天日期，如 `"2026-10-20"`）。
   - 更新 `downloadUrl` 对应版本号。
   - 维护 `changelog` 数组（分为“功能与体验革新”、“品牌协议深度融入与外设适配”、“稳定性与问题修复”，无前置序号，表达精炼）。
3. **`CHANGELOG.md`**：
   - 顶部追加 `## 🚀 [vX.Y.Z] - YYYY-MM-DD` 详细更新日志。
4. **Git Tag**：
   - 保持标签命名为 `vX.Y.Z`。

#### 步骤 2：纯净编译与 Hash/大小计算
```powershell
# 杀掉残留进程并编译
Stop-Process -Name "FerrisPulse" -Force -ErrorAction SilentlyContinue
cmd.exe /c "build.bat"

# 获取新文件的真实哈希与大小并同步回 version.json
$sha256 = (Get-FileHash -Path "FerrisPulse.exe" -Algorithm SHA256).Hash.ToLower()
$fileSize = (Get-Item "FerrisPulse.exe").Length
```

#### 步骤 3：全量回归测试验证（一票否决制）
必须运行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`，确保全部测试用例 **100% PASS**（40+ 项全绿），方可进入发布。

#### 步骤 4：自动化流水线发布
直接调用发布脚本：
```powershell
powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version "2.1.1" -Title "FerrisPulse · 灵脉 v2.1.1 正式版"
```
脚本将全自动完成：Git 提交 ➡️ 打 Tag ➡️ 推送 GitHub & Gitee ➡️ GitHub Releases 资产上传 ➡️ Gitee Releases 资产上传。

### 4.2 “发布后本地留旧测新”实战法则 (Local Test vs Remote Release SOP)

> 💡 **核心痛点与需求背景**：  
> 每次新版本（如 v2.1.0）成功发布上线后，用户或测试者需要第一时间在本地验证应用内自更新功能（启动检测 -> 弹出更新提示框 -> 点击立即更新 -> 后台静默下载与哈希校验 -> 替换可执行文件并优雅重启）。  
> 如果发布完成后，本地的 `FerrisPulse.exe` 也是最新版 v2.1.0，软件启动检测发现本地版本等于远程最新版本，会直接提示“已是最新版本”，导致**完全无法在本地进行端到端的热更新链路实测**！

**为此确立“本地留旧测新”铁律：**

1. **发布后本地主动回滚版本**：
   - 云端发布完成后，**必须将本地 `src/Properties/AssemblyInfo.cs` 恢复为上一稳定版本（例如 `2.0.4.0`）**。
   - 重新执行 `cmd.exe /c "build.bat"`，在本地编译出一个版本号为 `2.0.4.0` 的 `FerrisPulse.exe`。
2. **本地 Git 工作区保持脏状态隔离**：
   - 本地 `src/Properties/AssemblyInfo.cs` 保持在修改为旧版的状态，**不要 `git commit`**。
3. **端到端用户自测闭环**：
   - 用户双击运行本地 `FerrisPulse.exe`（v2.0.4.0）。
   - 软件向云端请求 `version.json`，识别到新版 v2.1.0。
   - 触发 `UpdateNoticeForm` 弹出更新卡片，展示新版特性。
   - 用户点击“立即升级”，体验下载进度、校验与无缝重启至 v2.1.0。
4. **测试通过后的最终归档**：
   - 待用户人工验证更新全链路 100% 成功后，后续 Agent 若需继续日常开发，再根据指令重新对齐 `AssemblyInfo.cs`。

---

## 5. 杀毒软件误报与数字签名防御体系

### 5.1 误报核心成因
- **缺少商业 EV 证书**：未签名的二进制文件从公网下载，SmartScreen 和 Defender 默认信誉为零。
- **底层硬件操作特征**：直接读写 HID、扫描游戏窗口句柄、注册表自启动、原子自更新替换。

### 5.2 软件代码防御（防红叉崩溃）
- 在 `UpdateManager.cs` 和 `UpdateNoticeForm.cs` 中，对所有下载与文件校验逻辑实施安全拦截。
- 捕获 `0x800700E1 (ERROR_VIRUS_INFECTED)` 时，严禁将异常抛给未处理的全局异常处理器（避免弹红叉堆栈），而是优雅提示：
  *“⚠️ 更新文件被系统安全软件误拦截。软件完全开源安全，请在安全中心放行，或手动下载便携版 Zip。”*

### 5.3 官方白名单与证书通道
1. **微软安全情报提交 (WDSI)**：
   每次发布重大版本，访问 [Microsoft Defender Security Intelligence](https://www.microsoft.com/en-us/wdsi/filesubmission)，以 "Software developer" 身份提交 `FerrisPulse.exe`，通常 24 小时内全网 Defender 白名单放行。
2. **SignPath.io 权威开源签名体系**：
   - 本项目已配置 GitHub Actions 自动签名流水线：`.github/workflows/build-and-sign.yml`。
   - 依赖的仓库 Secrets：`SIGNPATH_API_TOKEN` 与 `SIGNPATH_ORGANIZATION_ID`。
   - 每次打 Tag 推送，SignPath 硬件云端自动签名，彻底解决客户端信任问题。

---

## 6. 测试驱动与自动化回归体系

### 6.1 单元测试与 Mocking 规范
- **测试用例存放位置**：`tests/test_*.ps1`
- **编写原则**：
  - 任何针对 Bug 的修复或新功能开发，必须附带一个能精确复现并验证修复的独立 PowerShell 测试脚本。
  - 严禁依赖 GUI 人工肉眼点击；全部通过反射（Reflection）注入 Mock 数据，断言窗体内部控件状态、坐标与尺寸。
  - 所有测试脚本在 `tools/run_all_tests.ps1` 中由 8 秒看门狗守护，防止测试脚本意外死循环卡死流水线。

### 6.2 自动化测试防锁死铁律（全内存反射加载，杜绝 Add-Type 句柄独占）
- **致命陷阱**：
  - 若在 PowerShell 测试脚本中直接使用 `Add-Type -Path "FerrisPulse.exe"` 加载被测程序集，当前 PowerShell 进程将与操作系统建立该二进制文件的强互斥文件句柄锁（Handle Lock）。
  - 这会导致后续重新运行 `build.bat` 时，`csc.exe` 抛出毁灭性编译错误：  
    `CS0016: Could not write to output file 'FerrisPulse.exe' -- 'The process cannot access the file because it is being used by another process.'`
  - 必须手动杀死整个 PowerShell 控制台才能解锁，严重破坏本地高频迭代与 CI/CD 流程。
- **黄金解决方案：全内存反射加载 (Byte-Array Reflection Load)**：
  - 所有 PowerShell 测试脚本加载 `FerrisPulse.exe` 时，必须使用字节流一次性读取到内存中，再通过内存反射装配，零文件句柄占用：
    ```powershell
    $exePath = (Resolve-Path "$PSScriptRoot\..\FerrisPulse.exe").Path
    $bytes = [System.IO.File]::ReadAllBytes($exePath)
    $assembly = [System.Reflection.Assembly]::Load($bytes)
    ```
  - 这样文件被读取完后文件句柄瞬间释放，后续无论编译多少次都不会产生任何进程冲突或锁死。

---

## 7. 智能体接力公约与知识飞轮机制

为了保证任何 Agent 接手项目后能无缝协同，特订立以下**四大行动公约**：

1. **上手必读，以终为始**：
   任何新 Agent 接手本对话或工作区时，**第一件事必须阅读 `AGENTS.md` 和本手册 (`docs/AGENT_HANDBOOK.md`)**，快速对齐架构认知与铁律约束。
2. **零垃圾文件残留 (Zero Scatter Policy)**：
   根目录下严禁散落任何临时测试文件（`scratch/` 脚本用完即删，或移入 `tools/` 正式归档）。任务完成前必须执行 `git status` 确认工作区纯净。
3. **零二进制提交 (Zero Binary in Git)**：
   严禁将 `FerrisPulse.exe`、`*.zip` 等编译产物使用 `git add` 提交到仓库历史中，保持 Git 仓库极致轻量。
4. **知识反哺制度 (Living Knowledge Flywheel)**：
   当你在后续开发中：
   - 发现并攻坚了一个新的 Windows 系统/驱动 Bug；
   - 接入了新的外设品牌协议（如达摩鲨、蜻蜓、魔导等）；
   - 优化了打包、签名或发布流水线；  
   **你必须主动修改本手册，将最新的经验、坑点与应对策略记录下来**，让项目的智慧代代相传，真正实现可持续演进！

---

## 8. FDAS v1.0 外设适配规范与智能体自动化融合流水线 (Automated Integration)

为了彻底解决新外设逆向成果融合耗时、沟通成本高以及普通用户缺乏逆向环境的痛点，项目确立了 **FDAS v1.0 (FerrisPulse Device Adapter Specification)** 规范体系与自动化融合引擎：

### 8.1 架构与运作全景图
```
【用户端】向导探测到未识别设备 ➡️ 点击【📋 复制带硬件指纹的 AI 提示词】（自动注入真实 VID/PID/Usage/报告长度）
   ⬇
【外部 AI 端】粘贴至 ChatGPT/DeepSeek ➡️ AI 识别芯片方案并输出 FDAS v1.0 规范包 (*.fp-adapter.json)
   ⬇
【项目端】运行自动化融合引擎:
powershell -ExecutionPolicy Bypass -File tools/integrate_adaptation.ps1 -AdapterFile <path-to-json>
   ⬇
【结果】自动完成 Hex 样本断言、生成/更新 C# 5.0 驱动代码、挂载 DeviceManager、生成单元测试并跑通编译！
```

### 8.2 核心操作指令集
1. **仅校验规范包（测试断言模式）**：
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/integrate_adaptation.ps1 -AdapterFile examples/adapters/mchose_a5_v2_ultra.fp-adapter.json -ValidateOnly
   ```
2. **预演融合路径（DryRun）**：
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/integrate_adaptation.ps1 -AdapterFile my_device.fp-adapter.json -DryRun
   ```
3. **正式自动化融合并回归验证**：
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/integrate_adaptation.ps1 -AdapterFile my_device.fp-adapter.json
   ```

### 8.3 避坑与维护铁律
1. **抓包样本强制闭环**：规范包内必须提供真实的 `sampleRxHex`（且必须通过 `tools/integrate_adaptation.ps1` 的样本回放断言），严禁合并凭空捏造的幻觉报文。
2. **键盘 HID 0 权限握手**：键盘外设在 Win32 P/Invoke 时必须传 `0` 权限（`FILE_READ_ATTRIBUTES`），防 Windows 内核独占防监听锁死。
3. **编码规范**：所有由规范包衍生出的驱动与测试文件，必须严格以 **UTF-8 with BOM** 编码保存。

### 8.4 FDAS 动态设备全局“第一公民”与两阶段防幻觉逆向经验 (Two-Stage Anti-Hallucination & First-Class Citizen Architecture)

在实际外设适配推进中，我们排查并攻坚了三大深层硬件与架构陷阱：

1. **跨代固件 Opcode 幻觉陷阱与物理现场探针解法**：
   - **痛点**：外部 AI Agent 极易受到网络旧资料或通用主控文档的过时诱导。例如将早期 VGN 蜻蜓 F1 1K 系列的 `08 04`（电量）与 `08 09`（DPI）指令误当成所有 CompX 方案的万能指令，捏造到 F2 Pro Max 8K 鼠标的 JSON 中，导致设备固件丢弃报文、现场测试 0 字节超时、功能完全无效。
   - **对策**：推行“两阶段严谨逆向工作流”并内置零依赖硬件探针 `tools/probe_hid_device.ps1`。提示词强制 Agent 在输出 JSON 之前必须先在终端运行探针实测候选指令。若无物理回显，宁缺毋滥（仅保留电量，不编造虚假 DPI/回报率模板），以真实物理反馈（如 F2 8K 实测为 `08 02 ...` 指令，第 16 字节为电量）为唯一事实依据。

2. **跨前缀硬件去重与就地升阶机制 (In-Place Upgrade & Deduplication)**：
   - **痛点**：用户导入 JSON 后，软件界面同时出现两个相同设备（一个是免驱扫描到的 `VGN:3554:FB3E` 或 `Gaming:3554:FB3E`，另一个是导入的 `FDAS:3554:FB3E`）。
   - **对策**：在 `DeviceManager` 的 `GetDeviceIdVariants` 中将 `FDAS` 纳入标准前缀池。在 `QueryAllDevices` 流水线中，当 FDAS 模块探测到活跃适配设备时，若列表中已有同一物理硬件（跨 VID:PID 匹配）的 stub 设备，自动执行“就地升阶”（保留用户别名与自定义配置，将设备实例无损升级为具备完整 DPI/回报率能力的 FDAS 设备），彻底消除多设备并存。

3. **解除品牌白名单硬编码：全管线“第一公民”待遇**：
   - **痛点**：以往老代码散落着大量 `curDev.Brand == "Razer" || curDev.Brand == "Logitech"` 的硬编码品牌白名单，导致非四大品牌鼠标：
     - 点击 DPI 管理按钮被直接拦截退出（`OpenDpiManagementDialog` 无法打开）；
     - 开启全屏电竞双模联动后，`ApplyGameOptimization` 穿透掉入 Razer 指令分支导致调节不生效；
     - 休眠唤醒、回报率按钮或托盘菜单无法正确映射。
   - **对策**：在 `FdasRuntimeHelper` 抽象出 `IsFdasMouse(dev)` 与 `IsFdasDevice(id)` 统筹判定，打通 `OpenDpiManagementDialog`（自动映射适配包声明的传感器精度与最高 DPI）、全屏游戏自动优化管线（`ApplyGameOptimization`、`ApplyGamePollingRate`、`ApplyGameDpiStage`）以及底层 RawInput 唤醒事件，确保任意品牌通过 FDAS 导入后均享受与雷蛇、罗技完全同等的原生旗舰体验！

### 8.5 硬编码品牌门禁全景治理与双模式电竞优化架构 (Gatekeeper Purge & Dual-Mode Game Optimization)

#### 1. 全代码库 Brand 检查定性与清白清单
在代码库排查中，共发现 114 处涉及 `Brand` 的比较语句，经严苛工程审计，严格定性为两类：
- **无害/必要逻辑 (112 处)**：
  - *原厂品牌主题色*（37 处，`MouseBatteryInfo.cs`）：为已知品牌赋予辨识度极高的高级主题色（雷蛇绿、罗技蓝、苹果银、雷柏天蓝、VGN橙等），纯视觉渲染，FDAS 设备可自由指定自身色值，不阻断任何业务。
  - *驱动协议派发层 (Dispatcher)*（约 30 处）：在真正向 USB 写入底层硬件二进制指令时，按设备类别分流至各芯片私有 Helper（如 `RazerDeviceHelper`、`LogitechDeviceHelper`、`RapooDeviceHelper`、`ValkyrieDeviceHelper`、`MchoseMouseHelper`、`FdasRuntimeHelper`）。此为各厂商硬件专有协议通信必不可少的派发器，FDAS 在第一优先级被直接分发给动态引擎。
  - *设备配对白名单与去重*（约 15 处，`DeviceManager.cs`）：Apple/Beats 经典蓝牙防路人误扫白名单、NuPhy 键盘双模去重等。
  - *专属硬件控制台*（约 8 处）：NuPhy 键盘侧翼灯效调节与物理休眠管理面板等。
- **已彻底拔除的恶性拦截门禁 (2 处)**：
  - ⛔ `StartGameMonitor()` 第 892 行：曾残留 `if (curDev.Brand != "Razer" && curDev.Brand != "Valkyrie" && ... ) return;`，导致任何 FDAS 适配鼠标（VGN、迈从等）即使配置了游戏优化，后台监控也会瞬间 `return` 阻断！现已彻底拔除，统一替换为基于设备物理能力的 `IsDeviceSupportingAutoGameRate(curDev)`。
  - ⛔ `OpenDpiManagementDialog()` 曾对非 FDAS 设备使用硬编码品牌白名单，现已彻底解耦，统一由 `IsDeviceSupportingAutoGameRate` 判定放行。

#### 2. “游戏检测偶发失效、必须回桌面打开灵脉才生效”的根因与解法
- **根因分析**：旧版 `StartGameMonitor()` 与所有档位切换方法盲目依赖 `var curDev = selectedDevice;`。若用户在灵脉主界面当前查看的是机械键盘或耳机卡片，`selectedDevice` 即为键盘；后台定时器轮询时检测到其不支持电竞回报率，直接抛弃检测！只有当用户切回桌面打开灵脉主界面、点击鼠标卡片后，`selectedDevice` 才被重置为鼠标，造成“必须回桌面打开灵脉才偶尔生效”的假象。
- **工程解法**：全面引入 `GetActiveMouseDevice()` 动态寻路穿透机制。后台游戏轮询及所有应用优化逻辑（`ApplyGameOptimization`、`ApplyGamePollingRate`、`RestoreDesktopPollingRate`、`ApplyGameDpiStage`、`RestoreDesktopDpiStage`）全部统一调用 `GetActiveMouseDevice()`。即便当前主界面正浏览键盘或耳机，后台也能瞬间跨设备寻路至已连接的电竞鼠标，杜绝一切漏报与假死！

#### 3. 电竞优化双模式设计与防反复跳变机制
针对用户“看个时间、切去聊天也会导致鼠标频繁在游戏与桌面参数间来回切换”的痛点，推出双模切换架构并在 `GameProcessManagerForm.cs` 中提供直观的可视化切换面板：
- **模式 1: 进程存活常驻模式 (ProcessPersistent - 默认推荐)**：
  - 只要系统进程列表中**有任意一个受监控的游戏进程仍在存活运行**（`Process.GetProcessesByName` 命中），即便用户 Alt-Tab 切到桌面查攻略、看时间、回复微信，鼠标 DPI 与回报率依然保持在电竞状态，彻底杜绝来回跳变的不适感；
  - 只有当该游戏的所有进程彻底退出/销毁后，才经防抖恢复常规桌面节能参数。
- **模式 0: 仅前台激活模式 (ForegroundActive)**：
  - 严格保持传统前台窗口感知：仅当受监控游戏处于前台活动窗口时拉升，切出后经 2 秒防抖立即恢复桌面参数。
- **配置持久化**：用户偏好通过注册表 `GameOptMode`（0 或 1）实时同步与持久化恢复。

### 8.6 开机自启动双通道容灾与 2.4G 无线外设半双工避让铁律 (Autostart Resilience & Wireless Motion Guard)

#### 1. 开机自启动失效深层机理与双通道自愈架构
- **根因全景剖析**：
  1. *Windows 10/11 任务管理器仲裁陷阱 (`StartupApproved\Run`)*：Windows 10/11 引入了独立的启动项仲裁注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`。如果用户或系统优化软件曾禁用过软件，该键值第一字节会被写入奇数（如 `0x03`）。旧版 `MainForm.cs` 中存在一套陈旧的独立 `SetAutoStart`，仅仅向 `HKCU\...\Run` 写入启动命令，**完全未触碰 `StartupApproved\Run`**！导致 Windows Explorer 在开机时发现禁用标记直接跳过自启动，而软件主界面内的复选框却误以为自启已开启。
  2. *第三方管家与组策略静默拦截*：火绒、360、联想电脑管家等软件常常静默限制对注册表 `Run` 键的写入或在开机时延后拉起无数字签名的便携软件。
- **双通道自愈体系 (Dual-Channel Resilience)**：
  1. *双通道协同登记*：自启动同时挂载 **注册表 `Run` 键** 与 **Windows 用户级开始菜单启动文件夹 (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\FerrisPulse · 灵脉.lnk --autostart`)**。无论系统采用注册表管道还是 Shell 快捷方式管道，只要任意一个触发，进程即可通过单实例互斥体极速拉起，另一个通道触发后无缝安全退避退出。
  2. *StartupApproved 奇数禁用位自动治愈*：在开启自启动时，自动遍历 `StartupApproved\Run` 与 `StartupApproved\StartupFolder`，若检测到第一字节为奇数禁用态（`val[0] % 2 != 0`），强制修复回 `0x02`（启用状态）或干净重置，彻底粉碎 Windows 任务管理器对启动项的静默封锁！
  3. *代码架构单一真理源 (Single Source of Truth)*：拔除 `MainForm.cs` 中所有重复残余方法，100% 汇流至 `StartupManager.cs` 统一调度。

#### 2. 雷柏 (Rapoo) 鼠标防卡死与舒缓轮询规范
- **偶发卡死历史根因与复盘**：
  1. *时序漏置导致 50ms 狂暴轰炸*：旧版 `DpiMonitorWorker` 中，`lastRapooDpiPollTime = DateTime.Now;` 曾被错误放置在 `if (FastQueryDpi(...))` 成功分支内部。一旦鼠标休眠、正在位移或无线微丢包导致 `FastQueryDpi` 返回 `false`，轮询时间戳**完全不更新**！导致工作线程在随后的每一个 50ms 循环中，**以每秒 20 次的疯狂频率持续向 USB 堆叠阻塞式 Feature Report 查询**！
  2. *5 次同步阻塞重试累积延迟*：`ReadRegister` 内部循环 `Thread.Sleep(5)` 并调用同步 Win32 内核 `HidD_GetFeature`，重试高达 5 次。在鼠标休眠或无响应时，单次调用可阻塞内核数百毫秒，持续死锁 `hidLock`。
  3. *端点打开权限过度占用*：Feature Report 端点使用了 `GENERIC_READ | GENERIC_WRITE`，促使 Windows HID 类驱动建立独占队列，与 2.4G 接收器的指针输入集合争夺总线带宽。
  4. *2.4G 物理半双工射频冲突*：2.4G 游戏鼠标的单芯片射频在 1000Hz~8000Hz 高速回报下几乎占满空口。在用户高速移动光标时强行插发 33 字节双向 EEPROM 控制包，必定导致接收器射频缓冲溢出与指针包丢弃。
- **极简极速防护体系 (Clean & Responsive Guard)**：
  1. *精简纯粹轮询与绝对时间戳推移*：坚决剔除复杂的“光标拉枪/位移速率检测”以免引入误判与切档 OSD 延迟；无论查询成功与否，强制推移 `lastRapooDpiPollTime = DateTime.Now`，维持 200ms 舒缓轮询，彻底封死 50ms 狂轰死循环漏洞。
  2. *错误指数温和退避*：`ReadRegister` 或 `WriteRegister` 遇到任何通信阻断或无回显，重试上限严格限制为 2~3 次，失败时触发 400ms 温和退避，坚决不在休眠或离线状态下高频重试。
  3. *0 权限特征端点直连*：探测与 Feature 端点彻底改用 `0` 权限（`FILE_READ_ATTRIBUTES`），输出端点仅请求写权限，完全消除 HID 驱动层与鼠标指针输入端点的资源争抢。
  4. *EEPROM 写入静默期扩大*：由 350ms 提升至 800ms，为雷柏外设 MCU 内置 Flash 擦写留足物理裕量。

#### 3. 雷柏 (Rapoo) 官方 WebHID 逆向工程与 Report 0x07 (col09) 19 字节物理遥测流全景规范
- **电量不准历史根因 (从 100% 盲测到真实电量)**：
  - 过去错误假设雷柏电量存储于 EEPROM 寄存器中，导致 `CachedBatteryPercent` 常年未更新，UI 兜底返回 100%。经扫描 EEPROM 全部 Bank 寄存器，证实雷柏硬件根本不在 EEPROM 保存电量。
  - 经深入逆向雷柏官方 WebHID 驱动（Rapoo AHUB 2.0 / `https://hub.rapoo.cn`），揭示官方核心驱动对 `VT_nrf54L` / `Dongle_NordicL5` 架构采用的是 **主动遥测流协议**！
- **Report ID 0x07 (col09) 19 字节遥测报文结构**：
  - 通信端点：`col09`（UsagePage `0xFF00`, Usage `0x0002`, InputReportLength = 19）。
  - 报文格式：
    - `Byte 0`: `0x07` (Report ID)
    - `Byte 2`: 当前活跃 DPI 档位索引 (0-based，加 1 为实际档位)
    - `Byte 3..4`: X 轴实时 DPI (uint16 little-endian: `Byte[3] | (Byte[4] << 8)`)
    - `Byte 5..6`: Y 轴实时 DPI (uint16 little-endian: `Byte[5] | (Byte[6] << 8)`)
    - `Byte 7`: 物理充电状态 (`0x01` 为放电/使用中, `0x02` 为充电中)
    - `Byte 8`: **物理电池电量百分比** (真实硬件读值，如 `0x33` = 51%)
    - `Byte 10`: 当前动态回报率代码
    - `Byte 13..14`: 硬件 Product ID (如 `0x1413`)
    - `Byte 17`: 设备支持的 DPI 档位总数 (如 5)
- **非阻塞异步遥测架构与冷启动热恢复**：
  1. *非阻塞异步流读取*：在 `RapooDeviceHelper.cs` 中打开 `col09` 时追加 `FILE_FLAG_OVERLAPPED`，使用 .NET 原生 `FileStream.BeginRead` / `EndRead` 监听 Report ID 0x07。有数据时微秒级消费并更新缓存，无数据时完全不阻塞工作线程。
  2. *双通道极速返回*：`FastQueryDpi` 与 `QueryRapooDeviceInfo` 优先直接消费遥测流，3 秒内有活跃遥测即无需发起 EEPROM 特性读取，彻底消灭射频包冲突。
  3. *冷启动热恢复*：通过静态构造函数 `static RapooDeviceHelper() { LoadHardwareCache(); }` 与注册表持久化机制，应用冷启动时立即毫秒级恢复上一次关机前的真实电量与档位，并在后台静默连通后瞬间刷新为最新物理电量。
