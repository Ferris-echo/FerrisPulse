# ============================================================================
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 本文件受 AGENT_HANDBOOK.md 铁律约束：
# 1. 严格 C# 5.0 / .NET 4.0 语法基准
# 2. 零外部依赖
# 3. 所有文本文件必须 UTF-8 with BOM
# 10. 测试脚本严禁 Add-Type -Path / LoadFrom / LoadFile（会锁死 exe），
#     必须 [System.IO.File]::ReadAllBytes + [System.Reflection.Assembly]::Load($bytes)
# ============================================================================
# 迈从 4D 协议族「板载档位真实性」回归测试
#
# 用户诉求 (m02400 / m02513)：
#   读取鼠标当前 DPI 与回报率时，必须与板载实际值完全一致，
#   不得私自增加档位、不得凭空生成设备上不存在的选项。
#
# 本测试锁定三件事：
#   A. 源码层：SetDpi / SetDpiStage 不得存在「档位不够就自增」的自动扩档逻辑
#   B. 源码层：休眠占位卡片不得硬编码伪造回报率
#   C. 运行时：越界写入必须被拒绝，且不改变板载档位数量
# ============================================================================

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
Set-Location $root

$script:fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) {
        Write-Host ('PASS: ' + $name)
    } else {
        Write-Host ('FAIL: ' + $name + ' -- ' + $detail)
        $script:fail++
    }
}

# ---------------------------------------------------------------- A. 源码层
$helperPath = Join-Path $root 'src\Hardware\MchoseMouse4DHelper.cs'
Check 'MchoseMouse4DHelper.cs exists' (Test-Path $helperPath) $helperPath
$srcBytes = [System.IO.File]::ReadAllBytes($helperPath)
$hasBom = ($srcBytes.Length -ge 3) -and ($srcBytes[0] -eq 0xEF) -and ($srcBytes[1] -eq 0xBB) -and ($srcBytes[2] -eq 0xBF)
Check 'MchoseMouse4DHelper.cs is UTF-8 with BOM' $hasBom 'missing BOM'
$src = [System.Text.Encoding]::UTF8.GetString($srcBytes)
Check 'no CRCRLF corruption' (-not $src.Contains([string][char]13 + [string][char]13 + [string][char]10)) 'found CRCRLF'

$autoGrow = 'if (target >= stageCount) stageCount = target + 1;'
Check 'no auto-grow statement in helper' (-not $src.Contains($autoGrow)) 'auto-grow still present'

$guard = 'if (target < 0 || target >= stageCount) return false;'
$guardCount = 0
$pos = 0
while ($true) {
    $idx = $src.IndexOf($guard, $pos)
    if ($idx -lt 0) { break }
    $guardCount++
    $pos = $idx + $guard.Length
}
Check 'bounds guard present twice' ($guardCount -eq 2) ('count=' + $guardCount)

$fakeRate = 'dev.PollingRate = 1000;'
Check 'stale card does not hardcode 1000 Hz' (-not $src.Contains($fakeRate)) 'stale card still fakes 1000 Hz'

Check 'snapshot field CachedDpiStages' ($src.Contains('CachedDpiStages')) 'missing'
Check 'snapshot field CachedPollingRate' ($src.Contains('CachedPollingRate')) 'missing'
Check 'snapshot field CachedDpiStage' ($src.Contains('CachedDpiStage')) 'missing'

# ------------------------------------------------- B. UI 层不得套用默认档位
$uiPath = Join-Path $root 'src\UI\Forms\MainForm.Hardware.cs'
Check 'MainForm.Hardware.cs exists' (Test-Path $uiPath) $uiPath
$uiBytes = [System.IO.File]::ReadAllBytes($uiPath)
$uiBom = ($uiBytes.Length -ge 3) -and ($uiBytes[0] -eq 0xEF) -and ($uiBytes[1] -eq 0xBB) -and ($uiBytes[2] -eq 0xBF)
Check 'MainForm.Hardware.cs is UTF-8 with BOM' $uiBom 'missing BOM'
$ui = [System.Text.Encoding]::UTF8.GetString($uiBytes)
Check 'no CRCRLF corruption in UI' (-not $ui.Contains([string][char]13 + [string][char]13 + [string][char]10)) 'found CRCRLF'

$singleStageAnchor = 'dpiStageList = new List<int>(new int[] { info.Dpi });'
Check 'UI falls back to device-reported single stage' ($ui.Contains($singleStageAnchor)) 'single-stage fallback missing'

# 上游其他鼠标品牌的回报率表与本修复无关，只断言 UpdatePerformanceSection 的 baseSupported 兵底。
$uiShort = $ui.Replace(' ', '')
$badFallback = 'MaxPollingRate==1000?newint[]{125,250,500,1000}:newint[]{125,250,500,1000,2000,4000,8000}'
Check 'UI rate fallback does not fabricate full table' (-not $uiShort.Contains($badFallback)) 'fabricated rate table still present'

$goodRateFallback = 'baseSupported=newint[]{info.PollingRate};'
Check 'UI rate fallback uses device-reported value' ($uiShort.Contains($goodRateFallback)) 'device-reported rate fallback missing'

# ---- 严格档位数量：不得用 Math.Max(1,...) 凭空造出一个档位
$emptyStageFallback = 'dpiStageList=newList<int>();'
Check 'UI clears stages when device reports none' ($uiShort.Contains($emptyStageFallback)) 'empty-stage branch missing'

$ui2Path = Join-Path $root 'src\UI\Forms\MainForm.UI.cs'
$ui2Bytes = [System.IO.File]::ReadAllBytes($ui2Path)
$ui2Bom = ($ui2Bytes.Length -ge 3) -and ($ui2Bytes[0] -eq 0xEF) -and ($ui2Bytes[1] -eq 0xBB) -and ($ui2Bytes[2] -eq 0xBF)
Check 'MainForm.UI.cs is UTF-8 with BOM' $ui2Bom 'missing BOM'
$ui2 = [System.Text.Encoding]::UTF8.GetString($ui2Bytes)
$ui2Short = $ui2.Replace(' ', '').Replace([string][char]13, '').Replace([string][char]10, '')
Check 'no CRCRLF corruption in MainForm.UI.cs' (-not $ui2.Contains([string][char]13 + [string][char]13 + [string][char]10)) 'found CRCRLF'

$badDpiCount = 'intcount=Math.Max(1,Math.Min(btnDpiStages.Length,dpiStageList.Count));'
Check 'DPI button count not forced to at least 1' (-not $ui2Short.Contains($badDpiCount)) 'Math.Max(1,...) still present'
$goodDpiCount = 'intcount=Math.Min(btnDpiStages.Length,dpiStageList.Count);'
Check 'DPI button count equals onboard stage count' ($ui2Short.Contains($goodDpiCount)) 'exact count missing'

$badRateCount = 'intcount=Math.Max(1,Math.Min(5,Math.Min(btnRates.Length,(pollingRateValues!=null)?pollingRateValues.Length:4)));'
Check 'rate button count not forced to at least 1' (-not $ui2Short.Contains($badRateCount)) 'rate Math.Max(1,...) still present'
$goodRateCount = 'intcount=(pollingRateValues!=null)?Math.Min(5,Math.Min(btnRates.Length,pollingRateValues.Length)):0;'
Check 'rate button count equals device-reported count' ($ui2Short.Contains($goodRateCount)) 'exact rate count missing'

$badGameDpi = 'intdpiCount=Math.Max(1,Math.Min(btnGameDpiStages.Length,dpiStageList.Count));'
Check 'game DPI count not forced to at least 1' (-not $ui2Short.Contains($badGameDpi)) 'game dpi Math.Max(1,...) still present'
$badGameRate = 'intrateCount=Math.Max(1,Math.Min(5,Math.Min(btnGameRates.Length,(pollingRateValues!=null)?pollingRateValues.Length:4)));'
Check 'game rate count not forced to at least 1' (-not $ui2Short.Contains($badGameRate)) 'game rate Math.Max(1,...) still present'

# ---- SetGameRateControlsVisibility 里的可见性也不得凭空造档位
$badVisDpi = 'intcount=Math.Max(1,Math.Min(5,dpiStageList.Count));'
Check 'game visibility DPI count not forced to at least 1' (-not $ui2Short.Contains($badVisDpi)) 'visibility dpi Math.Max(1,...) still present'
$goodVisDpi = 'intcount=(dpiStageList!=null&&dpiStageList.Count>0)?Math.Min(5,dpiStageList.Count):0;'
Check 'game visibility DPI count equals onboard count' ($ui2Short.Contains($goodVisDpi)) 'exact visibility count missing'
$goodVisRate = 'intcount=(pollingRateValues!=null&&pollingRateValues.Length>0)?Math.Min(5,Math.Min(btnGameRates.Length,pollingRateValues.Length)):0;'
Check 'game visibility rate count equals reported count' ($ui2Short.Contains($goodVisRate)) 'exact visibility rate count missing'
$badVisRate = 'intcount=Math.Max(1,Math.Min(5,Math.Min(btnGameRates.Length,(pollingRateValues!=null)?pollingRateValues.Length:4)));'
Check 'game visibility rate count not forced to at least 1' (-not $ui2Short.Contains($badVisRate)) 'visibility rate Math.Max(1,...) still present'

$cfg = [System.IO.File]::ReadAllText((Join-Path $root 'src\UI\Forms\MainForm.cs'))
$cfgShort = $cfg.Replace(' ', '')
$badDefaultStages = 'privateList<int>dpiStageList=newList<int>{400,800,1600,3000,6400};'
Check 'no hardcoded default DPI stage list' (-not $cfgShort.Contains($badDefaultStages)) 'default stage list still present'
$goodEmptyStages = 'privateList<int>dpiStageList=newList<int>();'
Check 'default DPI stage list is empty' ($cfgShort.Contains($goodEmptyStages)) 'default stage list not emptied'
$badDefaultRates = 'privateint[]pollingRateValues=newint[]{1000,2000,4000,8000};'
Check 'no hardcoded default polling rates' (-not $cfgShort.Contains($badDefaultRates)) 'default rate array still present'
$badRegDpi = 'var customDpiVal = key.GetValue(' + [char]34 + 'CustomDpiStages' + [char]34 + ') as string;'
Check 'registry stages not used as UI source' (-not $cfg.Contains($badRegDpi)) 'CustomDpiStages still read into UI'

# ---- 写入与 OSD 文案也不得凭空补 800 DPI / 5 档
$bad800 = ': 800;'
Check 'no fabricated 800 DPI write fallback' (-not $ui.Contains($bad800)) 'still writes 800 DPI when no stages'
$badFive = 'dpiStageValues != null ? dpiStageValues.Length : 5'
Check 'no fabricated stage-count 5 in OSD' (-not $ui.Contains($badFive)) 'OSD still reports 5 stages'
$badValid = '(dpiStageValues != null && dpiStageValues.Length > 0) ? dpiStageValues.Length : 5'
Check 'no fabricated validCount 5' (-not $ui.Contains($badValid)) 'validCount still faked'

# ---- 不得残留 1600 DPI 伪造兜底
$bad1600 = ': 1600;'
Check 'no fabricated 1600 DPI fallback' (-not $ui.Contains($bad1600)) 'still falls back to 1600 DPI'

# ---- 不得残留硬编码档位上限 5（设备可能有 1 档，也可能有 6 档）
$badCap5a = 'targetStage < 1 || targetStage > 5'
$badCap5b = 'stage < 1 || stage > 5'
Check 'no hardcoded targetStage cap 5' (-not $ui.Contains($badCap5a)) 'targetStage still capped at 5'
Check 'no hardcoded stage cap 5 in game setter' (-not $ui.Contains($badCap5b)) 'game stage still capped at 5'

# ---- 回报率管理对话框不得补全表 / 不得编造 1000 Hz
$uiOnly = [System.IO.File]::ReadAllText((Join-Path $root 'src\UI\Forms\MainForm.UI.cs'))
$uiOnlyShort = $uiOnly.Replace(' ', '').Replace([string][char]13, '').Replace([string][char]10, '')
$badRateTable = 'newint[]{125,250,500,1000,2000,4000,8000}'
Check 'no fabricated rate table in rate dialog' (-not $uiOnlyShort.Contains($badRateTable)) 'rate dialog still fabricates full table'
$badCurHz = 'curDev.PollingRate>0?curDev.PollingRate:1000;'
Check 'no fabricated 1000 Hz in rate dialog' (-not $uiOnlyShort.Contains($badCurHz)) 'rate dialog still fabricates 1000 Hz'

# ------------------------------------------------------------- C. 运行时验证
$bin = Join-Path $root 'FerrisPulse.exe'
if (-not (Test-Path $bin)) { $bin = Join-Path $root 'bin\FerrisPulse.exe' }
if (Test-Path $bin) {
    # 铁律 10：绝不用 Add-Type -Path / LoadFrom / LoadFile
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($bin))
    $t4d = $asm.GetType('RazerBatteryTray.MchoseMouse4DHelper')
    Check 'MchoseMouse4DHelper type resolvable' ($t4d -ne $null) 'type missing'

    if ($t4d -ne $null) {
        $BF = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
        $t4d.GetMethod('InvalidateCache', $BF).Invoke($null, @()) | Out-Null
        $dev = $t4d.GetMethod('QueryMchose4DMouse', $BF).Invoke($null, @())

        if ($dev -ne $null) {
            $stageCount = [int]$dev.DpiStageCount
            $stages = @($dev.DpiStages)
            Write-Host ('  device: ' + $dev.DeviceName + ' stageCount=' + $stageCount + ' stages=' + ($stages -join ',') + ' rate=' + $dev.PollingRate)

            Check 'stageCount matches stages length' ($stageCount -eq $stages.Length) ('stageCount=' + $stageCount + ' len=' + $stages.Length)

            $allPositive = $true
            foreach ($v in $stages) { if ([int]$v -le 0) { $allPositive = $false } }
            Check 'all stage values positive' $allPositive 'found non-positive stage'

            $rejected = $true
            if ($stageCount -ge 1 -and $stageCount -lt 6) {
                $callArgs = New-Object object[] 3
                $callArgs[0] = [int]3200
                $callArgs[1] = [int]3200
                $callArgs[2] = [int]($stageCount + 1)
                $res = $t4d.GetMethod('SetDpi', $BF).Invoke($null, $callArgs)
                $rejected = -not [bool]$res
            }
            Check 'out-of-range SetDpi rejected' $rejected 'write beyond stageCount was accepted'

            $t4d.GetMethod('InvalidateCache', $BF).Invoke($null, @()) | Out-Null
            $after = $t4d.GetMethod('QueryMchose4DMouse', $BF).Invoke($null, @())
            if ($after -ne $null) {
                Check 'onboard stage count unchanged after rejected write' ([int]$after.DpiStageCount -eq $stageCount) ('before=' + $stageCount + ' after=' + $after.DpiStageCount)
            }
        } else {
            Write-Host 'SKIP: no 4D device attached (runtime checks skipped)'
        }
    }
} else {
    Write-Host 'SKIP: FerrisPulse.exe not built (runtime checks skipped)'
}

Write-Host ''
if ($script:fail -eq 0) {
    Write-Host '[ALL PASSED] 板载档位真实性测试通过：不自动扩档、不伪造选项'
    exit 0
} else {
    Write-Host ('[FAILED] ' + $script:fail + ' check(s) failed')
    exit 1
}
