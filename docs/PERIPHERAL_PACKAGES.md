# MR0097 与 Keychron M6 外设扩展包

本目录的两份源码驱动通过官方 `IHardwareDriver` 接口加载，无第三方 DLL 或 NuGet 依赖。感谢 [GaiusC](https://github.com/GaiusC) 提供真实设备报文、官方软件读数核对及 USB 插拔验证。

| 设备 | 已验证端点 | 支持范围 |
| --- | --- | --- |
| Logitech PRO X SUPERLIGHT 2 / MR0097 | 接收器 `046D:C54D`，USB `046D:C09B`；`FF00/0002`，20/20/0；配对身份 `40A9:C09B:0000` | 电量、充电、X/Y DPI、Hz、无线板载档位读取；选择已验证的现有无线档位 |
| Keychron M6 | Link-KM `3434:D026`，USB `3434:D060`；`FFC1/0001`，64/64/0；身份报文必须确认 `3434:D060` | 电量、充电、同端点休眠缓存 |

两者均不开放任意 DPI、回报率或板载档表写入。M6 不开放性能设置。型号、端点及报文长度不匹配时拒绝接管；不会仅根据接收器名称或共享 VID/PID 判定设备。

## 安装

先在包含本次宿主修正的源码根目录执行 `cmd.exe /c "build.bat"`，再验证扩展包：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/install_local_peripherals.ps1 -HostExe .\FerrisPulse.exe -ValidateOnly
```

验证通过后，退出目标 FerrisPulse 实例并执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/install_local_peripherals.ps1 -HostExe .\FerrisPulse.exe
```

安装器只更新 `%APPDATA%\FerrisPulse\adapters` 中的 `keychron_m6.fp-driver.cs` 和 `logitech_mr0097.fp-driver.cs`。写入前会编译两个包并校验驱动契约，备份原文件；部分写入失败时恢复旧文件。安装回执位于该目录的 `.backups` 子目录。其他扩展包和启动项不变。重新启动 FerrisPulse 后生效。

这两个包也能在官方 2.3.0 上完成编译与离线报文回放，但稳定的实例缓存、精确端点归属以及 M6 跨传输页面连续性依赖本次宿主修正。仅验证包可编译，不能视为旧宿主上的完整运行兼容性保证。升级前应保存 EXE、扩展包和配置，并重新验证接口。

## M6 无线与 USB 身份关联

实测端点未提供可用于跨传输关联的序列号。因此默认保留独立的 USB 与无线身份，不会把所有同型号 M6 自动合并。确认两条路径属于同一只鼠标后，支持以下可选映射：

- 注册表：`HKCU\Software\FerrisPulse\Keychron\WiredReceivers`
- 值名：该鼠标的完整 USB 控制端点路径。
- `REG_SZ` 值：`Keychron:ReceiverPath:<该接收器完整 RF 控制端点路径>`。

这两个路径必须分别来自满足上表描述的真实端点；占位符不能直接写入。映射仅在对应接收器通过身份校验时生效。更换端口导致路径变化时需重新确认关联。本包不会自动创建映射，也不会猜测多接收器间的关系。未建立映射不影响各传输自身的电量和充电读取。

已确认同一实物时，实时读数优先于休眠缓存，两路实时则优先 USB。插拔后保持当前设备页面。休眠值保留原采样时间，并显示充电状态未知，避免把旧充电标记当成实时状态。

## 协议及测试

实机 TX/RX、偏移和能力限制见 [硬件协议记录](HARDWARE_PROTOCOLS.md)。数据包测试不需要真实硬件：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_external_peripheral_packages.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_logitech_mr0097.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_external_driver_ownership.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_local_peripheral_installer.ps1
```

`test_keychron_m6.ps1` 额外覆盖当前设备页面的 USB 切换连续性。涉及窗口或系统配置的回归应在独立测试账户或隔离环境执行，避免原有测试脚本修改日常配置。回放测试不能替代新的型号、固件版本或写指令的实机验证。
