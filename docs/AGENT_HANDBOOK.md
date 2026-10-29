# 📘 FerrisPulse · 灵脉 智能体实战操作与接力维护手册 (Agent Operational Handbook)

> **致所有接手本项目的 AI Agent 伙伴**：  
> 本手册仅保留**跨模块的通用架构原则、通用硬件避坑共性规律、社区贡献融合规范与版本发布 SOP**，避免具体单品字节表稀释你的上下文注意力。  
> - **各品牌具体 HID 报文偏移字典**：请仅在修改特定品牌底层协议时，按需查阅 👉 **[docs/HARDWARE_PROTOCOLS.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/docs/HARDWARE_PROTOCOLS.md)**  
> - **FDAS v2.0 / `.fp-driver.cs` 扩展包规范**：请按需查阅 👉 **[docs/ADAPTER_SPECIFICATION.md](file:///D:/FerrisPulse%20%C2%B7%20%E7%81%B5%E8%84%89/docs/ADAPTER_SPECIFICATION.md)**

---

## 1. 模块分层与核心架构

严禁单文件巨石化，所有代码严格遵守分层：
- `src/Core/`: 底层 Win32 P/Invoke (`NativeMethods.cs`)、注册表持久化 (`ConfigManager.cs`)、双通道自启动 (`StartupManager.cs`)、微表达式引擎 (`ExpressionEvaluator.cs`)、程序入口 (`Program.cs`)。
- `src/Hardware/`:
  - **统一插件与指令总线层**：`IHardwareDriver.cs`, `HardwareDriverBase.cs`, `HardwareDriverRegistry.cs`, `HardwareCommandBus.cs`, `DriverStateStore.cs`, `BuiltInDrivers.cs`, `CsScriptDriverLoader.cs`, `FdasRuntimeHelper.cs`, `BuiltInPackageExporter.cs`。
  - **品牌协议实现层**：各品牌 `*DeviceHelper.cs` 与通用传输层 `HidTransport.cs`。
- `src/Models/`: 强类型数据模型 (`MouseBatteryInfo.cs`, `DeviceCapabilities.cs`, `CustomDeviceAction.cs`, `FdasAdapterDefinition.cs` 等)。
- `src/Services/`: 全屏游戏探测 (`AutoGameManager.cs`)、自更新 (`UpdateManager.cs`)、音效反馈 (`SoundFeedbackService.cs`)。
- `src/UI/`: `Forms/MainForm.*.cs` 主窗体分部类体系、对话框 (`AddDeviceWizardForm.cs`, `ImportAdapterDialog.cs`, `DpiManagementDialog.cs`, `DpiOsdForm.cs`) 与自定义控件。

---

## 2. 环境锚定与开发终端指令集

- **唯一合法工作区**：`D:\FerrisPulse · 灵脉\`
- **常用指令**：
  - **原生编译**：`Stop-Process -Name "FerrisPulse" -Force -ErrorAction SilentlyContinue; cmd.exe /c "build.bat"`
  - **全量回归测试**：`powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`
  - **BOM 与换行符校准**：`python tools/ensure_boms_safe.py`
  - **全自动双平台发布**：`powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version "X.Y.Z"`
- **平台路径注入避坑**：若 `run_command` 因中文/中点路径抛出 `/"D:/FerrisPulse · 灵脉": no such directory`，严禁盲目死循环重试，立即引导用户在本地终端或双击 `build.bat` 编译。

---

## 3. 全局架构与硬件避坑十大通用共性法则

### 3.1 C# 5.0 语法与 UTF-8 BOM 铁律
- 编译器锁定 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (`/codepage:65001`)。
- 严禁 C# 6.0+ 语法（禁止 `$"..."` 插值、`?.` 空条件、`nameof`、`out var`、`=>` 表达式属性）。
- 所有 `.cs`, `.ps1`, `.md` 必须保存为 **UTF-8 with BOM**（修改后运行 `python tools/ensure_boms_safe.py` 防 `\r\r\n` Double-CR 与无 BOM 乱码）。

### 3.2 双模硬件扩展包与 UI 零硬编码解耦架构 (`HardwareCommandBus`)
1. **UI 层零品牌硬编码**：`MainForm.*.cs` 严禁通过 `Brand == "Razer"` 等字符串拼接判断功能开关；一律读取 `dev.Capabilities` (`SupportsDpi`, `SupportsPollingRate`, `SupportsXyIndependence`, `CustomActions`) 并通过 `HardwareCommandBus` 派发指令。
2. **三级优先级热覆盖 (`HardwareDriverRegistry`)**：
   - **Priority 300 (`*.fp-driver.cs`)**：内存动态编译的 C# 5.0 脚本驱动，支持复杂多步握手与状态机。
   - **Priority 200 (`*.fp-adapter.json`)**：FDAS v2.0 声明式扩展包，支持多 PID `devices` 矩阵、`setStagesTxTemplate` 与 `customActions`。
   - **Priority 100 (`BuiltInDrivers`)**：内置驱动插件集，支持被同 `DriverId` 或同 `VID:PID` 的外部包无缝热覆盖。
3. **按物理指纹细粒度互斥锁**：`HardwareCommandBus.GetDeviceLock` 按 `VID:PID` 加锁，防止 50ms 高频 `FastQueryDpi` 与后台电量轮询并发交错。

### 3.3 原生 DWM 弹窗动效、抗白框与坐标绝对记忆
1. **严禁 `AnimateWindow` 与 `Minimized` 隐藏**：`AnimateWindow` 会在深色模式下闪现白框；`Minimized` 会导致还原时从底栏飞出。
2. **标准唤醒范式 (`this.RecreateHandle()`)**：主窗体从托盘唤醒时调用 `this.RecreateHandle()` 重置 DWM 呈现状态机，随后 `RestoreWindowPosition()` 锁定原位坐标并 `this.Show()`，配合拦截 `WM_ERASEBKGND` 返回 `(IntPtr)1`，实现零白框、原生 DWM 居中微光展开。

### 3.4 硬件 HID 通信七大共性避坑准则（血泪沉淀）
1. **键盘 0 权限握手**：Windows 内核对键盘集合 (`\kbd` 或 `UsagePage 0x01 / Usage 0x06`) 实施防监听独占锁，传 `GENERIC_READ` 必报 `Access Denied (5)`。打开键盘或特征查询句柄必须优先传 **`0` 权限 (`FILE_READ_ATTRIBUTES`)**。
2. **内置设备过滤 (`IsInternalHidDevice`)**：笔记本内置 `HIDI2C Device` / `I2C HID` / `ACPI\HTIX` 触控板在通用 HID 扫描与自定义配置加载时必须双重过滤，防止误判为无线键盘。
3. **同厂公版 VID 必须用精确端点四元组隔离**：多个品牌/品类常共享公版 VID（如 CompX `0x3554` 同时用于 17 字节 VGN 鼠标与 20 字节迈从 G75 PRO 键盘）。严禁仅凭 `VID` 或宽松的 `ReportLength >= 17` 匹配，必须联合校验 **`VID + PID + UsagePage/Usage + 精确 Input/OutputReportLength`**。
4. **8K 接收器中断端点谨慎预排空 (`DrainReports`)**：部分仅暴露 Interrupt In/Out 端点（无 Feature Report）的 8K 接收器（如 ATK F1 Ultimate 2.0 `373B:11D9`），在 `WriteFile` 前执行 `DrainReports` 会打断接收器与鼠标的无线握手状态机，此时应直接写入查询帧并按报表头过滤响应。
5. **警惕无线链路微状态位伪装充电**：部分接收器（如宏碁 OMR331 `249A:5C2F`）响应帧中的状态字节在未插线时也会随鼠标唤醒/休眠在 `0x01/0x00` 间跳变。未证实为真实 USB 充电位前，切勿盲目绑定为 `isCharging`，以免闪电图标反复横跳。
6. **物理事实优先，严禁盲扫异构协议或虚标能力**：严禁向同一设备盲目轮番发送多套异构 Feature Report；未实机验证 DPI/回报率写指令的设备，必须显式设置 `SupportsDpi = false`、`SupportsPollingRate = false`，宁缺毋滥。
7. **同一台鼠标的三种硬件形态是三个独立身份，能力必须按形态如实分流**：迈从 K7 V2 Ultra+ 实测有三种 PID —— `0x1018`（2.4G 接收器）、**`0x4028`（USB 有线直连）**、**`0x4026`（蓝牙 BLE）**。三者能力完全不同，绝不能只认一个 PID。
   - **有线 `0x4028` 与 2.4G `0x1018` 协议完全一致**：`0x4D` 帧全可用，电量 / 充电 / DPI 档位 / 回报率与 2.4G 无差别。**漏掉 `0x4028` 白名单会让有线形态完全不被识别**。
   - **有线形态的致命坑：端点必须优先选 `0x4028`**。插线后 2.4G 接收器通常仍插在机上，`UsagePage 0xFF01` 会同时枚举出 `0x1018`（已断链）与 `0x4028`（在线）两个端点；若不优先选 `0x4028`，写帧会全打进断链接收器，表现为「句柄有效、写入 False、读无应答」，功能全哑。
   - **连接形态判定以接口 PID 为主判据**，不要只信 `0x0900` 回包的 `[18]`：实测 `0x4028` 回 `[18]=1`，与旧固件「`0` 表示有线」的约定不符；只信 `[18]` 会把有线误标成「2.4G 无线」。
   - **蓝牙 `0x4026` 是 HID over GATT 节点，完全没有厂商通道**（`0x4D` 帧不可达）。此时只能读电量（Windows 原生 GATT 电池服务 `0x180F`），DPI / 回报率 / XY 双轴必须全部置 `false`，UI 落入 Generic Mouse 分支。
   - **VID/PID 与电量分属两个不同节点**（仅 BLE 形态）：`BTHLEDEVICE\{GATT GUID}_DEV_VID&023837_PID&4026_REV&0100_<MAC>\...` 有 VID/PID 但无电量；`BTHLE\DEV_<MAC>\...` 有电量与 FriendlyName 但无 VID/PID。必须先枚举 `BTHLEDEVICE` 拿 MAC/PID，再回 `BTHLE` 按 MAC 读电量。
   - **VID/PID 前两位是序号前缀**（`VID&023837` → 真实 `0x3837`），只取末 4 位；MAC 取**最后一个反斜杠之前、该段最后一个下划线之后**的 12 位（`ExtractMac` 对 BLE 格式会截错）。
   - **电量属性是 `DEVPROP_TYPE_BYTE`**，只能取 `battBuf[0]`，按 `ToInt32` 读会算进相邻字节；读不到返回 `-1`，绝不拿缓存冒充实时值。
   - **性能铁律**：BLE 在线时必须先返回、跳过厂商通道查询。断链的 2.4G 接收器仍在机上时，`0x4D` 每次查询要耗完 `BusyRetryCount(8) × BusyRetryDelayMs(90)`，单次约 4.2 秒（实测跳过后退到 55 ms）。
   - **反射按名取方法时严禁加同名重载**：既有测试用 `GetMethod(name, flags)` 无参签名匹配，一旦新增同名重载会抛 `AmbiguousMatchException` 让整个测试脚本崩掉。需要新语义时另起方法名（如 `GetTransportNameByPid`）。

---

## 4. 社区贡献代码评估与架构融合五步守则 (Contributor Integration SOP)

当收到社区开发者提交的补丁 (`.patch`)、源码包或协议摘要时，Agent 必须按以下五步法融合：
1. **静态审计与实机帧核验**：检查贡献者提供的真实硬件抓包样本 (`TX` / `RX` Hex)、校验和公式及未完成项说明，确认无 C# 6.0+ 语法及硬编码污染。
2. **端点隔离与冲突排查**：核对新设备的 `VID:PID` 是否与现有公版方案（如 `0x3554`, `0x373B`, `0x3837`）存在共享 VID 冲突，确保在 `UniversalGamingDeviceHelper`、`GenericHidHelper` 与专属 Helper 之间建立互斥边界。
3. **模块化插件挂载**：
   - 将协议实现合入对应 `*DeviceHelper.cs`，补齐离线/休眠状态缓存（`DriverStateStore` 或专属注册表键）；
   - 若为新品牌，在 `BuiltInDrivers.cs` 中注册 `HardwareDriverBase` 派生类，并在 `DeviceManager.GetDeviceIdVariants`、`QueryAllDevices` 与 `AddDeviceWizardForm` 中挂载。
4. **独立回放测试固化**：在 `tests/` 下新增独立 PowerShell 测试脚本（使用 `[System.IO.File]::ReadAllBytes` 全内存反射加载），对贡献者捕获的真实报文（正常电量帧、充电帧、错误校验和帧、端点隔离断言）执行 100% 自动化回放验证。
5. **署名致谢与字典归档**：在代码文件头部注释、`docs/HARDWARE_PROTOCOLS.md` 及 `CHANGELOG.md` 中为贡献者妥善署名。

---

## 5. 版本发布与“本地留旧测新”标准作业程序 (Release SOP)

### 5.1 标准双平台发布四步法
1. **对齐版本号**：同步更新 `src/Properties/AssemblyInfo.cs`、`version.json` 与 `CHANGELOG.md`。
2. **纯净编译与计算 Hash**：运行 `build.bat`，计算 `FerrisPulse.exe` 的 SHA256 与文件大小写回 `version.json`。
3. **全量回归测试（一票否决）**：运行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`，确保 100% PASS。
4. **一键发布**：运行 `powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version "X.Y.Z"`。

### 5.2 发布后“本地留旧测新”铁律
- 云端发布最新版后，**本地 `src/Properties/AssemblyInfo.cs` 必须立即回滚为上一版本（如 `2.0.4.0`）并重新执行 `build.bat`**（保持未提交状态），以便用户在本地双击 `FerrisPulse.exe` 实测端到端自更新弹窗、下载校验与热替换闭环。

---

## 6. 自动化测试防锁死铁律 (Byte-Array Reflection Load)

- **严禁 `Add-Type -Path "FerrisPulse.exe"`**：会建立操作系统强互斥文件锁，导致后续 `build.bat` 报 `CS0016` 文件占用错误。
- **强制全内存反射加载**：
  ```powershell
  $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path "$PSScriptRoot\..\FerrisPulse.exe").Path)
  $asm = [System.Reflection.Assembly]::Load($bytes)
  ```

---

## 7. 虚拟 HID（VHF）结构化过滤铁律 (Virtual HID Filter)

- **现象**：设备向导或扫描列表中出现 `HID VHF Driver / 2.4G 无线 / HID:5E2A:71B4` 等假设备。
- **本质**：Windows VHF（Virtual HID Framework）设备。网易 UU 远程（uuycinput.sys）等远程控制/串流/按键映射工具通过内核驱动调用 VHF 注册虚拟键鼠集合。虚拟设备没有物理电池或无线实体，误报“2.4G 无线 / 0%”会误导用户。
- **治理法则**：`VirtualHidFilter` 统一按 VHF 总线结构（`HID_DEVICE_SYSTEM_VHF`、`VHF\VID_`、`vhf#`）识别，严禁写死单一厂商黑名单，并在 `GenericHidHelper.ScanGenericHidMice` 扫描阶段与 `DeviceManager.QueryAllDevices` 最终列表实行双层过滤。
