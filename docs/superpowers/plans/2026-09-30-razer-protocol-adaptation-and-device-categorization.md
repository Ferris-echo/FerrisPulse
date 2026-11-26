# 雷蛇全系外设协议自动适配与设备分类精准化实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 彻底解决 BlackWidow X 被错误识别为鼠标并尝试设置 DPI 的问题，建立权威的雷蛇键鼠/外设分类引擎；同时基于官方与 OpenRazer 协议规范，重构雷蛇鼠标 DPI 与回报率下发机制，实现全系列雷蛇鼠标（Modern 0x1F、Intermediate 0x3F、Legacy 0xFF）的自动适配与协议降级容灾。

**Architecture:** 
1. **外设分类与特征识别子系统**：在 `RazerDeviceHelper.cs` 中建立基于 PID 区间分段（键盘 0x0100..0x02FF、鼠标 0x0001..0x00FE、音频 0x0500..0x05FF）、官方 PID 哈希表与产品型号正则匹配的多级判决引擎；在 `DeviceManager.cs` 中移除所有一刀切强制 `Category = Mouse` 的硬编码。
2. **多协议自适应与事务 ID 探测引擎**：在 `RazerDeviceHelper.cs` 的硬件握手过程中动态捕获并缓存 `ActiveTransactionId`；将 DPI 查询/设置与回报率指令从硬编码 `0x1F` 改造为优先命中缓存、备用回路 `[0x1F, 0x3F, 0xFF]` 全自动重试；支持 `0x04 0x06`（多档表）与 `0x04 0x05`（实时 XY）双协议自动降级热备。
3. **UI 路由与防呆控制**：雷蛇键盘接入后准确路由至“机械键盘硬件状态与控制台”（展示供电、免驱状态、全键无冲测速），彻底隐藏并禁用 DPI 调节滑块与回报率按钮。

**Tech Stack:** C# 5.0, .NET Framework 4.0, Win32 HID APIs (`hid.dll`, `setupapi.dll`), Windows Forms.

**Spec:** OpenRazer Linux Kernel Driver (`razermouse_driver.c`, `razercommon.c`, `razerchromacommon.c`, `razerkbd_driver.h`).

## Global Constraints

- **语言与框架兼容性**：必须严格保持 C# 5.0 语法规范，兼容 .NET Framework 4.0 运行时，禁止使用 C# 6.0+ 语法特性（如 `$` 插值字符串、表达式体属性、空条件操作符 `?.` 等）。
- **文件编码规范**：所有新建和修改的源码文件必须带 UTF-8 BOM 签名（`\xef\xbb\xbf`），且文件头部必须包含 AI 维护者规范注释。
- **构建一致性**：必须通过 `D:\电量管家\build.bat` 成功编译生成，无编译警告与报错。

## Review Focus

1. **BlackWidow X 键盘误识别与 DPI 污染**：BlackWidow X（PID 0x0216, 0x0217, 0x021A 等）接入后，系统必须准确判定为 `DeviceCategory.Keyboard`，卡片与托盘提示不得出现 DPI 或回报率调节项。
2. **经典雷蛇鼠标（Transaction ID 0xFF）DPI 无法下发**：Viper Ultimate / Viper Mini / Viper 8K / Basilisk X HyperSpeed / DeathAdder Essential 接入后，下发 DPI 能够自动尝试并命中 `0xFF`，指令返回成功（状态码 0x02）。
3. **中间代雷蛇鼠标（Transaction ID 0x3F）DPI 无法下发**：DeathAdder Elite / Lancehead 有线 / DeathAdder V2 有线接入后，能自动命中 `0x3F`，指令返回成功。
4. **单档/非阶段表鼠标协议降级**：部分雷蛇鼠标不支持 `0x04 0x06` 38 字节档位表协议，系统必须在 `0x04 0x06` 失败后自动无缝回退至 `0x04 0x05` 实时 XY 报文下发。
5. **设备热插拔与休眠状态下的 Transaction ID 自愈**：鼠标从无线休眠唤醒或有线/无线切换时，自适应事务 ID 能自动重新协商并保证后续调参正常工作。

---

### Task 1: 雷蛇外设精准分类引擎与 BlackWidow X 键盘纠偏

**Files:**
- Modify: `D:\电量管家\src\Hardware\RazerDeviceHelper.cs`
- Modify: `D:\电量管家\src\Hardware\DeviceManager.cs:445-458,700-725`
- Test: `D:\电量管家\tests\test_razer_classification.ps1`

**Interfaces:**
- Produces: 
  - `public static DeviceCategory GetRazerDeviceCategory(int pid, string productName)`
  - `public static bool IsRazerKeyboard(int pid, string productName)`
- Consumes:
  - `DeviceCategory` enum (`src\Models\DeviceCategory.cs`)
  - `MouseBatteryInfo` model (`src\Models\MouseBatteryInfo.cs`)

- [ ] **Step 1: 编写测试用例 `tests\test_razer_classification.ps1` 验证分类逻辑**

```powershell
# tests\test_razer_classification.ps1
$asmPath = "D:\电量管家\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    Write-Error "FerrisPulse.exe not found for testing!"
    exit 1
}
$asm = [System.Reflection.Assembly]::LoadFrom($asmPath)
$t = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
$m = $t.GetMethod("GetRazerDeviceCategory", [System.Reflection.BindingFlags]'Public,Static')

$cases = @(
    @{ Pid = 0x0216; Name = "Razer BlackWidow X Chroma"; Expected = "Keyboard" },
    @{ Pid = 0x0217; Name = "Razer BlackWidow X Ultimate"; Expected = "Keyboard" },
    @{ Pid = 0x021A; Name = "Razer BlackWidow X Chroma TE"; Expected = "Keyboard" },
    @{ Pid = 0x0203; Name = "Razer BlackWidow Chroma"; Expected = "Keyboard" },
    @{ Pid = 0x0226; Name = "Razer Huntsman Elite"; Expected = "Keyboard" },
    @{ Pid = 0x00B6; Name = "Razer DeathAdder V3 Pro (Wired)"; Expected = "Mouse" },
    @{ Pid = 0x00B7; Name = "Razer DeathAdder V3 Pro (Wireless)"; Expected = "Mouse" },
    @{ Pid = 0x008A; Name = "Razer Viper Mini"; Expected = "Mouse" },
    @{ Pid = 0x0078; Name = "Razer Viper Ultimate"; Expected = "Mouse" }
)

$allPassed = $true
foreach ($c in $cases) {
    $res = $m.Invoke($null, @($c.Pid, $c.Name)).ToString()
    if ($res -ne $c.Expected) {
        Write-Host "FAIL: PID=$($c.Pid) Name=$($c.Name) Expected=$($c.Expected) Got=$res" -ForegroundColor Red
        $allPassed = $false
    } else {
        Write-Host "PASS: PID=$($c.Pid) Name=$($c.Name) -> $res" -ForegroundColor Green
    }
}
if (-not $allPassed) { exit 1 } else { Write-Host "ALL CLASSIFICATION TESTS PASSED!" -ForegroundColor Cyan }
```

- [ ] **Step 2: 运行测试并验证当前版本失败（预期：方法不存在或分类错误为 Mouse）**

Run: `powershell -ExecutionPolicy Bypass -File "D:\电量管家\tests\test_razer_classification.ps1"`
Expected: FAIL (GetRazerDeviceCategory 方法尚未实现)

- [ ] **Step 3: 在 `RazerDeviceHelper.cs` 中实现 `GetRazerDeviceCategory` 与分类判定**

在 `RazerDeviceHelper.cs` 中实现：
1. 依据 PID 范围判断：
   - `0x0100` 至 `0x02FF`：主要为雷蛇键盘/单手键盘（BlackWidow, Huntsman, Ornata, Cynosa, DeathStalker, Tartarus 等）
   - `0x0500` 至 `0x05FF`：雷蛇音频/耳机（Kraken, Nari, BlackShark）
   - `0x0001` 至 `0x00FE`：雷蛇鼠标
2. 依据名称关键字匹配判断：
   - 若名称中包含 `"keyboard"`, `"blackwidow"`, `"huntsman"`, `"cynosa"`, `"ornata"`, `"deathstalker"`, `"tartarus"`, `"orbweaver"`，判定为 `DeviceCategory.Keyboard`。
   - 若名称中包含 `"headset"`, `"kraken"`, `"blackshark"`, `"nari"`，判定为 `DeviceCategory.Headset`。
   - 否则默认为 `DeviceCategory.Mouse`。
3. 在 `QueryAllRazerDevices` 中构建 `MouseBatteryInfo` 时，给 `activeDev.Category` 赋予 `GetRazerDeviceCategory(curPid, resolvedModelName)`。

- [ ] **Step 4: 修复 `DeviceManager.cs` 中两处硬编码覆盖 `Category` 的 Bug**

1. 修改 `DeviceManager.cs:452`：
   原逻辑：
   ```csharp
   rz.Category = DeviceCategory.Mouse;
   ```
   修改为：保留 `rz.Category`，仅当 `rz.Category == DeviceCategory.Unknown` 时才调用 `RazerDeviceHelper.GetRazerDeviceCategory` 补充判定。
2. 修改 `DeviceManager.cs:702`：
   原逻辑：
   ```csharp
   else if (devInfo.Brand == "Razer")
   {
       devInfo.Category = DeviceCategory.Mouse;
   ```
   修改为：调用 `RazerDeviceHelper.GetRazerDeviceCategory(rzPid, cfg.OriginalName)` 赋予正确的 `devInfo.Category`。

- [ ] **Step 5: 执行全量编译并重新运行分类测试**

Run: `cmd.exe /c "D:\电量管家\build.bat" && powershell -ExecutionPolicy Bypass -File "D:\电量管家\tests\test_razer_classification.ps1"`
Expected: PASS，所有黑寡妇系列键盘均准确输出为 `Keyboard`，鼠标输出为 `Mouse`。

---

### Task 2: 架构升级：雷蛇多代鼠标 DPI 协议自动探测与自适应下发引擎

**Files:**
- Modify: `D:\电量管家\src\Hardware\RazerDeviceHelper.cs:720-850,1190-1240,1370-1410,1630-1675,1810-1835,1980-2000`
- Test: `D:\电量管家\tests\test_razer_dpi_protocol.ps1`

**Interfaces:**
- Produces:
  - `RazerDeviceHardwareState.ActiveTransactionId: byte` (记录 0x1F / 0x3F / 0xFF)
  - `private static byte[] GetAdaptiveTransactionIds(byte preferredTid)`
  - `private static bool TrySetRazerDpiByHandle(IntPtr handle, int reportLength, bool prepended, int dpi, byte preferredTid)`
  - `private static bool TryUpdateRazerHardwareStagesByHandle(IntPtr handle, int reportLength, bool prepended, int activeStage, int[] stages, int count, byte preferredTid)`
- Consumes:
  - `CreateRazerReport(byte transactionId, byte commandClass, byte commandId, byte dataSize, int reportLength, bool isPrepended, byte[] arguments)`

- [ ] **Step 1: 编写测试用例 `tests\test_razer_dpi_protocol.ps1` 校验报文生成与事务 ID 候选序列**

```powershell
# tests\test_razer_dpi_protocol.ps1
$asmPath = "D:\电量管家\FerrisPulse.exe"
$asm = [System.Reflection.Assembly]::LoadFrom($asmPath)
$t = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
$mCreate = $t.GetMethod("CreateRazerReport", [System.Reflection.BindingFlags]'NonPublic,Static')

# 验证针对不同 transaction ID 生成的雷蛇标准报文 (Class 0x04, ID 0x05, 0x07 args)
$argsDpi = [byte[]]@(0x01, 0x06, 0x40, 0x06, 0x40, 0x00, 0x00) # 1600 DPI
foreach ($tid in @([byte]0x1F, [byte]0x3F, [byte]0xFF)) {
    $report = $mCreate.Invoke($null, @($tid, [byte]0x04, [byte]0x05, [byte]0x07, 90, $false, $argsDpi))
    if ($report[1] -ne $tid) {
        Write-Error "Report Transaction ID mismatch for $tid! Got: $($report[1])"
        exit 1
    }
    if ($report[6] -ne 0x04 -or $report[7] -ne 0x05) {
        Write-Error "Command Class/ID mismatch! Class: $($report[6]), ID: $($report[7])"
        exit 1
    }
}
Write-Host "DPI packet generator verified for all transaction IDs (0x1F, 0x3F, 0xFF)!" -ForegroundColor Green
```

- [ ] **Step 2: 改造 `RazerDeviceHardwareState` 增加 `ActiveTransactionId` 记忆**

在 `RazerDeviceHardwareState` 类中增加属性：
```csharp
public byte ActiveTransactionId { get; set; }
```
并在 `QueryAllRazerDevices` 中，当某一个 `tid`（0x1F, 0x3F, 0xFF）成功读取到电量后，将该 `tid` 赋给 `stState.ActiveTransactionId = tid;`。

- [ ] **Step 3: 实现事务 ID 自适应候选构造函数 `GetAdaptiveTransactionIds`**

```csharp
private static byte[] GetAdaptiveTransactionIds(byte preferredTid)
{
    if (preferredTid == 0x3F) return new byte[] { 0x3F, 0x1F, 0xFF };
    if (preferredTid == 0xFF) return new byte[] { 0xFF, 0x1F, 0x3F };
    return new byte[] { 0x1F, 0x3F, 0xFF };
}
```

- [ ] **Step 4: 改造 `TryQueryRazerDpiByHandle` 支持自适应事务 ID**

将硬编码 `0x1F` 的 `stagesReq` 改为遍历 `GetAdaptiveTransactionIds(preferredTid)`：
1. 优先使用 `preferredTid` 发送 `0x04, 0x86` 读取阶段表；
2. 若阶段表读取成功（返回 0x02），更新设备记忆并返回；
3. 若阶段表不支持，尝试 `0x04, 0x85` 读取实时 DPI；
4. 记录能够成功响应的 `tid` 为当前设备的 `ActiveTransactionId`。

- [ ] **Step 5: 改造 `TrySetRazerDpiByHandle` 与 `TryUpdateRazerHardwareStagesByHandle` 支持自适应与双协议降级**

1. 在 `TryUpdateRazerHardwareStagesByHandle` 中：
   - 遍历 `GetAdaptiveTransactionIds(preferredTid)`。
   - 读取当前硬件阶段表并拼装 38 字节载荷（`0x04, 0x06`）。
   - 下发若成功返回 `true`，并更新 `st.ActiveTransactionId`。
2. 双协议热备降级：
   - 若 `TryUpdateRazerHardwareStagesByHandle` 全部失败（如部分旧鼠标固件不支持 0x06 阶段表），自动无缝调用 `TrySetRazerDpiByHandle`（`0x04, 0x05` 实时 XY 报文）。
   - 在 `TrySetRazerDpiByHandle` 中同样遍历 `GetAdaptiveTransactionIds(preferredTid)`，确保 `0x3F` 和 `0xFF` 鼠标均能成功下发。
3. 在 `TrySetRazerPollingRateByHandle` 中：
   - 同样将硬编码 `0x1F` 改为遍历 `GetAdaptiveTransactionIds(preferredTid)` 发送 `0x00, 0x40`。

- [ ] **Step 6: 编译并运行报文生成自测**

Run: `cmd.exe /c "D:\电量管家\build.bat" && powershell -ExecutionPolicy Bypass -File "D:\电量管家\tests\test_razer_dpi_protocol.ps1"`
Expected: PASS，构建成功且报文校验全部通过。

---

### Task 3: 界面防呆与雷蛇键盘专属控制台端到端联动验证

**Files:**
- Modify: `D:\电量管家\src\UI\Forms\MainForm.Hardware.cs:1240-1280,1455-1510`
- Test: `D:\电量管家\tests\test_razer_ui_routing.ps1`

**Interfaces:**
- Consumes:
  - `MouseBatteryInfo`
  - `DeviceCategory.Keyboard`

- [ ] **Step 1: 编写测试脚本 `tests\test_razer_ui_routing.ps1` 模拟雷蛇键盘接入**

验证当一个带有 `Brand = "Razer"`, `DeviceId = "Razer:1532:0216"`, `Category = DeviceCategory.Keyboard` 的设备传给 `UpdateHardwareUI` 时，DPI 滑块、DPI 阶段按钮、回报率按钮全部处于不可见状态（`Visible == false`），而键盘测速与控制面板处于就绪状态。

- [ ] **Step 2: 检查并强化 `MainForm.Hardware.cs` 中的 UI 分发逻辑**

1. 确认当 `info.Category == DeviceCategory.Keyboard` 时：
   - 无论品牌是 `NuPhy` 还是 `Razer` 还是其他品牌，一律隐藏雷蛇鼠标独有的 `lblDpiTitle`、`btnDpiManage`、`btnDpiStages`、`lblRateTitle`、`btnRates`。
   - 非 NuPhy 键盘走标准机械键盘硬件卡片（显示 USB/BLE/2.4G 徽标、持续供电状态、全键无冲测速按钮 `btnKeyTestSpeed`、Windows 键盘设置按钮 `btnKeyboardSettings`）。
2. 在托盘右键菜单和气泡通知中：
   - 键盘设备不显示 `"· XXX DPI · XXXHz"`，只显示品牌与键盘型号名称。

- [ ] **Step 3: 执行联合编译验证**

Run: `cmd.exe /c "D:\电量管家\build.bat"`
Expected: 编译完全无警告通过，生成最新的 `D:\电量管家\FerrisPulse.exe`。

---

### Task 4: 旗舰回归测试、代码审查与全量交付

**Files:**
- Modify: `D:\电量管家\CHANGELOG.md`
- Modify: `D:\电量管家\更新日志.txt`
- Modify: `D:\电量管家\src\Properties\AssemblyInfo.cs`

- [ ] **Step 1: 运行自动化测试套件**

Run: `powershell -ExecutionPolicy Bypass -Command "& 'D:\电量管家\tests\test_razer_classification.ps1'; & 'D:\电量管家\tests\test_razer_dpi_protocol.ps1'"`
Expected: 全部测试通过，零失败。

- [ ] **Step 2: 更新版本信息至 v4.1.0**

更新 `src\Properties\AssemblyInfo.cs`、`CHANGELOG.md` 和 `更新日志.txt`：
- 详细记录：
  1. 【雷蛇键鼠精准分类引擎】：解决 BlackWidow X 等机械键盘被误识别为鼠标导致尝试设置 DPI 报错的问题，雷蛇键盘现在享受专属键盘状态监控与全键无冲测速。
  2. 【雷蛇全系鼠标协议自动适配】：基于 OpenRazer 协议规范重构 HID 报文通信，支持 0x1F / 0x3F / 0xFF 全代际 Transaction ID 自动探测，支持阶段表与实时 XY 报文双协议智能降级。

- [ ] **Step 3: 重新执行 Release 优化编译**

Run: `cmd.exe /c "D:\电量管家\build.bat"`
Expected: 生成最终优化版 `FerrisPulse.exe`。

- [ ] **Step 4: 恢复后台守护运行**

启动最新版程序并校验进程驻留。
