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
   - 3.11 无线电竞耳机协议突破（赛睿 Nova 5 / 雷蛇旋风黑鲨 / 罗技 G PRO X）
   - 3.12 罗技 LIGHTSPEED 接收器 (046D:C53F) / G304·G305 适配实战（含虚拟 HID 甄别铁律）
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

### 3.12 罗技 LIGHTSPEED 接收器 (`046D:C53F`) / G304·G305 适配实战（含虚拟 HID 甄别铁律）

> 本节由 2026-10 实机适配沉淀：接收器 `046D:C53F` 后挂 G304/G305 鼠标（HID++ 2.0，devIdx `0x01`）。
> **切勿因“设备已被 DeviceManager 列出”就跳过逆向**——本机型曾因 DPI 读错函数而长期谎报 200 DPI。

1. **接收器端点拓扑（只有 20 字节集合说 HID++ 2.0）**：
   - `MI_02` 同时暴露三个 Vendor 集合：`0xFF00/0x0001`（7 字节）、`0xFF00/0x0002`（20 字节）、`0xFF00/0x0004`（32 字节）。
   - **仅 20 字节集合（Report ID `0x11`）可正常收发**；7 字节集合只回 HID++ 1.0 错误帧（`10 <dev> 8F ...`），32 字节集合 `WriteFile` 直接返回 `ERROR_INVALID_PARAMETER (87)`。端点优选逻辑必须优先 20 字节长集合。
   - 配对设备挂在 **devIdx `0x01`** 上作答；`0x02` 返回错误帧，`0x03~0x06` 完全静默。`0xFF`（接收器本体）对本固件不应答，禁止把它当作“设备离线”判据。

2. **⚠️ HID++ 2.0 特性 `0x2201` 读写函数号差一位（血泪根因）**：
   - `Func 1 (0x18)` = **getSensorDpiList**（受支持档位列表，列表首项常为 `0x00C8`=200）；
   - `Func 2 (0x28)` = **getSensorDpi**（当前生效 DPI）；`Func 3 (0x30)` = setSensorDpi。
   - 旧实现把 Func 1 的列表首项当当前 DPI，导致实测 **3200 DPI 全程显示 200 DPI**，且 UI/OSD/游戏切换全链路跟着错。
   - 实测原始帧（featIdx `0x1A`）：`11 01 1A 28 00 0C 80 03 20 00 …` → `[4]`=sensor 0、`[5..6]`=`0x0C80`=3200 当前 DPI、`[7..8]`=`0x0320`=800 出厂默认。
   - 解析统一收敛到 `LogitechDeviceHelper.ParseAdjustableDpiReply()`，量程外/错误帧一律返回 `-1` 交缓存兜底，**严禁把列表首项伪装成实测 DPI**。

3. **`0x1000` BATTERY_STATUS 是 G304 的正确电量通道（`0x1004`/`0x1001` 均不存在）**：
   - `Func 0` 应答 `11 01 05 08 <level> <nextLevel> <status>`：实测 `5A 32 00` → 90%、下次上报阈值 50%、`status=0` 放电中。
   - G304/G305 为 **AA 干电池供电**，不存在充电状态，`IsCharging` 必须恒 `false`，严禁把 `status` 非 1 误判为“正在充电”。

4. **`0x8060` 回报率：可读不可写（必须诚实失败）**：
   - `Func 0` = getReportRateList 位图（实测 `0x8B`）；`Func 1` = getReportRate，单位为毫秒（实测 `01` → 1000Hz）。
   - `Func 2` = setReportRate，但本固件对任意取值（0/1/2/4/8）均返回 HID++ 错误帧 `11 01 FF 09 28 02 …`（错误码 `0x02`）；`Func 3~6` 返回错误码 `0x07`（函数不存在）。
   - 结论：G304 的回报率由**板载配置文件扇区（`0x8100`）**管理，HID++ 直写不可行。`SetLogitechPollingRate` 应以“未收到成功应答即返回 `false`”收场，UI 按失败提示，**绝不允许回写缓存伪造已切换**。

5. **`0x2202`（XY 独立双轴）在 G304 上不存在**：`IsXyIndependent` 必须为 `false`，UI 不得展示双轴输入框。

6. **机型名分片读取（长名截断陷阱）**：
   - `0x0005` 特性 `getName` 每次最多回 16 字节（20 字节报告减去 4 字节头）。只取 offset `0` 会把 37 字符全名砍成 `G304 Lightspeed`。
   - 必须按 `nameOff += 16` 循环拉取，遇到 `0x00` 或读满 `nameLen` 收尾。

7. **🔴 虚拟 HID 甄别铁律（一个接收器环境里最容易误判的坑）**：
   - 本机同时存在 `HID\LINKLOOK&COLxx`（`BACD:001F`）、`HID\GVINPUT&COLxx`（`00FF:BACC`，NetEase 模拟器）、`HID\{GUID}&COLxx`（`BE27:F2E5`，Clixon `ClxVirtHid`）等节点，产品名一律伪装成 `Virtual Multitouch Device` / 键盘鼠标组合，甚至带 65 字节 `0xFF00` Vendor 集合。
   - **判定方法**：查询 `DEVPKEY_Device_Parent`，父实例以 `ROOT\HIDCLASS\` 开头即为软件虚拟节点（仿真器/映射工具/远程控制），而非物理外设。
   - **处置**：一律排除，绝不为其编写电量/DPI 协议——它们没有物理电池，硬适配只会在 UI 里制造幽灵设备与伪造电量。物理外设必须能追溯到 `USB\VID_…` 或 `BTHENUM\…` 父设备。

8. **`0x8100` 板载扇区只读访问（字节序陷阱 + G304 实测档位表）**：
   - **函数表**（G HUB 静态分析，OpenLogi / Solaar 一致）：`fn0` GetOnboardProfilesInfo、`fn1` SetOnboardMode、`fn2` IsOnboardMode、`fn3` SetActiveProfile、`fn4` GetActiveProfile、`fn5` ReadData、`fn6` BeginWrite、`fn7` WriteData、`fn8` EndWrite、`fn9` ExecuteMacro、`fn10` GetCRC、`fn11`/`fn12` ActiveProfileResolution。
   - **审计/取证只允许调用 `0/2/4/5/10`**；`fn1`/`fn3`/`fn6`/`fn7`/`fn8`/`fn9`/`fn12` 会改设备状态（切换模式、切配置、写扇区），禁止在“只读排查”里盲扫函数号。
   - 🔴 **参数字节序陷阱（最容易误判成“不支持”）**：扇区号按 **u16 小端**（p0=低字节、p1=高字节），偏移按 **u16 大端**（p2=高、p3=低）。实测 `(01,00,00,00)` = 扇区 `0x0001` 偏移 0 → 成功；照搬 Solaar 的 `(hi,lo)` 传 `(00,01,…)` 则一律收到 HID++ 错误帧 `11 01 FF 07 58 02`（错误码 `0x02` = INVALID_ADDRESS），曾因此误判“该接收器不支持扇区读取”。
   - **元数据（G304 实测）**：`memory=1`(flash)、`profileFormat=3`、`macroFormat=1`、`profiles=1`、`oob=1`、`buttons=6`、`sectors=6`、`sectorSize=255`、`flags=0x0A`；`isOnboardMode=1`、`activeProfile=1`。
   - **可读扇区只有 `0x0001` 与 `0x0101`**：前者是按键映射（`…01 C1 00 xx` 条目 + 机型名尾串 `" Wireless Gaming Mouse"`），后者是指针/DPI 扇区；其余地址全部 INVALID_ADDRESS。
   - **`0x0101` 布局（实测）**：`[0]=0x01 [1]=0x01 [2]=0x00`，`[3..12]` = **5 × u16 小端 DPI 档位**，实测 `400 / 800 / 1600 / 3200 / 0x0000（空槽）`；紧邻的 Y 轴槽位全 `0xFF`（G304 硬件无独立双轴，与 `0x2202` 缺失一致）。`[32..51]` 为 5 组 4 字节条目（`0x0180` + 标志字节 `01/02/04/08/10`，语义未定，禁止臆造）。
   - **末块读法**：255 字节扇区的最后 16 字节必须用 `offset = sectorSize - 16 = 239` 读取（按 16 对齐读到 240 会返回 INVALID_ADDRESS）。
   - **结论**：G304 的板载档位是 **400/800/1600/3200 + 空槽**，`6400` 并不在设备真实档位表里（传感器能力上限仍是 12000）。因此 UI 里写死的 `400/800/1600/3200/6400` 五档与 `DpiStage=2` 都是通用假设值，不得当作“设备实测档位”向用户呈现。

9. **⚠️ 档位号禁止硬编码（「点立即刷新就跳回 800」的根因与根治）**：
   - **症状**：点「立即刷新」明明读到了真实 DPI（实测 3200），主界面档位高亮却总跳回第 2 档（800）。
   - **根因**：`LogitechDeviceHelper` 构造 `MouseBatteryInfo` 时写死 `DpiStage = 2`、`DpiStageCount = 5`；而主界面高亮逻辑（`MainForm.Hardware.cs`）只要 `info.DpiStage > 0 && info.DpiStageCount > 0` 就直接用 `i == DpiStage - 1`，无条件信任该字段 → 无论实测多少 DPI，永远高亮索引 1（即 800）。
   - **正确做法**：`DpiStage = MatchDpiStage(实测 DPI, 档位表)` —— ±50 容差内的精确命中优先，未命中取数值最接近的有效档位，空槽（`<= 0` 或 `0xFFFF`）必须跳过。
   - **禁止返回 0**：`desktopDpiStage` / `RestoreDesktopDpiStage` 等“离开游戏还原档位”链路依赖 `DpiStage > 0`，返回 0 会把还原目标打回第 1 档（400），反而制造更隐蔽的 DPI 掉档。
   - **连带影响（同一根因）**：硬编码 2 还会让「离开全屏游戏」把 DPI 还原成 800 而不是用户原来的 3200 —— 这类“只在游戏回来后才发现”的掉档，第一时间应先查 `DpiStage` 是否被写死。
   - **回归锁定**：`tests/test_logitech_stage_from_dpi.ps1`（纯反射、不依赖硬件）同时断言 DPI→档位映射与 UI 高亮索引（3200 必须命中第 4 档、800 不得被选中）。

10. **「硬件生效」徽标必须取实测值，禁止取配置期望值（回报率谎报根因）**：
   - **症状**：右上角徽标显示 `● 硬件生效: 500 Hz`，但双击查询 `0x8060` Func1 实测是 `1000 Hz`。
   - **根因（两段式污染）**：
     1. `MainForm.SetPollingRateFromUI()` 旧实现在**下发硬件之前**就执行 `desktopPollingRate = hz; SaveConfig();`，一旦写入被拒（G304 固件对 `0x8060` Func2 一律回 `11 01 FF 09 28 02`），配置里就永久留下一个从未生效的“期望值”；
     2. `UpdatePollingRateDisplayStates()` 的徽标与桌面档位高亮都拿 `desktopPollingRate` 做基准 → 于是 UI 长期显示假值，还顶着“硬件生效”四个字。
   - **正确做法**：
     - 写入路径改为「**写成功才认账**」：`desktopPollingRate = hz` 与 `SaveConfig()` 全部搬到 `ok == true` 分支内，失败时保持旧值，配置不再被污染。
     - 徽标与桌面档位高亮改为「**实测优先、配置兜底**」：优先用 `selectedDevice.PollingRate`（设备实读），仅当实测为 0（读不到）时才回退 `desktopPollingRate`；电竞自动回报率生效期间仍用 `gamePollingRate`（那是本进程刚下发的值），桌面按钮高亮在此期间也仍按 `desktopPollingRate`。
   - **能力位现状（据实记录）**：全项目**没有**「按设备禁用回报率操作」的接口——只有按设备类别 `btnRates[i].Visible = false` 的隐藏逻辑，以及仅作用于电竞回报率按钮的 `btnGameRates[i].Enabled = autoGameRateEnabled`；桌面回报率按钮对任何鼠标都恒可点，写入失败也**不做任何提示**。若后续要做“只读回报率设备”，必须新增能力位（例如 `MouseBatteryInfo.SupportsPollingRateWrite`）+ 一次成功的无操作写入探测，禁止在 UI 层凭品牌硬编码。
   - **回归锁定**：`tests/test_logitech_rate_badge_measured.ps1`（实测 1000 + 配置残留 500 → 徽标必须是 1000；实测缺失 → 回退 500 且不得出现 `0 Hz`；按钮高亮同步）。

11. **回报率「可写能力位」三态机制（issue #35 落地）**：
   - **模型**：`MouseBatteryInfo.SupportsPollingRateWrite` 为 `bool?`（`true` 可写 / `false` 固件拒绝 / `null` 未知）。
   - **探测（一次性）**：`LogitechDeviceHelper.ProbePollingRateWriteNoOp()` 把**当前回报率原值**写回自身（no-op，绝不写其它值）；
     - 收到成功应答（`rx[2] == featureIndex`）→ `true`；
     - 收到 HID++ 错误帧（`rx[2] == 0xFF`）→ `false`（G304 实测 `11 01 FF 09 28 02`）；
     - **无应答 / 超时 → `null`（未知）**，绝不可判成“不可写”，否则会把异步固件设备误伤成只读。
   - **缓存**：结果按 `DeviceId` 存 `HKCU\Software\RazerBatteryTray\RateWriteCap`（`1`/`0`，值名把 `:` 换成 `_`）。**禁止每次轮询都探测**——探测本身是一次硬件写入。
   - **自愈**：`SetLogitechPollingRate()` 收到错误帧即写缓存 `false`，写成功即写 `true`；UI 侧 `SetPollingRateFromUI()` 失败分支会重新解析能力并刷新按钮，所以“先探测不到、后来才发现”的设备也能自动置灰。
   - **UI 策略（只在明确 `false` 时禁用）**：
     - `RelayoutRateButtons()` 里 `btnRates[i].Enabled = (capability != false)`，并切换 `Cursor`；禁用时 tooltip 追加「该设备不支持修改回报率，当前档位由设备自身配置决定」；
     - 齿轮按钮 `btnRateManage.Enabled` 同步（禁用后 WinForms 不再派发 Click，天然挡住回报率管理弹窗）；
     - **电竞回报率一并锁死**：`UpdatePerformanceSection()` 在 `rateWriteBlocked` 时把运行期 `autoGameRateEnabled` 置 `false`，**且必须用 `isUpdatingUI` 包住 `chkAutoGameRate.Checked = false`**——否则 `CheckedChanged` 会走 `SetAutoGameRateEnabled(false)` 并 `SaveConfig()`，把“关闭”永久落盘，换回可写设备后用户的原偏好就丢了；
       - **⚠️ 「全屏游戏自动优化」总开关是最大的旁路（实测概率复现）**：`SetAutoGameOptEnabled(true)` 旧实现无条件 `autoGameRateEnabled = true; chkAutoGameRate.Checked = true;`，只要用户点一下总开关（或刷新时 `chkAutoGameOpt.Checked = isOptExpanded` 触发 `CheckedChanged`），已锁死的「电竞回报率」就会被重新勾上。四条入口必须全部堵死：
         1. `SetAutoGameOptEnabled(true)` 分支改为「只开 DPI」：`autoGameRateEnabled = !rateBlocked`，同时把 `chkAutoGameRate` / `autoGameRateToggleItem` 的 `Checked` 与 `Enabled` 一并复位；
         2. 刷新时对 `chkAutoGameOpt.Checked` 的程序化赋值必须用 `isUpdatingUI` 包住，禁止触发 `CheckedChanged` 走总开关逻辑；
         3. `SetAutoGameRateEnabled(true)` 顶部加兜底闸门（`if (enabled && IsPollingRateWriteBlocked()) enabled = false;`），拦住托盘菜单/代码调用等所有旁路；
         4. 托盘菜单 `autoGameRateToggleItem.Enabled = !rateBlocked`（禁用后 WinForms 不再派发 Click），并补 `ToolTipText`。
       - **验证方式**：`tests/test_polling_rate_write_capability.ps1` 里直接断言「开总开关后 `autoGameRateEnabled` 仍为 false、`chkAutoGameRate` 不勾选且不可编辑、电竞 DPI 正常开启」，另做 20 轮「刷新+总开关来回切」压测确认零异常。
     - 电竞回报率开关 `chkAutoGameRate.Enabled = !rateLocked`，电竞档位按钮 `Enabled = autoGameRateEnabled && !rateLocked`；
     - `SetPollingRateFromUI()` 顶部有前置闸门：`SupportsPollingRateWrite == false` 时直接弹一次托盘提示并 `return`，不发起任何写入；
     - **未知（`null`）一律保持可点**，宁可在点击后由自愈机制置灰，也不要一上来就误伤。
   - **⚠️ 置灰不能直接 `Enabled = false`（原生气泡会跟着失效）**：WinForms 的 `ToolTip.SetToolTip` 对 `Enabled == false` 的控件不弹（禁用控件收不到鼠标消息）。
     - ❌ 反面写法（已废弃）：为了提示而手动 `toolTipTop.Show(text, ctl, x, y, 2600)` + 定时器。它会出现「固定驻留 2.6s、移开鼠标也不消失」的割裂体验，与项目原生 `SetToolTip` 的「悬停出现 / 移开即消失」语义完全不同。
     - ✅ 正确写法：`ModernSegmentButton` 增加 **`IsReadOnly` 只读态** —— 绘制与 hover 完全等同禁用（灰化、无 hover 高亮），但控件保持 `Enabled = true`，于是**原生 ToolTip 按原有逻辑工作**，点击仍会派发并由业务闸门拦截（`SetPollingRateFromUI` 顶部前置闸门 + 托盘气泡）。
     - **状态收敛**：`btnGameRates` 的可用性必须统一走 `ApplyGameRateButtonState()` 一处，否则 `UpdatePollingRateDisplayStates()` 里残留的 `Enabled = autoGameRateEnabled` 会把只读态覆盖掉（实测电竞按钮因此一度变回 `Enabled=false`）。
     - **回归锁死**：测试需同时断言 `IsReadOnly == true` 且 `Enabled == true`（后者保证气泡可弹），不能只断言“不可点”。
   - **范围收敛**：目前仅 Logitech 做探测（`ResolvePollingRateWriteCapability()` 决定取哪个来源）；其它品牌保持 `null`，等各自 Helper 补探测后再接。
   - **回归锁定**：`tests/test_polling_rate_write_capability.ps1`（能力位三态类型、缓存三态读写、不可写全置灰、电竞回报率锁死为关闭且不可编辑、常规/电竞档位与开关气泡提示、可写恢复可点、不可写点击被拦截且不污染 `desktopPollingRate`）。

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
