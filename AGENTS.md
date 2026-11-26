# 电量管家 (FerrisPulse) Agent Rules & Superpowers Configuration

> 本文件为“电量管家”工作区的最高指令文件。在本项目中，Superpowers 默认开启，且必须与项目的 13 大开发铁律及按需上下文加载规范协同执行。

---

## 1. Superpowers 项目级默认启用规范 (Project-Level Superpowers Active)

> **【最高优先级】** 在“电量管家”项目中，Superpowers 工程方法论为 **默认开启 (Always On)** 状态：

1. **新功能 / 需求开发**：
   - 编写任何代码之前，必须先调用 `brainstorming` 技能，通过对话明确意图、推演设计方案并获得确认。
   - 确认设计后，使用 `writing-plans` 制定分步实现计划，严格遵循决策记录与测试断言规范。
   - 执行阶段遵循 `test-driven-development` 与 `executing-plans`。

2. **缺陷排查 / Bug 修复**：
   - 遇到任何异常、逻辑缺陷或报错，必须先调用 `systematic-debugging` 技能，严格执行四步法（复现 -> 追踪根因 -> 根除修复 -> 回归验证），严禁盲目打补丁或掩盖症状。

3. **任务跟踪与验证**：
   - 多步计划使用 Task Artifacts (`write_to_file` 配合 `ArtifactType: "task"`) 维护并用 `replace_file_content` 打勾。
   - 宣布完成前，必须执行 `verification-before-completion` 验证实际编译与运行输出。

---

## 2. FerrisPulse 维护规范与工程准则（13 大铁律）

本项目开发同时必须严格遵守 [AGENT_RULES.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/AGENT_RULES.md) 中的底层铁律：

1. **语言标准与编译器基准**: 严格 C# 5.0 / .NET Framework 4.0，使用 Windows 原生自带 `csc.exe` 编译。严禁使用 C# 6.0+ 语法（禁止 `$"..."` 插值、禁止 `?.` 空条件运算符、禁止 `nameof` 等）。
2. **零外部依赖铁律**: 仅允许引用 .NET 4.0 核心程序集。系统交互严格采用 Win32 P/Invoke，严禁引入 NuGet 包或第三方 DLL。
3. **文件编码安全**: 所有 `.cs`, `.bat`, `.md`, `.ps1` 源码文件必须以 **UTF-8 带签名 (BOM)** 保存，编译始终加 `/codepage:65001`，修改后执行 `python tools/ensure_boms_safe.py`。
4. **模块化工程架构**: 禁止单文件巨石化，严格分层存放于 `src/Core/`, `src/Hardware/`, `src/Models/`, `src/Services/`, `src/UI/`。
5. **任务栏自愈与自启动守护**: 必须维护双通道自启动与 `WM_TASKBARCREATED` 托盘重建自愈逻辑；桌面图标绝不静默自建，严格由用户托盘手动触发。
6. **维护准则注释保留**: 每个代码文件头部必须永久保留 `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️`。
7. **工作区锁定与文件卫生规范**: 唯一合法工作区为 `D:\FerrisPulse · 灵脉\`；根目录严禁倾倒临时文件；严禁向 Git 仓库提交二进制（`*.exe`, `*.zip` 等）。
8. **DWM 原生弹窗动效与抗白框**: 严禁 `AnimateWindow` 软件模拟；呼出采用 `this.RecreateHandle()` 重置呈现状态机，重现原生平滑展开微光，拦截 `WM_ERASEBKGND` 彻底消除浅色底色闪烁。
9. **键盘 HID 0 权限握手与精确端点隔离**: 键盘 HID 句柄必须传权限 `0`（`FILE_READ_ATTRIBUTES`）避开 Windows 内核防监听独占锁；同厂公版 VID（如 CompX `0x3554`）必须联合校验 `VID + PID + UsagePage/Usage + 精确 Input/Output 长度` 实行物理隔离，并过滤 `HIDI2C` 内置触控板。
10. **自动化测试防锁死铁律**: 测试脚本加载二进制必须采用 `[System.IO.File]::ReadAllBytes` 全内存反射装配，严禁 `Add-Type -Path` 产生强互斥文件句柄锁死导致 `CS0016` 编译报错。
11. **平台路径注入避坑铁律**: Windows 下工作区路径遇平台会话压缩注入异常时，严禁盲目派生后台死循环重试，及时引导用户通过原生终端或双击 `build.bat` 极速编译。
12. **双模扩展包与 `HardwareCommandBus` 零硬编码铁律**: UI 层 (`MainForm.*.cs`) 严禁出现 `Brand == "..."` 控制门禁；所有硬件能力展示必须严格读取 `dev.Capabilities`，所有硬件读写操作必须通过 `HardwareCommandBus` 派发至三级优先级驱动注册表 (`*.fp-driver.cs` Priority 300 > `*.fp-adapter.json` Priority 200 > `BuiltInDrivers` Priority 100)。
13. **社区贡献融合与“物理事实优先”五步守则**: 融合外部贡献者补丁或新设备协议时，必须遵循：① 校验真实抓包帧 (`TX`/`RX` Hex) 与校验和；② 排查共享 VID 冲突并建立精确报告长度隔离，注意 8K 中断端点禁盲目 `DrainReports` 及无线跳变位防伪充电；③ 只开启实测验证过的能力（未实测写指令一律置 `SupportsDpi = false`）；④ 挂载至 `BuiltInDrivers` 与 `DriverStateStore` 休眠缓存；⑤ 编写独立回放单元测试并在代码与文档中为贡献者署名致谢。

---

## 3. 极速发布与“留旧测新”摘要 (Quick Release SOP)

1. **对齐版本与构建**：更新 `AssemblyInfo.cs`、`version.json`、`CHANGELOG.md` ➡️ 执行 `build.bat` ➡️ 计算 SHA256 与 FileSize 写回 `version.json`。
2. **全量回归测试**：执行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`，确保 100% PASS。
3. **一键双平台发布**：执行 `powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version "X.Y.Z"`。
4. **发布后本地“留旧测新”铁律**：云端发布后，**本地 `AssemblyInfo.cs` 必须立即回滚为上一版本（如 `2.0.4.0`）并重新编译 `build.bat`**（保留未提交），供用户本地触发自更新全链路实测。

---

## 4. 注意力保护与按需渐进式上下文加载规范 (Progressive Context Loading)

> **【保护 Agent 注意力预算 (Attention Budget Protocol)】**  
> 为防止长篇硬件字节表稀释 Agent 注意力，本项目文档采用**三级按需路由架构**：

1. **开机必读（轻量级全局共识）**：
   - `AGENTS.md`（本文件）
   - **[docs/AGENT_HANDBOOK.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/docs/AGENT_HANDBOOK.md)**（已精简为 ~160 行跨模块通用法则与发布 SOP）

2. **按需查阅（平时严禁盲目通读，仅在相关任务时定点检索）**：
   - 涉及**硬件扩展包格式、AI 提示词或动态脚本编写**时 ➡️ 查阅 **[docs/ADAPTER_SPECIFICATION.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/docs/ADAPTER_SPECIFICATION.md)**
   - 涉及**排查或修改某一特定老品牌底层 HID 报文偏移**时 ➡️ 定点检索 **[docs/HARDWARE_PROTOCOLS.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/docs/HARDWARE_PROTOCOLS.md)** 对应品牌章节

3. **完工反哺义务**：
   - 解决新的跨模块架构共性陷阱时，更新 `docs/AGENT_HANDBOOK.md`；
   - 新增特定设备私有报文细节时，归档至 `docs/HARDWARE_PROTOCOLS.md`，保持主手册永远精炼高效！
