# 一键深度重置、卸载清理与旧进程自动接管实现计划 (App Reset, Clean Uninstall & Legacy Process Takeover)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 彻底解决跨版本升级时“新旧两个软件同时开机自启”以及“旧版进程占坑/注册表残留导致看不到新版适配设备”的问题，并在「关于 FerrisPulse」窗口右上角提供低频维护专属的「重置 / 清理数据」入口（支持【一键重置并重启】与【彻底清理并退出】双模式）。

**Architecture:**
1. 在 `Program.cs` 与 `StartupManager.cs` 中引入**跨路径/跨版本实例智能比对与自动接管机制**：启动时扫描 `FerrisPulse` 及历史进程名（`RazerBatteryTray`、`RazerBattery`、`RazerTray`）；若已运行实例路径与当前 `.exe` 不同或版本更低，直接终止旧进程并由当前新版接管，同时自动修正开机自启路径。
2. 新增核心清理引擎 `src/Core/AppResetManager.cs`，统一清理所有历史版本注册表（`HKCU\Software\FerrisPulse`、`HKCU\Software\RazerBatteryTray` 全子树含 `HiddenDevices` / `CustomDevices` / `DeviceStates` / 各品牌 `*Cache`、AUMID 通知项）、全通道开机启动项（`Run`、`StartupApproved`、`Startup` 文件夹 `.lnk`）以及 `%AppData%\FerrisPulse` 扩展缓存与快捷方式。
3. 新增黑金电竞风格确认对话框 `src/UI/Forms/ResetConfirmDialog.cs`，并在 `src/UI/Forms/AboutForm.cs` 正面顶部右上角（与左上角 Logo 标题同行，远离「检查更新」按钮）挂载「重置 / 清理数据」按钮，不在托盘右键菜单添加以保持高频菜单清爽。

**Tech Stack:** C# 5.0, .NET Framework 4.0, Windows Forms, Win32 P/Invoke, Microsoft.Win32 Registry API

**Spec:** In-chat approved design (「关于 FerrisPulse」窗口顶部右上角入口 + 弹窗双模式【一键重置并重启 / 彻底清理并退出】 + 启动自动接管旧版本/异地进程)

## Global Constraints

- 严格遵守 C# 5.0 / .NET Framework 4.0 语法规范（严禁 `$"..."`、`?.`、`nameof` 等 C# 6+ 语法）。
- 零外部依赖铁律：仅使用 .NET 4.0 原生程序集与 Win32 API。
- 所有新增与修改的 `.cs`、`.ps1`、`.md` 文件必须保存为 **UTF-8 with BOM** 编码（执行 `python tools/ensure_boms_safe.py`）。
- 每个代码文件头部必须保留 `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️`。
- 自动化测试加载 `FerrisPulse.exe` 必须使用 `[System.IO.File]::ReadAllBytes` 内存反射，且测试注册表清理时必须在 `try/finally` 中完整备份与还原宿主机原有注册表状态，严禁破坏用户真实配置。

## Review Focus

1. **异地/旧版同名进程接管**：当旧文件夹下的 `FerrisPulse.exe` 开机自启占住 `Global\FerrisPulse_SingleInstance_Mutex` 时，用户双击新文件夹下的新版 `FerrisPulse.exe`，新版必须识别出路径不同（或版本更高），主动终止旧进程并自己启动，绝不能唤醒旧版后自己退出。
2. **模糊残留 Run 键清理**：有些用户曾将旧版改名为 `FerrisPulse_v2.0.exe` 或 `RazerBatteryTray (1).exe` 并写入注册表 `Run` 键；清理自启动时除精确匹配键名外，还应扫描 `HKCU\...\Run` 中所有键值命令行包含 `FerrisPulse` 或 `RazerBatteryTray` 的残留项并一并清除。
3. **按钮防误触与视觉平衡**：「重置 / 清理数据」按钮置于 `AboutForm` 正面顶部右上角（`y = 22`，右边缘与下方卡片对齐），与 `cardUpdate` 内的「检查更新」按钮保持充足物理距离与视觉区分。
4. **重置后重启无互斥锁冲突**：执行【一键重置并重启】时，在拉起新进程前需确保当前实例释放或允许新进程平滑接管互斥锁。
5. **单元测试零副作用**：测试脚本验证 `AppResetManager` 清理能力时，必须先将当前用户的注册表键值备份到内存并在测试结束时无损恢复。

---

### Task 1: 编写自动化回归测试 `tests/test_app_reset_and_takeover.ps1` (TDD Red)

**Files:**
- Create: `tests/test_app_reset_and_takeover.ps1`

**Interfaces:**
- Verifies:
  - `RazerBatteryTray.AppResetManager` (`ClearAllRegistryData`, `ClearAllAutoStartEntries`, `ClearAppDataAndShortcuts`, `PerformFullReset`, `ShouldTakeOverProcess`)
  - `RazerBatteryTray.ResetConfirmDialog` & `RazerBatteryTray.ResetActionMode` (`None`, `ResetAndRestart`, `CleanAndExit`)
  - `RazerBatteryTray.AboutForm` (`btnResetApp` 位于顶部右上角，且不在 `cardUpdate` 内)

- [x] **Step 1: 创建测试脚本 `tests/test_app_reset_and_takeover.ps1`**
- [x] **Step 2: 运行测试确认处于 Failing (Red) 状态**

---

### Task 2: 实现全量清理中枢 `AppResetManager` 与启动自动接管机制

**Files:**
- Create: `src/Core/AppResetManager.cs`
- Modify: `src/Core/StartupManager.cs`
- Modify: `src/Core/Program.cs`

- [x] **Step 1: 创建 `src/Core/AppResetManager.cs`**
- [x] **Step 2: 增强 `src/Core/Program.cs` 启动自动接管**

---

### Task 3: 实现 `ResetConfirmDialog` 并在 `AboutForm` 顶部右上角挂载入口

**Files:**
- Create: `src/UI/Forms/ResetConfirmDialog.cs`
- Modify: `src/UI/Forms/AboutForm.cs`

- [x] **Step 1: 创建 `src/UI/Forms/ResetConfirmDialog.cs`**
- [x] **Step 2: 在 `src/UI/Forms/AboutForm.cs` 顶部右上角添加 `btnResetApp`**

---

### Task 4: 编码规范检查、编译与全量测试验证 (TDD Green)

**Files:**
- Run: `tools/ensure_boms_safe.py`
- Run: `build.bat`
- Test: `tests/test_app_reset_and_takeover.ps1`
- Test: `tools/run_all_tests.ps1`

- [x] **Step 1: 运行 `python tools/ensure_boms_safe.py` 确保所有文件为 UTF-8 with BOM**
- [x] **Step 2: 运行 `cmd /c build.bat` 验证零错误编译**
- [x] **Step 3: 运行 `powershell -ExecutionPolicy Bypass -File tests/test_app_reset_and_takeover.ps1` 验证全部断言 PASS**
- [x] **Step 4: 运行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1` 验证全量测试 100% PASS**
