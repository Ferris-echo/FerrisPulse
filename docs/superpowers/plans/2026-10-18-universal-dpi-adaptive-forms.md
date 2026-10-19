# 全软件高 DPI 自适应架构实施计划 (Universal High-DPI Adaptive Form System)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立全软件通用的 High-DPI 自动缩放体系（基于 `DpiScaleHelper` 与 `DpiAwareForm` 窗体基类），使当前全部 13 个窗口及未来新增的任何新页面，在 100%~300% 缩放（如 4K 200%）及多屏跨 DPI 拖拽场景下自动等比适配，彻底根除文字覆盖、边框裁切与布局错位。

**Architecture:** 
1. 抽象基础核心层 `DpiScaleHelper`（原生 Win32 DPI 感知与等比几何运算工具）；
2. 构造通用窗口基类 `DpiAwareForm : Form`，统一接管 `AutoScaleMode.None`、物理屏幕 DPI 自探测、`WM_DPICHANGED` 跨屏拖拽事件流，并提供 `OnApplyDpiScale` 布局生命周期钩子；
3. 将包含 `AboutForm` 在内的全部现有二级对话框与主窗体接入基类体系，重构其绝对坐标排版为流式/等比相对排版。

**Tech Stack:** C# 5.0 (.NET Framework 4.0), WinForms, Win32 P/Invoke (`GetDpiForWindow`, `WM_DPICHANGED`), GDI+

**Spec:** 全局界面自适应规范：窗体尺寸、内间距、卡片边框、按钮宽高、自定义绘图按 `dpiScale` 等比缩放；多行文本禁止硬编码重叠 Y 坐标，强制采用 `Top = prevControl.Bottom + gap` 相对排列。

## Global Constraints
- C# 5.0 (.NET Framework 4.0) 严格语法兼容，严禁使用 C# 6+ 插值字符串 `$"..."`。
- 零外部 NuGet 依赖，仅使用 .NET 4.0 GAC 核心程序集。
- 所有修改及新增源文件必须带有 UTF-8 with BOM 签名。
- 全部 27+ 项现有自动化回归测试必须 100% 保持 PASS。

## Review Focus
1. `AboutForm` 在 200% DPI 缩放下大标题（`lblLogo`）与作者署名行（`lblAuthorPrefix`）不得发生任何纵向重叠（`Top >= lblLogo.Bottom`）。
2. `AboutForm` 赞赏描述长文本（`lblDonateDesc`）在 200% 缩放下不得被卡片右边界截断。
3. `DpiAwareForm` 在未显式传参时，直接通过窗体句柄探测真实屏幕 DPI 并正确初始化 `DpiScale`。
4. 窗体在跨不同 DPI 屏幕拖拽时（触发 `WM_DPICHANGED`），能够自动重算 `DpiScale` 并刷新布局，不抛出异常。
5. 所有派生窗体的资源释放与 `Dispose` 保持原样无内存泄露。

---

### Task 1: 建立核心高 DPI 辅助引擎 `DpiScaleHelper`

**Files:**
- Create: `src/UI/Helpers/DpiScaleHelper.cs`
- Modify: `src/Core/NativeMethods.cs`
- Test: `tests/test_universal_dpi_adaptation.ps1`

**Interfaces:**
- Produces:
  ```csharp
  public static class DpiScaleHelper {
      public static float GetDpiScaleForWindow(IntPtr hWnd);
      public static int Scale(int value, float scale);
      public static Size Scale(Size size, float scale);
      public static Point Scale(Point pt, float scale);
      public static Rectangle Scale(Rectangle rect, float scale);
  }
  ```

- [ ] **Step 1: 编写测试用例 `tests/test_universal_dpi_adaptation.ps1`，验证 `DpiScaleHelper` 的纯数学缩放与 API 声明**
- [ ] **Step 2: 运行测试验证失败（尚未创建 `DpiScaleHelper.cs`）**
- [ ] **Step 3: 在 `src/Core/NativeMethods.cs` 中补齐 `GetDpiForWindow` 与 `GetModuleHandle/GetProcAddress` 兼容声明**
- [ ] **Step 4: 实现 `src/UI/Helpers/DpiScaleHelper.cs`，实现动态探测与等比几何运算**
- [ ] **Step 5: 验证 Step 1 测试通过**

---

### Task 2: 构造统一高 DPI 窗体基类 `DpiAwareForm`

**Files:**
- Create: `src/UI/Forms/DpiAwareForm.cs`
- Test: `tests/test_universal_dpi_adaptation.ps1`

**Interfaces:**
- Produces:
  ```csharp
  public class DpiAwareForm : Form {
      public float DpiScale { get; protected set; }
      protected virtual void OnApplyDpiScale(float scale);
      protected virtual void OnDpiScaleChanged(float oldScale, float newScale);
  }
  ```

- [ ] **Step 1: 在测试中增加 `DpiAwareForm` 的生命周期与 `WM_DPICHANGED` 处理逻辑断言**
- [ ] **Step 2: 运行测试验证失败**
- [ ] **Step 3: 编写 `src/UI/Forms/DpiAwareForm.cs`**
  - 在构造函数锁定 `this.AutoScaleMode = AutoScaleMode.None;`
  - 在 `OnHandleCreated` 自动读取 `DpiScaleHelper.GetDpiScaleForWindow(this.Handle)`
  - 在 `WndProc` 中捕获 `NativeMethods.WM_DPICHANGED`，重设边界并触发 `OnDpiScaleChanged`
  - 提供 `protected virtual void OnApplyDpiScale(float scale)`
- [ ] **Step 4: 运行测试验证通过**

---

### Task 3: 重构 `AboutForm.cs`，实现完美 200% 4K 适配与零重叠

**Files:**
- Modify: `src/UI/Forms/AboutForm.cs`
- Test: `tests/test_universal_dpi_adaptation.ps1`

**Interfaces:**
- Consumes: `DpiAwareForm`, `DpiScaleHelper`

- [ ] **Step 1: 在测试脚本中增加 200% 缩放下 `AboutForm` 文本不重叠、文字未截断断言**
- [ ] **Step 2: 运行测试验证当前未适配的 `AboutForm` 报错失败**
- [ ] **Step 3: 重构 `AboutForm.cs` 继承自 `DpiAwareForm`**
  - 窗体尺寸改为 `Scale(600, DpiScale)`, `Scale(710, DpiScale)`
  - 作者署名行改为基于 `lblLogo.Bottom + gap` 相对排版
  - 更新卡片与赞赏卡片尺寸、二维码画板 `176x216`、按钮、底部链接全链路接入 `DpiScale`
  - 实现 `OnApplyDpiScale`，支持动态 DPI 重新缩放
- [ ] **Step 4: 运行测试验证通过**

---

### Task 4: 现有全部二级对话框统一接入 `DpiAwareForm`

**Files:**
- Modify: `src/UI/Forms/DevicePriorityForm.cs`
- Modify: `src/UI/Forms/AddDeviceWizardForm.cs`
- Modify: `src/UI/Forms/DpiManagementDialog.cs`
- Modify: `src/UI/Forms/UpdateNoticeForm.cs`
- Modify: `src/UI/Forms/PromptDialog.cs`
- Modify: `src/UI/Forms/EasterEggQuoteForm.cs`
- Modify: `src/UI/Forms/GameProcessManagerForm.cs`
- Modify: `src/UI/Forms/KeyRolloverSpeedForm.cs`
- Modify: `src/UI/Forms/MouseRateTestDialog.cs`
- Modify: `src/UI/Forms/ModernDeviceSwitcherFlyout.cs`
- Test: `tests/test_universal_dpi_adaptation.ps1`

- [ ] **Step 1: 在测试脚本中对上述 10 个窗体断言全部继承自 `DpiAwareForm`**
- [ ] **Step 2: 将这 10 个窗体的基类统一从 `Form` 切换为 `DpiAwareForm`**
- [ ] **Step 3: 在每个窗体中对接 `DpiScale`，消除固定写死像素尺寸，规范控件排列**
- [ ] **Step 4: 运行测试验证全部继承与基本布局通过**

---

### Task 5: 编码检查、全编译与全量 28 项自动化回归验证

**Files:**
- Modify: `tools/run_all_tests.ps1`
- Test: 全量 28 项自动化回归测试

- [ ] **Step 1: 扫描并确保所有新建与修改的 `.cs` 文件均带有 UTF-8 with BOM**
- [ ] **Step 2: 执行 `cmd /c build.bat`，确保 0 错误 0 警告编译生成 `FerrisPulse.exe`**
- [ ] **Step 3: 将 `test_universal_dpi_adaptation.ps1` 纳入全量测试**
- [ ] **Step 4: 运行 `powershell -ExecutionPolicy Bypass -File tools\run_all_tests.ps1`，确保全部测试 100% PASS**
