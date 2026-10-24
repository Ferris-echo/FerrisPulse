# Kraken V4 原生电量适配与贡献说明

日期：2026-10-24。基线：GitHub Ferris-echo/FerrisPulse，4917a10。
本次工作区按用户环境使用 D:\Ferris\FerrisPulse，保留 D:\Ferris\FerrisPulse.exe 原文件。

## 已验证设备

Razer Kraken V4（非 Pro），VID:PID `1532:056C`；用户确认采用 2.4G 接收器。
HID 原名 `Razer Kraken V4 - Chat`，制造商字符串 `MediaTek Inc`。

| UsagePage / Usage | Input / Output / Feature 字节 | 用途 |
| --- | --- | --- |
| FF13 / 0001 | 62 / 62 / 0 | 未使用 |
| 000C / 0001 | 2 / 0 / 0 | 消费控制，未使用 |
| 000B / 0005 | 2 / 2 / 0 | 耳机控制，未使用 |
| FF14 / 0001 | 64 / 64 / 9 | 本次实测的电量通信通道 |

只匹配 `056C + FF14/0001 + 64B 输入输出`，不向音频或其他 HID 集合发送报文。
完整设备接口路径已记录在本地 tools/scratch/hardware-inventory.json（含机器实例信息，不纳入贡献补丁）。

## 协议与证据

参考 [Sebax95 的 Kraken V4 Pro 逆向](https://github.com/Sebax95/Razer-Kraken-V4-Pro-Battery-Detector-Linux)，但本次仅在普通 Kraken V4 056C 真机上验证后启用；不扩展到未测试 PID。
另参见 [OpenRazer 的 Kraken V4 支持请求](https://github.com/openrazer/openrazer/issues/2825)。

使用 WriteFile / ReadFile 中断 OUT/IN，64 字节。请求前缀：

`02 00 60 00 00 00 04 00 00 80 CC 00`，其余补零。

`CC=21` 电量、`CC=2A` 充电、`CC=20` 无线连接。响应：

`02 02 60 00 00 00 05 00 80 80 CC 01 01 VV ...`

零基偏移 `[10]` 命令、`[11]` 成功、`[12]` 数据有效、`[13]` 值。`[12]` 不是充电位。
电量 VV 直接为 0..100；充电与连接分别为 0/1。拒绝 0xFF、越界、截断、错命令、错误状态与无关通知。
尾部 `[62]` 曾观察到变化，目前未解释，不据此推断电量或充电。

真实捕获前 14 字节（每条完整报告 64 字节）：

- 电量：`02 02 60 00 00 00 05 00 80 80 21 01 01 45` → 69%。
- 未充电：`02 02 60 00 00 00 05 00 80 80 2A 01 01 00`。
- 用户接线后充电：`02 02 60 00 00 00 05 00 80 80 2A 01 01 01`。
- 无线连接：`02 02 60 00 00 00 05 00 80 80 20 01 01 01`。

## 设计与实施记录

1. 通读工程规则及手册，建立原版编译与 61 项测试基线。
2. 枚举接口；查阅公开协议；三条只读查询获真实回包。
3. 新增捕获回放测试，先验证原版缺少解析方法而失败。
4. 在现有 RazerHeadsetHelper 中按 PID 分离协议；复用 DeviceManager 已有接入与 Headset UI/托盘通路，不增加依赖。
5. 新增型号命名、严格响应校验。失败时只回退真实缓存并标记休眠；无缓存保持 -1（未知）；不把历史充电当作当前充电。
6. 异步 I/O 缓冲区固定到完成，取消后回收操作完成状态，再释放资源；避免超时后继续写入已释放缓冲区。
7. 系统 csc / build.bat、全量测试、新驱动现场读取与充电状态切换验证。

## 变更文件

- src/Hardware/RazerHeadsetHelper.cs：Kraken V4 协议、识别、命名和 I/O 生命周期。
- src/Hardware/DeviceManager.cs：更新接入说明（原有注册已能复用）。
- tests/test_razer_headset_kraken_v4.ps1：实测回放、边界和错误响应测试；全内存反射，不锁 EXE。
- ARCHITECTURE.md、docs/AGENT_HANDBOOK.md：架构索引与避坑经验。
- 本文：协议、实测与贡献摘要。

## 验证边界

已实机读到 69%，并在用户插入充电线后确认 IsCharging 从 false 切为 true。
百分比为固件上报值；尚未与雷云界面数值对照，也未完成整段放电曲线验证。
本适配范围为 056C 2.4G，不宣称支持蓝牙、有线独立数据模式或 V4 Pro。
关机、断开接收器和长时间休眠尚未进行物理切换测试；错误帧与范围检查由自动化覆盖。
耳机不适用 DPI、XY 轴、鼠标回报率设置。其他外设尚未在本次变更中适配。

## 提交给作者

建议标题：Add Razer Kraken V4 (1532:056C) battery and charging support。
可在官方 GitHub / Gitee 建立 PR 或 Issue，附此说明与 kraken-v4.patch。补丁不含 EXE、缓存、序列号或本机设备路径。
在干净的基线 checkout 执行 `git apply --check kraken-v4.patch`，再执行 `git apply kraken-v4.patch`；随后 build.bat 和 tools/run_all_tests.ps1。
本次未代为推送、发布或联系作者；合并及致谢由维护者决定。
## 最终验证结果

- build.bat：成功，C# 5，零编译错误。
- 修改前 61 项、修改后 62 项自动化测试全部通过。
- DeviceManager 实机集成查询：恰好一个 Kraken V4，分类 Headset，显示名称正确，充电中且电量已由 69% 升至 72%。
- 其他 HID 原名补充：3837:3007 = Ace 68 GT，3837:1001 = MCHOSE K7 Ultra；0B05:1ACE 仅能确认 ROG OMNI RECEIVER，未确认其配对外设型号。
