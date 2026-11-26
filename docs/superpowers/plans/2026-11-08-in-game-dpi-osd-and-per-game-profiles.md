# 游戏内 DPI OSD 响应优化、独立游戏专属硬件策略与图标持久化实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 彻底解决游戏内物理按键切换 DPI 时 OSD 弹窗丢失与迟钝的问题，支持不同游戏独立配置专属 DPI 档位与回报率，并实现守护列表游戏图标长期持久化存储。

**Architecture:** 
1. **输入与轮询调度层**：在 `CalculateAdaptiveDpiPollInterval` 中引入 `isGamingActive` 感知锁定 50ms 极速轮询，并建立变档脉冲高频采样窗口（Burst Window），解耦物理按键切档与游戏优化 2500ms 冷却拦截；
2. **图标持久化引擎**：在 `%LOCALAPPDATA%\FerrisPulse\GameIcons\` 建立本地 PNG 图标库，添加游戏即固化，重启后未运行状态也能秒级加载；
3. **独立游戏专属硬件配置**：建立 `GameProfile` 数据模型并在注册表存储，在 `ModernProcessListControl` 中为已生效游戏卡片增加 `[⚙️]` 专属设定按钮，呼出 `GameProfileSettingForm` 配置独立 1~5 档 DPI 和回报率（默认跟随全局电竞预设），游戏启动时优先下发专属参数。

**Tech Stack:** C# 5.0, .NET Framework 4.0, Win32 P/Invoke, WinForms, GDI+, Registry.

## Global Constraints
- 严格遵循 C# 5.0 / .NET Framework 4.0 语法基准（严禁 `$"..."`, `?.`, `nameof` 等 C# 6+ 语法）。
- 零外部依赖铁律（仅允许引用 .NET 4.0 核心程序集，严禁引入第三方 NuGet/DLL）。
- 文件编码安全（源码及测试必须保存为 UTF-8 with BOM，运行 `python tools/ensure_boms_safe.py`）。
- 自动化测试防锁死铁律（内存反射加载 DLL/EXE，严禁独占句柄锁死）。
- 永久保留各源码文件头部的 `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️`。

## Review Focus
1. `CalculateAdaptiveDpiPollInterval` 在游戏模式（`isGamingActive == true`）下必须始终返回 50ms，不受无 `WM_INPUT` 的闲置判定影响。
2. 物理按键连续切档时，每次变档均能被快速确认识别，OSD 必须连续平滑更新当前档位并重置倒计时。
3. 进入游戏启动自动下发时，`ApplyGameOptimization` 必须优先读取匹配进程名的 `GameProfile`，专属档位生效；退出游戏后必须干净恢复桌面预设。
4. 守护列表中添加的游戏，即使完全杀死进程并重启 FerrisPulse，重新打开游戏管理窗口时图标依然 100% 正常显示。
5. 所有已有单元测试必须继续 100% PASS，无回归破坏。

---

### Task 1: 游戏模式 DPI 轮询锁定与物理切档即时响应 (In-Game DPI Polling Lock & Burst Switch)

**Files:**
- Modify: `src/UI/Forms/MainForm.Hardware.cs`
- Modify: `src/UI/Forms/DpiOsdForm.cs`
- Test: `tests/test_in_game_dpi_osd_polling.ps1`

- [ ] **Step 1: 编写红灯失败测试**
  在 `tests/test_in_game_dpi_osd_polling.ps1` 中编写单元测试：
  - 测试 1：`CalculateAdaptiveDpiPollInterval` 在 `idleSeconds = 60.0` 但 `isGamingActive = true` 时，断言必须返回 50ms（不再降级至 1000ms）。
  - 测试 2：模拟物理按键连续变档时，触发脉冲高速采样窗口，验证 `OnDpiChanged` 不受 `lastGameOptOsdTime` 2500ms 冷却拦截。
  - 运行测试确认抛出预期红灯（因当前代码缺少 `isGamingActive` 参数和脉冲追踪）。

- [ ] **Step 2: 改造 `CalculateAdaptiveDpiPollInterval` 支持游戏态感知**
  在 `src/UI/Forms/MainForm.Hardware.cs` 中：
  - 将 `CalculateAdaptiveDpiPollInterval` 扩展为接受 `bool isGaming` 与 `long burstUntilTicks` 参数（或重载保持向后兼容）；
  - 判定逻辑：若 `DateTime.UtcNow.Ticks < burstUntilTicks` 或 `isGaming`，且鼠标未完全休眠，直接返回 50ms（雷柏为 200ms）；
  - 在 `DpiMonitorWorker` 中：当检测到 `dpi != prevDpi || stage != prevStage` 时，立即更新 `burstUntilTicks = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(5).Ticks`，拉起高速采样脉冲；
  - 优化候选帧匹配逻辑：在高速采样脉冲期间，允许快速确认识别连续变档。

- [ ] **Step 3: 解耦物理按键切档与游戏优化 2500ms 冷却**
  在 `src/UI/Forms/MainForm.Hardware.cs` 的 `OnDpiChanged` 中：
  - 区分“游戏模式自动切档”与“用户物理按键/UI主动切档”；
  - 物理变档时，直接调用 `osdForm.ShowDpi(...)`，不受 `lastGameOptOsdTime` 拦截；
  - 仅在由 `ApplyGameOptimization` 主动下发时，才受到防顶替互斥保护。

- [ ] **Step 4: 运行测试确保绿灯**
  - 编译并运行 `tests/test_in_game_dpi_osd_polling.ps1`，确保全部 PASS。

---

### Task 2: 游戏图标本地持久化缓存引擎 (Persistent Game Icon Cache Engine)

**Files:**
- Modify: `src/UI/Forms/GameProcessManagerForm.cs`
- Test: `tests/test_game_icon_persistence.ps1`

- [ ] **Step 1: 编写红灯失败测试**
  在 `tests/test_game_icon_persistence.ps1` 中编写测试：
  - 构造一个离线进程项（模拟非运行状态游戏），调用图标持久化接口保存测试图标到 `%LOCALAPPDATA%\FerrisPulse\GameIcons\`；
  - 清空内存静态缓存 `processIconCache`，再次调用获取图标方法，断言能够成功从本地磁盘缓存还原并加载为有效的 `Image` 对象。
  - 运行测试确认红灯（当前无磁盘图标持久化逻辑）。

- [ ] **Step 2: 实现本地图标目录管理与文件读写**
  在 `src/UI/Forms/GameProcessManagerForm.cs` 中：
  - 增加 `GetIconCacheDirectory()`，指向 `%LOCALAPPDATA%\FerrisPulse\GameIcons\` 并确保目录存在；
  - 增加 `SaveProcessIconToDisk(string exeName, Image icon)`：采用安全的内存流 `icon.Save(ms, ImageFormat.Png)` 写入磁盘，杜绝文件句柄锁定；
  - 增加 `LoadProcessIconFromDisk(string exeName)`：安全读取本地 PNG 字节并加载为 `Bitmap`，填充到 `processIconCache`；
  - 在 `GetCachedProcessIcon(string exeName)` 中：内存未命中时，自动回退查询本地磁盘缓存；
  - 在 `AddGameProcess` 与 `BrowseLocalExe` 时：一旦获取到有效图标，同步调用 `SaveProcessIconToDisk` 进行固化。

- [ ] **Step 3: 运行测试确保绿灯**
  - 编译并运行 `tests/test_game_icon_persistence.ps1`，确保测试 100% 通过。

---

### Task 3: 游戏专属性能策略模型与配置持久化 (Game Profiles Model & Storage)

**Files:**
- Create: `src/Models/GameProfile.cs`
- Modify: `src/UI/Forms/MainForm.cs`
- Modify: `src/UI/Forms/MainForm.Hardware.cs`
- Test: `tests/test_game_profile_storage.ps1`

- [ ] **Step 1: 编写红灯失败测试**
  在 `tests/test_game_profile_storage.ps1` 中编写测试：
  - 测试 `GameProfile` 的序列化与反序列化（例如 `cs2.exe:dpi=1,rate=1000;apex.exe:dpi=2,rate=2000`）；
  - 测试 `MainForm` 中根据进程名匹配并提取专属 `targetStage` 与 `targetHz`；若未配置则回退到全局 `gameDpiStage` 与 `gamePollingRate`。
  - 运行测试确认红灯。

- [ ] **Step 2: 创建 `src/Models/GameProfile.cs`**
  - 定义 `GameProfile` 类：
    - `public string ProcessName { get; set; }` (如 `cs2.exe`)
    - `public int CustomDpiStage { get; set; }` (0=跟随默认电竞预设, 1~5)
    - `public int CustomPollingRate { get; set; }` (0=跟随默认电竞预设, 125..8000)
    - `public string ExePath { get; set; }` (可选本地绝对路径)
    - 静态方法 `SerializeProfiles(Dictionary<string, GameProfile> dict)` 与 `DeserializeProfiles(string raw)`。

- [ ] **Step 3: 在 `MainForm.cs` 与 `MainForm.Hardware.cs` 中接入专属调度**
  - 在 `MainForm.cs` 中增加 `Dictionary<string, GameProfile> gameProfiles`；
  - 在 `SaveConfig()` 与加载配置中读写注册表 `GameProfiles`；
  - 在 `MainForm.Hardware.cs` 的 `ApplyGameOptimization(bool isEntering)` 中：
    - 检测到运行中游戏时，尝试自 `gameProfiles` 读取该游戏专属配置；
    - 若 `CustomDpiStage > 0`，优先采用其专属 DPI 档位；否则使用全局 `gameDpiStage`；
    - 若 `CustomPollingRate > 0`，优先采用其专属回报率；否则使用全局 `gamePollingRate`；
    - OSD 和 Toast 提示中动态带出专属档位与回报率信息。

- [ ] **Step 4: 运行测试确保绿灯**
  - 编译并运行 `tests/test_game_profile_storage.ps1`，确保全部 PASS。

---

### Task 4: 守护列表微型设置按钮 `[⚙️]` 与专属配置弹窗 (UI & Interaction)

**Files:**
- Modify: `src/UI/Controls/ModernProcessListControl.cs`
- Create: `src/UI/Forms/GameProfileSettingForm.cs`
- Modify: `src/UI/Forms/GameProcessManagerForm.cs`
- Modify: `src/UI/Forms/MainForm.UI.cs`
- Test: `tests/test_game_profile_ui.ps1`

- [ ] **Step 1: 编写红灯失败测试**
  在 `tests/test_game_profile_ui.ps1` 中编写测试：
  - 反射检查 `ModernProcessListControl` 的 `OnSettingsClicked` 事件存在且能在右侧列表被触发；
  - 实例化 `GameProfileSettingForm`，模拟勾选“自定义策略”并切换 DPI 档位与回报率，验证返回值；
  - 运行测试确认红灯。

- [ ] **Step 2: 在 `ModernProcessListControl` 中增加微型设置按钮 `[⚙️]`**
  - 当 `ActionType == ProcessListActionType.Delete` 时，在右侧删除按钮 `[✕]` 左侧绘制微型设置按钮 `[⚙️]`；
  - 支持悬浮状态高亮、鼠标光标手型，并在点击时触发 `event Action<ProcessListItem> OnSettingsClicked`；
  - 保持与整体暗黑微光视觉语言协调统一。

- [ ] **Step 3: 创建 `GameProfileSettingForm.cs` 专属配置弹窗**
  - 继承自 `DpiAwareForm`，Dark 现代化圆角卡片弹窗风格；
  - 顶部显示游戏图标 + 进程名/标题 + “专属电竞硬件配置”；
  - 单选组：`● 跟随主界面默认电竞预设` vs `○ 自定义本游戏专属策略`；
  - 自定义区域：
    - 1~5 档 DPI 快捷按钮，动态获取当前鼠标各档位的真实数值进行标注（如 `[第1档 800]`、`[第2档 1600]`）；
    - 回报率快捷按钮，动态列出当前鼠标支持的档位（如 `[125Hz]` `[500Hz]` `[1000Hz]` `[2000Hz]` `[4000Hz]` `[8000Hz]`）；
  - 底部提供 `[确定保存]` 与 `[取消]`。

- [ ] **Step 4: 在 `GameProcessManagerForm.cs` 与主窗口串联交互**
  - `GameProcessManagerForm` 接收 `Dictionary<string, GameProfile>`；
  - 响应 `ctlMonitoredList.OnSettingsClicked`，呼出 `GameProfileSettingForm`；
  - 保存后更新列表项副标题（显示如 `专属: 第2档 1600 DPI · 1000 Hz`）；
  - 点击关闭/保存时通过回调将最新 Profile 字典回传主窗口并持久化。

- [ ] **Step 5: 运行测试确保绿灯**
  - 编译并运行 `tests/test_game_profile_ui.ps1`，确保全部 PASS。

---

### Task 5: 全量回归测试与发布准则验证 (Full Verification)

**Files:**
- All modified & created files

- [ ] **Step 1: 运行 UTF-8 with BOM 签名检查**
  - 执行 `python tools/ensure_boms_safe.py`，确保所有 `.cs` 和 `.ps1` 文件均为带 BOM 的 UTF-8。

- [ ] **Step 2: 编译生产二进制**
  - 执行 `cmd /c build.bat`，确保 0 警告 0 报错，生成合规的 `FerrisPulse.exe`。

- [ ] **Step 3: 运行全量自动化测试套件**
  - 执行 `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`，确保全部测试用例 100% 通过。
