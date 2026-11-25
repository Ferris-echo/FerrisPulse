# 低电量分级提醒设置与多托盘休眠琥珀记忆电量设计文档

**日期**: 2026-11-23  
**范围**: `ConfigManager`、`ModernCheckBox`、`LowBatteryAlertSettingsDialog`、`MainForm`、`TraySlotPlanner`

## 1. 需求共识
1. **入口与交互形态（方案 B — 双区融合）**：
   - 保留主界面「功能设置与偏好」左下角 `chkLowAlert`（「低电量提醒 (≤20%)」）的 2×2 复选框视觉对称。
   - **左侧方框**：作为低电量提醒总开关（取消勾选 = 一键静音全部三项提醒）。
   - **右侧文字**：复用关于页无下划线悬浮提亮点击样式（`Cursors.Hand`，悬停提亮，点击不误触勾选框），弹出「低电量提醒设置」对话框 `LowBatteryAlertSettingsDialog`。
2. **三项独立配置及默认值**：
   - `LowBatteryNotifyEnabled`（低电量系统通知提醒）：默认 **`true`**
   - `LowBatteryUiColorEnabled`（低电量主界面颜色提醒）：默认 **`true`**
   - `LowBatteryTrayColorEnabled`（低电量托盘图标变色提醒）：默认 **`false`**
   - 联动规则：三项全取消勾选时总开关自动置 `false`；总开关原为 `false` 时在弹窗勾选任意一项则总开关自动置 `true`。
3. **变色门禁（含 ≤20% 黄色与 ≤10% 红色）**：
   - 仅当「总开关 && 对应变色子项」为 `true` 且设备未充电时，才在 `≤20%` 转为黄色、`≤10%` 转为红色；否则（含默认托盘状态）**一并取消黄/红变色，始终保持设备品牌色 / 自定义配色**。
4. **多托盘休眠图标恢复琥珀色记忆电量**：
   - `TraySlotPlanItem` 新增 `IsSleeping`；当设备处于已连接休眠且有有效电量缓存（`d.IsConnected && d.IsSleeping && d.BatteryPercent >= 0`）时，保持 `VisualConnected = true` 且 `IsSleeping = true`，在多托盘模式下与单托盘一致绘制**琥珀黄色（`255, 183, 77`）记忆电量数字**；仅真正离线/无电量缓存时才显示灰色 `--` 断联图标。
