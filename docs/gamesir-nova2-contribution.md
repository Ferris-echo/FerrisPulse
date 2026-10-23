# 启明星2（Nova 2 Lite）贡献摘要

建议 PR 标题：feat: 支持盖世小鸡启明星2无线电量与有线充电识别

## 修改
- src/Hardware/GameSirDeviceHelper.cs：新增只读原生 HID 协议，按设备 MAC 关联有线与无线身份；有线优先，保留最后有效电量和真实采样时间。
- src/Hardware/DeviceManager.cs：注册扫描，归并旧自定义记录；同型号多设备只接受精确身份或唯一指纹匹配。新增 ShouldHideDevice：MAC 级唯一身份只认显式 MAC 隐藏，共享的 HID:3537:xxxx 指纹不再连带隐藏新品牌卡片。
- src/UI/Forms/MainForm.Hardware.cs：充电且未知百分比时显示“充电中”，不显示 -1% 或虚构满电。
- tests/test_gamesir_nova2.ps1、tests/test_gamesir_nova2_ui.ps1：协议、边界、身份、多设备歧义、隐藏策略、充电界面回归。
- ARCHITECTURE.md、docs/AGENT_HANDBOOK.md：模块索引与逆向、排障经验。

## 已验证协议
- 2.4G 接收器 VID:PID：3537:1098；有线：3537:100F。
- UsagePage FF7A / Usage 0001，Input/Output 65 字节。
- 查询：65 字节 `00 01 01` 后补零。
- 应答以 `00 01` 开头，原始 [2..7] 为稳定身份，[19] 为电量；0..100 为有效百分比，FF 为充电哨兵，F4 按键报告忽略。
- 无线实测 87%、93%、88%，最终 78%；有线 FF 充电哨兵已验证。
- FF 不表示实测 100%；首次仅有 FF 时百分比未知，当前会话已有有效读数则保留。缓存不跨进程持久化。

## 验证与边界
build.bat 编译成功；test_gamesir_nova2.ps1 的协议/身份/隐藏策略断言与 test_gamesir_nova2_ui.ps1 全部 PASS。完整套件在本机 DSH 沙箱受 HKCU 注册表 ACL、Assembly.LoadFrom 路径与真实联网限制，其余失败项与该适配无关。

旧隐藏记录：显式隐藏该手柄 MAC 仍然有效；旧通用记录留下的 HID:3537:1098 不再连带隐藏新品牌卡片，也不需要手工 UnhideDevice 才能显示；同型号多只手柄不会因其中一只被移除而全部消失。保持 C#5/.NET4、UTF-8 BOM、零运行时第三方依赖；未写键位、灯效或固件。

## 分享给上游
将源码、测试和文档作为一个提交推到自己的分支，在 https://github.com/Ferris-echo/FerrisPulse/compare 发起 Pull Request；也可向 https://gitee.com/Ferris-echo/ferris-pulse 提交 PR/Issue 或共享文本 patch。不要提交 exe、DLL、抓包日志和 tools/scratch 内容。
