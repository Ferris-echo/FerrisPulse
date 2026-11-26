# ============================================================================
# 工具名称: TestRegistryGuard.ps1
# 核心职责:
#   为测试脚本与回归运行器提供 HKCU 注册表子树（含子键、值数据及 RegistryValueKind）
#   的深快照与无损回滚，防止测试调用 SavePriorityList / SaveBatteryState / SaveConfig
#   污染用户真实生产配置。
# ============================================================================

function Save-TestRegistrySnapshot {
    param(
        [string[]]$Roots
    )

    $snapshot = @{}
    if ($null -eq $Roots) { return $snapshot }

    foreach ($root in $Roots) {
        if ([string]::IsNullOrEmpty($root)) { continue }
        if (-not (Test-Path $root)) {
            $snapshot[$root] = $null
            continue
        }

        $tree = @{}
        $stack = New-Object System.Collections.Stack
        $stack.Push('')

        while ($stack.Count -gt 0) {
            $rel = [string]$stack.Pop()
            if ($rel -eq '') { $path = $root } else { $path = $root + '\' + $rel }
            if (-not (Test-Path $path)) { continue }

            $key = Get-Item -Path $path
            $values = @{}
            foreach ($name in $key.GetValueNames()) {
                $val = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $kind = $key.GetValueKind($name)
                $values[$name] = @{
                    Value = $val
                    Kind  = $kind
                }
            }
            $tree[$rel] = $values

            foreach ($sub in $key.GetSubKeyNames()) {
                if ($rel -eq '') { $childRel = $sub } else { $childRel = $rel + '\' + $sub }
                $stack.Push($childRel)
            }
        }

        $snapshot[$root] = $tree
    }

    return $snapshot
}

function Restore-TestRegistrySnapshot {
    param(
        $Snapshot
    )

    if ($null -eq $Snapshot) { return }

    foreach ($root in $Snapshot.Keys) {
        $tree = $Snapshot[$root]

        if ($null -eq $tree) {
            if (Test-Path $root) {
                Remove-Item -Path $root -Recurse -Force -ErrorAction SilentlyContinue
            }
            continue
        }

        foreach ($rel in @($tree.Keys)) {
            if ($rel -eq '') { $path = $root } else { $path = $root + '\' + $rel }
            if (-not (Test-Path $path)) {
                New-Item -Path $path -Force -ErrorAction SilentlyContinue | Out-Null
            }
            if (-not (Test-Path $path)) { continue }

            $key = Get-Item -Path $path
            $values = $tree[$rel]

            foreach ($name in @($key.GetValueNames())) {
                if (-not $values.ContainsKey($name)) {
                    Remove-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue
                }
            }

            foreach ($name in @($values.Keys)) {
                $entry = $values[$name]
                Remove-ItemProperty -Path $path -Name $name -ErrorAction SilentlyContinue
                New-ItemProperty -Path $path -Name $name -Value $entry.Value -PropertyType $entry.Kind -Force -ErrorAction SilentlyContinue | Out-Null
            }
        }

        $existing = New-Object System.Collections.ArrayList
        $stack = New-Object System.Collections.Stack
        $stack.Push('')

        while ($stack.Count -gt 0) {
            $rel = [string]$stack.Pop()
            if ($rel -eq '') { $path = $root } else { $path = $root + '\' + $rel }
            if (-not (Test-Path $path)) { continue }
            $key = Get-Item -Path $path
            foreach ($sub in $key.GetSubKeyNames()) {
                if ($rel -eq '') { $childRel = $sub } else { $childRel = $rel + '\' + $sub }
                [void]$existing.Add($childRel)
                $stack.Push($childRel)
            }
        }

        $ordered = $existing | Sort-Object -Property @{ Expression = { $_.Length } } -Descending
        foreach ($rel in @($ordered)) {
            if (-not $tree.ContainsKey($rel)) {
                Remove-Item -Path ($root + '\' + $rel) -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
