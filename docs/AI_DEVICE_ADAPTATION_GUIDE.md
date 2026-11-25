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

在软件「添加设备向导」中点击“复制 AI 适配提示词”即可自动注入你的硬件物理指纹与驱动诊断轨迹；如果你手动向 AI 发送指令，可直接复制以下完整提示词：

````markdown
# 任务：为「FerrisPulse (灵脉)」外设系统生成零幻觉硬件适配扩展包 (FDAS v2.0 / .fp-driver.cs)

你好！我当前有一款外设暂未被「FerrisPulse (灵脉)」原生识别。请你作为 Windows HID 协议与固件逆向工程师，严格依据第 1 节提供的硬件指纹与驱动诊断上下文，遵循第 2 节的零幻觉工作流与安全红线，输出可直接导入主程序的 **FDAS v2.0** 规范包（`.fp-adapter.json`）或 **C# 5.0** 动态驱动脚本（`.fp-driver.cs`）。

## 1. 现场硬件指纹与驱动诊断上下文（请在下方填入你抓取到的硬件参数）
- 设备推测分类: Mouse / Keyboard / Headset
- 用户参考名称: [你的外设具体型号]
- 物理硬件 ID: VID = 0x[xxxx], PID = 0x[xxxx]
- 芯片 ROM 固件原名: [固件原名]
- 连接物理通道: 2.4G 无线 / USB 有线
- HID 通信拓扑端点列表: [UsagePage / Usage / Input / Output / Feature 长度]
- 驱动探测轨迹 (Driver Trace): [来自软件诊断报告或本地探测的日志]

## 2. 零幻觉工作流与硬件安全红线（必须严格遵守）
1. **缺口即停规则（Zero-Hallucination Gate）**：
   - 如果当前对话与上述指纹中**没有**经过验证的真实协议证据（例如：官方 WebHID 前端源码、OpenRazer/libratbag/开源项目文档、或实机 USBPcap 抓包的 `TX`/`RX` 字节序列），**请立刻停止并明确告知缺失哪些信息**。
   - 绝对禁止为了凑齐代码而编造 `queryBatteryTxHex`、猜测 Report ID / 字节偏移、或虚构 `sampleRxHex`。
   - 缺失协议事实时，按以下顺序指导用户采集真实数据后再继续：
     1) **Chrome WebHID F12 逆向（首选）**：打开厂商网页驱动（如 `hub.atk.pro`、`hub.vgn.cn`、`hub.mchose.com.cn` 等）-> 按 F12 在 Sources/Network 检索 `sendReport`、`sendFeatureReport`、`oninputreport`，提取真实报文构造与电量解析公式；
     2) **本地只读探针**：在工程根目录运行 `powershell -ExecutionPolicy Bypass -File tools/probe_hid_device.ps1`（只读收集描述符与安全白名单电量，自动拦截写入）；
     3) **Wireshark + USBPcap 抓包**：开启官方驱动读取电量的瞬间抓包，提取真实 `TX`/`RX` Hex 帧与校验和算法。
2. **只读优先与禁盲写 EEPROM 红线**：
   - **严禁盲测写指令（如 `0x07`、DPI/回报率/灯效写入、未知 Feature 写入）**：国产无线主控（CompX、PixArt 等）常直接将写指令映射至片上 EEPROM/Flash，猜错偏移或校验和可能损坏 2.4G 配对表或导致接收器变砖。
   - 未获取 100% 实证的写指令前，**只实现电量与充电状态只读查询**：在 JSON 中省略未验证的写模板并将 `supportsDpi`/`supportsPollingRate` 置为 `false`；在 `.fp-driver.cs` 中不要重写 `SetDpi` / `SetPollingRate`。
3. **Windows HID 底层避坑规则**：
   - 键盘类 HID 句柄必须以权限 `0`（仅属性）或 `HidTransport.OpenFeature` 避开 Windows 内核防监听独占锁；
   - 过滤 `HIDI2C` 内置触控板与虚拟 HID 设备；
   - 共用公版 VID（如 CompX `0x3554`）必须同时校验 `VID + PID + UsagePage + Usage + Input/Output 长度` 隔离设备；
   - 8K 高回报率鼠标/接收器严禁盲目死循环 `DrainReports`，控制读取次数防止阻塞。

## 3. 扩展包选型与契约规范（二选一）
### 方式 A：FDAS v2.0 声明式扩展包（保存为 `{brand}_{model}.fp-adapter.json`，常规单帧协议首选）
- 适用场景：通过单条或顺序管线报文查询电量，配合公式/掩码即可完成解析。
- 核心字段：`fdasVersion` (`"2.0"`)、`metadata` (`brand`/`model`/`category`/`brandColor`/`author`)、`hardware` (`vendorId`/`productId`/`devices` 多 PID 矩阵/`endpointTopology`)、`capabilities` (`battery` 含 `reportId`/`batteryPercentByteOffset`/`chargingByteOffset`/`chargingBitMask`/`batteryFormula`/`sampleRxHex`，可选 `mouse`/`customActions`)、`protocol` (`queryBatteryTxHex` 或 `queryBatteryPipeline`、`variables`、`rateCodeMap`、`checksumType`: `None`/`SumMod256`/`VgnCompxSum55`/`XorSum`/`SonyInzone`)。

### 方式 B：C# 5.0 动态驱动脚本扩展包（保存为 `{brand}_{model}.fp-driver.cs`，复杂协议使用）
- 适用场景：需要多步状态机握手、BUSY 重试、共享接收器拆分子设备、或自定义二进制解码。Priority 300 高于内置驱动。
- **严格 C# 5.0 / .NET Framework 4.0 编译约束（必须遵守）**：
  - **严禁使用 C# 6+ 语法**：禁止 `$"..."` 字符串插值、禁止 `?.` / `??=`、禁止 `nameof`、禁止 `out var`、禁止 `=>` 表达式体成员（必须用 `{ get { return ...; } }` 与 `string.Format`）。
- **宿主可用核心 API**：
  - 继承 `RazerBatteryTray.HardwareDriverBase`：必须实现 `DriverId`、`Brand`，建议重写 `IsExternalPackage { get { return true; } }`、`MatchesVidPid(int vid, int pid)`、`PopulateCapabilities(MouseBatteryInfo dev)`、`QueryDevices()`。
  - HID 通信使用 `HidTransport`：`Enumerate(int vid, int pid)`、`Open(path)`、`OpenRead(path)`、`OpenFeature(path)`、`Close(h)`、`WriteReport(h, buf, len, timeoutMs)`、`ReadReportExact(h, len, timeoutMs, out report)`、`SetFeature(h, buf)`、`GetFeature(h, buf)`。
  - 休眠缓存与诊断日志使用 `DriverStateStore.SaveBatteryState` / `TryLoadBatteryState`，异常或跳过端点请调用 `HardwareDiagnosticService.RecordDriverTrace`。

## 4. 经引擎验证的最小可用模板（请严格参照结构，勿照抄占位字节）
### 模板 A：FDAS v2.0 最小可用示例（已实测 ATK A87 PRO 33B 中断报文，只读电量）
```json
{
  "fdasVersion": "2.0",
  "metadata": {
    "brand": "ATK",
    "model": "A87 PRO",
    "category": "Keyboard",
    "brandColor": "#A78BFA",
    "author": "Community Contributor"
  },
  "hardware": {
    "vendorId": "0x373B",
    "productId": "0x111F",
    "endpointTopology": {
      "usagePage": "0xFF60",
      "usage": "0x0061",
      "reportLength": 33,
      "reportType": "Interrupt",
      "readTimeoutMs": 250
    }
  },
  "capabilities": {
    "battery": {
      "reportId": "0x00",
      "batteryPercentByteOffset": 4,
      "chargingByteOffset": 5,
      "chargingBitMask": "0x01",
      "batteryFormula": "DirectPercent",
      "sampleRxHex": "00 0B 03 00 64 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    }
  },
  "protocol": {
    "queryBatteryTxHex": "00 0B 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
    "checksumType": "None"
  }
}
```

### 模板 B：C# 5.0 动态驱动脚本最小编译模板（只读电量骨架）
```csharp
using System;
using System.Collections.Generic;
using RazerBatteryTray;

namespace FerrisPulse.Extensions
{
    public sealed class SampleVendorBatteryDriver : HardwareDriverBase
    {
        private const int TargetVid = 0x0000;
        private const int TargetPid = 0x0000;

        public override string DriverId { get { return "custom_sample_vendor"; } }
        public override string Brand { get { return "SampleBrand"; } }
        public override bool IsExternalPackage { get { return true; } }

        public override bool MatchesVidPid(int vid, int pid)
        {
            return vid == TargetVid && pid == TargetPid;
        }

        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            dev.Capabilities.SupportsBattery = true;
            dev.Capabilities.SupportsDpi = false;
            dev.Capabilities.SupportsPollingRate = false;
        }

        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> results = new List<MouseBatteryInfo>();
            List<HidDeviceDescriptor> eps = HidTransport.Enumerate(TargetVid, TargetPid);
            for (int i = 0; i < eps.Count; i++)
            {
                HidDeviceDescriptor ep = eps[i];
                if (ep.UsagePage != 0xFF00 || ep.InputReportLength <= 0) continue;
                // Replace with verified TX/RX query and DriverStateStore.SaveBatteryState
            }
            return results;
        }
    }
}
```

## 5. 规范索引、交付自检与社区反哺
- **项目源码与文档**：GitHub `https://github.com/Ferris-echo/FerrisPulse.git` | Gitee `https://gitee.com/Ferris-echo/ferris-pulse.git`
- **规范文档**：`docs/ADAPTER_SPECIFICATION.md`、`docs/AGENT_HANDBOOK.md`、`AGENT_RULES.md`
- **社区反哺**：
  - GitHub Issues / PR: https://github.com/Ferris-echo/FerrisPulse/issues
  - Gitee Issues / PR: https://gitee.com/Ferris-echo/ferris-pulse/issues
  - 作者个人邮箱直连投递: ferris.echo@gmail.com
````

---

### 1.3 如何向主线贡献代码
- **GitHub Pull Request**: Fork `https://github.com/Ferris-echo/FerrisPulse`，创建分支并提交 PR。
- **Gitee Pull Request**: Fork `https://gitee.com/Ferris-echo/ferris-pulse`，提交 PR。
- **提交 Issue / 邮件**: 如果你不会使用 Git，可直接在 GitHub / Gitee 的 Issue 页面贴上你生成的 `.fp-adapter.json` 或 `.fp-driver.cs`，或发送至 `ferris.echo@gmail.com`，作者会代为合入。

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
# Task: Generate Zero-Hallucination FDAS v2.0 / .fp-driver.cs Adapter for "FerrisPulse"

Hello! My peripheral device is not yet recognized by "FerrisPulse". As a Windows HID protocol and firmware engineer, strictly follow Section 1 (captured hardware fingerprint & driver traces) and Section 2 (zero-hallucination gate & safety rules) to output either a validated **FDAS v2.0** `.fp-adapter.json` or a **C# 5.0** `.fp-driver.cs` driver script.

## 1. Captured Hardware Fingerprint & Driver Diagnostic Context
- Category: Mouse / Keyboard / Headset
- Model Reference: [Your device model]
- Hardware IDs: VID = 0x[xxxx], PID = 0x[xxxx]
- Transport: 2.4G Wireless / USB Wired
- HID Endpoints: [UsagePage / Usage / Input / Output / Feature lengths]
- Driver Trace: [Diagnostic traces matched to VID:PID]

## 2. Zero-Hallucination Gate & Hardware Safety Red Lines
1. **Stop When Protocol Facts Are Missing**: If exact TX/RX packets, Report IDs, byte offsets, or checksum formulas are NOT confirmed in real packet captures, WebHID JS source, or OpenRazer/libratbag/official docs, **STOP immediately** and ask the user to collect them first — NEVER fabricate hex payloads.
   - How to collect: (1) Chrome **WebHID** DevTools F12 (`sendReport`/`sendFeatureReport`/`oninputreport`); (2) `powershell -ExecutionPolicy Bypass -File tools/probe_hid_device.ps1` (read-only); (3) Wireshark + USBPcap capture.
2. **Read-Only First & No Blind EEPROM Writes**: NEVER speculate write commands (e.g. Cmd `0x07`, DPI/polling write templates). Keep write actions disabled unless 100% verified from official WebHID/captures.
3. **Windows HID Rules**: Open keyboard HID handles with access `0`; filter out `HIDI2C` touchpads & virtual HIDs; isolate shared VIDs by `VID + PID + UsagePage + Usage + Report Length`.

## 3. Package Choice & Strict Constraints
- **Option A (`{brand}_{model}.fp-adapter.json` - FDAS v2.0)**: Best for single-command or pipeline battery queries + formula/checksum.
- **Option B (`{brand}_{model}.fp-driver.cs` - C# 5.0 / .NET 4.0)**: Inherit `RazerBatteryTray.HardwareDriverBase` (Priority 300). **NO C# 6+ syntax** (no `$"..."`, `?.`, `nameof`, `out var`, `=>`). Use `HidTransport`, `DriverStateStore`, and `HardwareDiagnosticService.RecordDriverTrace`.

## 4. Reference Docs & Contribution
- Docs: `docs/ADAPTER_SPECIFICATION.md`, `docs/AGENT_HANDBOOK.md`, `AGENT_RULES.md`
- GitHub: https://github.com/Ferris-echo/FerrisPulse/issues
- Gitee: https://gitee.com/Ferris-echo/ferris-pulse/issues
- Email: ferris.echo@gmail.com
````

---

### 2.3 How to Contribute Back
- **GitHub Pull Request**: Fork `https://github.com/Ferris-echo/FerrisPulse`, submit a PR with your `.fp-adapter.json` or `.fp-driver.cs`.
- **Gitee Pull Request**: Fork `https://gitee.com/Ferris-echo/ferris-pulse`.
- **Open an Issue / Email**: Attach your JSON or driver code to GitHub/Gitee Issues or email `ferris.echo@gmail.com`, and it will be merged into the official release with full credit!
