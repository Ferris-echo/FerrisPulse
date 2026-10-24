# VGN Neon75 3151:5038 电量/充电与主灯调节适配

> 实测日期：2026-10-24
>
> 实测环境：Windows / FerrisPulse 源码树
>
> 结论：`3151:5038` 是 **VGN Neon75 的 2.4G Dongle**，不是 ROYUAN 品牌键盘；ROYUAN 只是方案商。有线端点 `3151:502F` 经 `0x8F` 识别为 **VGN Neon75 Ultra（device id 2905）**，主灯读写已实机 round-trip 验证。

## 1. 品牌与设备识别

| 项目 | 实测值 |
|---|---|
| USB VID:PID | `3151:5038` |
| 用户侧品牌/型号 | **VGN Neon75 Ultra**（device id 2905，有线实测） |
| 方案商 | ROYUAN（融渊） |
| 产品字符串 | 2.4G: `2.4G Wireless Keyboard`；有线: `VGN Neon75 Keyboard` |
| 连接形态 | 2.4G 键鼠组合接收器 / USB-C 有线 |
| 2.4G 配置端点 | `3151:5038`，`UsagePage 0xFFFF / Usage 0x0002`，Feature 65B（Report ID 0 + 64B） |
| 有线配置端点 | `3151:502F`（本机），`UsagePage 0xFFFF / Usage 0x0002`，Feature 65B |
| 只读输入流 | `UsagePage 0xFFFF / Usage 0x0001`，32B |
| 其他有线 PID | `3151:502D` / `3151:5054`（Neon75 其他修订） |

识别依据：VGN 官方 VHUB 的 `app.asar` 设备表明确记录：

```text
vendorId 12625 (0x3151), productId 20536 (0x5038)
productName: "VGN Neon75 Dongle"
customDeviceType: "rongyuan"
receiver: true
```

实际启动 VGN VHUB 后，首页显示 **VGN Neon75**，确认型号。产品字符串本身只有 `2.4G Wireless Keyboard`，不能靠字符串猜型号。

## 2. 电量与充电协议（实机验证）

发送 65 字节 Feature（Bit7 校验和覆盖 payload 0..6，存于 payload 7）：

```text
TX: 00 F7 00 00 00 00 00 00 08 00 ... 00
RX: 00 00 64 00 00 01 01 01 00 00 ... 00
```

去掉首字节 Report ID 后：

| payload 偏移 | 含义 | 实测 |
|---|---|---|
| `[1]` | 电量 0..100 | `0x64 = 100%` |
| `[3]` | 在线，`0` = 在线 | `0` |
| `[6]` | 类型：`1` 键盘 / `2` 鼠标 / `3` 双模 | `1` |
| `[9]` | 充电标志，非 0 = 充电中 | `0`（当前未充电） |

VGN VHUB 的 `getDongleData()` / `setStatus()` 均读取 `result[9]` 判断充电；这是比“100% 即充电”可靠的唯一依据。旧版 FerrisPulse 没有读该位，把满电误显示为充电中。

有线 `0x82` 电量查询回读：`00 82 64 00 1E ...`，`payload[1]=100%`，与 2.4G 读值一致；有线模式下 `IsCharging=true`（USB 供电）。

## 3. 有线设置通道与主灯读写（实机验证）

- 该 Dongle 对 `0xF6`（选择键盘）+ `0x8F`（Identify）+ `0xF7` 轮询，16 个 target 均未返回 `reply_ready=1`，**不转发设置通道**。
- VGN 官方 VHUB 在 2.4G 下只展示设备页；sharkfin 对 Neon75 的说明也明确“Writes need the cable”。
- 因此 **灯光、按键映射、宏、磁轴行程等写入必须走 USB 数据线**，对应有线 PID `3151:502D/502F/5054`。2.4G 仅支持电量、充电与在线状态。
- 本机把模式开关拨到中间 Wired 档后，枚举出 `3151:502F`，产品字符串变为 `VGN Neon75 Keyboard`，`0x8F` 返回 `0B 59`（小端 0x0B59 = 2905，Neon75 Ultra）。

### 主灯读取 `0x87`

发送（65B，校验和在 payload[7]）：

```text
TX: 00 87 00 00 00 00 00 00 78 00 ... 00
RX: 00 87 06 01 03 01 FF FF FF 00 ... 00
```

去掉 Report ID 后的字段：

| 偏移 | 含义 | 实测 |
|---|---|---|
| `[1]` | mode | `6`（星点/动态灯效） |
| `[2]` | wire speed | `1` |
| `[3]` | luminance 0..4 | `3`（75%） |
| `[4]` | flags / direction | `1` |
| `[5..7]` | RGB | `FF FF FF` |

### 主灯写入 `0x07`

8 字节命令 + 1 字节校验和（整体位于 Report ID 之后的 payload[0..8]）：

```text
TX: 00 07 01 04 03 07 FF 00 00 EA 00 ... 00
     │  │  │  │  │  │  │  │  └ checksum
     │  │  │  │  │  │  │  └ B=0
     │  │  │  │  │  │  └ G=0
     │  │  │  │  │  └ R=255
     │  │  │  │  └ flags=7（固定色）
     │  │  │  └ luminance=3
     │  │  └ wire speed=4（UI 最慢）
     │  └ mode=1（常亮）
     └ cmd
```

实测流程：

1. 写入常亮红色 → 回读 `00 87 01 00 03 07 FF 00 00`，模式 1、亮度 3、RGB 红，校验通过；
2. 写入 `mode=0` → 回读 `00 87 00 00 03 07 00 00 00`，主灯关闭；
3. 用原始 `mode=6 / wire speed=1 / lum=3 / flags=1 / FFFFFF` 恢复 → 回读与初始完全一致。

`VgnKeyboardForm` 已把该路径接入主界面：选中 VGN 键盘时显示 **“💡 VGN Neon75 灯光调节”**，支持模式、亮度 0–4、速度 0–4、颜色选择、关闭灯光，并在每次写入后自动回读校验。

## 4. 代码变更

| 文件 | 变更 |
|---|---|
| `src/Hardware/VgnKeyboardHelper.cs` | 新增：2.4G Dongle `0xF7` 电量/充电、有线 `0x8F` 型号识别、`0x87`/`0x07` 主灯读写与回读校验、65/64 字节偏移兼容、注册表缓存 |
| `src/UI/Forms/VgnKeyboardForm.cs` | 新增：VGN Neon75 灯光调节面板（模式/亮度/速度/颜色/关闭/回读） |
| `src/Hardware/DeviceManager.cs` | 注册 VGN Neon75 查询（2.45）、`VGN` 变体池、自定义设备合并/离线缓存/品牌与品类路由；新增 `BuildTrayBatterySummary` 双设备摘要 |
| `src/Hardware/ValkyrieDeviceHelper.cs` | `3151:5038` 与有线 `502D/502F/5054` 从 Valkyrie 泛化分支排除 |
| `src/UI/Forms/AddDeviceWizardForm.cs` | 向导扫描 VGN Neon75，厂商名称区分 VGN / Valkyrie / ROYUAN OEM |
| `src/UI/Forms/MainForm.Hardware.cs` | 100% 键盘不再自动显示“充电中”；键盘分支接入“VGN Neon75 灯光调节”按钮 |
| `src/UI/Forms/MainForm.UI.cs` | 键盘设置按钮在 VGN Neon75 上打开 `VgnKeyboardForm`，其他键盘仍打开 Windows 属性 |
| `tests/test_vgn_neon75_3151_5038.ps1` | 11 项回归断言（BOM/C#5、校验和、100% 样本、payload[9] 充电、64B 裸帧、非法类型、Valkyrie 隔离、型号映射、灯光帧/回读、窗体存在、双设备托盘摘要） |
| `docs/AGENT_HANDBOOK.md` | 3.13 节更新为 VGN Neon75，记录品牌/方案商区分、充电位与有线设置边界 |
| `ARCHITECTURE.md` / `CHANGELOG.md` | 模块索引与未发布变更记录 |

## 5. 本地验证

```powershell
cd <repo>
cmd.exe /c "build.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_vgn_neon75_3151_5038.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run_all_tests.ps1
```

实测 `DeviceManager.QueryAllDevices()`（有线模式）：

```text
VGN | VGN:3151:502F:KB | VGN Neon75 Ultra | batt=100 chg=True online=True | Keyboard | USB 有线
```

专项测试 11/11 PASS；全量测试 62/62 PASS。

## 6. 后续：侧灯、按键与磁轴

主灯已实机 round-trip 完成。剩余能力需要逐项实测后再接：

1. **侧灯**：`0x88` 读取、`0x08` 写入；当前读回 `00 88 06 01 01 08 FF FF FF`（mode 6 / speed 1 / lum 1 / flags 8 / 白色），与主灯共用布局。
2. **按键映射 / Fn 层 / 宏**：`0x8A` 读、`0x0A` 写（gen2），需先读 4 层按键矩阵并做单键写入 + 回读 + 恢复。
3. **磁轴行程 / RT / DKS**：Neon75 Ultra 支持磁轴，需读 `0xE5`、写 `0x65`，逐键 round-trip；不同修订的行程刻度不同，不能直接套用。
4. **休眠时间 / Win 锁定 / WASD 交换**：`0x91` / `0x89` 等配置位，需读改写并恢复。

在拿到对应端点的实测回读前，不应发布未经验证的写入路径。

## 7. 提交方式

- **GitHub**：Fork https://github.com/Ferris-echo/FerrisPulse ，分支 `feat/vgn-neon75`，提交后发 PR。
- **Gitee**：Fork https://gitee.com/Ferris-echo/ferris-pulse ，同样发 PR/Issue。
- **邮件/文本**：将本摘要、`git diff --stat` 与 patch 发送到 `2949225840@qq.com` / `2949225840zpf@gmail.com`。

建议 PR 标题：

```text
feat: 适配 VGN Neon75 电量、充电与主灯调节
```
