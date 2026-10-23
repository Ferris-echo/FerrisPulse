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
- **根目录**：`D:\FerrisPulse · 灵脉`
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
   - **按钮呼吸感与自适应排版**：采用独立双轴后按钮文字由单值变为 `XXXX×YYYY` 格式（如 `3000×2250`）。主窗体基准宽度微调至 486px，性能卡片内部水平内边距设为 14px，标签列宽精简至 82px，档位间距设为 5px；雷柏最多显示 6 档，布局必须依据当前档数压缩按钮宽度。
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


### 3.12 雷柏 VT3 实时电量与三通道接入（2026-10-22）

- 宿主机实测 VT3 接收器为 `24AE:1412`；FF00/000E 的 col05 为 33 字节 Output，FF00/000F 的 col06 为 33 字节 Feature，FF00/0002 的 col09 为 19 字节 Input。0001/0006 键盘集合以及 FF00/0010 的另一 Feature 集合不能代替协议通道。不要仅按报告长度跨设备配对。
- 原 Helper 只有寄存器读取，电量直接取全局缓存或默认 100%，充电固定 false。适配必须监听 0x07 输入报告，不能只加 PID 型号映射。
- 报告 [2] 为 0..5 的当前档位索引（0-based），应用到 UI 时加 1；[3..6] 才是实时 X/Y DPI。寄存器 0x98 同样为零起始索引。报告 [8] 为 0..100；合法 0/1 不应永久过滤。无效值不得回退为 100%，无历史样本用 -1。最近电量时间必须由有效报告更新，不能被 UI 定时轮询伪造。
- 参考代码中的连接标志需按高四位匹配。物理 USB 接线不等于充电；充电条件来自 rapoo-tray，需在硬件开机后用插拔线报文确认。本轮用户确认鼠标关机，只有端点枚举与未应答状态得到实机验证，其余不宣称完成实测。
- Feature ACK 两种布局分别从 [4] / [5] 取数据。发送成功不等于 EEPROM 已写入，设置必须逐项读回一致；禁止靠任意 ACK 或多个结果 OR 就向 UI 报成功。避免写参考实现未使用的猜测性能寄存器。
- 新状态线程使用自有原生缓冲区及 OVERLAPPED；取消 I/O 后必须 GetOverlappedResult(wait=true) 等待完成，再释放内存/事件。不得在 hidLock 内 Join 需要同一锁发布数据的线程；切换时先使旧 session 失效。
- 主项目维持单雷柏主设备模型，有线完整通道组优先；两个相同 PID 接收器不能混配端点。其他 VT3S/MAX 型号映射有历史冲突，本轮保留未证实型号。
- VT3 支持六个板载 DPI 档位；状态报告给出零起始档位索引，映射为 1-based UI 档位时加 1，UI 和管理弹窗不得裁剪到五档。硬件 DPI 值与活动档位分别显示，但必须一致对应。
- 移植遵循 MIT Copyright (c) 2026 Iris，许可保存在 docs/rapoo-tray-MIT.md；诊断工具为 tools/probe_rapoo_vt3.ps1，贡献与后续实测清单见 docs/rapoo-vt3-contribution.md。
- DPI 实时状态来自 0x07 输入报告，回报率不在该报告中，必须读 bank 08/address 80。不要把 50ms 高频 EEPROM 轮询重新启用；DPI 监听线程对当前雷柏设备以至少 1000ms 间隔读回 Hz，变化后立刻同步 UI。读回时先更新时间戳，即使 I/O 失败也不能在每轮 50ms 重试。游戏自动回报率接管时只更新硬件实况，不覆盖桌面回报率预设。

- 审查补充：通用接收器 14FF/1500/1501 的物理 PID 不能直接代替鼠标身份，继续用 COMM/04 读有效 PID；休眠时保留最近已知型号。设置开始时绑定物理组，枚举发现插线/换接收器后必须拒绝旧请求，不能对刚清空的数组按旧档位索引。所有雷柏 DPI 入口（托盘、主界面档位、管理弹窗）和手动 Hz 入口均需检查 setter 成功后才发布 UI/OSD。


### 3.13 GANSS GS3108T-PRO 2.4G（2026-10-23）
- 用户确认雷柏适配暂时正常后，报告 GS3108T-PRO 被识别为鼠标/BLE。实机为 USB 接收器 05AC:024F，包含鼠标集合与多个键盘集合；不能让最先枚举的 Mouse 集合决定整机品类。按产品字符串匹配 GS3108T-PRO，并修正已保存的该型号分类，真实 BT 记录的连接模式不盲目重写。
- 05AC:024F 为共享的 Apple VID/PID，禁止仅按 ID 认定高斯或苹果。电量端点严格同时匹配产品串含 2.4G、UsagePage FF60、Usage 0061、Input/Output 均 33 字节。
- 根据用户本机 D:\ganss\GS3108T-PRO\DeviceDriver.exe 静态分析：电池 UI 函数 435840，协议调用 44F4C0；发送 33 字节 00 20 01 00 ... 21，末字节为前 32 字节和；WriteFile/ReadFile，非 Feature。官方查询间隔约 15 秒。
- 4511E0 会剥除输入起始的零 Report ID，故原始 Windows 应答中的电量在 [4]，剥除后为 [3]。应答命令需匹配 20 01 00，百分比 0..100；不从任意字节猜电量。
- 退出官方驱动后实机收到 00 20 01 00 64 ... 85，真实读数 100%；官方驱动同时运行时首轮无有效应答，存在读队列竞争。失败保留最近读数/采样时间，首次未知 -1。该查询未确认充电标志，不宣称支持充电检测。
- GanssDeviceHelper 复用可取消的原生重叠 I/O（RapooNativeIo 新增有界读取重载），取消后等待完成再释放缓冲区。新代码不引入 DLL/NuGet；pefile/capstone 仅位于忽略的 tools/scratch，用于本次静态分析，不属于产品依赖。

- 有线/2.4G 传输判别（2026-10-23 第二轮）：官方 `config.xml` 的 mode 表是唯一权威——mode 0（USB）产品串 `GS3108T-PRO` + `MI_00`，mode 2（2.4G）产品串 `GS3108T-PRO 2.4G` + `MI_03`。实机同时插线与接收器时枚举到两组独立复合设备：有线组 Mfg=SONiX，含 MI_00 键盘集合、FF68/61 65B、FF13/01 65B；接收器组产品串带 2.4G，含 FF60/61 33B 电量端点与 FF59/61 65B。禁止仅凭“出现 USB 接口/MI_03”改判有线：接收器自己也有 MI_00 与 MI_03 子集合。`GanssDeviceHelper.ResolveTransport` 要求“产品串不带 2.4G 的 MI_00 键盘集合”与“同产品串的 FF13/01 或 FF68/61 厂商接口”同时成立才返回 USB 有线，否则保持 2.4G。电量仍只走 FF60/61 33B，不得在 FF13/65B 上猜协议。关键实测：切到有线后连续 3 次向接收器发查询均为 Write=True / Read=False——射频关闭，接收器不再回报电量；因此有线模式必须返回 BatteryPercent=-1，大数字显示“已连接 · 供电中”，药丸“🔌 USB 有线”，脚注用 FormatWiredFootnote 标注“上次 2.4G 读数 X%（MM-dd HH:mm:ss）”，无历史样本时写“2.4G 电量不可读”。禁止把切换前最后一次 100% 当实时值挂在界面上（时间戳会冻结，属于假数据）。托盘菜单显示“供电中”，状态行与 tooltip 不得再写“2.4G 供电中”。接收器时代自动生成的型号串 `GS3108T-PRO` / `GS3108T-PRO 2.4G` 必须视为默认名（IsDefaultGenericName），否则有线时标题仍挂着 2.4G 后缀。实测闭环（同日）：键盘切到 2.4G 且线仍插着时，SONiX 有线复合设备整体从总线消失——枚举由 17 个接口降到 9 个，剩余全部属于接收器组，ResolveTransport 返回 2.4G 无线。说明“MI_00 有线复合设备出现/消失”本身就是可靠的通道判据，不需要再对 FF13/01 发协议探针；反之只看到 MI_03/FF13 片段不能判有线。

- 充电位 A/B 实机结论（2026-10-23）：2.4G 模式插线（100%）与拔线各抓 3 帧，33 字节应答完全相同（00 20 01 00 64 ... 00 85），保留位全 0；官方 2052/1033.lan 中没有任何“充电 / Charging / Battery”文案。当前只能确认百分比字段，禁止用 100% 或“线插着”推断充电，也不要在 2.4G 通道旁伪造 ⚡。该结论受限于满电样本：若日后电量降到 90% 以下仍要复测，需重新做 A/B，不能仅凭一次满电样本断言协议永远没有充电位。

- 高斯重复行根因（同日验收）：GS3108T-PRO 与 Apple 共用 VID 05AC；除了 HID 扫描的 IsAppleMagicDevice，配对设备扫描也必须在 VID 兜底前按产品字符串排除高斯，否则蓝牙入口会再造一条键盘。历史自定义蓝牙记录也须按 GS 型号归并到实时 2.4G 设备，即使旧名称没有“2.4G”后缀；不得让它覆盖有实测电量的主记录。不要只修 HID 扫描或在 UI 按名称去重；回归同时验证两个 Apple 识别入口排除高斯、旧蓝牙记录归并、真实 Magic Keyboard 保留。


### 3.14 盖世小鸡启明星 2 只读电量探测（2026-10-23，尚未接入产品）
- 实机 2.4G 接收器 3537:1098；专用 MI_02，UsagePage FF7A / Usage 0001，Input/Output 65 字节，无 Feature。标准 XInput 返回 type=1（WIRED）/level=3，不能当作真实 100%。
- 用户配套程序 D:\xiaoji\GameSir Connect：app.asar 的 T4N2 模型包含十进制 PID 4248；官方百分比来自 sapjoyuniapis_t4n2_x86_v2608251.dll 的 GetDeviceInfo，非 XInput 等级。仅做静态分析，没有将官方 DLL 作为产品依赖。
- 对照 DLL GetDeviceInfo RVA 44570、收发 53C20、读 60AB0：只读查询 Windows 报告 65 字节 00 01 01 后补零。读函数会剥除零 Report ID，设备信息的 battery 在剥除后 [18]，即 Windows 原始 [19]。不要把 F4 00 的实时按键报告作为设备信息应答。
- 两次实机均捕获 00 01 A9 63 45 D8 CE 01 00 00 00 28 00 00 00 00 00 04 02 57 ...，原始 [19]=0x57=87%，是直接读数而非插值。充电哨兵在官方 JS 中另有处理，本轮未测试充电变化。
- 本轮仅应用户请求确认能否读取，没有新增 GameSir 生产驱动、UI 注册或修改手柄配置。诊断脚本/日志在忽略的 tools/scratch；后续适配需按型号区分官方 255 哨兵与 supportRichCharginInfo 协议，不能套用其他小鸡型号。


### 3.15 启明星 2 接入与有线充电哨兵（2026-10-23）
- 用户要求接入后，实测充电有线 PID 3537:100F，FF7A/1、65 字节；同机出现的 3537:2106（HID zkm）没有型号/协议证据，不探测不写入。
- 有线应答与无线 MAC 一致，但 battery 原始值从 0x57（87%）变为 0xFF（255）。官方 app.asar 的 chargeState 判断为 battery==255，同时 Math/min 逻辑把百分比截到 100；这是充电哨兵，不是测得满电。
- GameSirDeviceHelper 用 MAC 构造稳定 DeviceId，有线优先，避免同时出现有线与无线副本。支持 0..100 正常电量、255 充电；充电时保留同一 MAC 的最后有效电量和采样时间，首个样本只有255则使用 -1。不能把另一只手柄的缓存借用过来。
- 主界面与托盘在充电且百分比未知时显示“充电中/电量未知”；手柄卡片不再错误写“电池供电”。原始 F4 实时输入、截断回复、未知 PID 与101..254电量值均拒绝。
- 已知设备无回复时保留离线记忆；当前会话缓存不跨进程保存，因此重启时若只收到255，仍应显示充电中而非过去诊断得到的87%。不硬编码过去读数。
- 旧通用 HID 3537:1098/100F 记录归并到新品牌卡片；旧通用指纹不能精确区分多只同型号设备，归并仅接受精确 DeviceId 或唯一传输指纹匹配，歧义时保留原记录。
- GetDeviceInfo 是只读查询；不加载官方 DLL，不写键位、灯效、固件，不振动手柄。官方软件并发读可能竞争应答，查询使用有界时间并过滤非设备信息报告。

- 同型号多手柄的旧自定义记录必须优先按稳定 MAC DeviceId 精确匹配，再按唯一 VID/PID 指纹匹配；多个候选时保留原记录，不按品牌取首项，以免错归名称。

- 启明星2 卡片消失根因（2026-10-23 验收）：用户移除旧通用设备时，`GetDeviceIdVariants` 会一次性写入 16 个品牌前缀的隐藏键（含 `HID:3537:1098`）；新品牌卡片的 `HardwareFingerprint` 仍是同一个 `HID:3537:1098`，于是被“DeviceId 或指纹命中隐藏”过滤连带删除。修复：新增 `DeviceManager.ShouldHideDevice`，对 `GameSir:Nova2Lite:<MAC>` 这类单机唯一身份只认显式 MAC 隐藏，不应用共享 VID:PID 指纹隐藏；历史隐藏键同时清理。这样旧通用记录的一次移除不会复辟隐藏新卡片，也不会让同型号其它手柄一起消失。回归必须同时验证：有隐藏指纹时 MAC 手柄仍显示、显式隐藏 MAC 仍生效、真正的通用 HID 设备仍能按指纹隐藏。

### 3.16 无线能读取但主界面缺失：历史隐藏记录（2026-10-23）
- 启明星2无线底层读取成功（本次93%），QueryAllDevices却只有键鼠，原因是 HiddenDevices 下 HID:3537:1098 及变体为1；最终过滤同时检查稳定 DeviceId 与 HardwareFingerprint。USB 100F未隐藏，造成只插线显示的假象。
- 排查必须验证完整调度返回值，不能用品牌 Helper 成功替代 UI 可见性验证，也不能直接建议刷新。先检查 IsDeviceHidden 两个身份，确认用户要求恢复该设备后调用 UnhideDevice 清除对应变体；不得在驱动中永久绕过用户隐藏选择或自动清空其他设备设置。
- 本次仅恢复该接收器的隐藏配置，完整扫描确认无线93%在线并恢复程序运行；未修改协议代码。

- 验收补充：独立扫描成功不代表运行中窗口已呈现。后续用户仍报告缺失时，临时记录实际主进程收发，确认无线应答与卡片88%均可出现；诊断代码已全部撤回，正常版本重新编译。用户最终截图确认手柄已显示，有线充电保留最后有效电量91%。不能把这91%说成充电期间的实时电量；也不能把未稳定复现的界面遗漏归因于确定的协议故障。
