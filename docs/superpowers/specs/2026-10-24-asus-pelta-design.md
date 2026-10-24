# ROG PELTA 原生适配设计（2026-10-24）

用户已确认推荐方案：原生接入 PELTA，修正 PELTA 与有线键盘分类，验证雷蛇现有支持。

- 现场设备：ASUS 0B05:1B84 ROG PELTA (2.4GHz)，FF00/0001，Input/Output 64，Feature 0。只对该 PID 开启耳机协议。
- 新建 AsusDeviceHelper，复用 HidTransport，在 DeviceManager 原生查询中注册；加入 ASUS ID 别名前缀以沿用自定义名称和去重。
- 仅发送 CC 12 07 00 00 / CC 12 08 00 00，64 字节补零。匹配 CC/12/key/00/00，拒绝 FF AA NAK、超范围电量和短报文；忽略无关事件，读取有总截止时间。
- 现场应答 CC 12 07 00 00 05 27 14 01：offset 6 = 39%；CC 12 08 00 00 00：offset 5 = 0 未充电，1 充电。
- 查询限频 5 秒，保留最后真实读数与采样时间。失联不冒充新样本；首次失败电量 -1；充电查询失败不报告正在充电。
- ASUS 0B05:1AB5 ROG STRIX SCOPE II RX 为有线键盘，仅枚举，无任何私有查询，电量未知。两个 PID 在通用鼠标探测入口明确排除。
- UI/托盘消费现有 MouseBatteryInfo；耳机无 DPI/回报率能力。雷蛇 1532:00B7 已受支持，只验证读取，不自动修改用户 DPI。
- 测试：真实报文回放，0/100 边界、错误/短帧/无关事件、分类与别名、防回退、UI 绑定；build.bat + 全量测试 + 现场复查。

方案比较：原生模块适合双查询与严格状态处理；FDAS 改动少但该双报文契约较复杂；泛化 ASUS 协议缺乏逐型号证据，因此采用精确原生模块。

协议参考（独立实现，无外部库或代码拷贝）：G-Helper app/Peripherals/Headset/AsusHeadset.cs 与 Models/Pelta.cs；RIGStats docs/control-architecture.md。