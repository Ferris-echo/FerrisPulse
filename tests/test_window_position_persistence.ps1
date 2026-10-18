## 问题

主窗口每次显示（托盘点击、双击、唤醒已有实例）都会从注册表恢复上次保存的窗口坐标，但 `RestoreWindowPosition()` 只要求窗口有 100×100 像素与屏幕工作区相交，就直接采用保存坐标，从不把窗口夹紧到工作区内。

一旦保存的 Y 偏低（窗口高度变化、拖动、分辨率/缩放变化等），窗口下边缘就会落到任务栏下方。由于每次显示都恢复同一坐标，会稳定复现“打开时下半截被任务栏挡住，必须手动拖上去”。

复现环境（Windows 11，150% 缩放，工作区 1707×1019，窗口约 474×807）：

1. 将 `HKCU\Software\FerrisPulse\WindowY` 设为 700
2. 启动程序
3. 窗口底部到达 1507，超过工作区底部 1019，下边缘被任务栏遮挡约 488px

## 修复

- 恢复位置时选择与保存矩形交集最大的显示器工作区，不再使用固定的“100×100 可见”判断
- 将窗口 X/Y 夹紧到 `WorkingArea` 内，保证整个窗口完整可见
- 窗口尺寸大于工作区时，优先保证左上角可见

核心改动：

```csharp
if (this.Width >= workArea.Width)
    restoredX = workArea.Left;
else
    restoredX = Math.Min(Math.Max(restoredX, workArea.Left), workArea.Right - this.Width);

if (this.Height >= workArea.Height)
    restoredY = workArea.Top;
else
    restoredY = Math.Min(Math.Max(restoredY, workArea.Top), workArea.Bottom - this.Height);

this.Location = new Point(restoredX, restoredY);
```

## 测试

更新 `tests/test_window_position_persistence.ps1`：

- 测试坐标改为基于 `Screen.PrimaryScreen.WorkingArea` 动态计算，避免小屏幕上的误报
- 新增越界坐标夹紧断言
- 测试结束后恢复原始注册表坐标，避免污染真实配置

本地验证：

- `build.bat`（csc.exe，C# 5.0）编译通过
- 注入 `WindowY=700`：修复前窗口底部 1507（被任务栏遮挡）；修复后底部 1019（正好贴任务栏上沿）
- `tests/test_window_position_persistence.ps1` 全部断言通过

## 备注

- 保持 C# 5.0 / .NET Framework 4.0 语法基准与 UTF-8 BOM 编码
- 未引入任何第三方依赖
- 仅修改 `src/UI/Forms/MainForm.cs` 与对应测试
