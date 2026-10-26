# 迈从 MCHOSE Z75S 2.4G 原生电量适配

适配日期：2026-10-26。源码基线：`4917a10e238ed3dba2938969752886353c8ff5e9`。

## 支持范围

Z75S 键盘通过 2.4GHz 接收器连接时，读取硬件百分比（0–100%）及充电状态。插 USB 充电线但保持 2.4G 开关时仍通过接收器读取；拔线后停止显示充电。注册到现有 DeviceManager、MainForm 键盘卡片和托盘，不增加鼠标 DPI、X/Y 或 Hz 写指令。

本次不宣称支持有线 PID `01B9`、蓝牙或其他共享接收器 ID 的型号。现有 DPI/回报率游戏 OSD 继续用于鼠标；Z75S 不提供这些性能控制。

## 设备识别

| 项目 | 实测值 |
| --- | --- |
| 品牌 / 型号 | MCHOSE / Z75S，由官方型号 token 和用户设备信息确认 |
| VID / PID | `258A:01BB` |
| USB 制造商字符串 | `SINOWEALTH`；不能作为品牌或型号判据 |
| 通信集合 | `MI_01 / COL01`，UsagePage `FF02`，Usage `0002` |
| Input / Output / Feature 长度 | `20 / 20 / 0` 字节 |
| 型号 token | `0A 00 00 00 00 09`；共享 VID/PID 的 Z75 token 末字节为 `01` |

本次机器的完整通信路径（其他机器的实例段不同）：

```text
\\?\hid#vid_258a&pid_01bb&mi_01&col01#a&1b88f82e&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

枚举使用访问权限 `0` 获取元数据；中断读写仅限上述厂商通信集合，不操作键盘按键输入集合或其 `\kbd` 节点。

## 报文

所有以下帧均为完整 Windows HID 缓冲区：包含 Report ID，总长 20 字节，不再加前导 `00`。

```text
型号 TX: 13 05 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 19
型号 RX: 13 05 01 00 06 0A 00 00 00 00 09 00 00 00 00 00 00 00 00 32
电量 TX: 13 4A 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 5E
```

| RX 偏移（含 Report ID） | 含义 |
| --- | --- |
| 0 | `13` Report ID |
| 1 | `05` 型号 / `4A` 电量 |
| 2 / 3 | 总包数 `01` / 包序号 `00` |
| 4 | 有效载荷长度：型号 `06` / 电量 `02` |
| 5 | 电量命令的百分比（0–100） |
| 6 | 电量命令的状态：高半字节充电，低半字节充满 |
| 19 | `sum(bytes[0..18]) & 0xFF` |

中断读取通过 `HidTransport.ReadReportExact` 核验 Win32 实际收取的 20 字节，拒绝短回包被缓冲区补零后伪造为完整帧。解析再拒绝错误长度、Report ID、命令、分包头、载荷长度、校验和及大于 100 的百分比。先取得匹配型号响应，再发送电量查询；不同型号不接入 Z75S 卡片。查询只发送只读命令 `05` 与 `4A`。

### 真实硬件样本

```text
先前放电 85%:
13 4A 01 00 02 55 00 00 00 00 00 00 00 00 00 00 00 00 00 B5

接入 USB 充电线，仍在 2.4G，50% / 充电中:
13 4A 01 00 02 32 10 00 00 00 00 00 00 00 00 00 00 00 00 A2

继续充电后，原生 Helper 读到 100% / 充电中。
用户拔下充电线并保持 2.4G 后，满电 / 未充电:
13 4A 01 00 02 64 01 00 00 00 00 00 00 00 00 00 00 00 00 C5
```

最后一个样本中 `01` 是充满位，高半字节为 0，因此不能显示“充电中”。原生 Helper 也验证了拔线后 `BatteryPercent=100`、`IsCharging=false`、`IsSleeping=false`。

百分比原样采用固件字段；早期 85→50 的变化未经官方 UI 独立校准，不能据此断言电池实际容量或线性精度。本适配不平滑、换算或捏造读数。

## 编译后仍找不到键盘时

工程目录编译成功，只会更新该目录的 EXE，不会自动替换 Downloads 等位置的现用副本。应先核对运行进程的 `ExecutablePath`、`CommandLine` 和 EXE 的 SHA256，再核对启动快捷方式。程序有全局单实例保护，旧版本未退出时，双击新 EXE 可能只唤醒旧进程。便携版无需系统卸载器；先退出旧进程，备份原文件及设置，再替换实际启动入口并重新运行。

保留的自定义名称可能仍是“迈从键盘”，不一定显示型号 Z75S。点击主界面顶部设备名称区域，在设备切换列表选择键盘即可。本次更新现用副本后，已在实际设备列表及键盘主卡片确认 100% / 2.4G 无线。

## 缓存与性能

- 两次有效事务之间至少间隔 5 秒；失败同样推进节流时间。
- 每端点一次型号+电量事务共用 320ms 截止时间；每条命令最多读取三帧，过滤无关/损坏回复；句柄必定关闭。
- 缓存按实例端点隔离，位于 `HKCU\Software\RazerBatteryTray\MchoseCache\Z75S\<端点编码>`，与 G87 V2 的旧缓存独立。只保存真实值及其采样时间。
- 休眠/超时保留旧电量并标记缓存，停止实时充电动画；首次无电量为 `-1`。首次没有型号握手成功，也没有验证过的缓存时，不猜测 Z75S。
- 接收器拔下后在下一次非节流枚举中移除；重新接入可恢复最后真实缓存。休眠及冷启动缓存经自动化验证；物理断电、接收器拔接和多接收器场景未作实机测试。
- 沿用工程以 VID/PID 为基础的 UI 去重规则；本次只实测一只接收器，未扩展同型号多接收器同时显示。

## 修改与验证

- `src/Hardware/MchoseDeviceHelper.cs`：型号识别、20 字节协议、严格解析、单独轮询/持久化；保留 G87 V2 路径。
- `src/Hardware/HidTransport.cs`：增加严格实际字节数的 `ReadReportExact`；旧接口保持兼容，Z75S 使用严格接口。
- `src/Hardware/UniversalGamingDeviceHelper.cs`：排除键盘接收器，避免通用鼠标报文误探。
- `src/Hardware/DeviceManager.cs`：复用已有品牌注册，缓存更新采用设备实际采样时间。
- `src/UI/Forms/MainForm.Hardware.cs`、`MainForm.Tray.cs`：正确显示 0%/100%、充电、休眠缓存，传递托盘充电标志，隐藏其他品牌的硬件菜单。
- `tests/test_mchose_z75s_*.ps1`：真实协议样本、损坏包、型号排除、端点选择、缓存持久化/隔离和实际控件显示；本地双向管道覆盖真实 Win32 完整/截断/无关帧读写边界。
- `ARCHITECTURE.md`、`AGENT_HANDBOOK.md`：同步架构与避坑经验；`docs/superpowers/` 保存设计与实现计划。

严格 C# 5.0 / .NET 4.0 核心程序集，无 NuGet、第三方运行库；改动源码、测试及文档均使用 UTF-8 BOM。

在 Windows 工程根目录构建/验证：

```powershell
cmd.exe /c "build.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_mchose_z75s_support.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_mchose_z75s_cache.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_mchose_z75s_ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_mchose_z75s_transport.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run_all_tests.ps1
```

全量测试中的历史自启动用例会修改当前用户注册表和快捷方式；实机回归前应保存完整快照并在结束后恢复，不应把它当成无副作用的纯单元测试。本次使用本地包装器执行和恢复，日志/快照不纳入提交。

验证结果：Windows 原生 `build.bat` 编译成功；新增协议 32 项、缓存/端点 14 项、UI 13 项断言及真实传输边界 3 个用例通过；全量 `tools/run_all_tests.ps1` 共 65 项测试脚本，65 PASS、0 FAIL、0 TIMEOUT。真实接收器经 Helper 和 DeviceManager 接入一张 Keyboard 卡片，拔线后 100% / 未充电，DPI=0、无鼠标回报率能力；插线/拔线两态已实测。最终严格传输路径实测 Helper 用时 151ms，DeviceManager 总查询用时 682ms（单次观测，不是性能基准）。测试恢复包装器检查了该次启动项值类型/内容和快捷方式原始字节，全部通过。

## 最终审查范围

独立审查发现 Win32 实际短读长度丢失的问题；以 7 字节 `13 4A 01 00 02 00 A0` 经真实本地管道复现误收，再增加严格读取接口修复，未向设备发送异常帧。既有通用读取接口及其异步取消生命周期未在本次重构。

暂缓的小问题：已有型号确认但从未取得电量的休眠键盘，主卡片正确等待首次读取，托盘悬浮提示仍可能显示通用“免驱在线 / 最后同步 00:00:00”。这仅影响提示措辞，不制造百分比。物理休眠、接收器拔接、多接收器、有线/蓝牙及固件电量校准均未扩大实机验证范围。

## 协议原始来源

- [迈从官方 M HUB](https://www.mchose.com.cn/)。
- [官方协议脚本 purify.es-CxLZ1Htw.js](https://www.mchose.com.cn/assets/purify.es-CxLZ1Htw.js)：`Rpe` 的 `getPassWord/getBattery`、`dJe` 中断命令、`bae` 电量 parser、型号 token 和 Report ID 表。位解析为 little endian，先 fullStatus 后 chargeStatus；只依据事实重写原生实现，不引入脚本或依赖。
- [官方 Z75S 使用手册](https://file.maicong.cn/uploads/24/07/Z75S.pdf)：连接开关与充电说明。

## 向作者贡献

建议标题：`feat: support MCHOSE Z75S 2.4G battery and charging`。

可将源码分支/patch 提交到 [GitHub PR](https://github.com/Ferris-echo/FerrisPulse/compare) 或 [Gitee PR](https://gitee.com/Ferris-echo/ferris-pulse/pulls)，也可创建 [GitHub Issue](https://github.com/Ferris-echo/FerrisPulse/issues/new)，附上本说明、patch、VID/PID、固件/连接方式和回归结果。无需上传 EXE 或用户注册表快照。

邮件或文本共享同一 patch 也可；署名请采用贡献者自己希望公开的名称。本次适配已按用户后续授权提交为 [GitHub PR #52](https://github.com/Ferris-echo/FerrisPulse/pull/52)，等待维护者审阅；本地源码、补丁及运行验证记录仍保留。无需再为同一适配创建重复 Issue。
