# ROG 夜魔 (Azoth) / OMNI 适配记录

## 范围与端点

接收器 VID:PID `0B05:1ACE`，配对型号代码 `1A85`。仅匹配已确认配对夜魔的接收器，不把所有 OMNI 都当作夜魔。

- 配对列表：UsagePage/Usage `FF02/0001`，MI02/col01，64B Input/Output。
- 键盘电量：`FF00/0001`，MI02/col02，64B Input/Output。
- `FF01/0001` 是独立鼠标通道，不发送键盘查询。
- 通过同一设备实例分组匹配集合；完整路径由运行时枚举，不硬编码本机路径。

## 只读协议

报文均补零至 64 字节，偏移从 0 开始。

| 查询 | 请求前缀 | 响应解析 |
| --- | --- | --- |
| 配对列表 | `01 A0 00 00` | 实测 `01 A0 00 01 00 85 1A 02 04`；从 [5] 开始每 4B 一项，PID 小端 |
| 电量 | `02 12 01` | [6] 电量 0–100；[9] 为 1 表示充电 |
| 版本探测 | `02 12 00` | 仅在电量查询失败后执行一次，再重试电量 |

实测电量响应前缀：`02 12 01 00 00 00 5F 02 00 00 1E 5F 9B 10`，对应 95%、未充电。`02 FF AA` 是无有效设备响应，不得解析为 0%。校验完整长度、命令、成功状态和数值范围。休眠保留进程内最后一次真实电量，首次查询失败为未知；不伪造 100%。

依据 [GHelper PeripheralsProvider](https://github.com/seerge/g-helper/blob/main/app/Peripherals/PeripheralsProvider.cs)、[AsusKeyboard](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Keyboard/AsusKeyboard.cs)、[Azoth](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Keyboard/Models/Azoth.cs) 的协议资料与本机回包独立实现，无第三方依赖。

## 实现与验证边界

新增 AsusDeviceHelper，通过 DeviceManager 注册为 Keyboard。旧版自定义 ROG OMNI RECEIVER 配置在唯一匹配时合并至夜魔，保留用户别名，防止重复卡片及错误 Dongle 分类。通用游戏设备扫描排除 OMNI，避免发送不适用芯片协议。HidTransport 固定异步 I/O 缓冲区，取消后等待完成，保留真实读取长度。

使用供应商专有 FF00/FF02 集合的中断传输；不读取受系统保护的标准键盘输入集合。Feature 查询的零访问权限规则不适用于这里的中断 ReadFile/WriteFile。

已通过系统 build.bat 编译及全部 63 项回归测试。原始探针曾实读 95%、未充电；2026-10-24 用户接上充电线并保持 2.4G 后，DeviceManager 主调度实读 94%、正在充电、在线，随后 AsusDeviceHelper 连续三次均返回相同有效状态。名称、Keyboard 分类及旧配置合并均已验证。拔线后的反向切换、长时间休眠唤醒及应用重启后的电量缓存尚未验证；首次查询发生在休眠时仍可能显示未知。蓝牙、原装独立接收器和其他 OMNI 型号不在此次范围内。

## 贡献

涉及 AsusDeviceHelper.cs、DeviceManager.cs、UniversalGamingDeviceHelper.cs、HidTransport.cs、test_asus_azoth_omni.ps1 及架构/手册。提交 PR 时附本记录及实际验收结果；不提交可执行文件或含本机设备路径的临时日志。