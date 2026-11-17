# ASUS ROG Integration & Three-Channel Arbitration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate community contributor "工程机风格"'s ASUS ROG AZOTH 96 HE Wireless (`0B05:1AD0`), ROG DELTA II (`0B05:1AFA`), and ROG AZOTH 96 HE Wired (`0B05:1C10`) adapters, three-channel keyboard arbitration, and 3-segment tray battery summary into mainline FerrisPulse.

**Architecture:** Split `AsusDeviceHelper` into `partial` with `src/Hardware/AsusDeviceHelper.Rog.cs` for the 3 new PIDs and `DriverStateStore` caching, guard `UniversalGamingDeviceHelper` from probing them, register `AsusDeviceDriver` in `BuiltInDrivers.cs`, and upgrade `DeviceManager.cs` for cross-transport arbitration and 3-segment `BuildTrayBatterySummary`.

**Tech Stack:** C# 5.0 / .NET Framework 4.0 (`csc.exe /codepage:65001`), Win32 HID (`HidTransport`), PowerShell pure-memory reflection tests.

**Spec:** `docs/superpowers/specs/2026-11-17-asus-rog-integration-design.md`

## Global Constraints
- C# 5.0 only (`csc.exe` v4.0.30319): no `$"..."`, no `?.`, no `nameof`, no `out var`, no expression-bodied members.
- Zero external dependencies; Win32 P/Invoke via `HidTransport` only.
- All `.cs`, `.ps1`, `.md` files saved as UTF-8 with BOM (`python tools/ensure_boms_safe.py`).
- Never fabricate battery (`-1` when unknown), never leave `IsCharging = true` on failed read, never enable unverified DPI/polling-rate capabilities (`SupportsDpi = false`, `SupportsPollingRate = false`).
- All test scripts load `FerrisPulse.exe` via `[System.IO.File]::ReadAllBytes` (never `Add-Type -Path`).

## Review Focus
- Truncated or corrupted packets (length < expected packet size, wrong report ID, or `0xFF 0xAA` sleep NAK) must return `false` and never alter cached battery.
- Opening a vanished HID path must preserve the last real battery and timestamp while clearing `Ready` and `Charging`.
- `1AD0` wireless keyboard must NOT be suppressed when only plugged in for charging while switch stays on 2.4G (wired/BT channel lacks valid response or is disconnected).
- Cross-model keyboards (e.g., an unrelated BT keyboard `Keychron K2`) must NOT suppress `ROG AZOTH 96 HE Wireless`.
- Existing PELTA (`1B84`), Scope II RX (`1AB5`), Azoth Omni (`1ACE`/`1A85`), and 2-segment `BuildTrayBatterySummary` tests (`test_vgn_neon75_3151_5038.ps1`) must remain 100% passing.

---

### Task 1: Replay & Contract Tests (TDD Red Step)

**Files:**
- Create: `tests/test_asus_rog_devices.ps1`

- [ ] **Step 1: Write `tests/test_asus_rog_devices.ps1`** covering:
  - `AsusDeviceHelper.TryParseRogAzoth96Battery` on captured `1AD0` (`02 12 01 00 00 00 64 ...`), `1C10` (`00 12 01 00 00 00 64 03 01 01 ...`), boundary `0`/`100`, >100 reject, `charging > 1` reject, `02 FF AA` sleep NAK reject, truncated packet reject, wrong report ID reject.
  - `AsusDeviceHelper.TryParseRogReady` on `02 12 00 00 00 01 ...` (ready) vs `02 12 00 00 00 FA ...` (sleep `0xFA`) vs short/invalid header.
  - `AsusDeviceHelper.TryParseBatteryReport` & `TryParseChargingReport` replay for `1AFA` (`CC 12 07 00 00 05 25 14 00 ...` -> 37%).
  - `AsusDeviceHelper.IsRogEndpoint` four-tuple isolation (`1AD0` 64B, `1AFA` 64B, `1C10` 65B, rejecting `FF01`, wrong packet size, wrong PID).
  - `UniversalGamingDeviceHelper.IsSupportedGamingDevice` excludes `pid_1ad0`, `pid_1afa`, `pid_1c10`.
  - Capability contract: all 3 built devices have `SupportsDpi = false`, `SupportsDpiStages = false`, `SupportsXyIndependence = false`, `SupportsPollingRate = false`, `SupportsHardwareDpiMonitor = false`, and failed read on nonexistent path preserves last battery/timestamp and clears `Ready`/`Charging`.
  - `DeviceManager.ApplyCrossTransportKeyboardArbitration`:
    - Wired `1C10` online with battery/charging suppresses `1AD0` (`IsConnected=false, IsSleeping=true`).
    - Wired `1C10` without real response (`BatteryPercent=-1, IsCharging=false`) does NOT suppress `1AD0`.
    - Bluetooth `BT_...` `ROG AZOTH 96 HE` online (`BatteryPercent >= 0`) suppresses `1AD0`, whereas unrelated BT keyboard does NOT.
  - `DeviceManager.BuildTrayBatterySummary`:
    - 3-segment output `"鼠标 51%  |  键盘 100%  |  耳机 37%"`.
    - Sleeping 2.4G keyboard listed before wired/active keyboard does NOT block `"键盘 供电中"` or active wireless battery.
    - 2-segment mouse+keyboard output unchanged when no headset.
- [ ] **Step 2: Run test to verify it fails before implementation**
  Run: `powershell -ExecutionPolicy Bypass -File tests/test_asus_rog_devices.ps1`
  Expected: FAIL (missing new methods/types).

### Task 2: Protocol Implementation (`AsusDeviceHelper.cs`, `AsusDeviceHelper.Rog.cs`, `UniversalGamingDeviceHelper.cs`, `BuiltInDrivers.cs`)

**Files:**
- Modify: `src/Hardware/AsusDeviceHelper.cs`
- Create: `src/Hardware/AsusDeviceHelper.Rog.cs`
- Modify: `src/Hardware/UniversalGamingDeviceHelper.cs`
- Modify: `src/Hardware/BuiltInDrivers.cs`

**Interfaces:**
- Produces:
  - `AsusDeviceHelper.IsRogEndpoint(HidDeviceDescriptor d, int pid, int packetSize)`
  - `AsusDeviceHelper.TryParseRogAzoth96Battery(byte[] rx, byte expectedReportId, int expectedLength, out int battery, out bool charging)`
  - `AsusDeviceHelper.TryParseRogReady(byte[] rx, byte expectedReportId, int expectedLength)`
  - `AsusDeviceHelper.BuildRogDeviceInfo(HidDeviceDescriptor d, int battery, bool charging, bool ready, DateTime updated)`
  - `AsusDeviceHelper.QueryAllRogDevices()`
  - `AsusDeviceHelper.QueryAllRogKeyboards()`
  - `AsusDeviceDriver` in `BuiltInDrivers.cs`

- [ ] **Step 1: Make `AsusDeviceHelper` `partial` and update header credits for "工程机风格".**
- [ ] **Step 2: Implement `src/Hardware/AsusDeviceHelper.Rog.cs` with C# 5.0 syntax, `DriverStateStore` persistence, `QueryAllRogDevices`, `QueryAllRogKeyboards`, and wired-over-2.4G arbitration.**
- [ ] **Step 3: Exclude `pid_1ad0`, `pid_1afa`, `pid_1c10` in `UniversalGamingDeviceHelper.IsSupportedGamingDevice`.**
- [ ] **Step 4: Add `AsusDeviceDriver` in `BuiltInDrivers.cs` (`EnsureRegistered` + `CanHandle` + `PopulateCapabilities` + `QueryDevices`).**

### Task 3: Cross-Transport Arbitration & 3-Segment Tray Summary (`DeviceManager.cs`)

**Files:**
- Modify: `src/Hardware/DeviceManager.cs`

**Interfaces:**
- Produces:
  - `DeviceManager.ApplyCrossTransportKeyboardArbitration(List<MouseBatteryInfo> devices)`
  - Upgraded `DeviceManager.BuildTrayBatterySummary(List<MouseBatteryInfo> devices)`

- [ ] **Step 1: Mount `QueryAllRogDevices()` and `QueryAllRogKeyboards()` in `DeviceManager.QueryAllDevices` section 2.13d.**
- [ ] **Step 2: Implement `ApplyCrossTransportKeyboardArbitration(List<MouseBatteryInfo> devices)` and invoke it in `QueryAllDevices` before priority/primary selection.**
- [ ] **Step 3: Upgrade `BuildTrayBatterySummary` for 3 segments (`鼠标 | 键盘 | 耳机`) and keyboard/headset best-candidate selection.**

### Task 4: Build, Verify & Documentation / Contributor Hall of Fame

**Files:**
- Modify: `README.md`
- Modify: `docs/HARDWARE_PROTOCOLS.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Run `python tools/ensure_boms_safe.py` on codebase.**
- [ ] **Step 2: Build with `cmd.exe /c "build.bat"` and confirm exit code 0.**
- [ ] **Step 3: Run `powershell -ExecutionPolicy Bypass -File tests/test_asus_rog_devices.ps1` and verify PASS.**
- [ ] **Step 4: Run `powershell -ExecutionPolicy Bypass -File tools/run_all_tests.ps1` and verify all tests pass.**
- [ ] **Step 5: Update `README.md` (Hall of Fame + Tier 1 ASUS table), `docs/HARDWARE_PROTOCOLS.md`, and `CHANGELOG.md` crediting B站网友 **“工程机风格”**, re-run `python tools/ensure_boms_safe.py` and re-verify tests if needed.**
