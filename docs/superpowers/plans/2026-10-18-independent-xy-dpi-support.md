# 雷蛇 & 瓦尔基里 XY 轴独立灵敏度与比例调节实现方案

> **文件路径**: `docs/superpowers/plans/2026-10-18-independent-xy-dpi-support.md`  
> **创建日期**: 2026-10-18  
> **状态**: 实施中 (In Progress)  
> **目标外设**: 雷蛇 (Razer) 全系鼠标、瓦尔基里 (Valkyrie) 全系鼠标  

---

## 1. 需求与工程背景

电竞玩家（尤其是 CS2、Apex、Valorant、CF 等第一人称射击游戏玩家）在采用 4:3 拉伸（如 1280×960、1440×1080）或 16:10 宽屏时，横向画面被拉伸导致 X 轴与 Y 轴在屏幕上的像素位移速度产生物理视觉差异。
为解决此问题，雷蛇官方雷云 (Synapse) 与瓦尔基里官方驱动 (VK HUB) 均提供了光学传感器级别的 **X/Y 轴灵敏度独立调节**。

本方案在 **FerrisPulse · 灵脉** (轻量免驱、零外部依赖、C# 5.0 / .NET Framework 4.0) 架构下，同时为**雷蛇**与**瓦尔基里**鼠标完整落地硬件级 X/Y 轴独立灵敏度与快捷比例调节功能。

---

## 2. 硬件协议底层实现设计

### 2.1 雷蛇 (Razer) 协议 (VID 0x1532)
1. **实时 DPI 写入 (`0x04 0x05`, Class 0x04, ID 0x05, Length 7)**:
   - Byte 0: `0x01` (VARSTORE)
   - Byte 1..2: `DpiX` (`(dpiX >> 8) & 0xFF`, `dpiX & 0xFF`)
   - Byte 3..4: `DpiY` (`(dpiY >> 8) & 0xFF`, `dpiY & 0xFF`)
   - Byte 5..6: `0x00, 0x00`
2. **多档板载 EEPROM 写入 (`0x04 0x06`, Class 0x04, ID 0x06, Length 0x26)**:
   - Byte 0: `0x01` (VARSTORE)
   - Byte 1: `activeStage` (1..5)
   - Byte 2: `stageCount` (1..5)
   - 每个档位 `s` (7 字节，起始于 `3 + s * 7`):
     - Byte 0: `s + 1` (档位序号)
     - Byte 1..2: `stagesX[s]` (`(x >> 8) & 0xFF`, `x & 0xFF`)
     - Byte 3..4: `stagesY[s]` (`(y >> 8) & 0xFF`, `y & 0xFF`)
     - Byte 5..6: `0x00, 0x00`
3. **实时 DPI 读取 (`0x04 0x85`) 与板载表读取 (`0x04 0x86`)**:
   - `0x04 0x85`: Offset + 9..10 为 `DpiX`，Offset + 11..12 为 `DpiY`。
   - `0x04 0x86`: 档位内 Offset + 1..2 为 `DpiX`，Offset + 3..4 为 `DpiY`。

### 2.2 瓦尔基里 (Valkyrie) 协议 (VID 0x373B / 0x30FA / 0x093A 等)
1. **Option1 板载多档 DPI 读写 (`FEA_CMD_MOUSE_SET_OPTIONPARAM1 = 0x54`, `FEA_CMD_MOUSE_GET_OPTIONPARAM1 = 0xD4`)**:
   - Byte 1: `0x54` / `0xD4`
   - Byte 2: `0x00` (Profile 0)
   - Byte 3: `curIdx` (0 索引当前档位)
   - Byte 4: `count` (档位数 1..8)
   - 字节偏移 `8 + i * 2` (低字节) 与 `9 + i * 2` (高字节): 第 `i` 档 **X 轴 DPI**。
   - 字节偏移 `24 + i * 2` (低字节) 与 `25 + i * 2` (高字节): 第 `i` 档 **Y 轴 DPI**。
2. **读写实现**:
   - `FastQueryDpi`: 同时解析 X 轴与 Y 轴，若 $Y \ne X$，设置 `IsXyIndependent = true`。
   - `SetValkyrieDpi(int dpiX, int dpiY)`: 写入当前激活档的 X/Y 值。
   - `UpdateValkyrieHardwareStages(int activeStage, int[] stagesX, int[] stagesY)`: 完整写入 1~5 档的 X/Y 硬件配置。

---

## 3. 数据契约与模型层 (Models)

### 3.1 `MouseBatteryInfo.cs`
- 新增属性:
  - `public int DpiY { get; set; }`
  - `public int[] DpiStagesY { get; set; }`
  - `public bool IsXyIndependent { get; set; }`
  - `public double XyRatio { get; set; }`

### 3.2 `RazerDeviceHardwareState.cs`
- 新增字段:
  - `public int DpiY;`
  - `public int[] DpiStagesY;`
  - `public bool IsXyIndependent;`
  - `public double XyRatio;`

---

## 4. 界面交互与控制层 (UI)

### 4.1 单档行控件 (`DpiStageRowControl.cs`)
- 属性增加:
  - `public int DpiValueY { get; }`
  - `public bool IsXyIndependent { get; }`
- 布局自适应:
  - 等比模式 (1:1): 显示单个 DPI 输入框 `[ 1600 ] DPI`。
  - 独立模式 (XY): 紧凑优雅排布 `X [ 1600 ]  Y [ 1200 ] DPI`。

### 4.2 DPI 管理对话框 (`DpiManagementDialog.cs`)
- 顶部增加 **XY 轴灵敏度独立调节卡片**:
  - 切换开关: `[ 🔗 锁定 X/Y 轴等比 (1:1) ]` / `[ 🔓 开启 X/Y 轴独立调节 ]`。
  - 快捷比例预设药丸按钮:
    - `1:1 (标准)`
    - `4:3 拉伸补偿 (Y = 75% X)`
    - `16:10 补偿 (Y = 90% X)`
    - `逆向拉伸 (Y = 133% X)`
  - 点击预设时，自动按照比例同步更新当前所有档位的 Y 轴输入值。
- 支持雷蛇与瓦尔基里全系鼠标调用。

### 4.3 主界面与 OSD (`MainForm.UI.cs`, `MainForm.Hardware.cs`, `DpiOsdForm.cs`)
- `MainForm.UI.cs`: 放开 `btnDpiManage`，允许瓦尔基里鼠标与雷蛇鼠标均能唤出 `DpiManagementDialog`。
- `MainForm.Hardware.cs`: 在保存时根据品牌调用对应的 `UpdateRazerHardwareStages` 或 `UpdateValkyrieHardwareStages`。
- `DpiOsdForm.cs`: 当 $DpiY \ne DpiX$ 时，动态展示 `1600 × 1200 DPI`，保证游戏内 HUD 一目了然。

---

## 5. 测试与验证策略

1. 编写专用自动化集成测试脚本 `tests/test_xy_dpi_integration.ps1`:
   - 校验 Models 属性与反射。
   - 校验雷蛇 HID 0x04 0x05 / 0x06 报文生成的 X/Y 字节偏移与大小端。
   - 校验瓦尔基里 HID 0x54 / 0xD4 报文生成的 X/Y 字节偏移与大小端。
   - 校验 DpiStageRowControl 与 DpiManagementDialog 的 XY 独立模式初始化与数据绑定。
2. 运行 `tools/run_all_tests.ps1`，确保全工程 29+ 项测试套件 100% 绿灯。
3. 校验 UTF-8 with BOM 与 C# 5.0 语法铁律。
