# FerrisPulse · 灵脉 系统架构与 AI 维护全域指南 (ARCHITECTURE.md)

本文档为 **FerrisPulse · 灵脉 (多品牌外设感知与电量管理系统)** 的全景架构地图与开发规范。
所有参与维护、功能扩展或 Bug 修复的工程师与 AI Agent，**必须严格遵循本文档定义的目录拓扑与行为准则**。

---

## 1. 系统目录拓扑与功能认知地图 (Cognitive Map)

```
D:\FerrisPulse · 灵脉\
├── FerrisPulse.exe             [单一原生二进制可执行文件]
├── app.ico                     [程序主图标]
├── build.bat                   [单命令模块化极速构建脚本]
├── AGENT_RULES.md              [AI 行为准则与高分屏规范]
└── src\
    ├── Properties\             [程序集版本信息与 PE 元数据]
    │   └── AssemblyInfo.cs     [Windows PE 元数据、版本号 4.0.0.0、版权与产品信息]
    │
    ├── Core\                   [系统核心中枢]
    │   ├── Program.cs          [应用入口、全局 Mutex 防多开、目录重置、异常兜底]
    │   ├── StartupManager.cs   [高鲁棒性自启动引擎：Run/StartupApproved 双键协同与快捷方式]
    │   ├── ConfigManager.cs    [统一注册表配置持久化管理]
    │   └── NativeMethods.cs    [Win32 P/Invoke API、COM 接口与 TaskbarCreated 消息定义]
    │
    ├── Models\                 [数据模型层（无副作用纯净模型）]
    │   ├── DeviceCategory.cs   [外设品类枚举：鼠标、键盘、耳机、手柄、接收器等]
    │   ├── MouseBatteryInfo.cs [全系统通用归一化外设状态实体]
    │   ├── CustomDeviceConfig.cs [用户手动添加与命名配置]
    │   ├── PriorityItem.cs     [优先级排序数据模型]
    │   ├── RunningProcessItem.cs [运行进程信息模型]
    │   └── RazerDeviceHardwareState.cs [雷蛇硬件内部状态缓存]
    │
    ├── Hardware\               [硬件协议驱动层（各品牌隔离）]
    │   ├── DeviceManager.cs    [外设扫描调度中心、优先级仲裁与多设备状态聚合]
    │   ├── RazerDeviceHelper.cs [雷蛇私有 HID 协议、90 字节报文、DPI 档位与 8K 轮询率]
    │   ├── RapooDeviceHelper.cs [雷柏无线鼠标 HID 通信协议]
    │   ├── VgnDeviceHelper.cs   [VGN / 蜻蜓 / ATK 协议解析]
    │   ├── LogitechDeviceHelper.cs [罗技 HID++ 协议解析]
    │   ├── NuphyDeviceHelper.cs [NuPhy 键盘电量协议与侧灯控制]
    │   ├── ValkyrieDeviceHelper.cs [瓦尔基里全系键盘/鼠标私有 HID 协议、2.4G 接收器多模解析、8K 回报率与 DPI 调节]
    │   ├── BluetoothDeviceHelper.cs [Windows 原生 BLE 电池服务]
    │   ├── GenericHidHelper.cs  [通用 HID 电池报告解析]
    │   ├── UniversalWinLockHelper.cs [Windows 键硬件/驱动级防误触锁定]
    │   ├── AudioDeviceHelper.cs [系统音频与耳机设备状态]
    │   └── XInputHelper.cs      [游戏手柄电量与状态检测]
    │
    ├── Services\               [后台长效服务层]
    │   ├── GameDetector.cs     [独占全屏与焦点游戏检测服务，带系统白名单与防抖]
    │   └── ChimeSynthesizer.cs [44.1kHz 16-bit PCM 柔和纯音双音合成器]
    │
    └── UI\                     [界面渲染与用户交互层]
        ├── Theme\              [设计系统与矢量渲染]
        │   ├── ModernThemeHelper.cs      [抗黑角矢量圆角算法、纯矢量闪电/电池绘制、色彩常量]
        │   └── ModernDarkMenuRenderer.cs [Fluent 暗黑上下文菜单渲染]
        │
        ├── Controls\           [22 个独立自绘制 GDI+ 原子控件]
        │   ├── RoundedCard.cs            [抗锯齿圆角卡片底板]
        │   ├── ModernButton.cs           [带悬浮微光质感现代按钮]
        │   ├── ModernBadge.cs            [状态徽章]
        │   ├── ModernCheckBox.cs         [矢量扁平勾选框]
        │   ├── ModernSegmentButton.cs    [分段选择按钮]
        │   ├── SmoothSegmentedTabBar.cs  [平滑滑动切换标签栏]
        │   ├── SubtleDivider.cs          [细腻冷灰分割线]
        │   ├── ModernProgressBar.cs      [抗锯齿进度条]
        │   ├── StatusPill.cs             [胶囊状态标签]
        │   ├── TactileSmoothSliderControl.cs [平滑阻尼滑块]
        │   ├── RapooStatusTile.cs        [雷柏状态小瓦片]
        │   ├── AnimatedContextMenuStrip.cs
        │   ├── ModernDarkDropdown.cs
        │   ├── ModernInputBox.cs
        │   ├── WizardRadarBadge.cs
        │   ├── ThreeLocksHudControl.cs
        │   ├── ScanningBeamControl.cs
        │   ├── HeadsetStatusTile.cs
        │   ├── FocusDeviceSelector.cs
        │   ├── PriorityReorderControl.cs
        │   ├── DpiStageRowControl.cs
        │   └── DpiActionButton.cs
        │
        └── Forms\              [独立弹窗与主窗体分部切片]
            ├── MainForm.cs               [主窗体生命周期中枢、DPI 初始化]
            ├── MainForm.UI.cs            [界面布局排布、60FPS 风琴折叠动画]
            ├── MainForm.Tray.cs          [托盘 NotifyIcon 与动态多设备右键菜单]
            ├── MainForm.Hardware.cs      [硬件轮询定时器与 UI 状态刷新]
            ├── MainForm.Events.cs        [WndProc 消息泵与 TaskbarCreated 重建机制]
            ├── ModernDeviceSwitcherFlyout.cs [悬浮式现代外设快速切换器]
            ├── DpiOsdForm.cs             [零焦点置顶无感 DPI 悬浮提示窗]
            ├── KeyRolloverSpeedForm.cs   [全键无冲与触发测速仪]
            ├── MouseRateTestDialog.cs    [回报率平滑度离线测试工具]
            ├── GameProcessManagerForm.cs [电竞游戏守护进程管理弹窗]
            ├── DevicePriorityForm.cs     [外设优先级排序调整窗口]
            ├── AddDeviceWizardForm.cs    [手动添加新设备向导]
            ├── PromptDialog.cs           [通用输入提示对话框]
            └── DpiManagementDialog.cs    [DPI 档位高级配置管理窗口]
```

---

## 2. 功能路由直达表 (Feature Routing Table)

当 Agent 或工程师需要修改具体功能时，请直接查阅下表直达对应文件：

| 目标修改功能 | 直达文件路径 | 说明 |
| :--- | :--- | :--- |
| **开机自启动与注册表 Run 键** | `src/Core/StartupManager.cs` | 处理自启动注册、StartupApproved 协同 |
| **托盘图标丢失 / Explorer 重启自愈** | `src/UI/Forms/MainForm.Events.cs` | 监听 `WM_TASKBARCREATED` 并自动恢复 |
| **雷蛇 DPI 档位 / 8K 回报率 / XY 轴独立灵敏度** | `src/Hardware/RazerDeviceHelper.cs` | 雷蛇私有 HID 报文、独立 X/Y 轴与板载 Flash 写入 |
| **瓦尔基里全系键盘/鼠标支持** | `src/Hardware/ValkyrieDeviceHelper.cs` | 瓦尔基里全系 2.4G 接收器/HID 协议解析、双模电量、8K 回报率与独立 X/Y 轴 DPI 调节 |
| **DPI 档位管理与 XY 轴比例调节** | `src/UI/Forms/DpiManagementDialog.cs` / `src/UI/Controls/DpiStageRowControl.cs` | DPI 档位增删、XY 轴独立调节 (4:3/16:10 比例联动) 与板载固化 |
| **屏幕悬浮 OSD 指示器** | `src/UI/Forms/DpiOsdForm.cs` | 游戏前台免切焦点悬浮 HUD、DPI/XY独立双轴显示与回报率变动提示 |
| **多设备优先级拖拽与首选设备** | `src/Hardware/DeviceManager.cs` / `DevicePriorityForm.cs` | 外设优先级权重与展示顺序 |
| **按钮、滑块、分段选项卡外观** | `src/UI/Controls/*.cs` | 独立的原子自定义双缓冲 GDI+ 控件 |

---

## 3. 全局 AI 维护准则 (AI Maintenance Directives)

任何 AI Agent 在本代码库中工作时，必须严格遵守以下法则：

1. **内嵌契约同步更新 (Contract Priming)**：
   每个 `.cs` 文件顶部均包含 `[AI MAINTENANCE DIRECTIVE]`。修改任何文件时，**必须同步维护该文件头部的核心职责与依赖说明**，严禁让文档与实现脱节。
2. **C# 5 / .NET 4.0 语法铁律**：
   本项目使用 Windows 原生 `csc.exe` 4.8 编译，**严禁使用任何 C# 6+ 语法**：
   - ❌ 禁用 `$"..."` 字符串插值（必须使用 `string.Format("{0}", ...)` 或 `+` 拼接）；
   - ❌ 禁用 `?.` 空条件操作符（必须显式 `if (obj != null)`）；
   - ❌ 禁用 `nameof()` 操作符；
   - ❌ 禁用 `out var` 内联变量声明；
   - ❌ 禁用表达式主体定义 `=>`（必须使用传统函数体 `{ return ...; }`）。
3. **编码格式锁定 (UTF-8 with BOM)**：
   所有新建与修改的源文件必须保存为带有 BOM 的 UTF-8 编码，彻底消除因编码丢失导致的 `?` 乱码与字符截断。
4. **高分屏绝对自适应 (High-DPI Adaptive)**：
   严禁在控件、窗口、边距、圆角处使用硬编码像素数值。必须统一通过 `S(...)` 或 `DpiScale` 计算缩放后的物理尺寸。
5. **单实例安全互斥**：
   严禁通过遍历进程名暴力调用 `Process.Kill()`。必须使用系统命名全局互斥体 `Mutex("Global\\FerrisPulse_SingleInstance_Mutex")` 进行优雅管理。
