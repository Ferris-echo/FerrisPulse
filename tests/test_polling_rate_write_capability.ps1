# tests/test_polling_rate_write_capability.ps1
# issue #35 回归验证：回报率「可写能力位」三态 + UI 只读置灰 + 前置拦截
#   1. MouseBatteryInfo 必须暴露 SupportsPollingRateWrite（三态 bool?）
#   2. Logitech 能力缓存必须能读写（null 未知 / false 拒绝 / true 可写）
#   3. 能力为 false 时：回报率快捷按钮与齿轮按钮必须 Enabled=false（不隐藏、不误伤其它设备）
#   4. 能力为 true（或未知）时：必须保持可点
#   5. 能力为 false 时点击回报率必须被前置拦截：不得下发、不得改写 desktopPollingRate
# 说明：全内存反射装载，严禁 Add-Type -Path 锁死可执行文件句柄。

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes((Resolve-Path $asmPath).Path))

$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")
$tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")
foreach ($t in @($tInfo, $tMain, $tLogi)) {
    if ($null -eq $t) { Write-Host "FAIL: 缺少必需类型" -ForegroundColor Red; exit 1 }
}

# ---------------------------------------------------------------
# 测试 1：模型能力位必须是三态（bool?）
# ---------------------------------------------------------------
$prop = $tInfo.GetProperty("SupportsPollingRateWrite")
if ($null -eq $prop) {
    Write-Host "FAIL: MouseBatteryInfo.SupportsPollingRateWrite 不存在" -ForegroundColor Red
    exit 1
}
if ($prop.PropertyType -ne [Nullable[bool]]) {
    Write-Host ("FAIL: SupportsPollingRateWrite 应为 Nullable<bool>（三态），实际 " + $prop.PropertyType.Name) -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 能力位为三态 bool?（未知/可写/不可写）" -ForegroundColor Green

# ---------------------------------------------------------------
# 测试 2：能力缓存读写（Logitech）
# ---------------------------------------------------------------
$mGet = $tLogi.GetMethod("GetPollingRateWriteCapability")
$mSet = $tLogi.GetMethod("SetPollingRateWriteCapability")
$testId = "Logitech:TEST:CAP01"
if ($null -eq $mGet -or $null -eq $mSet) {
    Write-Host "FAIL: Logitech 能力缓存读写方法缺失" -ForegroundColor Red
    exit 1
}
$null = $mSet.Invoke($null, @($testId, $false))
if ([bool]$mGet.Invoke($null, @($testId)) -ne $false) {
    Write-Host "FAIL: 能力缓存未记录「不可写」" -ForegroundColor Red
    exit 1
}
$null = $mSet.Invoke($null, @($testId, $true))
if ([bool]$mGet.Invoke($null, @($testId)) -ne $true) {
    Write-Host "FAIL: 能力缓存未记录「可写」" -ForegroundColor Red
    exit 1
}
$unknown = $mGet.Invoke($null, @("Logitech:TEST:UNKNOWN99"))
if ($null -ne $unknown) {
    Write-Host "FAIL: 未探测设备应返回 null（未知），实际非空" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 能力缓存三态读写正确（未知保持 null）" -ForegroundColor Green

# ---------------------------------------------------------------
# 测试 3/4：UI 置灰与恢复
# ---------------------------------------------------------------
function New-LogiInfo([object]$capability) {
    $info = [Activator]::CreateInstance($tInfo)
    $info.Brand = "Logitech"
    $info.DeviceId = "Logitech:TEST:CAP01"
    $info.DeviceName = "G304 Lightspeed Wireless Gaming Mouse"
    $info.Category = [Enum]::Parse($tCat, "Mouse")
    $info.Transport = "Lightspeed 无线"
    $info.IsConnected = $true
    $info.IsSleeping = $false
    $info.BatteryPercent = 90
    $info.Dpi = 3200
    $info.DpiY = 3200
    $info.DpiStage = 4
    $info.DpiStageCount = 5
    $info.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
    $info.DpiStagesY = [int[]]@(400, 800, 1600, 3200, 6400)
    $info.PollingRate = 1000
    $info.SupportedPollingRates = [int[]]@(125, 250, 500, 1000)
    $info.SupportsPollingRateWrite = $capability
    return $info
}

$form = [Activator]::CreateInstance($tMain, @($false))
$null = $form.Handle
$mUpdate = $null
foreach ($m in $tMain.GetMethods([System.Reflection.BindingFlags]"Instance,NonPublic")) {
    if ($m.Name -eq "UpdatePerformanceSection" -and $m.GetParameters().Count -eq 2) { $mUpdate = $m; break }
}
if ($null -eq $mUpdate) { Write-Host "FAIL: UpdatePerformanceSection(info, accent) 缺失" -ForegroundColor Red; $form.Dispose(); exit 1 }

$fldRates = $tMain.GetField("btnRates", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldManage = $tMain.GetField("btnRateManage", [System.Reflection.BindingFlags]"Instance,NonPublic")

# 3. 不可写 → 全部置灰
$infoNo = New-LogiInfo ([Nullable[bool]]$false)
$null = $mUpdate.Invoke($form, @($infoNo, $infoNo.BrandColor))
$rateBtns = $fldRates.GetValue($form)
$checked = 0
for ($i = 0; $i -lt $rateBtns.Length; $i++) {
    if ($null -ne $rateBtns[$i] -and -not [string]::IsNullOrEmpty($rateBtns[$i].Text)) {
        $checked++
        # 只读态：必须灰化（IsReadOnly）但保持 Enabled=true，否则原生 ToolTip 不会弹出
        if (-not $rateBtns[$i].IsReadOnly) {
            Write-Host ("FAIL: 不可写设备的回报率按钮 [{0}] '{1}' 未灰化" -f $i, $rateBtns[$i].Text) -ForegroundColor Red
            $form.Dispose(); exit 1
        }
        if (-not $rateBtns[$i].Enabled) {
            Write-Host ("FAIL: 回报率按钮 [{0}] 被 Enabled=false 禁用，原生气泡无法弹出" -f $i) -ForegroundColor Red
            $form.Dispose(); exit 1
        }
    }
}
if ($checked -eq 0) { Write-Host "FAIL: 未找到任何回报率快捷按钮" -ForegroundColor Red; $form.Dispose(); exit 1 }
if ($fldManage.GetValue($form).Enabled) {
    Write-Host "FAIL: 不可写时回报率齿轮按钮仍可点" -ForegroundColor Red
    $form.Dispose(); exit 1
}
$gameBtnsCheck = $tMain.GetField("btnGameRates", [System.Reflection.BindingFlags]"Instance,NonPublic").GetValue($form)
if ($null -ne $gameBtnsCheck -and $null -ne $gameBtnsCheck[0]) {
    if (-not $gameBtnsCheck[0].IsReadOnly) {
        Write-Host "FAIL: 不可写设备的电竞档位按钮未灰化" -ForegroundColor Red
        $form.Dispose(); exit 1
    }
}
Write-Host ("PASS: 不可写设备 {0} 个回报率按钮 + 齿轮按钮全部灰化（只读态仍可悬停显示气泡）" -f $checked) -ForegroundColor Green

# 3b. 电竞回报率必须锁死为关闭且不可编辑，并给出气泡提示
$fldAutoRate = $tMain.GetField("autoGameRateEnabled", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldChkGameRate = $tMain.GetField("chkAutoGameRate", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldToolTip = $tMain.GetField("toolTipTop", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldGameRates = $tMain.GetField("btnGameRates", [System.Reflection.BindingFlags]"Instance,NonPublic")
if ($null -eq $fldChkGameRate) { Write-Host "FAIL: chkAutoGameRate 字段缺失" -ForegroundColor Red; $form.Dispose(); exit 1 }
$chkGameRate = $fldChkGameRate.GetValue($form)
if ($null -ne $chkGameRate) {
    if ([bool]$fldAutoRate.GetValue($form)) {
        Write-Host "FAIL: 不可写设备下电竞回报率仍处于开启状态" -ForegroundColor Red
        $form.Dispose(); exit 1
    }
    if ($chkGameRate.Checked) {
        Write-Host "FAIL: 不可写设备下电竞回报率复选框仍为勾选" -ForegroundColor Red
        $form.Dispose(); exit 1
    }
    if ($chkGameRate.Enabled) {
        Write-Host "FAIL: 不可写设备下电竞回报率复选框仍可编辑" -ForegroundColor Red
        $form.Dispose(); exit 1
    }
}
$tip = $fldToolTip.GetValue($form)
$gameTip = ""
$gameBtns = $fldGameRates.GetValue($form)
if ($null -ne $gameBtns -and $null -ne $gameBtns[0]) { $gameTip = $tip.GetToolTip($gameBtns[0]) }
if ($gameTip -notmatch "不支持修改回报率") {
    Write-Host ("FAIL: 电竞回报率按钮缺少不支持修改提示，实际: " + $gameTip) -ForegroundColor Red
    $form.Dispose(); exit 1
}
$rateTip = $tip.GetToolTip($rateBtns[0])
if ($rateTip -notmatch "不支持修改回报率") {
    Write-Host ("FAIL: 常规回报率按钮缺少不支持修改提示，实际: " + $rateTip) -ForegroundColor Red
    $form.Dispose(); exit 1
}
if ($null -ne $chkGameRate) {
    $chkTip = $tip.GetToolTip($chkGameRate)
    if ($chkTip -notmatch "电竞回报率已自动关闭") {
        Write-Host ("FAIL: 电竞回报率开关缺少锁定说明，实际: " + $chkTip) -ForegroundColor Red
        $form.Dispose(); exit 1
    }
}
Write-Host "PASS: 电竞回报率已锁死（关闭 + 不可编辑），常规/电竞档位按钮均带不支持提示" -ForegroundColor Green

# 3c. 「全屏游戏自动优化」总开关不得把已锁死的电竞回报率重新勾上（概率复现的根因）
$fldAutoDpi = $tMain.GetField("autoGameDpiEnabled", [System.Reflection.BindingFlags]"Instance,NonPublic")
$mOpt = $tMain.GetMethod("SetAutoGameOptEnabled")
$null = $mOpt.Invoke($form, @([bool]$true))
$chkAfterOpt = $fldChkGameRate.GetValue($form)
if ([bool]$fldAutoRate.GetValue($form)) {
    Write-Host "FAIL: 开启「全屏游戏自动优化」后电竞回报率被重新打开" -ForegroundColor Red
    $form.Dispose(); exit 1
}
if ($chkAfterOpt.Checked) {
    Write-Host "FAIL: 开启「全屏游戏自动优化」后电竞回报率复选框被勾上" -ForegroundColor Red
    $form.Dispose(); exit 1
}
if ($chkAfterOpt.Enabled) {
    Write-Host "FAIL: 开启「全屏游戏自动优化」后电竞回报率复选框恢复可编辑" -ForegroundColor Red
    $form.Dispose(); exit 1
}
if (-not [bool]$fldAutoDpi.GetValue($form)) {
    Write-Host "FAIL: 锁死回报率时「电竞 DPI」应仍可正常开启，实际未开" -ForegroundColor Red
    $form.Dispose(); exit 1
}
# 直接调用内部开关也必须被兜底闸门拦住
$mRateSwitch = $tMain.GetMethod("SetAutoGameRateEnabled", [System.Reflection.BindingFlags]"Instance,NonPublic")
$null = $mRateSwitch.Invoke($form, @([bool]$true))
if ([bool]$fldAutoRate.GetValue($form)) {
    Write-Host "FAIL: 直接调用 SetAutoGameRateEnabled(true) 绕过了锁死闸门" -ForegroundColor Red
    $form.Dispose(); exit 1
}
Write-Host "PASS: 总开关只开「电竞 DPI」，电竞回报率保持关闭且不可编辑（兜底闸门生效）" -ForegroundColor Green

# 4. 可写 → 恢复可点
$infoYes = New-LogiInfo ([Nullable[bool]]$true)
$null = $mUpdate.Invoke($form, @($infoYes, $infoYes.BrandColor))
$rateBtns2 = $fldRates.GetValue($form)
for ($i = 0; $i -lt $rateBtns2.Length; $i++) {
    if ($null -ne $rateBtns2[$i] -and -not [string]::IsNullOrEmpty($rateBtns2[$i].Text)) {
        if (-not $rateBtns2[$i].Enabled -or $rateBtns2[$i].IsReadOnly) {
            Write-Host ("FAIL: 可写设备的回报率按钮 [{0}] 被误灰化/误禁用" -f $i) -ForegroundColor Red
            $form.Dispose(); exit 1
        }
    }
}
if (-not $fldManage.GetValue($form).Enabled) {
    Write-Host "FAIL: 可写时齿轮按钮被误禁用" -ForegroundColor Red
    $form.Dispose(); exit 1
}
if ($null -ne $chkGameRate -and -not $chkGameRate.Enabled) {
    Write-Host "FAIL: 可写设备下电竞回报率开关仍被锁死" -ForegroundColor Red
    $form.Dispose(); exit 1
}
Write-Host "PASS: 可写设备按钮恢复正常可点，电竞回报率开关解除锁定（无误伤）" -ForegroundColor Green

# 5. 不可写时点击必须被前置拦截
$null = $mUpdate.Invoke($form, @($infoNo, $infoNo.BrandColor)) | Out-Null
$fldDesktop = $tMain.GetField("desktopPollingRate", [System.Reflection.BindingFlags]"Instance,NonPublic")
$before = [int]$fldDesktop.GetValue($form)
$tMain.GetMethod("SetPollingRateFromUI").Invoke($form, @([int]2000)) | Out-Null
Start-Sleep -Milliseconds 500
$after = [int]$fldDesktop.GetValue($form)
if ($after -ne $before) {
    Write-Host ("FAIL: 不可写设备仍改写了 desktopPollingRate（{0} -> {1}）" -f $before, $after) -ForegroundColor Red
    $form.Dispose(); exit 1
}
Write-Host ("PASS: 不可写设备点击被拦截，desktopPollingRate 保持 {0} 未被污染" -f $after) -ForegroundColor Green

$form.Dispose()
Remove-ItemProperty -Path 'HKCU:\Software\RazerBatteryTray\RateWriteCap' -Name 'Logitech_TEST_CAP01' -ErrorAction SilentlyContinue
Write-Host "ALL PASS: issue #35 能力位、UI 置灰与前置拦截全部生效" -ForegroundColor Green
exit 0
