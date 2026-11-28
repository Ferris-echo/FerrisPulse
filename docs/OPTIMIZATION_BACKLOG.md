# 🛠️ FerrisPulse · 灵脉 — 性能与稳定性待优化清单 (OPTIMIZATION_BACKLOG.md)

> **用途**：把"哪里慢、哪里会崩、为什么、怎么改、怎么验"固化成可逐条开工的工单。每条都带真实代码证据（`文件:行号`，基于 `dev2.0` @ `2f9dc3e` 实测）。
> **与其它文档的分工**：
> - 开发铁律 13 条 ➡️ `AGENT_RULES.md`（最高约束）
> - 项目全景与坑位 ➡️ `docs/PROJECT_MAP.md`（本清单不重复其内容，只引用章节号）
> - 跨模块通用陷阱与发布 SOP ➡️ `docs/AGENT_HANDBOOK.md`
> - 单品牌 HID 报文偏移 ➡️ `docs/HARDWARE_PROTOCOLS.md`（按需定点检索）

---

## 0. 结论摘要（按性价比排序）

| 排名 | 问题 | 一句话影响 | 工单 |
| ---: | :--- | :--- | :--- |
| 1 | 全量扫描没有在途闸门 | 定时器、开机轮询、USB 通知、手动刷新会同时堆多个全量扫描，期间 UI 与硬件命令全部排队 | **M1** |
| 2 | HID 枚举零缓存 | 一次扫描要重复做数十次 SetupAPI 全量枚举 + 逐接口开句柄，是扫描慢的主因 | **P1** |
| 3 | 严格身份名单比文档少两项 | `3554:FA09` 同接收器裂成两张卡（`VGN:` 幽灵卡），污染"桌面是否有休眠鼠标"判断，放大卡死 | **M3** |
| 4 | DPI 监听常态 50ms，且每轮重算驱动 | 20Hz 的注册表/锁/List 分配，笔记本上直接吃 CPU 与耗电 | **P4** |
| 5 | 进程级 TLS 校验被关闭 | 自更新通道可被中间人替换，且 SHA256 来自同一通道，校验形同虚设 | **U1** |

---

## 1. 字段说明与开工流程

每条工单固定六段：**症状 / 证据 / 根因 / 改法 / 风险 / 验证**。

单个工单的开工流程：

1. 只改该工单列出的文件，改完同步文件头部"核心职责/依赖关系"注释（铁律：契约同步更新）。
2. 新/改的 `.cs` 存成 **UTF-8 with BOM + CRLF**，随后 `python tools/ensure_boms_safe.py`。
3. 走到该工单的「验证」一栏，逐条跑。
4. 需要新用例的工单，用例必须走全内存反射加载（铁律 10，`[System.IO.File]::ReadAllBytes` + `Assembly.Load`）。

**每批收尾统一动作**：

```powershell
Stop-Process -Name FerrisPulse -Force -ErrorAction SilentlyContinue
cmd.exe /c "build.bat"
powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1
```

基线为 **17/18 PASS**，`test_dpi_and_polling_pipeline.ps1` 为既有失败（PROJECT_MAP §9.3），**任何批次都不得因它变红而改生产行为**，也不得让其它用例由绿转红。

---

## 2. 批 0 — 测量与止血

> 目标：先拿到数字，再动性能代码；顺手堵住最容易复发的稳定性缺口。全部为小改动，可独立上线。

### M1 · 全量扫描缺"在途闸门"，扫描会堆积

| 项 | 内容 |
| :--- | :--- |
| 症状 | 插拔设备、开机、点刷新、定时器同时命中时，多个全量扫描串在 `lock (ScanSyncRoot)` 上排队；排队期间 DPI 监听、托盘刷新、硬件写入全部被拖慢，表现为"点了没反应"或"界面一顿" |
| 证据 | `src/UI/Forms/MainForm.Hardware.cs:2163-2195`（`RefreshBatteryStatus` 每次调用直接 `ThreadPool.QueueUserWorkItem`，无在途标志）；触发源：`updateTimer`（`MainForm.cs:1026-1035`）、`StartBootPolling` 8 次 × 1200ms（`MainForm.Hardware.cs:2130-2158`）、`OnDeviceHardwareChange` 的双定时器 250ms + 1000ms（`MainForm.Events.cs:945-1008`）、手动刷新与设备页签切换 |
| 根因 | 刷新入口只判断 `PauseBackgroundRefresh`（暂停），没有"已在跑就不重入"的合流机制；`DeviceManager.QueryAllDevices()`（`DeviceManager.cs:1513-1517`）靠全局锁串行，锁不消除排队，只让排队变成阻塞 |
| 改法 | 新增进程内合流器：`int scanInFlight` + `int pendingRescan`。入口用 `Interlocked.CompareExchange(ref scanInFlight, 1, 0)` 抢锁失败时只置 `pendingRescan=1` 并立即返回；扫描结束在 `finally` 里复位 `scanInFlight`，若 `pendingRescan` 被置位则清位并补跑一次。禁止用 `Thread.Sleep` 等锁 |
| 风险 | 合流后"最后一次刷新"可能晚一个周期才生效（最长一次扫描时长）。要求补跑逻辑必须到位，否则表现为"插了设备不刷新" |
| 验证 | ① 新增 `tests/test_scan_coalescing.ps1`：反射触发入口 10 次，断言实际进入 `QueryAllDevices` 的次数 ≤ 2（用测试注入的计数探针）；② 实机连续插拔接收器 5 次，观察只触发 1–2 次扫描且卡片状态最终正确；③ `test_device_lifecycle_and_sync.ps1`、`test_mouse_wake_input_storm.ps1`（该用例已存在）全绿 |
| 预估 | 0.5 天 |

### M2 · 扫描耗时与枚举次数无量化数据

| 项 | 内容 |
| :--- | :--- |
| 症状 | 讨论"扫描慢"时缺基线数字，无法判断改完到底快了多少；也无法定位是哪个品牌 helper 拖慢 |
| 证据 | `DeviceManager.cs:1519+` 的 `QueryAllDevicesCore` 全流程无任何计时；`HidTransport.cs` 无枚举缓存（全文搜索 `Cache` 零命中），本机 `Get-PnpDevice -Class HIDClass -Present` 实测 **23** 个接口，扫描核心串行调用 **26** 个品牌 helper |
| 根因 | 历史实现只关心"功能对不对"，从未留下可观测性 |
| 改法 | 在 `QueryAllDevicesCore` 里加 `Stopwatch`，为每个 helper 调用包一层分段计时（建议抽 `ProbeTimer` 小工具类），扫描结束写一行汇总到 **exe 所在目录** 的 `scan_trace.log`（滚动保留最近 N 次）。日志落盘位置必须用 `AppDomain.CurrentDomain.BaseDirectory`，**禁用** `Path.GetTempPath()`（PROJECT_MAP §11.1） |
| 风险 | 日志写入本身引入 I/O。要求：仅在文件行数 < 上限时写、单次扫描最多一行汇总、异常全部吞掉（该功能只用于测量，不得因写日志影响主流程） |
| 验证 | 冷启动一次 + 等待一次 60s 定时刷新，产出基线表：总耗时、Top5 慢 helper 及各自 ms、每 helper 内部 HID 枚举次数。此表为 P1 的验收依据 |
| 预估 | 0.5 天 |

### M3 · 严格身份名单与文档不一致 → FA09 幽灵重复卡

| 项 | 内容 |
| :--- | :--- |
| 症状 | 同一 `3554:FA09` 接收器在列表里裂成两张卡（`VGN:3554:FA09` 与 `CompX:3554:FA09`，前者 `IsConnected=false, IsSleeping=true`），污染"桌面是否有休眠鼠标"的聚合判断；PROJECT_MAP §12.3 已确认这是 V3 Pro 卡死的**放大器**，当前只能"关于 → 清除缓存"临时处置 |
| 证据 | `src/Hardware/HardwareDriverRegistry.cs:28-34` 的 `RequiresStrictIdentity` 只登记了 `BuiltIn:LogitechMR0097` 与 `BuiltIn:KeychronM6` 两项；而 PROJECT_MAP §6 明确要求 4 项：还应包含 `BuiltIn:KzziKeyboard` 与 `BuiltIn:CompxFa09Keyboard`。二者确有内置驱动实体（`KzziDeviceHelper.cs:30/181`） |
| 根因 | 文档描述的是"应该有的名单"，代码只落地了 2 条；共用公版 PID 的驱动可以仅凭 `MatchesVidPid` 就吞下对方的设备 |
| 改法 | ① 补齐两个 `DriverId` 到 `RequiresStrictIdentity`；② 补齐前**先复核**这两个驱动的 `CanHandle` 是否真的按"VID + PID + UsagePage/Usage + 精确报告长度"四元组判定（PROJECT_MAP §11.4、铁律 9），若只按 VID:PID 判定，需先补严格判定再登记，否则会从"重复卡"变成"丢卡" |
| 风险 | 收紧后原本能识别的设备可能不再被接管（表现为卡片消失）。必须实机验证三台：FA09 接收器、KZZI 键盘、Logitech MR0097 |
| 验证 | ① `tests/test_compx_fa09_identity.ps1`、`tests/test_core_driver_architecture.ps1` 全绿；② 实机插 FA09 接收器，列表只剩一张卡，`DeviceId` 命名空间稳定；③ 休眠/唤醒一次，确认不出现 `IsSleeping=true` 的幽灵卡 |
| 预估 | 0.5 天（含实机复核） |

---

## 3. 批 1 — 扫描链路性能

> 目标：把一次扫描的"重复劳动"砍掉。M2 的基线表是这批的验收尺。

### P1 · HID 枚举零缓存，单次扫描数十次全量枚举

| 项 | 内容 |
| :--- | :--- |
| 症状 | 一次全量扫描期间，CPU 有明显尖峰、风扇起转；插拔后 UI 反应慢半拍 |
| 证据 | `src/Hardware/HidTransport.cs:203-278`：每次 `EnumerateDevices` 都完整执行 `SetupDiGetClassDevs` → 逐接口 `SetupDiGetDeviceInterfaceDetail` → `CreateFile`/`CloseHandle`（:234、:278）；全项目 `EnumerateDevices` 调用点 **52 处**（`AcerDeviceHelper` 6、`MchoseDeviceHelper` 4、`VgnKeyboardHelper` 4 …），本机实测 23 个 HID 接口 |
| 根因 | 每个品牌 helper 各自"从零问系统要一遍设备列表"，只为了筛出自己那一个 VID:PID；扫描核心又是串行调用 26 个 helper，重复成本线性叠加 |
| 改法 | 在 `HidTransport` 增加**按扫描代次**的只读快照：`BeginScanGeneration()` 递增代次并清空缓存；`EnumerateDevicesCached()` 在同一代次内返回上次枚举结果（`List<HidDevicePath>` 的防御性副本或只读视图）。扫描入口（`DeviceManager.QueryAllDevices`）开头调 `BeginScanGeneration()`；`WM_DEVICECHANGE` 处理（`MainForm.Events.cs:275-297`）也 bump 一次代次，保证热插拔立即可见。写操作路径不变，仍走实时枚举/实时开路 |
| 风险 | ① 缓存期内设备增删 → 用"代次 + 显式失效"兜住，且**只在单次扫描生命周期内有效**，不做跨扫描长缓存；② 快照被 helper 修改 → 必须返回副本或只读包装；③ 代码里存在"写指令前重新枚举以取句柄"的路径，改缓存时不得让写路径复用到过期句柄 |
| 验证 | ① M2 日志：单次扫描的 HID 枚举次数由 30+ 降到 1–3；扫描总耗时下降（目标先定 ≥30%）；② 热插拔 5 次，设备在 2 秒内出现/消失；③ `test_core_driver_architecture.ps1`、`test_brand_*.ps1` 全绿；④ 至少 2 台真机（雷蛇 + 任一 2.4G 键盘）走一次"改 DPI → 生效"全链路 |
| 预估 | 1.5 天 |

### P2 · 读设备配置时顺带写注册表

| 项 | 内容 |
| :--- | :--- |
| 症状 | 每次刷新都在写 `HKCU`；偶发"配置读完立刻被改写"的困惑现场；注册表被频繁写会导致系统盘上的注册表 hive 持续 dirty |
| 证据 | `src/Hardware/DeviceManager.cs:805-880`：`LoadCustomDevices()` 在**枚举子键的过程中**调用 `SaveCustomDevice(cfg)` — 键盘品类迁移（:844-852）与 `GanssDeviceHelper.NormalizeConfig(cfg)`（:854）；该函数在一次扫描/一次 UI 刷新中被调用 10+ 次（:569、:944、:962、:982、:1005、:1021、:1037、:1055、:1075、:1134、:1200 …） |
| 根因 | 把"数据纠正"塞进了"读取"路径，且纠错条件写在读取循环里，导致读路径既慢又带副作用 |
| 改法 | ① 读路径改为纯只读，删掉其中的 `SaveCustomDevice` 调用；② 数据迁移/纠错抽成 `MigrateCustomDevicesOnce()`，在启动时（`Program.Main` 的单实例确认之后）执行一次，并用 `HKCU\Software\FerrisPulse\MigrationVersion` 记录已迁移版本；③ 读结果加进程内缓存，在"添加/删除/改名/隐藏设备"等处显式失效 |
| 风险 | 老用户的历史脏数据不再自动纠正 → 需要一次性迁移覆盖全部已知迁移规则（键盘品类、`Ganss NormalizeConfig`、复合外设品类纠错）。迁移必须幂等 |
| 验证 | ① `test_device_lifecycle_and_sync.ps1`、`test_game_opt_engine_and_ui.ps1` 全绿；② Procmon 过滤 `RegSetValue` + 进程名 `FerrisPulse`，静置 3 分钟（≥3 次刷新）后写入次数为 0；③ 用一份含脏数据的 `CustomDevices` 备份导入，验证启动迁移后卡片品类正确 |
| 预估 | 1 天 |

### P3 · `GetDeviceIdVariants` 每次分配前缀表，且被 UI 线程循环调用

| 项 | 内容 |
| :--- | :--- |
| 症状 | 设备多时 UI 刷新有轻微卡顿（`UpdateUI` 本身约 6ms，但前置判定会放大）；GC 压力偏高 |
| 证据 | `src/Hardware/DeviceManager.cs:38-86`：每次调用都 `new string[]{...29 个前缀...}`（:70），并对每个前缀做两次 `List.Contains`（:75-77），单设备最多生成 ~60 个候选字符串；`IsDeviceHidden`（:293-313）每次调用 `GetDeviceIdVariants` 且**打开一次注册表键**；该组合在 `GetAllKnownDevicesForPriority`（:558-565）里被逐设备调用，而后者由 UI 线程的 `ApplyDeviceStatus`（`MainForm.Hardware.cs:2218`）触发 |
| 根因 | 前缀表写成了局部数组；隐藏判定每次都读注册表，没有快照 |
| 改法 | ① 前缀表提为 `private static readonly string[] CommonPrefixes`；② 变体去重改 `HashSet<string>`（`OrdinalIgnoreCase`），返回仍为 `List<string>` 以保签名兼容；③ `IsDeviceHidden` 增加进程内隐藏集合快照（启动/首次使用时加载一次，`HideOrDeleteDevice`、`ShowDevice` 等写入口处失效），提供 `InvalidateHiddenCache()` |
| 风险 | 隐藏/取消隐藏后若忘记失效，会表现为"隐藏不生效"。要求：所有写隐藏状态的入口**逐一**核对（`HideOrDeleteDevice` :315、相关 Show 方法、清除缓存 `CacheResetManager`） |
| 验证 | ① `test_tray_and_ui_core.ps1`、`test_multi_tray_and_colors.ps1` 全绿；② 隐藏一台设备 → 立刻从列表消失；重启后仍隐藏；取消隐藏 → 立刻回来；③ 记录一次 `GetAllKnownDevicesForPriority` 的耗时，改造后应明显下降（3 台设备场景目标 <2ms） |
| 预估 | 0.5 天 |

---

## 4. 批 2 — 高频轮询

### P4 · DPI 监听常态 50ms 轮询，且每轮重算驱动与能力

| 项 | 内容 |
| :--- | :--- |
| 症状 | 主窗口开着时笔记本耗电/温度明显上升；进程 CPU 常驻偏高；与"电量管家"定位不符 |
| 证据 | `src/UI/Forms/MainForm.Hardware.cs:45-63`：`CalculateAdaptiveDpiPollInterval` 在"非游戏且非空闲（idle<30s）"时仍返回 **50ms**（雷柏 200ms）；`DpiMonitorWorker` 每轮对每个鼠标目标调用 `EnsureCapabilitiesInitialized`（:122、:135）→ `HardwareDriverRegistry.ResolveDriver` → `GetAllDriversOrdered()`（`HardwareDriverRegistry.cs:183-190`，**每次 `new List<IHardwareDriver>(drivers)` 拷贝 31 个内置驱动并加全局锁**）→ `PopulateCapabilities`（`HardwareDriverRegistry.cs:251-273`）。按 20Hz × N 设备计算，每秒产生数十次列表分配 + 锁竞争 + 能力重算 |
| 根因 | 轮询间隔只有"游戏/空闲"两档，缺少"普通桌面"档；能力与驱动解析被当成每轮必做的前置计算 |
| 改法 | ① 新增普通档：非 burst、非游戏、非空闲时用 150–250ms；仅 `isGamingActive || burstUntilTicks` 保留 50ms（雷柏维持 200ms）；② 驱动解析结果按 `DeviceId` 缓存（`Dictionary<string, IHardwareDriver>`，在 `RegisterDriver`/`UnregisterDriverInstance`/`ReloadExternalPackages` 处清空），`ResolveDriver` 先查缓存；③ `EnsureCapabilitiesInitialized` 改为"仅当 `dev.Capabilities == null` 或缓存版本变化时重算"；④ `GetAllDriversOrdered` 增加只读快照缓存（写操作后失效），消灭每次 new List |
| 风险 | ① 延迟变大 → OSD 响应变钝；以 burst 窗口补偿：检测到变化后 5 秒内回落到 50ms（现有 `dpiMonitorBurstUntilTicks` 机制可直接复用）；② 缓存不失效 → 外部包热加载后不生效（必须在三个注册入口清缓存） |
| 验证 | ① `tests/test_dpi_and_polling_pipeline.ps1` 保持其既有断言状态（不得为了让其变绿改生产行为）；② 新增计时日志：每秒 `ResolveDriver` 次数、每秒硬件查询次数，改造后桌面静置场景应下降一个数量级；③ 实机拨 DPI 档位，OSD 弹出延迟感知无差异（目测 + 日志记录从硬件变化到 OSD 弹出的 ms）；④ 热加载一个 `.fp-adapter.json` 后能力立即生效 |
| 预估 | 1 天 |

### P5 · 提示音回收器 250ms 常驻定时器

| 项 | 内容 |
| :--- | :--- |
| 症状 | 即使从不播放提示音，一旦类被触碰过就永久每 250ms 唤醒一次线程池，妨碍 CPU 进入低功耗态 |
| 证据 | `src/Services/ChimeSynthesizer.cs:255-269`：静态构造里 `new Timer(OnVoiceReaperTick, null, 250, 250)`；`OnVoiceReaperTick`（:271-305）在 `activeVoices` 为空时什么都不做 |
| 根因 | 回收器挂在类型构造上，生命周期与"是否正在播放"无关 |
| 改法 | 改为按需：首次 `PlayXxx` 入队时启动（`Timer.Change(250, 250)`），`activeVoices` 清空后 `Change(Timeout.Infinite, Timeout.Infinite)` 停表；静态构造只保留预热的 PCM 缓存 |
| 风险 | 停表时机若漏判会泄漏 waveOut 句柄（音频句柄耗尽）。要求：`activeVoices` 非空就绝不真的销毁定时器，只停止触发 |
| 验证 | ① 反射探针断言：未播放时定时器不再触发（用计数探针 + 等 2 秒）；② 连续播放 50 次提示音，句柄数（`GetGuiResources` 或进程句柄计数）回到基线；③ `test_game_opt_engine_and_ui.ps1` 全绿 |
| 预估 | 0.5 天 |

---

## 5. 批 3 — 自更新链路

### U1 · 进程级 TLS 证书校验被整体禁用

| 项 | 内容 |
| :--- | :--- |
| 症状 | 任何 HTTPS 请求（版本检查、release-notes、安装包下载）都接受任意证书；攻击者控制 DNS/代理即可下发伪造的 `version.json`，而 SHA256 与 `downloadUrl` **来自同一响应**，等于校验可被同时伪造 |
| 证据 | `src/Services/UpdateManager.cs:29-37`：`ServicePointManager.ServerCertificateValidationCallback = delegate { return true; }`，同时把 `SecurityProtocol` 覆盖为含 `Tls`（TLS 1.0）的位组合；该校验是**进程级全局**设置，影响所有 `HttpWebRequest`（含 :247、:386 的下载） |
| 根因 | 为兼容个别镜像源的证书问题，直接把进程内证书校验整体关掉 |
| 改法 | ① 删除 `ServerCertificateValidationCallback` 赋值（回归 .NET 默认校验）；② `SecurityProtocol` 只显式设 `(SecurityProtocolType)3072`（Tls12），去掉 `Tls` 与魔法数字 12288；③ 若确有镜像源证书异常，改为**按 host 白名单**的局部回调（在回调内判断 `sender` 的 host，仅对白名单放行），绝不全局放行 |
| 风险 | 老系统（Win7 且未打补丁）可能握手失败 → 表现为"检查更新失败"。要求：失败时给出明确文案（沿用现有 `lastError` 通道），不得静默 |
| 验证 | ① 本机三源（github / gitee / ghproxy.net）各跑一次版本检查与一次下载，全部成功；② 用 Fiddler/mitmproxy 自签证书拦截，请求被拒绝（改造前会成功）；③ `test_app_update_startup_and_reset.ps1` 全绿 |
| 预估 | 0.5 天 |

### U2 · 原位替换后无回滚，坏包即永久起不来

| 项 | 内容 |
| :--- | :--- |
| 症状 | 一旦下载到的 exe 能启动但随即崩溃/卡死，用户机器上就只剩这个坏版本；`.old` 在下一次启动时被无条件删除，等于删掉了唯一退路 |
| 证据 | `src/Services/UpdateManager.cs:488-528`：`File.Move(exePath, oldExe)` → `File.Move(updateFilePath, exePath)` → `Process.Start(exePath, "--updated")`，无"启动是否成功"的确认；`src/Core/Program.cs:56-64`：启动第一件事就是 `File.Delete(exePath + ".old")` |
| 根因 | 更新是"一次性不可逆动作"，没有 pending 状态与失败判定 |
| 改法 | ① 替换前写标记 `HKCU\Software\FerrisPulse\UpdatePending = <版本> + <时间>`；② 新版本成功进入主循环（`MainForm` Load 完成）后清除标记与 `.old`；③ 启动时若发现标记存在（说明上次新版本没走完），且 `.old` 仍在 → 自动回滚（`.old` 移回原位并重启一次），并记录一条日志；④ `Program.cs` 的 `.old` 清理改为"仅在标记不存在时" |
| 风险 | 回滚本身出错会留下"两个文件都不对"的状态 → 回滚实现必须"先复制到临时名、再原子重命名"，且失败时保留 `.old` 不动 |
| 验证 | ① 手工演练：把 `version.json` 指向一个能启动但立刻退出的坏 exe，验证自动回滚到旧版并留下日志；② 正常更新一次，确认标记被清除、`.old` 被删除；③ `test_app_update_startup_and_reset.ps1` 全绿 |
| 预估 | 1 天 |

---

## 6. 批 4 — 结构与诊断

> 这批不直接带来帧率/耗电收益，但决定后续迭代速度与线上排障能力。可延后，但 S1 建议在下一轮需求之前做掉。

### S1 · 扫描核心巨石 + 静默异常

| 项 | 内容 |
| :--- | :--- |
| 证据 | `DeviceManager.cs:1519+` `QueryAllDevicesCore` 用 240+ 行顺序硬编码 26 个品牌 helper，每段一个 `try { } catch { }`（该文件裸 `catch {}` 共 **84** 处，全项目 **516** 处） |
| 根因 | 每加一个品牌就往同一个函数里追加一段，异常一律吞掉；出问题只能靠复现 |
| 改法 | ① 抽 `IHardwareProbe`（`Name / Priority / Probe() / IsEnabled()`）清单，扫描核心改为遍历清单；内置品牌各建一个 probe 适配器（先做 5 个高频品牌验证范式，再批量搬迁，每批保持 `run_all_tests` 全绿）；② 新增 `SafeLog`：`SafeLog.Capture(ex, "上下文")` 写滚动日志；只替换"硬件写路径 / 更新 / 托盘"三类关键位置的裸 catch，其余保持现状，避免一次性大爆改 |
| 风险 | 搬迁过程中漏掉某个 helper 的副作用（有的 helper 内部会写 `DriverStateStore`）。要求：每个 probe 搬迁前后用同一台真机对比 `DeviceState` 注册表内容一致 |
| 验证 | 每搬迁 5 个 probe 跑一次全量测试；全部搬完后 `QueryAllDevicesCore` 从 240+ 行降到 30 行以内，且 DeviceIds 集合与改造前**完全一致**（导出对比） |
| 预估 | 3 天（分批） |

### S2 · 三个超大分部类

| 项 | 内容 |
| :--- | :--- |
| 证据 | `MainForm.Hardware.cs` 4058 行、`DeviceManager.cs` 3716 行、`MainForm.UI.cs` 3230 行（全项目 160 个 `.cs` / 90577 行） |
| 改法 | 按职责拆 `partial`：`MainForm.Hardware` → 扫描 / DPI 监听 / 游戏感知 / 灯光；`DeviceManager` → 身份合并 / 配置读写 / 扫描编排。拆分**纯搬代码不改逻辑**，每拆一个文件跑一次全量测试 |
| 风险 | 拆分中途合并冲突。要求：一次只拆一个文件，拆完立即提交 |
| 验证 | 编译无新警告；全量测试保持 17/18；行数拆分后各文件 < 1500 行 |
| 预估 | 2 天（可穿插） |

### S3 · 测试与文档卫生

| 项 | 内容 |
| :--- | :--- |
| 证据 | `tools/run_all_tests.ps1:14` 残留 `"test_mchose_onboard_truthfulness.ps1" = 60000`，该文件已不存在（`tests/` 实测 **15** 个用例文件，PROJECT_MAP §9.1 写 18 个）；`test_dpi_and_polling_pipeline.ps1` 既有失败；`docs/PROJECT_MAP.md` 尚未提交（`git status` 显示 `??`）；文档内"版本 2.4.1 / 分支 dev / 工作区 C 盘桌面"与实际 `dev2.0` @ `2f9dc3e` 有出入 |
| 改法 | ① 删除残留超时项；② 既有失败用例在文件头加 `# EXPECTED-FAIL: 原因（见 PROJECT_MAP §9.3）`，并让套件汇总区分"预期失败"与"回归失败"；③ PROJECT_MAP 的用例数、分支、版本号与实测对齐并提交 |
| 风险 | 改套件汇总逻辑可能与既有用例输出格式耦合 → 只改汇总展示，不改单文件断言 |
| 验证 | 全量套件输出形如 `15 用例 / 14 PASS / 1 EXPECTED-FAIL / 0 REGRESSION` |
| 预估 | 0.5 天 |

---

## 7. 开工顺序与依赖

```
批 0   M2（先测量，产出基线表） → M1 → M3
        ↓
批 1   P1（用 M2 基线验收） → P2 ∥ P3
        ↓
批 2   P4（依赖 P1 的解析/能力缓存思路） → P5
        ↓
批 3   U1 → U2
        ↓
批 4   S1（分批） → S2；S3 随时可插空
```

并行建议：P2 与 P3 互不重叠可同时开；U1 独立，任何批之间的空档都能做。

**红线**：每批结束必须是"可编译 + 全量测试不劣化 + 实机验证过一次"的完整状态，禁止多批混在一个提交里。

---

## 8. 假设与约束

- 允许新增 `internal` 诊断钩子（滚动日志、计时探针），不改变对外行为。
- 清单中的"数十次枚举""20Hz"等量级来自静态计数与本机实测（23 个 HID 接口 / 31 个内置驱动），**绝对耗时的验收以 M2 基线表为准**。
- 既有失败用例 `test_dpi_and_polling_pipeline.ps1` 保持失败状态，不作为任何批次的门禁。
- 全部改动遵守：C# 5.0 / .NET Framework 4.0、零外部依赖、`csc.exe` 原生编译、源码 UTF-8 with BOM + CRLF。

---

## 9. 明确不做

1. 不重做 PROJECT_MAP §12.2 已整体回退的 DPI 数值框补丁与托盘注册重试补丁。
2. 不为让测试变绿去改生产逻辑（尤其 §9.3 记录的 DPI 徽标契约）。
3. 不引入 NuGet 包、第三方 DLL、C# 6+ 语法。
4. 不在工单范围外顺手重构无关品牌 helper。
5. 不新增 `Thread.Sleep` 轮询式的"等锁"写法。

---

## 10. 复测记录（每批完成后填写）

| 日期 | 工单 | 基线（扫描 ms / 枚举次数 / CPU） | 改造后 | 全量测试 | 实机设备 | 结论 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
|  |  |  |  |  |  |  |
