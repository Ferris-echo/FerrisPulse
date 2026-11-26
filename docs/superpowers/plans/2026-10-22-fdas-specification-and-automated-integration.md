# FDAS v1.0 设备适配规范与智能融合体系实现计划 (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建 FerrisPulse 设备适配规范 (FDAS v1.0) 及其自动化全流程闭环：向导自动提取硬件现场探针数据并注入神级 AI 提示词、标准化规范包定义、以及 `tools/integrate_adaptation.ps1` 自动化解析融合与回归测试引擎。

**Architecture:** 
1. **硬件探针与提示词引擎**：在 `AddDeviceWizardForm.cs` 中实现即时硬件描述符提取（VID/PID/UsagePage/Usage/ReportLengths/芯片原名/厂商名/内置品牌透视），动态拼装高抗错 FDAS v1.0 提示词。
2. **规范契约层 (FDAS v1.0)**：在 `docs/ADAPTER_SPECIFICATION.md` 中固化 JSON Schema 契约，全面支持鼠标（电量、DPI各档位、XY轴分离、回报率频段125~8000Hz、双向读写报文）及非鼠标（键盘0权限、耳机、手柄、TWS三节点电量），并配备官方基准样例。
3. **自动化融合引擎**：编写 `tools/integrate_adaptation.ps1`，一键导入 `*.fp-adapter.json`，自动校验 Hex 抓包样本、生成符合 C# 5.0 / UTF-8 BOM 的硬件驱动、自动注册挂载 `DeviceManager.cs`、生成回放单元测试并跑通编译验证。

**Tech Stack:** C# 5.0, .NET Framework 4.0, Win32 HID P/Invoke (SetupAPI / hid.dll), PowerShell 5.1+, JSON

**Spec:** [docs/ADAPTER_SPECIFICATION.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/docs/ADAPTER_SPECIFICATION.md)

## Global Constraints

- 语言与编译器基准：严格遵循 C# 5.0 语法（严禁 `$"..."`, `?.`, `nameof`, `out var` 等），Windows 原生 `csc.exe` 零报错编译。
- 零外部依赖：严禁引入 NuGet 包或第三方 DLL，通信严格走 Win32 HID P/Invoke。
- 文件编码安全：所有源码文件必须严格保存为 **UTF-8 带签名 (BOM)**。
- 头部维护准则：所有修改或新增的 `.cs` 文件顶部必须保留 `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️`。
- 测试防锁死：PowerShell 自动化测试严禁 `Add-Type -Path`，统一采用 `[System.IO.File]::ReadAllBytes` 全内存反射。

## Review Focus

1. **用户输入模糊或空白时的健壮性**：用户未选中设备或输入纯乱码时，提示词能优雅降级并依靠 VID 透视品牌与硬件探针输出。
2. **抓包 Hex 样本验证**：规范包必须携带 `sampleRxHex` 与 `sampleTxHex`，自动化融合脚本必须在生成驱动前验证其字节长度与解析断言，杜绝 AI 幻觉代码。
3. **XY 轴分离 DPI 兼容性**：不支持 XY 分离的鼠标，DpiY 必须平滑回落至 DpiX，支持的设备需正确下发独立高低位字节。
4. **键盘 HID 0 权限铁律**：键盘分类设备在驱动生成时必须自动传权限 `0`（`FILE_READ_ATTRIBUTES`），防 Windows 内核独占锁死。
5. **增量无损融合**：融合脚本向 `DeviceManager.cs` 插入注册项时，必须保证幂等性，严禁重复注入或破坏现有品牌逻辑。

---

### Task 1: 编写 FDAS v1.0 设备适配规范标准文档与基准样例
**Files:**
- Create: `docs/ADAPTER_SPECIFICATION.md`
- Create: `examples/adapters/mchose_a5_v2_ultra.fp-adapter.json`
- Create: `examples/adapters/sony_inzone_buds.fp-adapter.json`

- [ ] **Step 1.1**: 编写 `docs/ADAPTER_SPECIFICATION.md`，定义 FDAS v1.0 规范，包含元数据（metadata）、硬件指纹（hardware）、鼠标全能特性（mouse: dpi/stages/xy-split/pollingRate）、非鼠标电量/多节点（battery/multiEndpoints）、协议指令字典与抓包校验样本格式。
- [ ] **Step 1.2**: 编写旗舰鼠标示例 `examples/adapters/mchose_a5_v2_ultra.fp-adapter.json`，展示电量、DPI 5档位、XY 轴分离、8K 回报率及真实抓包 Hex。
- [ ] **Step 1.3**: 编写多节点入耳耳机示例 `examples/adapters/sony_inzone_buds.fp-adapter.json`，展示左耳、右耳、充电盒独立电量与动态累加和校验。
- [ ] **Step 1.4**: 验证文件 UTF-8 BOM 编码无误。

---

### Task 2: 改造 `AddDeviceWizardForm.cs` 实现硬件现场探针与神级提示词注入
**Files:**
- Modify: `src/UI/Forms/AddDeviceWizardForm.cs`
- Modify: `src/Hardware/GenericHidHelper.cs` (若需暴露更详细的硬件描述符探针方法)

- [ ] **Step 2.1**: 在 `GenericHidHelper.cs` 或向导中增加 `ProbeDeviceDetails(string devicePathOrFingerprint)`，提取真实的 VID、PID、UsagePage、Usage、Input/Output/Feature 报告长度、ProductString 和 ManufacturerString。
- [ ] **Step 2.2**: 建立轻量级内置品牌 VID 映射字典（覆盖 MCHOSE, VGN, LAMZU, AULA, AJAZZ, SinoWealth, Keychron, Flydigi 等常见品牌）。
- [ ] **Step 2.3**: 重构 `GetAiAdaptationPrompt(CustomDeviceConfig selectedItem)` 方法，动态注入设备探针数据、四层防错反查指令、FDAS v1.0 输出约束。
- [ ] **Step 2.4**: 更新点击复制按钮事件，若用户选中设备，立即提取该设备的现场数据拼接并提示“✓ 已复制含真实硬件指纹的 AI 提示词！”。
- [ ] **Step 2.5**: 执行 `cmd.exe /c "build.bat"` 验证 C# 5.0 编译零警告零报错。

---

### Task 3: 研发自动化融合脚本 `tools/integrate_adaptation.ps1`
**Files:**
- Create: `tools/integrate_adaptation.ps1`

- [ ] **Step 3.1**: 编写参数解析逻辑（支持 `-AdapterFile <path>`、`-ValidateOnly`、`-DryRun`）。
- [ ] **Step 3.2**: 编写 JSON Schema 格式校验与抓包 Hex 样本语法检查逻辑。
- [ ] **Step 3.3**: 实现 C# 5.0 代码生成器模块：
  - 若为已知品牌（如 MCHOSE, Sony），安全更新对应 Helper；
  - 若为全新品牌，生成标准的 `src/Hardware/<Brand>DeviceHelper.cs`（带头部维护准则，UTF-8 BOM，严格遵循 C# 5.0）。
- [ ] **Step 3.4**: 实现 `DeviceManager.cs` 注册挂载逻辑（增量且幂等）。
- [ ] **Step 3.5**: 自动生成单元测试 `tests/test_<slug>.ps1`，回放 `sampleRxHex` 并断言电量/DPI/回报率解析结果。
- [ ] **Step 3.6**: 自动执行 `cmd.exe /c "build.bat"` 与全量测试套件进行端到端闭环验证。

---

### Task 4: 端到端验证、全量回归与文档沉淀
**Files:**
- Create: `tests/test_fdas_system.ps1`
- Modify: `docs/AGENT_HANDBOOK.md`

- [ ] **Step 4.1**: 编写 `tests/test_fdas_system.ps1`，对规范验证、提示词生成、样本测试进行自动化断言。
- [ ] **Step 4.2**: 运行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`，确保全部测试通过（目标 56/56 PASS）。
- [ ] **Step 4.3**: 将 FDAS v1.0 操作手册与自动化融合指令更新进 `docs/AGENT_HANDBOOK.md`，固化知识飞轮。
- [ ] **Step 4.4**: 提交 Git 变更并推送远程。
