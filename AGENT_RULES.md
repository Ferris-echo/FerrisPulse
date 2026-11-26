# FerrisPulse AI 维护规范与工程准则 (AGENT RULES)

> 本文档为所有介入维护本项目（FerrisPulse）的 AI Agent 及开发者的全局最高效力规范。任何修改与代码审查必须严格遵从以下 11 大铁律。

---

## 1. 语言标准与编译器基准 (C# 5.0 / .NET Framework 4.0)
- **编译工具**: 严格限定使用 Windows 原生自带的 `csc.exe` (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 或 32位版本)。
- **语言版本**: 严格 C# 5.0。
- **严禁使用的语法 (C# 6.0+ 禁止列表)**:
  - ❌ 严禁使用字符串插值 (`$"hello {name}"`) -> 必须使用 `string.Format()` 或 `+` 拼接。
  - ❌ 严禁使用空条件运算符 (`obj?.Property` 或 `action?.Invoke()`) -> 必须使用显式 `if (obj != null)`。
  - ❌ 严禁使用 `nameof(...)` -> 必须使用字面量字符串。
  - ❌ 严禁使用表达式主体成员 (`int Foo => 1;` 或 `void Bar() => ...`) -> 必须使用完整花括号方法体 `{ return 1; }`。
  - ❌ 严禁使用自动属性初始化器 (`public int Val { get; set; set; } = 1;`) -> 必须在构造函数中赋值。
  - ❌ 严禁使用 `using static` 语法。

---

## 2. 零外部依赖铁律 (Zero Third-Party Dependencies)
- **依赖范围**: 仅允许引用 .NET 4.0 核心程序集（`mscorlib.dll`, `System.dll`, `System.Drawing.dll`, `System.Windows.Forms.dll`）。
- **扩展方式**: 任何操作系统底层交互（DWM 暗色主题、RawInput 鼠标唤醒、USB 即插即用、注册表配置、系统通知）必须通过标准 Win32 P/Invoke 声明在 `src/Core/NativeMethods.cs` 或对应底层驱动中实现。严禁引入任何 NuGet 包或第三方 DLL。

---

## 3. 文件编码与中文字符集安全 (UTF-8 with BOM & Codepage 65001)
- **文件编码**: 所有代码文件（`.cs`, `.bat`, `.md`, `.ps1`）必须以 **UTF-8 带签名 (BOM)** 格式保存。
- **编译参数**: `build.bat` 中必须始终包含 `/codepage:65001` 参数。
- **防止乱码**: 严禁使用不带 BOM 的 ANSI 或普通 UTF-8 编辑器保存包含中文提示与注释的文件。

---

## 4. 模块化工程架构规范 (Modular Architecture)
- **禁止单文件巨石化**: 严禁将新增功能合并回单一 `.cs` 文件。所有代码必须归入 `src\` 下对应分层：
  - `src/Core/`: 系统级底层支撑、P/Invoke、注册表配置、自启动管理器、程序主入口。
  - `src/Hardware/`: 品牌 HID 硬件协议、外设通信助手、设备调度总线。
  - `src/Models/`: 强类型数据模型与枚举定义。
  - `src/Services/`: 后台长效服务（全屏游戏侦测、声音合成）。
  - `src/UI/`: 界面主题体系、原子级自定义控件、二级弹窗及 `MainForm` 分部类。
- **MainForm 分部类协作**:
  - `MainForm.cs`: 核心字段声明、构造中枢、窗口生命周期。
  - `MainForm.UI.cs`: 控件装配、卡片折叠动画、对话框路由。
  - `MainForm.Tray.cs`: 任务栏托盘、右键菜单、GDI+ 矢量图标绘制。
  - `MainForm.Hardware.cs`: 硬件轮询分发、DPI实时监听、游戏回报率感知。
  - `MainForm.Events.cs`: 系统消息 `WndProc`、USB 即插即用、RawInput 鼠标唤醒。

---

## 5. 任务栏与开机自启动守护机制 (Startup & Taskbar Resilience)
- **自启动三重对齐**: 开机自启动由 `StartupManager.cs` 统一管理，必须同步处理：
  1. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  2. `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`（清除 Windows 11/10 任务管理器启动禁用标志）
  3. `Shell:Startup` 启动文件夹快捷方式冗余保障。
- **任务栏重建自愈**: Windows 资源管理器（`explorer.exe`）重启或系统启动缓慢时，Windows 会广播 `WM_TASKBARCREATED`。`MainForm.Events.cs` 的 `WndProc` 中必须侦测该消息并立即调用 `ReinitializeTrayIcon()` 自动重建托盘图标。
- **进程工作目录重置**: `Program.cs` 启动首行必须调用 `Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory`，杜绝因自启动时工作目录落在 `C:\Windows\System32` 导致相对路径资源加载失败的问题。
- **绿色免安装纯净桌面原则**: 严禁在软件启动（手动或自启）时自动静默向用户桌面创建快捷方式。桌面图标仅允许用户在托盘右键菜单中主动点击触发创建。

---

## 6. 维护准则注释保留与架构索引同步
- **头部准则**: 每个代码文件头部必须永久保留：
  `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️`
- **文档同步**: 任何新增模块、公共方法调整或协议变更，必须同步维护 `ARCHITECTURE.md` 目录。

---

## 7. 工作区聚焦与文件系统卫生规范 (Workspace Focus & Repository Cleanliness)
- **工作区唯一锁定**:
  - 本项目唯一有效工作区为 **`D:\FerrisPulse · 灵脉\`**。
  - 严禁向已弃用的 `proud-hertz` 或其他外部目录写入任何代码、文档或脚本。
- **根目录纯净化 (Zero Scatter in Root)**:
  - 根目录只允许保留核心工程标准文件：
    `README.md`, `CHANGELOG.md`, `LICENSE`, `ARCHITECTURE.md`, `AGENT_RULES.md`, `AGENTS.md`, `build.bat`, `.gitignore`, `version.json`, `app.ico`, `app.png`。
  - 严禁在根目录下随意倾倒任何临时测试图片（`*.png`, `*.jpg`）、调试代码（`*.py`, `*.cs`）、文本日志（`*.txt`, `*.log`）或历史巨石代码。
- **零二进制提交铁律 (Zero Binary Commit Policy)**:
  - **严禁向 Git 版本库提交任何二进制编译产物**（包括 `*.exe`, `*.zip`, `*.dll`, `*.pdb`, `*.raw.exe`, `Obfuscated/` 等）。
  - 编译产生的二进制仅在本地用于运行调试，软件公开发布一律通过 GitHub Releases 附件上传，彻底消除 Git 仓库体积膨胀。
- **工程分类目录严密归属 (Folder Taxonomy)**:
  - `src/`：生产环境代码（严格分层）。
  - `tests/`：所有自动化测试用例（统一以 `test_*.ps1` 命名）。
  - `tools/`：核心构建与维护脚本（`obfuscar/`, `build_release_zip.ps1`, `setup_obfuscator.ps1`, `ensure_bom.ps1`, `run_all_tests.ps1`）。
  - `tools/scratch/`：临时硬件抓包、逆向草稿脚本（本地保留，Git 忽略）。
  - `docs/`：公开的技术方案设计、架构规范与常见问题。
  - `promo/`：宣传视频脚本、文案草稿与录制工具（本地保留，Git 忽略）。
  - `archive/`：历史巨石代码备份与归档素材（本地保留，Git 忽略）。
  - `resources/`：官方矢量图标与赞助二维码等正式只读资源。
- **智能体收工自省 (Agent Self-Clean Duty)**:
  - 任何 AI Agent 在宣布任务完成前，必须执行 `git status` 审查工作区，主动清除临时文件，绝不允许将 scratch 垃圾遗留给用户。

---

## 8. DWM 原生弹窗动效与窗口状态机准则 (Native DWM Animation & White Flash Prevention)
- **严禁 AnimateWindow**: `AnimateWindow` 属于传统 GDI 模拟，在 Win10/Win11 DWM 硬件合成架构下会触发离屏内存擦除，导致深色背景闪现刺眼的白色背景框，严禁在主窗体使用。
- **RecreateHandle 状态机重塑**: 托盘呼出时必须调用 `this.RecreateHandle()` 销毁旧句柄并重新生成原生句柄，触发 DWM 将其识别为新窗口诞生，100% 重现原生居中缩放微光展开动效。
- **坐标绝对锁定与底层擦除拦截**: 严格使用 `StartPosition = FormStartPosition.Manual` 原位展开；在 `WndProc` 中拦截 `WM_ERASEBKGND` 并重写 `OnPaintBackground`，彻底阻绝任何浅色底色闪烁。

---

## 9. 键盘 HID 0 权限握手与多通道隔离铁律 (Win32 Keyboard Security & Device Isolation)
- **0 权限握手规避内核排他保护**: Windows 内核对键盘类设备有严格的防监听独占锁保护。使用 `GENERIC_READ` 打开键盘集合句柄会导致 `ERROR_ACCESS_DENIED` (5) 拒绝访问。打开键盘 HID 句柄必须传入权限参数 `0`（即 `FILE_READ_ATTRIBUTES`），方可安全执行 `HidD_GetFeature` 报文握手读取物理电量。
- **键鼠同接收器独立分类管理**: 针对一个 2.4G 接收器同时连接键盘与鼠标的多模外设，`DeviceManager` 必须按 `DeviceCategory.Mouse` 与 `DeviceCategory.Keyboard` 实行物理通道隔离，严禁状态缓存互相覆盖。

---

## 10. 自动化测试防锁死铁律 (Zero-Lock Byte-Array Reflection Loading)
- **严禁 Add-Type 路径独占**: 测试脚本严禁使用 `Add-Type -Path "FerrisPulse.exe"`，否则当前 PowerShell 宿主进程将独占持有可执行文件句柄，导致后续 `build.bat` 出现 `CS0016: Could not write to output file 'FerrisPulse.exe'` 致命编译冲突。
- **强制全内存反射加载**: 必须使用 `[System.IO.File]::ReadAllBytes` 将二进制以纯字节流方式载入内存，再调用 `[System.Reflection.Assembly]::Load($bytes)` 反射解析，确保文件读取完毕后句柄瞬间释放，实现零冲突持续编译与测试。

---

## 11. Antigravity 平台终端路径注入避坑铁律 (Platform Path Injection & Local Build Safety)
- **底层异常特征**: Windows 环境下工程路径包含中文或特殊字符（如 `·`）时，平台会话重载可能偶发将底层终端工作目录注入为带前导斜杠和引号的非法路径（如 `/"D:/FerrisPulse · 灵脉"`），导致 `run_command` 抛出 `Command failed to run: ...: no such directory.`。
- **严禁死锁等待**: Agent 严禁在遭遇此报错时重复派生后台任务死循环等待；必须及时告知用户，引导在宿主环境终端执行 `cmd.exe /c "build.bat"` 或双击 `build.bat` 进行原生秒级编译。
