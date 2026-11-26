# FerrisPulse · 灵脉 — 状态栏多设备独立托盘与双色个性化设计规格

- **日期**: 2026-11-17
- **状态**: 已与用户对齐（待按三阶段路线推进：1. 测试沙盒隔离与清理 ➡️ 2. 修复鼠标休眠唤醒延迟 ➡️ 3. 本功能开发与测试）
- **适用平台与约束**: C# 5.0 / .NET Framework 4.0（`csc.exe`）、零外部依赖（仅 Win32 P/Invoke）、UTF-8 with BOM

---

## 1. 目标与核心原则

为拥有多台无线外设（鼠标、键盘、耳机、手柄等）的用户提供**状态栏多设备独立托盘显示**能力，同时保证：
1. **默认零打扰**：老用户升级后默认保持既有“单图标合并/低电量抢占”模式；多图标模式作为可选项在**托盘右键菜单**和**设备优先级管理页 (`DevicePriorityForm`)** 均可开关与持久化。
2. **图标数量按需自适应**：
   - 当用户设备库已知设备数 `<= 5` 时，仅提供一键预设档位（`2` 至 `min(knownCount, 5)`，最少展示 `2` 个预设或跟随设备数），不显示多余输入框；
   - 只有当设备库已知设备数 `> 5` 时，才在 `2~5` 预设旁提供「自定义数量...」入口，支持手动输入数字（上限可配，如 `16`）。
3. **重连与重启不缩回 `^` 折叠区**：采用 Win32 `Shell_NotifyIconW` 为每个托盘槽位绑定永久不变的静态 `GUID` (`NIF_GUID`)；只要用户曾经将第 2、第 3 等槽位从任务栏溢出区 `^` 拖出一次，之后即使设备全部拔出、休眠或重启应用，Windows 都会通过 `ExePath + SlotGUID` 自动恢复到外显区。
4. **休眠原位驻留与兜底图标**：
   - 接收器在线或蓝牙已连接、且未被用户隐藏的设备，即便进入休眠（`IsSleeping == true`）也保留其托盘槽位并绘制**已设计的断联图标（`--`）**，唤醒后在原槽位直接刷新为绿/品牌色电量，不跳位；
   - 当没有任何符合条件的设备时，保留 **0 号槽位** 展示 1 个断联图标兜底，保证用户随时可右键呼出菜单或关闭多图标模式。
5. **双色独立存储 + 可选联动**：每台设备的「主界面主题色」与「托盘图标颜色」在底层为两个独立变量（支持默认联动），并严格保持主界面外观极简。

---

## 2. 架构与模块划分

### 2.1 底层托盘宿主：`MultiTrayIconHost` (`src/UI/Tray/MultiTrayIconHost.cs`)
- **单图标模式（默认，关闭多图标）**：
  - 沿用现有 `MainForm.trayIcon`（WinForms `NotifyIcon`），100% 保持既有单图标逻辑、`ShowBalloonTip` 与既有托盘反射测试兼容性。
- **多图标模式（开启后）**：
  - 隐藏 `trayIcon`（`trayIcon.Visible = false`，保留对象引用供气泡通知及兼容性使用），由 `MultiTrayIconHost` 通过 `NativeWindow` + `Shell_NotifyIconW` 管理槽位 `0 .. maxSlots-1`。
  - 每个槽位 `slotIndex` 拥有：
    - 确定性静态 `GUID`（如 `8F2C0100-4E3A-4B9D-A011-0000000000XX`）；
    - 独立回调消息 `uCallbackMessage = WM_APP + 0x100 + slotIndex`；
    - 独立 `Icon` 句柄管理（变更时 `DestroyIcon` 旧句柄再 `NIM_MODIFY`，杜绝 GDI 句柄泄漏）；
    - 状态变更差分化更新（仅在电量/充电/连接/休眠/颜色/样式/Tooltip 真正变化时重建句柄或发 `NIM_MODIFY`）；
    - 多余尾部槽位调用 `NIM_DELETE` 撤回；
    - 监听 `WM_TASKBARCREATED`，Explorer 重启后自动按当前有效槽位全量重建。
- **单/双击防抖**：
  - 复用 `MainForm` 约 200ms 单击延时与 `lastTrayClickTick` / `lastTrayDoubleClickTick` 判别逻辑；
  - 记录触发点击/右键的 `slotIndex` 对应的 `DeviceId`。

### 2.2 槽位调度与仲裁核心：`TraySlotPlanner` (`src/UI/Tray/TraySlotPlanner.cs`)
纯静态无副作用算法单元，便于完整单元测试与回放：
- **输入**：`allDevices`、`priorityList`、`lowBatteryFirstEnabled`、`isMultiTrayEnabled`、`maxTrayCount`、`isDeviceHiddenFunc`。
- **过滤条件**：
  - 排除手动隐藏设备（`IsDeviceHidden(d.DeviceId)` 或硬件指纹隐藏）；
  - 纳入“可驻留状态栏设备”：接收器在线 / 蓝牙已连接（含休眠 `IsSleeping == true` 的设备）。
- **排序与仲裁（复用既有低电量仲裁 + 用户优先级规则）**：
  1. 若开启 `lowBatteryFirstEnabled`：电量 `0 <= BatteryPercent <= 30` 且 `!IsCharging` 且 `!IsSleeping` 且已唤醒连接的有效设备置顶，多台低电量按电量升序/优先级排；
  2. 其余设备严格按 `DeviceManager.FindPriorityIndex(priorityList, d.DeviceId)` 升序排列（未入表者置后）。
- **分配槽位**：
  - 取前 `maxTrayCount` 台设备对应槽位 `0 .. K-1`；
  - 对每个已分配设备确定视觉参数：
    - 若 `!d.IsConnected || d.IsSleeping`：传入 `isConnected = false` 绘制既有断联 `--` 图标；Tooltip 显示休眠/离线提示；
    - 否则：`isConnected = true`，绘制实时 `BatteryPercent`、`IsCharging`，着色选用该设备的**托盘独立色 `TrayAccentColor`**；
  - 若 `K == 0`：输出 1 个仅包含槽位 `0` 的**兜底断联状态**（Tooltip 为 `"未检测到设备 (未连接)"`）。

### 2.3 双色独立与个性化持久化：`DeviceAppearance` + `CustomDeviceConfig` + 注册表
- **存储字段**（对任意已发现设备均可保存，即使非 Wizard 添加的内置品牌设备，经由 `DeviceManager` 按 `DeviceId` / `HardwareFingerprint` 统一持久化）：
  - `CustomName`（自定义名称）
  - `ThemeColorHex`（主界面主题高亮色，如 `#00E676`，空串表示使用原品牌默认色）
  - `TrayColorHex`（托盘数字/胶囊高亮色，空串表示跟随联动或使用原品牌默认色）
  - `LinkTrayColorToTheme`（`bool`，默认 `true`）
- **联动契约**：
  - 当 `LinkTrayColorToTheme == true` 时：修改界面主题色会同步写入/计算 `TrayColorHex`，两者视觉一致；
  - 当用户取消「托盘色跟随主题色联动」复选框，或单独调整了托盘颜色时：`LinkTrayColorToTheme` 自动设为 `false`，后续修改界面主题色不再覆盖 `TrayColorHex`，两个变量彻底独立；
  - 提供「恢复品牌默认」动作，一键清空两色自定义值并恢复 `LinkTrayColorToTheme = true`。
- **模型投影 (`MouseBatteryInfo`)**：
  - `BrandColor`：优先返回自定义 `ThemeColorHex`，否则走原品牌/品类默认色（自动作用于主卡片电量条、设备选择器、选项卡指示条、`DevicePriorityForm` 卡片等全部现有界面高亮处）；
  - 新增 `TrayAccentColor`：若独立设置且未联动（或联动生效）则优先返回自定义 `TrayColorHex`，否则在联动下跟随 `BrandColor`。

### 2.4 全局配置项 (`ConfigManager` / `DeviceManager`)
- `MultiTrayEnabled` (`bool`，默认 `false` —— 保证老用户升级行为不变)
- `MultiTrayMaxCount` (`int`，默认 `3` 或跟随当前已发现设备数与 5 的较小值，不低于 `2`，合法范围 `2..16`)

### 2.5 交互入口与 UI 改动
1. **`DevicePriorityForm`（核心管理与发现入口）**：
   - 顶部增加：
     - 开关复选框：「启用状态栏多设备独立图标」
     - 数量选择区（仅在开启多图标时激活）：
       - 已知设备数 `<= 5`：直接呈现一键预设按钮/胶囊（例如 `2 / 3 / 4 / 5` 中不超出设备数或合理档位，无输入框）；
       - 已知设备数 `> 5`：在预设按钮旁增加「自定义...」按钮，点击弹出数字输入对话框或切换输入框，支持手动输入所需数量。
   - **每张设备卡片 (`PriorityReorderControl`) 右侧**：增加小圆点配色色块按钮，点击后弹出 `DevicePersonalizeDialog`。
2. **主界面设备名称静默入口 (`MainForm.UI.cs`)**：
   - 点击主界面顶部设备名称（原 `RenameCurrentDevice()`），直接打开同一款 `DevicePersonalizeDialog`，**不在主界面新增额外图标或角标**，完整保留主卡片极简观感。
3. **`DevicePersonalizeDialog` (`src/UI/Forms/DevicePersonalizeDialog.cs`)**：
   - 包含：自定义名称文本框、主界面主题色（常用电竞预设色块 + `ColorDialog` 自定义取色）、托盘图标色（预设色块 + 自定义取色）、「托盘图标色跟随主界面主题色联动」复选框、「恢复默认」按钮、确定/取消。
4. **托盘右键菜单 (`MainForm.Tray.cs`)**：
   - 增加「状态栏多设备独立图标」勾选菜单项，方便快速启停；**不额外增加颜色设置菜单项**，保持右键菜单简洁。
   - 左键点击第 `i` 颗图标：呼出/切换主窗口并自动调用 `SelectDevice(slotDeviceId)` 定位到该设备；
   - 右键点击第 `i` 颗图标：在 `contextMenu` 打开前将本次菜单作用域 `trayContextDeviceId` 指向该图标对应设备，顶部状态摘要、DPI/回报率/灯光/NuPhy 快捷控制自动围绕该设备渲染。

---

## 3. 关联前置项与风险控制

1. **测试沙盒与清理先行**：当前 `tests/` 目录存在 176 个测试脚本，部分涉及真实硬件读写；在开发前先引入硬件写入测试沙盒并精简整合测试套件。
2. **鼠标休眠唤醒延迟前置排查与根除**：当前单图标模式存在“休眠后移动鼠标未立即变绿恢复”问题，将在多托盘开发前通过 `systematic-debugging` 查明根因并修复，以确保多托盘槽位休眠 `--` 与移动唤醒原地恢复均毫秒级响应。
3. **Windows 10/11 首次新增槽位须知**：由于 Windows 安全限制，应用程序无法绕过系统设置强行外显从未使用过的新 GUID 图标；历史首次启用第 2/3 个槽位时若出现在 `^` 溢出区，用户拖出固定一次后，后续即可跨插拔与重启永久记忆外显位置。
