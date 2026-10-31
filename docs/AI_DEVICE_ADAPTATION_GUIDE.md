# 🤖 AI 智能体专属外设适配指南 / AI Agent Device Adaptation Guide

> **致所有 FerrisPulse 用户与开源极客**：  
> 市场上外设品牌与型号千差万别，仅靠开发者逐个适配已难以满足大家日益丰富的外设需求。  
> **找不到你的设备？试试自己做自己的开发者！**  
> 借助本地 AI Agent（如 DeepSeek Harness、Workbuddy、Antigravity、Codex 等），可以让智能体自动为你探测硬件通信、分析报文并编写适配代码。完成适配后，诚挚邀请你将代码共享回主线，共同打造最强跨品牌桌面引擎！

---

## 目录 / Table of Contents
1. [🇨🇳 中文指南与 AI 适配提示词](#-中文指南与-ai-适配提示词)
   - 1.1 推荐 AI Agent 工具
   - 1.2 复制给 AI Agent 的完整适配提示词 (FDAS v2.0 / .fp-driver.cs 最新标准)
   - 1.3 如何向主线贡献代码
2. [🌐 English Guide & AI Prompt](#-english-guide--ai-prompt)
   - 2.1 Recommended AI Agent Tools
   - 2.2 Full Prompt for Your AI Agent (FDAS v2.0 / .fp-driver.cs Latest Standard)
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

### 1.2 复制给 AI Agent 的完整适配提示词 (软件最新同款)

在软件「添加设备向导」中点击“复制 AI 适配提示词”即可自动注入你的硬件物理指纹；如果你手动向 AI 发送指令，可直接复制以下完整提示词：

````markdown
# 任务：为「FerrisPulse (灵脉)」开源外设系统生成 FDAS v2.0 / .fp-driver.cs 硬件适配扩展包

你好！我现在使用的外设设备暂未被「FerrisPulse (灵脉)」原生识别。请作为资深系统级硬件逆向与固件协议工程师，根据以下从当前物理硬件中捕获的硬件现场指纹与通信拓扑，为我输出一份符合 **FDAS v2.0 (FerrisPulse Device Adapter Specification)** 标准（向下兼容 FDAS v1.0）的适配规范包（`.fp-adapter.json`）或动态 C# 5.0 驱动脚本扩展包（`.fp-driver.cs`）。

## ⚠️ 核心工作流：两阶段严谨逆向法（物理事实优先，严禁幻觉）
你必须严格按以下两阶段开展工作：

### 阶段一：硬件物理安全第一与官方 WebHID 源码逆向 (Safety First)
1. **严厉安全红线：严禁在真机上下发盲测性质的写指令 (如 Cmd 0x07、写未知内存地址)**！
   - 现代国产无线外设（如 CompX、PixArt 等主控）多将单片机板载 EEPROM / Flash 直接映射为写指令。
   - 盲猜写指令或写错地址/长度，会瞬间冲毁 2.4G RF 配对表、校验扇区或引导记录，导致接收器硬件彻底死锁变砖！
2. **官方 WebHID 源码逆向法（首选且唯一推荐的写指令来源，100% 零风险）**：
   - 现代外设厂商（VGN, ATK, VXE, 迈从, 达摩鲨, 狼蛛, 雷神, 机械师等）90% 均提供官方 Web 驱动（Chrome 浏览器网页端，如 hub.vgn.cn、hub.atk.pro 等）。
   - 请优先让用户或你在浏览器打开该网页驱动，按 F12 打开开发者工具：在 Sources / Network 面板中检索 `sendReport`、`write`、`0x07`、`0x55`、`rate`、`dpi` 等关键词；
   - 直接将官方前端工程师编写的报文生成逻辑与公式提取出来！这套公式是原厂 100% 验证过的安全逻辑，绝不会损害硬件！
3. **探针安全只读原则**：本工程根目录提供了 `tools/probe_hid_device.ps1`，默认仅允许执行只读探针或白名单安全电量查询。

### 阶段二：FDAS v2.0 声明式扩展包与动态微表达式 (Dynamic Calculation)
FerrisPulse 底层已内置轻量微表达式求值器与 `DriverStateStore` 统一注册表缓存，无需改动主程序即可实现内置包同等待遇：
1. **FDAS v2.0 多 PID 矩阵与主题契约 (`metadata.brandColor` / `hardware.devices`)**：支持声明有线 USB 与 2.4G 接收器多 PID。
2. **动态派生变量 (`protocol.variables`)**：支持基础四则运算、移位、位掩码与互补校验。
3. **私有回报率编码映射 (`protocol.rateCodeMap`)**：如 `"rateCodeMap": { "125": "0x08", "1000": "0x01", "8000": "0x40" }`
4. **多步时序握手管线 (`protocol.queryBatteryPipeline`)**：支持多步查询序列。
5. **全套五档批量写入与自定义硬件动作 (`capabilities.customActions`)**：支持 Toggle / Slider / Button 控件。

## 🛡️ 逃生舱机制：`.fp-driver.cs` 动态 C# 5.0 驱动脚本包与源码深度定制授权
如果该设备的物理协议极其复杂，超出了声明式 JSON 的表达边界：直接输出继承自 `RazerBatteryTray.HardwareDriverBase` 的 C# 5.0 驱动类，优先级（Priority 300）高于内置驱动。同时也明确授权你直接深入修改 FerrisPulse 本地 C# 源码。

## 🤝 开源生态飞轮：社区反哺指引
成功适配后请输出《灵脉设备适配成果报告》并引导提交至 GitHub/Gitee Issues 或邮箱投递。
1. GitHub Issues / PR: https://github.com/Ferris-echo/FerrisPulse/issues
2. Gitee Issues / PR: https://gitee.com/Ferris-echo/FerrisPulse/issues
3. 作者个人邮箱直连投递: 2949225840@qq.com 或 2949225840zpf@gmail.com

## 2. 官方源码规范索引
- GitHub: https://github.com/Ferris-echo/FerrisPulse.git
- Gitee: https://gitee.com/Ferris-echo/ferris-pulse.git
- 规范文档: docs/ADAPTER_SPECIFICATION.md
- 避坑指南: docs/AGENT_HANDBOOK.md & AGENT_RULES.md

## 3. 当前硬件现场物理指纹（请在下方填入你抓取到的硬件参数）：
- 设备推测分类: Mouse / Keyboard / Headset
- 用户参考名称: [你的外设具体型号]
- 物理硬件 ID: VID = 0x[xxxx], PID = 0x[xxxx]
- 芯片 ROM 固件原名: [固件原名]
- 连接物理通道: 2.4G 无线 / USB 有线

## 4. 标准化示例参考（Razer DeathAdder V4 Pro 旗舰双模鼠标 FDAS v2.0 范例）
```json
{
  "fdasVersion": "2.0",
  "metadata": {
    "brand": "Razer",
    "model": "DeathAdder V4 Pro",
    "category": "Mouse",
    "brandColor": "#00FF00",
    "sensorDescription": "Focus Pro 35K Gen-2 · 1-DPI 精准步进 (100 ~ 35,000 DPI)",
    "author": "FerrisPulse Official"
  },
  "hardware": {
    "vendorId": "0x1532",
    "productId": "0x00BE",
    "devices": [
      { "productId": "0x00BE", "model": "Razer DeathAdder V4 Pro (2.4G)", "transport": "2.4G 无线", "maxRate": 8000 },
      { "productId": "0x00BF", "model": "Razer DeathAdder V4 Pro (Wired)", "transport": "USB 有线", "maxRate": 8000 }
    ],
    "endpointTopology": { "usagePage": "0x0001", "usage": "0x0002", "reportType": "Feature", "reportLength": 91 }
  },
  "capabilities": {
    "battery": {
      "reportId": "0x00", "batteryPercentByteOffset": 10, "chargingByteOffset": 9, "chargingBitMask": "0x01",
      "batteryFormula": "(b[10] * 100) / 255",
      "sampleRxHex": "00 02 1F 00 00 00 02 07 80 01 CC 00 00 00 00 00 00 00 00 00"
    },
    "mouse": {
      "dpi": { "minDpi": 100, "maxDpi": 35000, "step": 1, "defaultStages": [400, 800, 1600, 3200, 6400], "supportsXyIndependence": true },
      "pollingRate": { "supportedRates": [125, 250, 500, 1000, 2000, 4000, 8000], "defaultRate": 1000, "maxRate": 8000 }
    }
  },
  "protocol": {
    "queryBatteryTxHex": "00 00 1F 00 00 00 02 07 80 00 00 00 00 00 00 00 00 00 00 00",
    "setDpiTxTemplate": "00 00 1F 00 00 00 07 04 05 01 {stage} {dpiX_hi} {dpiX_lo} {dpiY_hi} {dpiY_lo} 00 00",
    "setPollingRateTxTemplate": "00 00 1F 00 00 00 02 00 40 01 {rateIndex} 00 00 00 00 00",
    "checksumType": "XorSum"
  }
}
```
````

---

### 1.3 如何向主线贡献代码
- **GitHub Pull Request**: Fork `https://github.com/Ferris-echo/FerrisPulse`，创建分支并提交 PR。
- **Gitee Pull Request**: Fork `https://gitee.com/Ferris-echo/ferris-pulse`，提交 PR。
- **提交 Issue**: 如果你不会使用 Git，可直接在 GitHub / Gitee 的 Issue 页面贴上你修改的代码文件或生成的 `.fp-adapter.json`，作者会代为合入。

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

### 2.2 Full Prompt for Your AI Agent (FDAS v2.0 / .fp-driver.cs)

````markdown
# Task: Generate FDAS v2.0 / .fp-driver.cs Hardware Adapter for "FerrisPulse"

Hello! My current peripheral device is not yet recognized by "FerrisPulse". As a senior system-level hardware reverse engineer and firmware protocol expert, please generate an adapter package conforming to the **FDAS v2.0 (FerrisPulse Device Adapter Specification)** (`.fp-adapter.json`) or dynamic C# 5.0 driver script (`.fp-driver.cs`) based on the captured physical device fingerprint.

## ⚠️ Core Workflow: Two-Phase Rigorous Reverse Engineering (Physical Facts First)

### Phase 1: Hardware Safety & Official WebHID Source Inspection (Safety First)
1. **Strict Safety Red Line: NEVER send speculative raw write commands (e.g. Cmd 0x07, unknown memory offsets) to actual physical hardware!**
   - Modern gaming controllers (CompX, PixArt MCU) map onboard EEPROM/Flash directly to write commands. Speculative writes can corrupt 2.4G RF pairing tables or calibration sectors, permanently bricking the receiver!
2. **Official WebHID Source Inspection (Primary & Safe Method)**:
   - 90% of gaming peripheral vendors (VGN, ATK, VXE, MCHOSE, Darmoshark, AULA, etc.) provide official Web Drivers (Chrome browser, e.g. hub.vgn.cn, hub.atk.pro).
   - Open the web driver in browser, press F12, and search for `sendReport`, `write`, `0x07`, `rate`, `dpi` in Sources/Network. Extract the exact payload formulas designed by the vendor engineers!

### Phase 2: FDAS v2.0 Declarative Adapter & Dynamic Expressions
FerrisPulse provides a built-in micro-expression evaluator and unified `DriverStateStore` cache:
1. Multi-PID matrix & brand theme (`metadata.brandColor` / `hardware.devices`)
2. Dynamic derived variables (`protocol.variables`)
3. Vendor polling rate mappings (`protocol.rateCodeMap`)
4. Multi-step query pipeline (`protocol.queryBatteryPipeline`)
5. Custom hardware actions (`capabilities.customActions`)

## 🛡️ Escape Hatch: Dynamic C# 5.0 Driver Script (`.fp-driver.cs`)
If the proprietary protocol is too complex for declarative JSON, implement a class inheriting from `RazerBatteryTray.HardwareDriverBase` (Priority 300), which overrides built-in drivers without recompilation.
````

---

### 2.3 How to Contribute Back
- **GitHub Pull Request**: Fork `https://github.com/Ferris-echo/FerrisPulse`, submit a PR with your `.fp-adapter.json` or `.fp-driver.cs`.
- **Gitee Pull Request**: Fork `https://gitee.com/Ferris-echo/ferris-pulse`.
- **Open an Issue**: Attach your JSON or driver code to GitHub/Gitee Issues, and it will be merged into the official release with full credit!
