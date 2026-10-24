# 雷柏 VT3 适配贡献摘要

基线：FerrisPulse main e0429c956ae82827cfce24bb33c85c4865070c33，与 2026-10-22 查询到的官方 main 一致。

## 设备与验证边界

宿主机 SetupAPI 实测：雷柏 VT3，VID `24AE`、PID `1412`，产品串 `Rapoo Gaming Device`，2.4G 接收器。
`4612` 为参考项目收录的 USB 有线对应 PID，本轮没有插线实测。没有变更其他 VT3S/MAX 的型号映射，以免覆盖两个项目中存在冲突的型号库。

实际状态路径：
```text
\\?\hid#vid_24ae&pid_1412&mi_01&col09#8&15514e62&0&0008#{4d1e55b2-f16f-11cf-88cb-001111000030}
```
控制/Feature 为相同父接口的 `col05` / `col06`。路径只用于本机记录，程序动态枚举，不硬编码。

| 通道 | UsagePage / Usage | 报告 |
|---|---|---|
| 状态 | FF00 / 0002 | Input 19 字节，Report ID 07 |
| 控制 | FF00 / 000E | Output 33 字节，Report ID 06 |
| 寄存器应答 | FF00 / 000F | Feature 33 字节，Report ID 08 |

用户确认鼠标目前关机/不在手边。本轮实际采样没有获得 0x07 报告，返回电量未知、鼠标未应答；不能宣称真实电量、充电切换、DPI 写入或 Hz 切换已实机通过。通用接收器 14FF/1500/1501 继续通过 COMM/04 解析鼠标有效 PID；物理 PID 仅负责端点与连接模式。测试中的报文均明确标注为依据参考协议构造的合成样本。

## 协议与行为

- 0x07：[1] 高四位 10/20；[2] 当前档位索引 0..5（0-based），映射到 UI 时加 1；[3..4] X DPI 与 [5..6] Y DPI 均为小端；[8] 0..100 电量；充电按 [7] bit1 或 [9] 的 1/2 解析。用户实机截图确认官方选中 1600 DPI 时 FerrisPulse 曾高亮 1200 DPI；与寄存器 0x98 的零起始索引一致，修复将状态索引 +1 后映射为六档 UI。
- 0x06 控制：`06 A5 A4 <len> <addr> <bank> 00 00 ...` 读；`06 A5 A5 <len> <addr> <bank> 00 00 <data...>` 写，payload 从 [8] 开始。
- 初始化：`06 A5 A3` 后补零到 33 字节。
- Feature 应答兼容 ACK 01 位于 [0]（payload [4]）或 Report ID 08 + ACK 01（payload [5]）。busy/截断均不当作成功。
- bank 08：80 为 Hz；88/C8 为 X/Y 档位表；96 为档位数减一；98 为当前档位减一。Hz 编码：125=08，250=04，500=02，1000=01，2000=84，4000=82，8000=81。高 Hz 的实际可用性取决于设备/接收器固件，设置成功必须回读一致。
- 每个写入事务绑定开始时的物理端点组；插线或更换接收器导致组变化时拒绝旧请求。档位缓存更新在回读后再次验证数组边界，避免切换清空缓存后的越界。
- 写入全部串行且逐项回读；部分写入失败不会返回成功。多寄存器修改不保证硬件事务原子性，失败后下次刷新读取实际状态。
- 未知电量使用 -1；0/1 是合法读数；无效电量保留最后有效值和采样时间。USB 直连不自动判充电；轮询不伪造电量采样时间。
- 同一物理 HID 父接口成组选择，Feature Usage 0010 不会冒充 000F；有线完整组优先。维持项目现有单个主雷柏设备模型，不声称新增多只雷柏并行监控。
- 重叠 I/O 取消后等待完成，再释放原生内存和事件；线程不访问 WinForms。

## 修改文件

- src/Hardware/RapooProtocol.cs：状态/Feature 解析、端点分组、DPI 参数校验。
- src/Hardware/RapooDeviceHelper.cs：控制链路、真实查询、写后回读、未知电量处理。
- src/Hardware/RapooDeviceHelper.Status.cs：后台输入、状态缓存与归一化、可取消 I/O。
- src/Hardware/DeviceManager.cs：雷柏自定义设备 0%/充电/采样时间传递。
- src/UI/Forms/MainForm.Hardware.cs / MainForm.UI.cs：待机 0%、未知采样显示；DPI/档位表/手动 Hz 在读回确认后才更新 UI/OSD。
- src/Hardware/RapooProtocol.cs / src/UI/Forms/DpiManagementDialog.cs：VT3 1-based 六档状态映射，以及六档主面板/管理弹窗支持。
- tests/test_rapoo_vt3_*.ps1：协议、状态、配置边界及 UI 行为测试。
- tools/probe_rapoo_vt3.ps1：用户开机后采样工具。
- ARCHITECTURE.md、docs/AGENT_HANDBOOK.md、设计/计划与许可文件。

## 本轮自动验证

2026-10-22：原生 build.bat 编译成功且无警告/错误；宿主环境全量测试 61/61 PASS（原有 56 项 + 新增 5 项 VT3 测试）。独立审查发现的设备切换、通用接收器身份和失败设置 UI 问题已修复并加入回归测试。

## 开机后验收

运行：
```powershell
cmd.exe /c build.bat
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/probe_rapoo_vt3.ps1 -Seconds 30
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run_all_tests.ps1
```

依次确认电量与官方工具一致、机身 DPI 档位实时更新、插拔充电线时标志变化、有线与接收器切换正确、休眠唤醒后恢复、关闭程序后能立即重编译。DPI/独立 XY/Hz 写入后需要回读与物理体验确认，不把配置值相同等同传感器实际频率测量。

## 贡献方式与署名

代码参考本地 rapoo-tray 的 MIT 源码（Copyright (c) 2026 Iris），完整许可见 rapoo-tray-MIT.md。可向 https://github.com/Ferris-echo/FerrisPulse 或 https://gitee.com/Ferris-echo/ferris-pulse 提交 PR/Issue，附合成测试、实际采样与本轮未实测项；也可发送本地 diff/patch。提交前排除 exe、日志、注册表数据和 tools/scratch。致谢署名由维护者决定，不作代作者承诺。
