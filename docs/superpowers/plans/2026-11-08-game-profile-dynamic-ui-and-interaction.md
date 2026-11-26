# 独立游戏专属策略动态联动与游戏管理交互重构计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 
1. 解决进入特定专属策略游戏时主界面显示与实际生效的 DPI / 回报率不一致的问题；
2. 支持游戏内手动变档（鼠标物理按键等）时，电竞按钮组动态跟随高亮当前实际生效档位；
3. 退出游戏后自动且干净地回到桌面日常预设，并恢复原有的显示效果；
4. 将游戏管理中双击已生效游戏条目的行为重构为弹出单游戏策略定制窗口（删除仍由专属垃圾桶图标触发）。

**Architecture:**
1. **交互层重构 (`GameProcessManagerForm.cs`)**：
   - 将 `ctlMonitoredList.OnItemDoubleClicked` 的事件委托由 `RemoveGameProcess` 改为绑定 `OpenGameProfileSetting(item.ProcessName)`。
   - 保留各条目右侧垃圾桶图标（`OnActionClicked`）的删除行为。
   - 优化空列表占位提示文案，明确提示用户“双击可定制专属策略”。
2. **专属策略感知引擎 (`MainForm.Hardware.cs`)**：
   - 修复 `GetActiveGameProfile()` 的遍历与截断逻辑：优先匹配前台运行的窗口进程；取消非匹配项的提前 `break`，确保排在列表后面的已配置专属游戏能被正确识别。
   - 改造 `ApplyGamePollingRate()`，消除仅勾选“电竞回报率”时的硬编码默认值缺陷。
3. **游戏运行态动态硬件与 UI 联动引擎 (`MainForm.UI.cs` & `MainForm.Hardware.cs`)**：
   - 引入游戏内运行时动态档位与回报率跟踪变量：`inGameRuntimeDpiStage` 与 `inGameRuntimePollingRate`。
   - 进入游戏（`ApplyGameOptimization(true)` / `ApplyGameDpiStage()` / `ApplyGamePollingRate()`）时，初始化为专属策略值（若无专属则为全局电竞值）。
   - 游戏内物理按键变档（`OnDpiChanged`）时，若处于 `isGamingActive`，动态更新 `inGameRuntimeDpiStage = newStage`，并触发 UI 联动。
   - 改造 `UpdatePollingRateDisplayStates()`：处于游戏态时，电竞 DPI 按钮组与回报率按钮组根据当前动态生效值高亮；右上角硬件生效徽标显示：`● 硬件生效: {Hz} Hz · {DPI} DPI (专属: {GameTitle})` 或 `(电竞)`。
   - 退出游戏（`ApplyGameOptimization(false)` / `RestoreDesktop...`）时，重置动态变量为 0，完全恢复日常桌面的生效态与预设态呈现。

**Tech Stack:** C# 5.0, .NET Framework 4.0, Win32 P/Invoke, WinForms, GDI+.

---

## Global Constraints
- 严格遵循 C# 5.0 / .NET Framework 4.0 语法基准（严禁 `$"..."`, `?.`, `nameof` 等 C# 6+ 语法）。
- 零外部依赖铁律（仅允许引用 .NET 4.0 核心程序集，严禁引入第三方 NuGet/DLL）。
- 文件编码安全（源码及测试必须保存为 UTF-8 with BOM，运行 `python tools/ensure_boms_safe.py`）。
- 自动化测试防锁死铁律（内存反射加载 DLL/EXE，严禁独占句柄锁死）。
- 永久保留各源码文件头部的 `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️`。

---

### Task 1: 游戏管理列表双击交互重构 (Double-Click Customization in Game Manager)

**Files:**
- Modify: `src/UI/Forms/GameProcessManagerForm.cs`
- Test: `tests/test_game_manager_double_click_customization.ps1`

- [ ] **Step 1: 编写单元测试**
  在 `tests/test_game_manager_double_click_customization.ps1` 中编写测试：
  - 反射检查 `GameProcessManagerForm` 初始化过程，验证 `ctlMonitoredList` 的 `OnItemDoubleClicked` 事件绑定的目标方法是定制策略而非删除方法。
  - 验证点击 Action 按钮依然触发删除，双击 item 触发打开专属定制。

- [ ] **Step 2: 改造 `GameProcessManagerForm.cs` 事件绑定与提示文案**
  - 在 `ctlMonitoredList` 初始化时，将 `ctlMonitoredList.OnItemDoubleClicked += (item) => RemoveGameProcess(item.ProcessName);` 替换为 `ctlMonitoredList.OnItemDoubleClicked += (item) => OpenGameProfileSetting(item.ProcessName);`。
  - 将占位文字更新为更具引导性的说明。

- [ ] **Step 3: 运行测试确保 PASS**
  运行 `powershell -ExecutionPolicy Bypass -File tests/test_game_manager_double_click_customization.ps1`。

---

### Task 2: 专属策略感知引擎健壮性修复 (Per-Game Profile Active Matching Engine)

**Files:**
- Modify: `src/UI/Forms/MainForm.Hardware.cs`
- Test: `tests/test_active_game_profile_matching.ps1`

- [ ] **Step 1: 编写单元测试**
  在 `tests/test_active_game_profile_matching.ps1` 中验证：
  - 当 `monitoredGameProcesses` 中第一个进程正在运行但未定制策略，第二个进程正在运行且定制了专属策略时，`GetActiveGameProfile()` 必须能正确返回第二个进程的专属配置，而不是提前 break 返回 null。
  - 当专属游戏在前台运行时，能够提取到 FriendlyTitle（如 "CS2" 或友好进程名）。

- [ ] **Step 2: 修复 `GetActiveGameProfile` 与 `ApplyGamePollingRate`**
  - 改造 `GetActiveGameProfile`：
    - 优先检查当前前台窗口对应的进程；若命中且有专属策略，立即返回。
    - 遍历 `monitoredGameProcesses` 时，遇到无专属策略的运行中进程不执行 `break`，而是记录为备选 fallback，继续向后搜索是否有定制了专属策略的运行中进程；优先返回配置了自定义 DPI/回报率的 `GameProfile`。
  - 修复 `ApplyGamePollingRate()`：当进入游戏时，检查 `GetActiveGameProfile()`，若配置了 `CustomPollingRate > 0` 则优先下发该专属回报率。

- [ ] **Step 3: 运行测试确保 PASS**
  运行 `powershell -ExecutionPolicy Bypass -File tests/test_active_game_profile_matching.ps1`。

---

### Task 3: 游戏运行态动态硬件与 UI 联动引擎 (Dynamic In-Game DPI/Rate UI Sync)

**Files:**
- Modify: `src/UI/Forms/MainForm.cs`
- Modify: `src/UI/Forms/MainForm.Hardware.cs`
- Modify: `src/UI/Forms/MainForm.UI.cs`
- Test: `tests/test_game_profile_dynamic_ui.ps1`

- [ ] **Step 1: 编写动态 UI 状态机单元测试**
  在 `tests/test_game_profile_dynamic_ui.ps1` 中编写端到端状态机测试：
  - 测试 1：进入配置了专属策略的游戏（如 2000 Hz / 第 3 档 DPI）时，`btnGameRates` 高亮 2000 Hz，`btnGameDpiStages` 高亮第 3 档，`lblLiveRateBadge` 显示专属游戏标签。
  - 测试 2：在游戏进行中触发 `OnDpiChanged(newStage = 4)`（模拟游戏内物理变档），验证 `btnGameDpiStages` 动态切换高亮至第 4 档，微标实时更新。
  - 测试 3：退出游戏后，验证界面干净恢复至桌面日常状态，`btnGameRates` 和 `btnGameDpiStages` 恢复预设态（深绿），日常回报率和日常 DPI 恢复为主激活态。

- [ ] **Step 2: 在 `MainForm.cs` 中增加游戏态动态状态跟踪字段**
  - 添加 `private int inGameRuntimeDpiStage = 0;` 与 `private int inGameRuntimePollingRate = 0;`。
  - 添加辅助属性或方法获取当前游戏友好名称。

- [ ] **Step 3: 改造 `ApplyGameOptimization` 与 `RestoreDesktop...` 状态重置**
  - 在进入游戏时，将计算出的目标 `targetStage` 与 `targetHz` 存入 `inGameRuntimeDpiStage` 与 `inGameRuntimePollingRate`。
  - 在退出游戏时，将 `inGameRuntimeDpiStage = 0; inGameRuntimePollingRate = 0;` 彻底清空。

- [ ] **Step 4: 改造 `OnDpiChanged` 动态感知**
  - 在 `OnDpiChanged` 中：若 `isGamingActive` 为 true，且新档位有效，同步更新 `inGameRuntimeDpiStage = newStage;`。
  - 触发 `UpdatePollingRateDisplayStates()`。

- [ ] **Step 5: 改造 `UpdatePollingRateDisplayStates` 呈现引擎**
  - 动态计算当前生效的回报率与 DPI 档位：
    - 处于游戏态时：优先采用 `inGameRuntimePollingRate` / `inGameRuntimeDpiStage`，若为 0 则取专属策略，再若无则取全局电竞默认值。
    - 电竞按钮组：根据上述动态生效值进行 `Selected` 高亮。
    - 右上角硬件生效徽标：
      - 若命中专属策略，显示 `● 硬件生效: {Hz} Hz · {DPI} DPI (专属: {GameTitle})`；
      - 若命中通用电竞策略，显示 `● 硬件生效: {Hz} Hz · {DPI} DPI (电竞)`；
      - 处于桌面时，显示 `● 硬件生效: {Hz} Hz · {DPI} DPI`。
    - 退出游戏后完全恢复原有的桌面生效 + 电竞预设显示效果。

- [ ] **Step 6: 运行测试确保 PASS**
  运行 `powershell -ExecutionPolicy Bypass -File tests/test_game_profile_dynamic_ui.ps1`。

---

### Task 4: 全量回归测试与实机编译验证 (Verification Before Completion)

- [ ] **Step 1: 源码安全与 BOM 校验**
  运行 `python tools/ensure_boms_safe.py`。

- [ ] **Step 2: 执行全量自动化测试**
  运行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`，确保全部测试 100% PASS。

- [ ] **Step 3: 编译发布验证**
  执行 `build.bat` 验证原生 C# 5.0 编译无任何警告与错误。
