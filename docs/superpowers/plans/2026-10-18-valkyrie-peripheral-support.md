# 瓦尔基里（Valkyrie）全系外设接入与电量/状态感知实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 FerrisPulse 引入完整的瓦尔基里（Valkyrie）品牌外设驱动子系统，全面支持其官方驱动库中的所有机械键盘、电竞鼠标及 2.4G 无线接收器，实现实时电量读取、在线/休眠感知、型号精确识别与注册表持久化。

**Architecture:** 
1. 在 `src/Hardware/ValkyrieDeviceHelper.cs` 中实现独立的硬件驱动单例，使用 Win32 HID P/Invoke 接口进行 65 字节 Feature Report (`0xF7`) 握手。
2. 在 `src/Hardware/DeviceManager.cs` 中挂载轮询分发链，将识别到的外设归一化为 `MouseBatteryInfo`，接入托盘电量渲染与多外设优先级管理。
3. 在 `src/Hardware/GenericHidHelper.cs` 中过滤瓦尔基里专有 VID (`0x374A`)，防止通用 HID 接口产生设备抢占。

**Tech Stack:** C# 5.0, .NET Framework 4.0, Win32 HID P/Invoke (`SetupAPI` / `hid.dll`), Windows 注册表配置。

---

## 全局铁律约束 (Global Constraints)
1. **语言标准**: 严格限制为 C# 5.0，绝对禁用 C# 6+ 语法（如 `$"{...}"`, `?.`, `nameof()`, `=>` 等）。
2. **零第三方依赖**: 仅允许引用 .NET 4.0 核心程序集（`mscorlib.dll`, `System.dll`, `System.Drawing.dll`, `System.Windows.Forms.dll`）。
3. **文件编码**: 源码文件必须以 UTF-8 with BOM 格式保存。
4. **代码头准则**: 保留 `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️` 头部注释。

---

## 任务拆解与执行步骤 (Task Breakdown)

### Task 1: 建立瓦尔基里自动化测试套件 (Red 阶段)
- [ ] 创建自动化测试脚本 `tests/test_valkyrie_integration.ps1`。
- [ ] 编写测试断言：
  1. 源代码语法合规检查（检查 `src/Hardware/ValkyrieDeviceHelper.cs` 是否存在 C# 6 禁用语法）。
  2. 编码格式检查（检查是否为 UTF-8 with BOM）。
  3. 报文与算法模拟测试（模拟 65 字节 0xF7 返回报文解析，校验键盘/鼠标电量及休眠状态提取）。
  4. 官方型号匹配与品类映射测试（校验 VK 99 Max V2, VK 87 Max V2, VK M5 Pro 等 PID 映射）。
- [ ] 运行测试确认初始失败（Red）。

### Task 2: 实现瓦尔基里专属硬件驱动层 `ValkyrieDeviceHelper.cs`
- [ ] 在 `src/Hardware/ValkyrieDeviceHelper.cs` 中实现驱动核心：
  - 静态字段与注册表缓存读写（`Software\RazerBatteryTray\ValkyrieCache`）。
  - 瓦尔基里 VID 过滤逻辑（`VID_374A`，及代工 Dongle PID 识别）。
  - 完整的全系型号字典（VK 99/87/75/68 键盘系列与 VK M5/M7/M4 鼠标系列）。
  - HID 设备句柄枚举与 `SendValkyrieCommand`（Feature Report 发送 `0xF7`，10ms 延时，读取 65 字节返回）。
  - 电量、在线/休眠状态与充电状态解析。
  - 公共查询入口 `QueryAllValkyrieDevices()`。

### Task 3: 改造 `GenericHidHelper.cs` 杜绝设备抢占冲突
- [ ] 在 `src/Hardware/GenericHidHelper.cs` 的黑名单过滤列表中，加入 `vid_374a`。
- [ ] 确保通用 HID 扫描器跳过瓦尔基里外设，避免重复加载或句柄独占错误。

### Task 4: 接入外设调度中枢 `DeviceManager.cs`
- [ ] 在 `src/Hardware/DeviceManager.cs` 的 `QueryAllDevices()` 中添加 `ValkyrieDeviceHelper.QueryAllValkyrieDevices()` 轮询调度。
- [ ] 自动同步电量至自定义设备配置缓存 `UpdateCustomDeviceBattery`。
- [ ] 支持用户在 `DevicePriorityForm` 中对瓦尔基里设备进行优先级排序、隐藏与自定义命名。

### Task 5: 运行全量测试与回归验证 (Green 阶段)
- [ ] 运行 `tests/test_valkyrie_integration.ps1` 确保全部用例通过。
- [ ] 执行 `build.bat` 验证原生 C# 5.0 编译器零错误零警告生成 `FerrisPulse.exe`。
- [ ] 运行系统既有测试 `tests/test_pure_numeric_and_disconnected_tray.ps1` 等确保无任何已有功能回归。

### Task 6: 文档与架构索引同步
- [ ] 在 `ARCHITECTURE.md` 的功能路由直达表和目录拓扑中，正式登记 `ValkyrieDeviceHelper.cs`。
- [ ] 审查 Git 工作区，清理临时文件，确保工作区纯净。
