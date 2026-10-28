# tests\test_single_instance_tray_health.ps1
# 验证单实例健康检查不会把「最小化到托盘」的实例误判为卡死
#
# 历史 bug：健康检查只看 MainWindowHandle 是否 IsWindowVisible。
# 托盘态窗口永远不可见 → 必然判为「未响应」→ 重复启动时旧实例被 Kill，
# 表现为「窗口闪一下重开」或用户感知为「软件掉了」。
#
# 修复：窗口句柄存在但不可见时，改用 Process.Responding（消息循环响应性）判断；
# 句柄为 0 时保持原有超时接管逻辑，不丢失「旧实例真死锁时能接管」的能力。

$ErrorActionPreference = "Stop"

$src = Get-Content "src/Core/Program.cs" -Raw -Encoding UTF8

$failures = 0

function Check($cond, $label) {
    if ($cond) {
        Write-Host ("PASS: {0}" -f $label) -ForegroundColor Green
    } else {
        Write-Host ("FAIL: {0}" -f $label) -ForegroundColor Red
        $script:failures++
    }
}

Write-Host ""
Write-Host "===== 1. 健康检查必须使用 Responding =====" -ForegroundColor Cyan
Check ($src -match 'existingProc\.Responding') "健康检查包含 Process.Responding 判断"

Write-Host ""
Write-Host "===== 2. 必须保留窗口可见性快路径 =====" -ForegroundColor Cyan
Check ($src -match 'IsWindowVisible\(hWnd\)') "仍保留 IsWindowVisible 作为快路径"

Write-Host ""
Write-Host "===== 3. 不得用 IsWindowVisible 作为唯一判定 =====" -ForegroundColor Cyan
# 旧写法：if (hWnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hWnd)) 单独决定成功
$oldPattern = 'hWnd\s*!=\s*IntPtr\.Zero\s*&&\s*NativeMethods\.IsWindowVisible'
Check (-not ($src -match $oldPattern)) "不存在「句柄非空 && 可见」的单条件判定（该写法会误杀托盘实例）"

Write-Host ""
Write-Host "===== 4. 句柄为 0 时仍走超时接管 =====" -ForegroundColor Cyan
Check ($src -match 'if \(!successfullyWoken\)') "保留 !successfullyWoken 的接管分支"
Check ($src -match 'existingProc\.Kill\(\)') "保留 Kill 接管能力"

Write-Host ""
if ($failures -eq 0) {
    Write-Host "ALL PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host ("FAILED: {0} 项" -f $failures) -ForegroundColor Red
    exit 1
}
