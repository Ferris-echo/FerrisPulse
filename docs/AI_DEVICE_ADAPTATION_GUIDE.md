# 🤖 AI 智能体专属外设适配指南 / AI Agent Device Adaptation Guide

> **致所有 FerrisPulse 用户与开源极客**：  
> 市场上外设品牌与型号千差万别，仅靠开发者逐个适配已难以满足大家日益丰富的外设需求。  
> **找不到你的设备？试试自己做自己的开发者！**  
> 借助本地 AI Agent（如 DeepSeek Harness、Workbuddy、Antigravity、Codex 等），可以让智能体自动为你探测硬件通信、分析报文并编写适配代码。完成适配后，诚挚邀请你将代码共享回主线，共同打造最强跨品牌桌面引擎！

---

## 目录 / Table of Contents
1. [🇨🇳 中文指南与 AI 适配提示词](#-中文指南与-ai-适配提示词)
   - 1.1 推荐 AI Agent 工具
   - 1.2 复制给 AI Agent 的完整适配提示词
   - 1.3 如何向主线贡献代码
2. [🌐 English Guide & AI Prompt](#-english-guide--ai-prompt)
   - 2.1 Recommended AI Agent Tools
   - 2.2 Full Prompt for Your AI Agent
   - 2.3 How to Contribute Back

---

## 🇨🇳 中文指南与 AI 适配提示词

### 1.1 推荐 AI Agent 工具
你可以选用任意支持本地命令执行、文件读写与网络检索的智能体工具：
- 🤖 **DeepSeek Harness (dsh)**: 开源轻量级智能体运行时，支持本地命令与工具链自动化调度。  
  🔗 官方入口: [https://www.deepseek.com/harness/](https://www.deepseek.com/harness/)
- 🤖 **Tencent Workbuddy**: 腾讯桌面级自主智能体工作台，内置编码环境与本地自主规划。  
  🔗 官方入口: [https://workbuddy.ai/](https://workbuddy.ai/)
- 🤖 **Antigravity (Google DeepMind)**: 新一代自主编程与硬件调试智能体。 *(提示: 需科学上网)*  
  🔗 官方入口: [https://antigravity.google/](https://antigravity.google/)
- 🤖 **OpenAI Codex**: 深度集成的代码智能体开发环境。 *(提示: 需科学上网)*  
  🔗 官方入口: [https://openai.com/zh-Hans-CN/codex/](https://openai.com/zh-Hans-CN/codex/)

---

### 1.2 复制给 AI Agent 的完整适配提示词

直接复制以下 Markdown 内容，完整发送给你的 AI Agent：

````markdown
# 任务：为开源软件「FerrisPulse (灵脉)」专属适配我正在使用的硬件外设

你好！我现在使用的外设设备暂未被「FerrisPulse (灵脉)」原生识别。请你作为专业的系统级硬件逆向与 C# 智能体工程师，帮助我完成当前电脑上连接设备的电量监控与性能适配。

## ⚠️ 第 1 步必读：继承工程经验（杜绝踩坑）
在动工编写任何代码之前，**必须首先通读工程根目录下的 `docs/AGENT_HANDBOOK.md` 与 `AGENT_RULES.md`**！
本项目前序 AI 智能体已经沉淀了 11 大底层铁律与血泪经验（包括严格 C# 5.0 / .NET 4.0 语法基准、Win32 键盘 HID 0 权限握手、DWM 原生窗口抗白框状态机、测试全内存反射防句柄锁死等）。请务必全盘继承前序经验，切勿重复踩坑！

## 2. 获取工程源码
请使用 git 从以下官方仓库克隆最新完整源码：
- GitHub (首选): https://github.com/Ferris-echo/FerrisPulse.git
- Gitee (镜像): https://gitee.com/Ferris-echo/ferris-pulse.git

## 3. 探测我当前物理连接的硬件设备
请利用你的系统工具（如 PowerShell / Win32 SetupAPI / HID 接口查询等），自动检索我当前电脑接入的外设：
1. 找出未被识别外设的 Vendor ID (VID)、Product ID (PID)、Usage / UsagePage 以及设备完整物理路径。
2. 识别该外设的产品品牌、型号与连接形态（2.4G 接收器 / 蓝牙 BLE / 有线 USB）。

## 4. 逆向与通信协议检索（发挥你的联网能力）
请通过联网检索、开源社区逆向成果（如 OpenRGB、libratbag、Linux 内核驱动源码、hidapi、抓包分析等）查询该型号的私有通信报文：
- **核心目标（必选）**：实现精准电池电量读取（Battery Level 0-100%）与充电状态识别。
- **扩展目标（如果是鼠标）**：探测并尝试实现 DPI 档位读取/设置、X/Y 轴独立双轴调节、以及回报率（Hz）切换指令。

## 5. 遵守项目的开发铁律与架构规范
代码实现必须严格遵循：
1. **编译器基准**：严格使用 C# 5.0 语法（严禁使用 C# 6.0+ 语法，如 `$"..."`、`?.`、`nameof`），必须能够使用系统自带 `csc.exe` 通过根目录的 `build.bat` 零报错编译。
2. **零外部依赖**：仅允许引用 .NET 4.0 核心程序集，通信采用 Win32 HID P/Invoke，严禁引入 NuGet 包或第三方 DLL。
3. **文件编码安全**：所有新建或修改的源码文件必须严格保存为【UTF-8 with BOM】签名。
4. **模块化落位**：
   - 若为已存在品牌（如 Razer, Logitech, Rapoo, Valkyrie 等）：在 `src/Hardware/<Brand>DeviceHelper.cs` 中增加该型号的识别与报文解析。
   - 若为全新品牌：在 `src/Hardware/` 下新建 `<Brand>DeviceHelper.cs`，并在 `src/Hardware/DeviceManager.cs` 中统一注册接入。
   - 适配完成后，设备分类、电量与支持的特性无缝对接到主界面 UI（`MainForm`）与 OSD。

## 6. 本地编译与回归测试验证
在源码根目录下执行 `cmd.exe /c "build.bat"` 验证编译是否成功生成 `FerrisPulse.exe`，并运行相关自动化测试确保没有回归问题。

## 7. 引导我向作者贡献代码（开源知识飞轮）
适配成功并完成实测后，请为我整理一份简洁的贡献摘要（包含修改了哪些文件、适配了什么型号、设备 VID/PID 与报文细节），并引导我通过以下途径之一分享给作者合并入官方主线：
- 方式 A：提交 GitHub Pull Request / Issue (https://github.com/Ferris-echo/FerrisPulse)
- 方式 B：提交 Gitee Pull Request / Issue (https://gitee.com/Ferris-echo/ferris-pulse)
- 方式 C：将代码变更或 patch 文件通过邮件/文本形式共享给作者
作者会将你的适配成果永久纳入官方支持库，并在更新日志（CHANGELOG）与关于页面中为你署名致谢！
````

---

### 1.3 如何向主线贡献代码
- **GitHub Pull Request**: Fork `https://github.com/Ferris-echo/FerrisPulse`，创建分支并提交 PR。
- **Gitee Pull Request**: Fork `https://gitee.com/Ferris-echo/ferris-pulse`，提交 PR。
- **提交 Issue**: 如果你不会使用 Git，可直接在 GitHub / Gitee 的 Issue 页面贴上你修改的代码文件或 diff，作者会代为合入。

---

## 🌐 English Guide & AI Prompt

### 2.1 Recommended AI Agent Tools
You can use any local or autonomous coding agent with shell execution, file system access, and web search capabilities:
- 🤖 **DeepSeek Harness (dsh)**: Open-source agent runtime with modular toolchain execution.  
  🔗 Official: [https://www.deepseek.com/harness/](https://www.deepseek.com/harness/)
- 🤖 **Tencent Workbuddy**: Desktop autonomous agent workspace supporting autonomous coding.  
  🔗 Official: [https://workbuddy.ai/](https://workbuddy.ai/)
- 🤖 **Antigravity (Google DeepMind)**: Next-gen autonomous coding and hardware debugging agent. *(Note: VPN/Proxy may be required depending on region)*  
  🔗 Official: [https://antigravity.google/](https://antigravity.google/)
- 🤖 **OpenAI Codex**: Deeply integrated coding agent and developer tools. *(Note: VPN/Proxy may be required depending on region)*  
  🔗 Official: [https://openai.com/zh-Hans-CN/codex/](https://openai.com/zh-Hans-CN/codex/)

---

### 2.2 Full Prompt for Your AI Agent

Copy the following Markdown prompt and paste it directly to your AI Agent:

````markdown
# Task: Adapt My Peripheral Hardware for Open-Source "FerrisPulse"

Hello! My current peripheral device is not yet natively recognized by "FerrisPulse". As an expert system-level reverse engineer and C# agent engineer, please help me adapt battery level monitoring and performance features for the hardware currently connected to this PC.

## ⚠️ Step 1 (Mandatory): Inherit Project Knowledge & Avoid Known Traps
Before writing any code, **you MUST thoroughly read `docs/AGENT_HANDBOOK.md` and `AGENT_RULES.md` in the repository root**!
Previous AI agents have established 11 core golden rules and hard-won lessons (strictly C# 5.0 / .NET Framework 4.0 `csc.exe` baseline, Win32 keyboard HID 0-permission handshake, native DWM anti-flash window state machine, in-memory reflection test assembly to prevent CS0016 file locks, etc.). You must inherit these findings and never repeat known mistakes!

## 2. Clone the Source Repository
Clone the latest source code using git:
- GitHub (Preferred): https://github.com/Ferris-echo/FerrisPulse.git
- Gitee (Mirror): https://gitee.com/Ferris-echo/ferris-pulse.git

## 3. Probe the Physically Connected Hardware
Use system tools (e.g., PowerShell, Win32 SetupAPI, HID queries) to inspect devices plugged into this PC:
1. Identify the unrecognized peripheral's Vendor ID (VID), Product ID (PID), Usage / UsagePage, and physical device path.
2. Identify product brand, exact model, and transport type (2.4GHz dongle / Bluetooth BLE / USB wired).

## 4. Reverse-Engineering & Protocol Search
Use web search and open-source references (such as OpenRGB, libratbag, Linux kernel drivers, hidapi, USB packet captures) to find the proprietary communication packets:
- **Core Goal (Required)**: Accurate battery percentage reading (0-100%) and charging status.
- **Extended Goal (If Mouse)**: DPI stage reading/setting, independent X/Y axis DPI, and polling rate (Hz) switching.

## 5. Follow Architectural & Coding Directives
All code changes must strictly adhere to:
1. **Compiler Baseline**: Pure C# 5.0 (.NET Framework 4.0). No C# 6.0+ syntax (NO `$"..."`, NO `?.`, NO `nameof`). Must compile cleanly via `build.bat` using Windows built-in `csc.exe`.
2. **Zero External Dependencies**: Only .NET 4.0 core framework assemblies. Low-level communication uses Win32 HID P/Invoke. No NuGet packages, no third-party DLLs.
3. **File Encoding Safety**: All new or modified files must be saved with UTF-8 BOM (`EF BB BF`).
4. **Modular Architecture**:
   - Existing brand: add model detection & packet parsing in `src/Hardware/<Brand>DeviceHelper.cs`.
   - New brand: create `src/Hardware/<Brand>DeviceHelper.cs` and register it in `src/Hardware/DeviceManager.cs`.
   - Connect category, battery, and supported capabilities to `MainForm` UI and OSD.

## 6. Build & Regression Testing
Run `cmd.exe /c "build.bat"` to ensure `FerrisPulse.exe` compiles with 0 errors and 0 warnings. Run automated PowerShell test suites to ensure zero regressions.

## 7. Guide Me to Contribute Back (Knowledge Flywheel)
Once verified, summarize the changes (files modified, model name, VID/PID, protocol details) and guide me to share the code with the project author via:
- Method A: GitHub Pull Request / Issue (https://github.com/Ferris-echo/FerrisPulse)
- Method B: Gitee Pull Request / Issue (https://gitee.com/Ferris-echo/ferris-pulse)
- Method C: Share patch file or code diff via email/text
The author will merge your contribution into official releases and credit you in the CHANGELOG and About dialog!
````

---

### 2.3 How to Contribute Back
Submit a Pull Request or Issue on GitHub (`https://github.com/Ferris-echo/FerrisPulse`) or Gitee (`https://gitee.com/Ferris-echo/ferris-pulse`). Even sharing code snippets or diffs in an Issue helps!
