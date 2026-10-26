# MCHOSE Z75S Battery Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. The user approved the design and requested no further plan/implementation confirmation.

**Goal:** Native Z75S 2.4G battery and charging monitoring without changing G87 V2 behavior.
**Architecture:** Extend MchoseDeviceHelper with a 20-byte interrupt protocol, strict model identity validation, and a separate per-endpoint cache. Reuse the registered DeviceManager/UI/notification path.
**Tech Stack:** C# 5.0, .NET 4.0 core assemblies, Win32 HID through HidTransport; Windows PowerShell reflection tests.
**Spec:** docs/superpowers/specs/2026-10-26-mchose-z75s-design.md

## Global Constraints

- UTF-8 BOM for modified code, tests and documentation; preserve maintenance directives.
- No NuGet or third-party runtime assemblies. Native build.bat must compile.
- Only FF02/0002 20-byte configuration endpoints receive Z75S interrupt queries.
- Require identity 0A0000000009; never infer model from shared VID/PID alone.
- Preserve last real battery/time on timeout; unknown remains -1. G87/Z75S caches must be separate.
- This newly cloned checkout is dedicated to this task; use branch codex/mchose-z75s-battery in the canonical D: project directory. No push, publish or merge is requested.

## Review Focus

Malformed/truncated packets; shared VID/PID with a different keyboard; cold-start sleep with no battery; receiver removal/reconnection; UI classification and mouse-protocol exclusion.

### Task 1: Protocol and native integration

Files: src/Hardware/MchoseDeviceHelper.cs, src/Hardware/HidTransport.cs, src/Hardware/UniversalGamingDeviceHelper.cs, src/Hardware/DeviceManager.cs, src/UI/Forms/MainForm.Hardware.cs, src/UI/Forms/MainForm.Tray.cs, tests/test_mchose_z75s_*.ps1.
UI integration was extended after real control tests exposed 0%/full/charging/sleep binding defects; see the ledger ruling.
Interfaces: BuildZ75sIdentityFrame(), BuildZ75sBatteryFrame(), ParseZ75sIdentityResponse(byte[], int), ParseZ75sBatteryResponse(byte[], int, out int, out bool), existing QueryAllMchoseDevices().

- [x] Write reflection tests with literal hardware RX fixtures; require Z75S identity, battery 85/discharging and 50/charging, 0/100 boundaries, invalid-frame rejection, and generic mouse exclusion.
- [x] Run the new test against the baseline binary and observe the expected unsupported-device failure.
- [x] Implement bounded HID queries and separate per-endpoint persistence; keep G87 polling behavior intact.
- [x] Build using cmd.exe /c build.bat; run the new test and existing MCHOSE/G87 tests. Expected: compile success and PASS.
- [x] Verify DeviceManager receives one Keyboard card with the physical RX battery/charging, no DPI controls; verify cached/unknown model construction without hardware.

### Task 2: Regression, hardware evidence and contribution

Files: ARCHITECTURE.md, docs/AGENT_HANDBOOK.md, docs/MCHOSE_Z75S_ADAPTATION.md.

- [x] Run tools/run_all_tests.ps1, preserving app settings and shortcuts touched by existing tests; report all pre-existing or new failures honestly.
- [x] Read the native helper on the connected device, then confirm charge/discharge switching with the user.
- [x] Document protocol, sources, real samples, exact validation results and practical limitations.
### Whole-branch review and handoff

- [x] Perform a fresh review of the diff, check BOM/C# syntax, rebuild after reflection tests to verify no binary lock, and create a local patch.

Final review found a transport-length issue, reproduced with a real local pipe and fixed with ReadReportExact. Native build and 65/65 test scripts PASS; post-fix hardware verified 100% / not charging. One minor first-sample sleeping tray wording issue is deferred in the contribution notes. The patch was checked against baseline 4917a10; the local branch is retained without push, merge or PR creation.

后续交付：用户追加授权提交 PR，并允许替换旧版本。已创建 [PR #52](https://github.com/Ferris-echo/FerrisPulse/pull/52)，备份后更新实际运行副本，保留启动路径和配置；实际键盘主卡片显示“迈从键盘”、100%、2.4G 无线。上述不推送/不开 PR 的约束对应初次本地交付，后续授权已扩展此范围。
