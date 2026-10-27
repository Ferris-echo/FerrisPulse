# PULSE Explore电量适配与验证边界

日期：2026-10-22（Asia/Hong_Kong）。延续同一官方基线及已有ATK/Razer/DualSense Edge修改，未安装/替换驱动，未发送音量、EQ、复位或固件写入命令。

## 已实现

- **PS Link无线**：054C:0ECC，FF01/0020，Input64 / Output0 / Feature64。零权限打开厂商集合，仅GET_FEATURE B0，不读中断输入。确认B0的型号字段为0011后才认定PULSE Explore；同一接收器可用于Elite，不能只匹配PID。
- **双耳电量**：B0[55]低四位为左耳档位、高四位为右耳档位，范围0..15。使用整数公式`(level*100+7)/15`，得到0..100显示值；每档约6.7%，不是1%精度。
- **在线与休眠**：B0[39] bit0为耳塞链路；链路有效时，B0[56] bit4/bit5分别表示左右耳无线在位。双耳入盒、接收器仍插着时，不显示为耳塞在线，不拿上次上报的离线电量当成当前电量。
- **USB盒内耳塞**：充电盒接USB时枚举054C:0ED0，FF13/0001，Input64 / Output62 / Feature64；B0型号为0111。B0[55]仍为左右耳电量，B0[56] bit4/bit5在此USB路径表示左右耳在盒。按官方USB模型的在盒充电模式处理：已满显示已满；在盒且未满显示充电；不在盒的一侧标未知，不把0字节当作0%电量。
- **物理隔离**：无线与USB只有在B0[3..16]的14字节非零设备标识完全一致时才合并；不得把另一副耳塞的充电盒合入当前耳塞。标识仅用于内存匹配，不显示或发布。无线/USB快照分别缓存，合并不修改原始缓存。
- **显示**：优先显示当前在线耳塞的最低电量；无线耳塞均离线但USB有在盒耳塞时显示盒内耳塞最低电量及充电状态。主界面提供L/R电量、在盒充电/已满及悬停说明；托盘复用归一化电量。USB单独连接也可读取在盒耳塞，不等于PS Link音频已连接。
- **低负担与只读**：5秒电量节流；生产代码仅读取B0。不更换HidUsb、不引入WinUSB/libusb或第三方DLL，不打开充电盒串口，不修改任何设备设置。

## 实机对照

| 人工状态 | 关键报文字段 | 结果 |
| --- | --- | --- |
| 双耳使用 | model0011、levels DD、flags FF、link=1 | 左右各13/15，约87%；用户确认官方面板更新后同为87% |
| 左耳入盒、右耳使用 | levels DF、flags AD、link=1 | 左100%为上报值但离线；右87%在线，主电量取右耳 |
| 右耳入盒、左耳使用 | levels DF、flags 5F、link=1 | 左100%在线；右87%为离线上报值，主电量不取右耳 |
| 双耳入盒合盖，无USB线 | levels DE、flags 0F、link=0 | 耳塞离线/休眠，接收器在位；不伪报0%或活跃电量 |
| USB只放左耳 | model0111、levels 0F、flags 1F | 左耳100%、在盒已满；右耳未知，不是0% |
| USB双耳在盒 | model0111、levels BF、flags 3F | 左100%、右约73%；左已满、右在盒充电 |
| USB后续读取 | 左右档位均15 | 左右均100%、已满，不显示仍在充电 |

上述是不同时间的录制样本，电量会变化。首张官方截图显示100/100，随后用户确认面板刷新为87%，与连续读取的13/15一致。没有将初始化/旧截图当作独立电池证据。

无线与USB路径均单独完成实机读取；相同设备标识及双通道合并使用录制样本回放测试验证，不能将其描述为所有接收器/固件/多耳塞组合均已实测。接收器0143与本机USB盒0143是本次验证范围；其他PID或型号字段暂不泛化。

## 仍未实现：充电盒自身电量

**没有找到已验证的充电盒自身剩余电量字段。** USB PID0ED0是充电盒接口，但B0[55]报告的是盒内耳塞，不能把其中的100%当作盒电量。官方PS Link面板也只显示左右耳；本次不能据此断言硬件绝对不支持盒电量。

USB抓取按HID描述符只读查询了声明的Feature报告：80/84/A0/B0/C0/D0/D8/F4/F5，其中F4没有有效应答。未发现可用外部数值核验的盒电量字段，因此生产代码只用B0，盒电量明确为未知。没有扫描未声明报告、写固件命令或试探串口。

仅PS Link无线连接时，旧型号0011没有本次可确认的独立充电标志，入盒/离线不能直接等同正在充电；这时仍显示充电未知。USB在盒模式可显示耳塞充电/已满，不代表读取了精确充电电流，也不能覆盖异常电池或未验证固件的语义。

## 代码与测试

- `SonyDeviceHelper.PulseExplore.cs`：无线/USB解析、只读查询、物理标识隔离合并、缓存与归一化。
- `SonyPulseExploreState.cs`与`MouseBatteryInfo.cs`：双耳状态契约。
- `HidTransport.cs`：OpenFeature / ReadFeatureReport，零权限、控制传输读取，无中断Input操作。
- `MainForm.PulseExplore.cs` / `MainForm.Hardware.cs`：双耳音频卡片与悬停提示。
- `test_sony_pulse_explore.ps1` / `test_sony_pulse_explore_ui.ps1`：已录制字段、全部16档边界、单耳选择、休眠、USB充电/满电、未在盒为未知、异机隔离和设备切换。
- `test_razer_headset_blackshark.ps1`：其独立编译清单需包含新增纯模型依赖，避免模型扩展导致旧测试CS0246。

原始抓取包含设备标识/序列号，仅在本地忽略目录保留；公开测试使用去标识化字段样本。没有把官方程序或反汇编实现拷入项目；只提取本机官方模型的协议事实，并以用户物理操作和官方显示交叉核对。

## 协议参考

- [Sony官方PS Link PC Driver说明](https://www.playstation.com/en-us/support/hardware/playstation-link-pc-driver/)：Explore设备支持、面板电量与USB连接行为。
- [Jprnp/pslink-libusb](https://github.com/Jprnp/pslink-libusb)：FF01/0020与B0连接状态的研究线索。其单电池82方案面向Elite，未套用为Explore左右电量。
- 本机已安装的Sony PlayStation Link程序：PulseStereoBattery模型0011/0111的静态核验；未修改或重新分发官方二进制。
- [Microsoft HIDP_VALUE_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps)：只读取描述符声明的Feature报告ID。