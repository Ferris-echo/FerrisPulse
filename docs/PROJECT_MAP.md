# 🗺️ FerrisPulse · 灵脉 — 项目全景与快速开工地图 (PROJECT_MAP.md)

> **用途**：接手本项目时先读这一份，5 分钟内建立「哪个文件管什么、改一处要动哪几处、怎么编译验证」的完整心智模型。
> **与其它文档的分工**：
> - 开发铁律 13 条 ➡️ `AGENT_RULES.md`（最高约束，**必读**）
> - 跨模块通用陷阱与发布 SOP ➡️ `docs/AGENT_HANDBOOK.md`（~120 行，**必读**）
> - 全量文件清单（逐文件一行说明）➡️ `ARCHITECTURE.md`（清单纯度高，但其中的绝对路径与版本号已过时，见 §11.1）
> - 单品牌 HID 字节偏移 ➡️ `docs/HARDWARE_PROTOCOLS.md`（**按需定点检索，不要通读**）
> - 扩展包格式 (.fp-adapter.json / .fp-driver.cs) ➡️ `docs/ADAPTER_SPECIFICATION.md`（按需）
> - 性能与稳定性待优化清单（工单化，按批次开工）➡️ `docs/OPTIMIZATION_BACKLOG.md`（**动手优化前必读**，含证据行号与验证方式）

---

## 1. 现状快照（2026-11-26 实测）

| 项 | 事实 |
| :--- | :--- |
| 产品 | 多品牌外设电量管家 + 电竞优化引擎，WinForms 单 exe，进程名 `FerrisPulse` |
| 版本 | `2.4.1`（`src/Properties/AssemblyInfo.cs` 2.4.1.0 / `version.json` 2.4.1） |
| 工作区 | `C:\Users\60972\Desktop\FerrisPulse`（**不是** 文档里写的 `D:\FerrisPulse · 灵脉`） |
| 分支 | `dev` @ `12c9c07 修复雷蛇V3pro启动后卡死`（ahead origin/dev 1）；`main` @ `2f9dc3e` (v2.4.1 发布提交) |
| 远端 | `https://github.com/yamlling/FerrisPulse.git` |
| 构建 | `build.bat` 通过；产物 `FerrisPulse.exe` 1,591,808 B，SHA256 `522A356A…273A6AAD7E` |
| 测试基线 | **22 / 22 PASS**（2026-11-28 实测，含 5 个本轮新增用例）；`test_dpi_and_polling_pipeline.ps1` 已全绿，不再是既有失败（见 §9.3） |
| 语言环境 | 本机 Windows 中文版（zh-CN）、高分屏缩放 115% |

---

## 2. 五分钟开工（照抄即可）

```powershell
cd "C:\Users\60972\Desktop\FerrisPulse"

# 1. 关掉正在跑的实例（单实例互斥体，不停旧进程会拿不到新 exe）
Stop-Process -Name FerrisPulse -Force -ErrorAction SilentlyContinue

# 2. 编译（csc.exe 4.0.30319，/recurse:src\*.cs 自动收录新文件，无需改工程文件）
cmd.exe /c "build.bat"

# 3. 全量回归（18 个用例，约 25 秒，自带注册表快照回滚）
powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1

# 4. 单个测试调试（务必带测试沙盒环境变量，避免真机写 DPI/回报率/灯光）
$env:FERRISPULSE_TEST_MODE="1"; powershell -ExecutionPolicy Bypass -File tests/test_xxx.ps1; Remove-Item Env:FERRISPULSE_TEST_MODE
```

**改完代码两件必做**：① 新/改的 `.cs` 存成 **UTF-8 with BOM + CRLF**，然后 `python tools/ensure_boms_safe.py`；② 若动了 `AssemblyInfo.cs` 版本号，同步 `version.json` 的 `sha256` 与 `fileSize`。

---

## 3. 分层地图

```
src/
├── Core/       入口、单实例、自启动、注册表配置、Win32 P/Invoke、缓存清理
├── Models/     纯数据契约（无副作用）
├── Hardware/   ★ 全项目 70% 体量：驱动框架 8 文件 + 品牌协议 ~70 文件
├── Services/   自更新、游戏检测、提示音合成、更新日志
└── UI/         Theme / Controls / Forms / Tray / Helpers
```

### 3.1 `src/Core/` — 进程与大环境

| 文件 | 职责 |
| :--- | :--- |
| `Program.cs` | 入口。锁工作目录（防开机自启漂到 System32）、Per-Monitor DPI 感知、全局互斥体 `Global\FerrisPulse_SingleInstance_Mutex` 单实例、解析 `--autostart`、未捕获异常兜底 |
| `StartupManager.cs` | 自启动双通道：`Run` 键 + `StartupApproved` 协同 + 快捷方式 |
| `ConfigManager.cs` | **所有用户设置项的唯一出入口**（`HKCU\Software\FerrisPulse`，自动回退旧树 `RazerBatteryTray`）。新增开关＝在此加 `const string` + Get/Set 方法 |
| `CacheResetManager.cs` | 清除缓存：先导出 `.reg` 备份 → 清设备/设置树 + 图标缓存，强制保留 `adapters/`、自启动、快捷方式；含测试注入点 |
| `AppResetManager.cs` | 恢复出厂 / 干净卸载 |
| `NativeMethods.cs` | 全部 Win32 P/Invoke、Shell_NotifyIcon、Raw Input、TaskbarCreated 常量 |

### 3.2 `src/Hardware/` — 驱动框架（先认这 8 个，再认品牌）

| 文件 | 职责 |
| :--- | :--- |
| `IHardwareDriver.cs` | 驱动插件契约（三级驱动统一实现） |
| `HardwareDriverBase.cs` | 抽象基类默认实现 + `TryExtractDeviceVidPid` 等工具 |
| `HardwareDriverRegistry.cs` | 注册中心：按 Priority 排序、`ResolveDriver` 三级仲裁、外部包端点归属 `ClaimedDevicePaths`、`RequiresStrictIdentity` 严格身份名单、`ScanSyncRoot` 扫描锁 |
| `HardwareCommandBus.cs` | **UI 唯一允许的硬件写入入口**：`SetDpi` / `UpdateHardwareStages` / `SetPollingRate` / `FastQueryDpi` / `ExecuteCustomAction`，内建按 `VID:PID` 互斥锁与测试沙盒闸门 |
| `DriverStateStore.cs` | 冷启动/休眠状态记忆（电量、充电态、DPI 档位、回报率、自定义动作），落 `HKCU\Software\RazerBatteryTray\DeviceState\<DeviceId>` |
| `HidTransport.cs` | 零依赖 HID 总线：SetupAPI 枚举、HidD 能力查询、重叠 I/O 读写与超时 |
| `FdasRuntimeHelper.cs` | FDAS JSON 扩展包解析 + `FdasGenericHardwareDriver`（Priority 200） |
| `CsScriptDriverLoader.cs` (+`.Cache.cs`) | `.fp-driver.cs` 内存动态编译（CSharpCodeProvider），按源码哈希复用实例（Priority 300） |

**品牌协议层查找法**（~70 个 `*Helper.cs`，不要逐个读）：
1. 已知 `VID:PID` → 先 `grep -r "0x1532" src/Hardware` 之类按十六进制定位；
2. 已知品牌英文名 → 文件即品牌名（`RazerDeviceHelper.cs` / `RapooDeviceHelper.cs` / `VgnDeviceHelper.cs` …）；
3. 未知设备/只读兜底 → `GenericHidHelper.cs`（标准 HID 电量报告 0x59/0x85）与 `UniversalGamingDeviceHelper.cs`；
4. 公版 VID 冲突 → 先读 `CompxFa09KeyboardHelper.cs` 与 `docs/HARDWARE_PROTOCOLS.md` 1.5.1（同一 PID 多品牌共用的正确姿势）。

### 3.3 `src/UI/`

| 子目录 | 内容 |
| :--- | :--- |
| `Theme/` | `ModernThemeHelper.cs`（矢量圆角、电池/闪电绘制、色彩常量、`S()` 缩放）、`ModernDarkMenuRenderer.cs`（Fluent 右键菜单） |
| `Controls/` | ~22 个自绘原子控件（`RoundedCard` / `ModernButton` / `ModernBadge` / `SmoothSegmentedTabBar` / `DpiStageRowControl` / `ModernDarkScrollPanel` …）。改外观只动这里，不改 Form |
| `Forms/` | 主窗体 6 个分部类 + ~25 个独立对话框（见 §5、§6） |
| `Tray/` | `MultiTrayIconHost.cs`（Win32 `Shell_NotifyIconW` + 固定 `NIF_GUID` 多图标宿主）、`TraySlotPlanner.cs`（纯静态槽位规划，可单测） |
| `Helpers/DpiScaleHelper.cs` | 高分屏尺寸换算；`Resources/DonateQrAssets.cs` | 打赏二维码内嵌资源 |

---

## 4. 三条主数据流（定位问题的关键）

**① 扫描（谁把设备变成卡片）**
```
MainForm.Hardware.cs 定时器 (RefreshInterval, 默认 60000ms)
  → DeviceManager.QueryAllDevices()
       ├─ HardwareDriverRegistry.QueryExternalDevices()   // 仅 .fp-driver.cs (300)，先占端点
       ├─ 各内置 *Helper.cs / BuiltInDrivers (100)         // 跳过已被外部包 Claim 的路径
       └─ 合并去重：GetDeviceIdVariants 命名空间前缀归一 + 物理身份合并 (PhysicalUnitId)
  → List<MouseBatteryInfo>（带 DriverId + Capabilities）
  → MainForm.UI.cs UpdateUI()   // ★ 一次约 6ms，绝不可放进输入回调（见 §12.2）
```

**② 写硬件（改 DPI / 回报率）**
```
UI 点击 → HardwareCommandBus.SetDpi(dev, x, y, stage)
  → HardwareDriverRegistry.ResolveDriver(dev)   // VID:PID 精确 → DriverId → CanHandle
  → lock(GetDeviceLock(vid,pid))                // 防 50ms 高频 FastQueryDpi 抢包
  → drv.SetDpi(...)                             // 品牌 Helper 里真正发 90B/65B 报文
  → 成功则回写 dev.Dpi/DpiStage + DriverStateStore.SaveDpiState(...)
```
**铁律**：UI 层禁止出现 `Brand == "Razer"`；能力判断一律读 `dev.Capabilities`（`SupportsDpi` / `SupportsDpiStages` / `SupportsPollingRate` / `SupportsXyIndependence` / `SupportedPollingRates` / `MinDpi`~`MaxDpi` / `DpiStep` / `CustomActions`）。

**③ 原始输入（8K 鼠标卡死的旧现场）**
```
WM_INPUT → MainForm.Events.cs → 唤醒判定（只允许 O(1) 字段判读）
  → 命中则合并为一次 BeginInvoke(UpdateUI)（pendingWakeUiRefresh 合并位 + 重入闸门）
```

---

## 5. `MainForm` 六个分部类（最容易找错文件的地方）

| 文件 | 体量 | 职责（改这个功能就改这个文件） |
| :--- | ---: | :--- |
| `MainForm.cs` | 75 KB | 字段声明、构造中枢、窗口生命周期、配置读写、缩放记忆 |
| `MainForm.UI.cs` | **160 KB** | 界面树装配 `InitializeFormUI()`、卡片风琴折叠动画、二级对话框路由（`OpenDpiManagementDialog` 等） |
| `MainForm.Tray.cs` | 100 KB | 主托盘 `NotifyIcon` 生命周期、右键菜单、任务栏重建自愈、GDI+ 矢量电量图标绘制 |
| `MainForm.Hardware.cs` | **197 KB** | 硬件轮询定时器、UI 状态刷新、DPI 实时监听、全屏游戏感知与切档同步 |
| `MainForm.Events.cs` | 41 KB | `WndProc` 消息泵、`WM_TASKBARCREATED`、`WM_INPUT` 鼠标唤醒、USB 即插即用监听 |
| `MainForm.AtkLighting.cs` / `.PulseExplore.cs` | 小 | 品牌专属入口（例外，其余品牌能力一律走总线） |

---

## 6. 驱动注册表三层与身份仲裁

| Priority | 形态 | 存续 |
| ---: | :--- | :--- |
| 300 | `.fp-driver.cs`（内存编译 C# 5.0 脚本） | `adapters/` 或 `%APPDATA%\FerrisPulse\adapters` |
| 200 | `.fp-adapter.json`（FDAS v1/v2 声明式） | 同上 |
| 100 | `BuiltInDrivers.cs` 内置插件集 | 编译进 exe |

- 高优先级**热覆盖**低优先级：同 `DriverId` 或同 `VID:PID` 时取 Priority 大者。
- `RequiresStrictIdentity()` 名单（严格身份校验，禁止仅凭 `MatchesVidPid` 吞卡）：`BuiltIn:LogitechMR0097`、`BuiltIn:KeychronM6`、`BuiltIn:KzziKeyboard`（CompX `3554:FA09` 公版共用 PID，与 MCHOSE G75 PRO / MARVO / AULA / YUNZII 共用），以及所有 Priority ≥ 300 外部包。新增共用公版 PID 的 Helper 必须登记。清单里曾出现的 `BuiltIn:CompxFa09Keyboard` 实际不存在，已于 2026-11-28 校正。
- 能力声明必须如实：未实机验证写指令的设备一律 `SupportsDpi = false`。

---

## 7. 托盘子系统（两轨，别混）

| 轨道 | 实现 | 说明 |
| :--- | :--- | :--- |
| 单图标（默认，`MultiTrayEnabled=0`） | `System.Windows.Forms.NotifyIcon`（`MainForm.Tray.cs`） | 右键菜单 + 矢量电量图标 |
| 多图标（`MultiTrayEnabled=1`） | `MultiTrayIconHost.cs` + `TraySlotPlanner.cs` | 固定 `NIF_GUID` 按槽位绑定，设备重连/重启不缩回溢出区；`WM_TASKBARCREATED` 全量重建 |

排查托盘问题前先记住：**从本机高完整性（管理员）终端启动的实例，其托盘图标不会出现在 explorer 的任务栏**，会得到假阴性。必须用普通权限双击或 `explorer.exe` 启动再观察。

---

## 8. 持久化落点

| 位置 | 内容 |
| :--- | :--- |
| `HKCU\Software\FerrisPulse` | 全部用户设置（`ConfigManager`）。实测当前值示例：`TrayIconStyle=2`、`RefreshInterval=60000`、`WindowZoomScale=1150`、`AutoGameOptEnabled=0`、`MultiTrayEnabled` 不存在（默认关） |
| `HKCU\Software\RazerBatteryTray\DeviceState\<DeviceId>` | 驱动状态记忆（`DriverStateStore`），子键如 `Razer_1532_00C0` |
| `HKCU\Software\RazerBatteryTray\PriorityOrder` | 设备优先级列表 |
| `<exe目录>\adapters\`、`%APPDATA%\FerrisPulse\adapters\` | 外部扩展包搜索路径（两处都扫） |
| `version.json`（内嵌为资源 `FerrisPulse.release-notes.json` + 磁盘文件） | 自更新版本源：`latestVersion` / `sha256` / `fileSize` / `downloadUrl`（Gitee Release） |

---

## 9. 测试体系

### 9.1 约定
- 位置 `tests/test_*.ps1`，**22 个**，`tools/run_all_tests.ps1` 顺序执行，单文件 **12 秒**看门狗超时即判 FAIL（个别需要真实硬件往返的用例在脚本内单独给宽限预算）。
- 用例文件头 8 行内写 `# EXPECTED-FAIL: 原因` 会被汇总识别为**预期失败**（不计回归），若它反而通过则标 `PASS-BUT-EXPECTED-FAIL` 提示摘标记；当前**没有**任何用例登记为预期失败。
- 加载被测程序集的唯一合法方式（铁律 10）：
  ```powershell
  $bytes = [System.IO.File]::ReadAllBytes("$PSScriptRoot\..\FerrisPulse.exe")
  $asm   = [System.Reflection.Assembly]::Load($bytes)   # 严禁 Add-Type -Path（文件锁 → CS0016）
  ```
- 套件自动设置 `FERRISPULSE_TEST_MODE=1`（开启硬件写沙盒）并在结束时回滚注册表快照（`tools/TestRegistryGuard.ps1`）。
- 被反射的方法**严禁新增同名重载**（`GetMethod(name, flags)` 会抛 `AmbiguousMatchException`，整个脚本崩）。

### 9.2 用例分布（按主题，2026-11-28 实测 22 个）
- 框架与仲裁：`test_core_driver_architecture`、`test_device_lifecycle_and_sync`、`test_shared_pid_identity`（共用公版 PID 身份隔离）
- 品牌族：`test_brand_razer`、`test_brand_logitech`、`test_brand_rapoo`、`test_brand_mchose`、`test_brand_vgn_atk_vxe`、`test_brand_asus_valkyrie_zaopin`、`test_brand_audio_ble_gamepad`
- 扫描链路性能：`test_scan_coalescing`（在途合流）、`test_hid_enumeration_cache`（代次枚举快照）、`test_custom_devices_readonly_cache`（只读缓存与一次性迁移）、`test_device_id_variants_and_hidden_cache`、`test_dpi_and_polling_pipeline`
- UI / 托盘 / 音效：`test_tray_and_ui_core`、`test_multi_tray_and_colors`、`test_game_opt_engine_and_ui`、`test_chime_reaper_on_demand`
- 自更新与安全：`test_app_update_startup_and_reset`、`test_update_tls_and_rollback`、`test_sandbox_and_guard`

### 9.3 当前基线（2026-11-28 更新）
- **22/22 PASS**，耗时约 25 秒。
- 过去的「既有失败」`test_dpi_and_polling_pipeline.ps1`（XY DPI 徽标断言）现已通过，不再是基线噪声；该用例已随 P4 把普通桌面轮询档从 50ms 放宽到 200ms 而同步更新契约断言。
- 历史遗留（仅供追溯）：
- **既有失败**：`test_dpi_and_polling_pipeline.ps1` 断言 `XY DPI badge text` 为空。原因是该用例构造的设备没开「自动电竞优化」，而 DPI 徽标按现行 UI 契约仅在 `autoGameRateEnabled || autoGameDpiEnabled` 时呈现。**改动前基线同样失败**，不要误判为回归，也不要为了让套件变绿去改生产行为。
- `run_all_tests.ps1` 里的 60 秒宽限表仍指向已删除的 `test_mchose_onboard_truthfulness.ps1`，属无害残留。

---

## 10. 发布 SOP（需要发版时才看）

1. 同步版本：`src/Properties/AssemblyInfo.cs` + `version.json` + `CHANGELOG.md`。
2. `build.bat` → 算 SHA256 与 FileSize **回填 `version.json`**。
3. `tools/run_all_tests.ps1` 全绿（一票否决）。
4. `powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version "X.Y.Z"`（GitHub + Gitee 双平台）。
5. **发完立刻把 `AssemblyInfo.cs` 回滚为上一版本号重新编译**（留旧测新：本地旧版才能触发自更新全链路实测）。

---

## 11. 硬坑清单（血泪，逐条实测过）

### 11.1 环境与路径
- **工作区实际在 C 盘桌面**：`AGENTS.md` / `docs/AGENT_HANDBOOK.md` / `ARCHITECTURE.md` 里写的 `D:\FerrisPulse · 灵脉` 是历史路径，**照抄会找不到文件**。同理 `ARCHITECTURE.md` 里 `AssemblyVersion 4.0.0.0`、控件数量描述已过时。
- 会话 shell 常为**高完整性（管理员）**：用它启动的 FerrisPulse 与 explorer 不同权限边界，托盘图标不显示属测试假象；写实机 DPI 也可能被沙盒/权限拦。
- `TEMP` 实际是 `C:\Users\60972\AppData\Local\Temp`，但进程内 `Path.GetTempPath()` 可能指向别处 —— 临时诊断日志一律写 `AppDomain.CurrentDomain.BaseDirectory`。

### 11.2 语言与编码
- 严格 **C# 5.0 / .NET Framework 4.0**：禁止 `$"..."`、`?.`、`nameof`、`out var`、`=>` 表达式体、`using static`。
- 所有 `.cs/.md/.ps1/.bat` 必须 **UTF-8 with BOM + CRLF**，编译必须 `/codepage:65001`，否则中文变 `?` 或编译报错。
- PowerShell 5.1 **无法解析无 BOM 的中文脚本**（乱码/语法错）。生成脚本用：
  `[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($true)))`
- 用 PowerShell 的 `-replace` / `$s.Replace()` 改含引号的中文代码行极易毁行，改代码优先用编辑器工具按整行替换。

### 11.3 PowerShell / 进程
- `pwsh` 里跑长脚本、且脚本会拉起 GUI 子进程时，**stdio 继承会导致整体挂住**：改用 `Start-Process -RedirectStandardOutput/-RedirectStandardError` 并缩短单条命令。
- 编译前务必 `Stop-Process -Name FerrisPulse`：单实例互斥体会让新 exe 直接把控制权交给旧进程，看起来像「改动没生效」。

### 11.4 硬件协议（通用八条见 `docs/AGENT_HANDBOOK.md` §3.4）
- 键盘 HID 句柄必须传权限 `0`（`FILE_READ_ATTRIBUTES`），否则 `Access Denied (5)`。
- 公版 VID（`0x3554` / `0x373B` / `0x258A`…）必须联合校验 `VID + PID + UsagePage/Usage + 精确 Input/OutputReportLength`（四元组）。
- 8K 接收器无 Feature Report 时**禁止** `DrainReports` 预排空，会打断无线握手状态机。
- 无线帧里的状态位可能在未插线时跳变，未经证实不要绑 `isCharging`。

---

## 12. 缺陷交接（接手上一个 Agent 时先看这里）

### 12.1 已修复并已提交：毒蝰 V3 Pro（`1532:00C1`）启动后整窗卡死
提交 `12c9c07 修复雷蛇V3pro启动后卡死`。根因与修法已沉淀到 `docs/AGENT_HANDBOOK.md` §3.5，回归用例 `tests/test_mouse_wake_input_storm.ps1`（50 个连续报文伪唤醒次数必须为 0，实测由 50/50 降到 0，耗时 307ms→8ms）。

### 12.2 上一轮 DPI / 托盘改动已整体回退（**不要复用旧补丁**）
回退点即 `12c9c07`。被回退的两处改动与当时的现场结论如下，下次重做请重新取证，不要照搬：

| 问题 | 当时改动 | 状态 |
| :--- | :--- | :--- |
| DPI 档位数值框改不动（回车即关窗 / 新值被追加成越界值 / 空框静默回落 800） | 改 `DpiStageRowControl.cs`、`DpiManagementDialog.cs`、`DpiAwareForm.cs`（Enter/Esc 收敛、MouseUp 重全选、严格取值与区间收敛、`CanCloseOnEscape()` 闸门） | **已回退，判为不合格方案** |
| 任务栏看不到图标 | `MainForm.Tray.cs` 加 4×900ms 幂等注册重试 + 一次性引导气泡，`ConfigManager` 加 `TrayOverflowHintShown` | **已回退** |

回退前实测到的客观事实（可复用来省时间）：
- 托盘探针日志：`InitializeTray` 内 `multi=False sandbox=False visible=True iconNull=False`，图标句柄有效、无异常；`Shell_NotifyIconGetRect` 对已注册控件也返回 `0x80004005`，**不能**作为「未注册」的判据。
- Windows 11 按 **exe 完整路径**记忆托盘归位（`HKCU\Control Panel\NotifyIconSettings` 的 `IsPromoted`），换目录运行会被当成新应用重新收进 `^` 隐藏区。
- DPI 弹窗里点数值框后键入，值能被替换进去（当时 `SendKeys("1600")` 显示 1600）；但**回车/失焦/保存整条链路**的行为未达标。

重做建议：先用一份最小复现（打开 DPI 弹窗 → 改第 2 档 → 保存 → 重开验证板载值）确认「到底哪一步吞掉了输入」，再动代码；托盘问题先确认「普通权限启动 + 是否落在 `^` 隐藏区」，避免继续改注册时序。

### 12.3 已知未修的其它隐患
- **幽灵重复设备卡（2026-11-28 已根治，待实机复核）**：`VGN:3554:FA09` 与 `CompX:3554:FA09` 曾是同一接收器裂出的两张卡（前者 `IsConnected=false, IsSleeping=true`），会污染「桌面是否有休眠鼠标」的聚合判断（这正是 12.1 卡死的放大器）。根因是 `VgnDeviceHelper` 的 4 处 `devicePath.Contains("vid_3554")` 端点守卫会把共用公版 PID `0xFA09`（KZZI K75 / MCHOSE G75 PRO 等）的接收器铸成 VGN 卡；现已抽出 `IsVgnCandidatePath(devicePath, allowVgnName)` 统一排除 `pid_fa09`，并给 `BuiltIn:KzziKeyboard` 登记严格身份。历史机器上已落库的旧卡仍需「关于 → 清除缓存」一次性清理。
- `HKCU\Software\RazerBatteryTray\DeviceState\HID_9999_0001` 为历史测试残留键，无害。

---

## 13. 命名与身份约定（看代码前先记住）

- `DeviceId` 形如 `<命名空间>:<VID>:<PID>`，命名空间由 `DeviceManager.GetDeviceIdVariants` 的 `commonPrefixes` 归一（`Razer` / `Rapoo` / `CompX` / `LEOBOG` / `VGN` …）。**新增品牌命名空间必须同步补这张前缀表**，否则同一物理设备裂成两张卡。
- `DriverId` 前缀决定来源：`BuiltIn:` = 内置驱动，`FDAS:` = JSON 扩展包，脚本驱动自带 id。
- `VID:PID` / 槽位**不是**物理唯一键；端点状态与命令路由必须带完整 HID 路径，跨传输关系用 `PhysicalUnitId` 维护。
- 同一设备在不同连接形态（2.4G / 有线 / 蓝牙）是**不同身份**，能力集必须按形态如实降级。

---

## 14. 修改任何功能的标准动作清单

1. `grep` 定位真正的实现文件（硬件改动先看是「框架」还是「品牌 Helper」）。
2. 能力判断走 `dev.Capabilities`，写入走 `HardwareCommandBus`，UI 不碰 `Brand`。
3. 改完同步文件头部的「核心职责/依赖关系」注释（铁律：契约同步更新）。
4. 新增行为补一条 `tests/test_*.ps1`（全内存反射加载）。
5. `build.bat` → `run_all_tests.ps1`（基线 22/22）→ 实机验证一次。
6. 解决新的跨模块共性陷阱 → 补 `docs/AGENT_HANDBOOK.md`；新增私有报文 → 补 `docs/HARDWARE_PROTOCOLS.md`。
