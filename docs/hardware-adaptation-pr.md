# 建议 PR 标题

feat: 适配雷柏 VT3 与高斯 GS3108T-PRO 电量监控

# PR 正文

## 改动

- 为雷柏 VT3 接入 0x07 状态输入，读取电量、X/Y DPI 与档位；保持寄存器操作串行，对设置执行写后回读。
- 按同一物理 HID 父接口绑定雷柏通道，避免有线/接收器混配；恢复通用接收器的有效鼠标 PID 识别。
- 修正未知电量被默认显示为满电、真实 0% 被吞掉、设置失败仍更新 UI/OSD，以及切换端点时档位缓存越界的问题。
- 新增高斯 GS3108T-PRO 2.4G 适配，严格匹配产品名称、05AC:024F、FF60:0061、33 字节 Input/Output；使用官方驱动中的 0x20/0x01 电量查询及校验和。
- 排除高斯共享 Apple VID 引发的重复鼠标条目，修正复合 HID 键盘分类与连接方式。
- 调整 VT3 名称显示及雷柏最多 6 档 DPI 的管理界面。
- 保持 C# 5.0/.NET Framework 4.0、Win32 HID、零运行时第三方依赖、UTF-8 BOM；补充回归测试和维护文档。

## 验证

2026-10-23 最终工作区验证：`cmd.exe /c build.bat` 编译成功；`powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run_all_tests.ps1` 退出码 0，62/62 项 PASS。包含重复高斯条目排除修复及后续名称/DPI 界面编辑。

实机设备：雷柏 VT3 接收器 24AE:1412；高斯 GS3108T-PRO 2.4G 05AC:024F。
高斯原始应答实测为 `00 20 01 00 64 ... 85`，对应 100% 电量。

## 已知边界

- 高斯官方驱动同时运行时可能争抢 HID 应答；本轮成功读取在退出官方驱动后验证。
- 高斯目前确认的是 2.4G 电量百分比，未确认充电标志与蓝牙模式的私有电量协议。
- 雷柏保留单主设备模型，高回报率能力依赖接收器/固件；不把软件回读等同于物理频率测量。

## 来源与许可

雷柏协议参考本地 rapoo-tray（MIT，Copyright (c) 2026 Iris），完整许可随代码保存于 docs/rapoo-tray-MIT.md。高斯查询根据用户本机官方驱动静态分析并以实机应答验证，未复制官方二进制到仓库。

# 提交文件范围

建议纳入 src/、tests/、ARCHITECTURE.md、docs/AGENT_HANDBOOK.md、docs/rapoo-tray-MIT.md。
不纳入 FerrisPulse.exe、tools/scratch/、官方驱动、分析工具、调试日志。tools/probe_rapoo_vt3.ps1 可作为诊断工具一并纳入。
本文件用于复制 PR 正文，无需作为软件源码提交；docs/superpowers/ 是本地实施记录，也可暂不纳入 PR。
