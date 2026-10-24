# ROG PELTA Implementation Plan

> 执行方式：本会话 inline，按 executing-plans / test-driven-development 执行；全新克隆、独立 feat/asus-pelta-battery 分支。

**Goal:** 将已实测 PELTA 电量与充电状态接入原生设备总线，准确识别有线键盘。
**Architecture:** AsusDeviceHelper 负责精确白名单、纯报文解析、带截止时间的双查询与真实读数缓存。DeviceManager 聚合，现有 MainForm/托盘使用通用状态模型。
**Tech Stack:** C# 5、.NET 4、Win32 HID，所有修改 UTF-8 BOM。

- [x] 完整读取 AGENTS / AGENT_RULES / AGENT_HANDBOOK，克隆官方源码，枚举本机并得到真实回包。
- [x] 用户确认上述方案，记录设计及计划。
- [x] tests/test_asus_pelta_support.ps1：全内存加载程序集，Assert 类型存在；回放 CC 12 07 00 00 05 27，断言 battery=39；回放 CC 12 08，断言 charging=false/true；畸形、NAK、0/100、越界；分类、白名单、别名与 UI 数据契约。
- [x] 在未实现时执行 powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_asus_pelta_support.ps1，预期 FAIL: AsusDeviceHelper missing。
- [x] src/Hardware/AsusDeviceHelper.cs：实现两种受支持产品与纯解析器；5 秒限频，1000ms/查询总预算（现场750ms恢复应答），匹配应答，try/finally 关闭句柄；只缓存真实读数。
- [x] src/Hardware/DeviceManager.cs：注册 QueryAllAsusDevices，并增加 ASUS ID 前缀；src/Hardware/UniversalGamingDeviceHelper.cs 排除这两个精确 PID，防止通用协议误写。
- [x] cmd.exe /c build.bat；执行新增测试，预期 PASS；用该模块现场读取和检查 DeviceManager 结果、MainForm 显示与雷蛇性能参数。
- [x] 更新 ARCHITECTURE.md 与 docs/AGENT_HANDBOOK.md；运行全量测试，区分已有失败与新增回归。
- [x] 核验 BOM / 换行、git diff --check / git status；导出 patch、源码、运行包与贡献摘要到 outputs，保留真实实测范围。
充电未知边界补充：新增 IsChargingUnknown 数据属性，MainForm 主界面和托盘显示未知；完整64字节报文逐字段损坏测试。原生现场42%、832ms验证通过；charging=1仅回放，物理仍返回0。
