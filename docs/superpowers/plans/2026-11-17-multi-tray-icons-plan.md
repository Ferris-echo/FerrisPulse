# Multi-Tray Independent Icons & Dual-Color Personalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add toggleable multi-device independent taskbar tray icons (default single-icon preserved) with Win32 `NIF_GUID` slot persistence, adaptive count selector (`<=5` presets only / `>5` manual input), per-device dual-color customization (UI theme + tray glyph, linked by default), silent main-UI name click personalization, and slot-scoped left/right tray interactions.

**Architecture:** Extract pure slot arbitration into `TraySlotPlanner` (`src/UI/Tray/TraySlotPlanner.cs`), implement native Win32 multi-icon lifecycle via `Shell_NotifyIconW` + deterministic `NIF_GUID` in `MultiTrayIconHost` (`src/UI/Tray/MultiTrayIconHost.cs`), persist per-device appearance (`CustomName`, `ThemeColorHex`, `TrayColorHex`, `LinkTrayColorToTheme`) cleanly via `DeviceManager` + `ConfigManager`, and expose UI controls in `DevicePersonalizeDialog`, `DevicePriorityForm`/`PriorityReorderControl`, `MainForm.UI.cs`, and `MainForm.Tray.cs`.

**Tech Stack:** C# 5.0 / .NET Framework 4.0 (`csc.exe`), WinForms + Win32 P/Invoke (`user32.dll`, `shell32.dll`), PowerShell Pester-style test harness (`tools/TestHarness.ps1`).

**Spec:** [docs/superpowers/specs/2026-11-17-multi-tray-icons-design.md](../specs/2026-11-17-multi-tray-icons-design.md)

## Global Constraints
- Strictly C# 5.0 / .NET Framework 4.0 (`csc.exe` `/codepage:65001`) — NO `$""`, NO `?.`, NO `nameof`, NO `out var`, NO pattern matching.
- Zero external libraries / NuGet packages — Win32 P/Invoke only.
- Save all `.cs`, `.md`, `.ps1` files as UTF-8 with BOM (run `python tools/ensure_boms_safe.py` after edits).
- Preserve `// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️` header in every code file.
- Tests must run inside `FERRISPULSE_TEST_MODE=1` sandbox (`Load-FerrisPulseAssembly` / `New-SandboxMainForm`), zero hardware writes, zero production registry leakage.

## Review Focus
- Existing single-icon users upgrade with `MultiTrayEnabled == false` untouched, zero `Shell_NotifyIconW` slots active.
- `>5` devices reveals custom numeric input cleanly clamped to `2..16`; `<=5` devices hides custom input completely.
- Sleeping/disconnected-with-dongle devices retain their slot showing `--` in tray without jumping positions; `0` eligible devices yields 1 fallback disconnected icon at Slot 0.
- Decoupling `LinkTrayColorToTheme` lets UI accent (`BrandColor`) and tray glyph accent (`TrayAccentColor`) evolve independently without overwriting each other.
- Right-clicking slot `i` scopes tray menu controls (`DPI`/`Rate`/`Lighting`/`NuPhy`) to slot `i`'s device, while left-clicking focuses slot `i` in `MainForm`.

---

### Task 1: Dual-Color Appearance & Global Multi-Tray Configuration

**Files:**
- Modify: `src/Models/MouseBatteryInfo.cs`
- Modify: `src/Models/CustomDeviceConfig.cs`
- Modify: `src/Models/PriorityItem.cs`
- Modify: `src/Core/ConfigManager.cs`
- Modify: `src/Hardware/DeviceManager.cs`
- Test: `tests/test_multi_tray_and_colors.ps1` (create with TDD Case 1)

- [x] **Step 1**: Write failing test case in `tests/test_multi_tray_and_colors.ps1` asserting `ConfigManager.GetMultiTrayEnabled()` defaults `false`, `GetMultiTrayMaxCount()` defaults `3` (clamped `2..16`), `MouseBatteryInfo.TrayAccentColor` independence/linkage, and `DeviceManager.SaveDeviceAppearance` round-trip persistence under sandbox.
- [x] **Step 2**: Run `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1 -Filter "test_multi_tray_and_colors.ps1"` -> confirm expected FAIL.
- [x] **Step 3**: Add `MultiTrayEnabled` / `MultiTrayMaxCount` getters/setters to `ConfigManager`; add `CustomThemeColorHex`, `CustomTrayColorHex`, `LinkTrayColorToTheme`, `TryParseHexColor`, and `TrayAccentColor` to `MouseBatteryInfo`; add appearance fields to `CustomDeviceConfig` + `PriorityItem`; add `DeviceAppearance` registry persistence (`SaveDeviceAppearance`, `GetDeviceAppearance`, `ApplyDeviceAppearances`) in `DeviceManager` (called inside `QueryAllDevices` & `GetAllKnownDevicesForPriority`).
- [x] **Step 4**: Run `python tools/ensure_boms_safe.py`, compile `FerrisPulse_verify.exe`, run `test_multi_tray_and_colors.ps1` -> confirm PASS.

---

### Task 2: Pure Slot Arbitration Engine (`TraySlotPlanner`)

**Files:**
- Create: `src/UI/Tray/TraySlotPlanner.cs`
- Test: `tests/test_multi_tray_and_colors.ps1` (TDD Case 2)

- [x] **Step 1**: Add failing test cases in `tests/test_multi_tray_and_colors.ps1` covering: hidden device exclusion, sleep slot preservation (`IsConnected = false` visual for `IsSleeping == true`), low-battery `<=30%` priority arbitration (charging/sleeping excluded from low-battery jump), `maxSlots` cap, and `0` eligible devices returning Slot 0 fallback.
- [x] **Step 2**: Run `test_multi_tray_and_colors.ps1` -> confirm FAIL (`TraySlotPlanner` missing).
- [x] **Step 3**: Implement `TraySlotPlanItem` & `TraySlotPlanner.PlanSlots(...)` in `src/UI/Tray/TraySlotPlanner.cs`.
- [x] **Step 4**: Recompile & run `test_multi_tray_and_colors.ps1` -> confirm PASS.

---

### Task 3: Win32 Native Multi-Icon Host (`MultiTrayIconHost`)

**Files:**
- Create: `src/UI/Tray/MultiTrayIconHost.cs`
- Test: `tests/test_multi_tray_and_colors.ps1` (TDD Case 3)

- [x] **Step 1**: Add failing test assertions in `tests/test_multi_tray_and_colors.ps1` checking deterministic slot GUID uniqueness (`GetSlotGuid(0..15)`), `SyncSlots(...)` updating tracked slot states & trimming excess slots cleanly in sandbox mode, and `ProcessCallbackMessage` / click routing events (`SlotClicked`, `SlotDoubleClicked`, `SlotContextMenuRequested`).
- [x] **Step 2**: Run test -> confirm FAIL.
- [x] **Step 3**: Implement `MultiTrayIconHost` using `Shell_NotifyIconW` (`NOTIFYICONDATAW` with `NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID`, `NIF_SHOWTIP`, `NOTIFYICON_VERSION_4` / classic `WM_LBUTTONUP` + `WM_RBUTTONUP`), deterministic per-slot `GUID`, diff-only icon HICON management (`DestroyIcon` safe cleanup), `WM_TASKBARCREATED` rebuild, and sandbox guard.
- [x] **Step 4**: Recompile & run `test_multi_tray_and_colors.ps1` -> confirm PASS.

---

### Task 4: `DevicePersonalizeDialog`, Card Color Swatch & Adaptive `DevicePriorityForm`

**Files:**
- Create: `src/UI/Forms/DevicePersonalizeDialog.cs`
- Modify: `src/UI/Controls/PriorityReorderControl.cs`
- Modify: `src/UI/Forms/DevicePriorityForm.cs`
- Test: `tests/test_multi_tray_and_colors.ps1` (TDD Case 4)

- [x] **Step 1**: Add failing UI tests in `tests/test_multi_tray_and_colors.ps1`:
  - `DevicePersonalizeDialog` linkage check (`LinkTrayColorToTheme` default `true`, modifying tray color decouples link, reset restores default).
  - `DevicePriorityForm` adaptive count selector (`<=5` items shows preset pills `<=5` and hides custom numeric input; `>5` items exposes custom numeric input `2..16`).
  - `PriorityReorderControl` renders card color swatch & fires `SwatchClicked`.
- [x] **Step 2**: Run test -> confirm FAIL.
- [x] **Step 3**: Implement `DevicePersonalizeDialog`, update `PriorityReorderControl` card swatch drawing + hit-testing + `SwatchClicked`, and upgrade `DevicePriorityForm` with `chkMultiTray`, adaptive count presets / manual numeric input, and card swatch click -> `DevicePersonalizeDialog` -> persist on Save.
- [x] **Step 4**: Recompile & run `test_multi_tray_and_colors.ps1` -> confirm PASS.

---

### Task 5: `MainForm` Integration, Silent Name Entry & Slot L/R Menu Scoping

**Files:**
- Modify: `src/UI/Forms/MainForm.cs`
- Modify: `src/UI/Forms/MainForm.UI.cs`
- Modify: `src/UI/Forms/MainForm.Tray.cs`
- Modify: `src/UI/Forms/MainForm.Hardware.cs`
- Modify: `src/UI/Forms/MainForm.Events.cs`
- Test: `tests/test_multi_tray_and_colors.ps1` + full suite `tools/run_all_tests.ps1`

- [x] **Step 1**: Add failing integration tests in `tests/test_multi_tray_and_colors.ps1` checking:
  - Toggle `SetMultiTrayEnabled(true)` hides single `trayIcon` and populates `MultiTrayIconHost` slots with `TrayColorHex`; `SetMultiTrayEnabled(false)` cleans host slots and restores single `trayIcon`.
  - Right-clicking slot `i` (`PrepareContextMenuForDeviceId`) scopes `UpdateTrayDeviceMenuItems()` DPI/Rate/NuPhy menu items to slot `i`'s device rather than `PrimaryDevice`.
  - Clicking `lblDeviceName` invokes `OpenPersonalizeDialogForCurrentDevice` (replaces plain rename dialog without adding any extra button to `MainForm`).
- [x] **Step 2**: Run test -> confirm FAIL.
- [x] **Step 3**: Wire `MainForm` config load/save, `OpenPersonalizeDialogForCurrentDevice`, `multiTrayMenuItem` in tray context menu, `trayContextDeviceId` scoping in `UpdateTrayDeviceMenuItems`, `MultiTrayIconHost` lifecycle (`ReinitializeTrayIcon`, `ApplyDeviceStatus`, `TryProcessMouseWakePath`, `Dispose`), and single <-> multi mode toggle.
- [x] **Step 4**: Run `python tools/ensure_boms_safe.py`, recompile, and execute the COMPLETE 15-file regression test suite (`powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1`) -> confirm 100% PASS!
