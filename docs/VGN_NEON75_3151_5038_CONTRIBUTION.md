# VGN Neon75 3151:5038 电量/充电适配与贡献摘要

> 实测日期：2026-10-24
>
> 实测环境：Windows / FerrisPulse 源码树
>
> 结论：`3151:5038` 是 **VGN Neon75 的 2.4G Dongle**，不是 ROYUAN 品牌键盘；ROYUAN 只是方案商。旧版 FerrisPulse 将其误识别为 Valkyrie，并把真实 100% 电量读成 0%。

## 1. 品牌与设备识别

| 项目 | 实测值 |
|---|---|
| USB VID:PID | `3151:5038` |
| 用户侧品牌/型号 | **VGN Neon75** |
| 方案商 | ROYUAN（融渊） |
| 产品字符串 | `2.4G Wireless Keyboard`（通用名） |
| 连接形态 | 2.4G 键鼠组合接收器（键盘在线，鼠标未配对） |
| 2.4G 配置端点 | `UsagePage 0xFFFF / Usage 0x0002`，Feature 65B（Report ID 0 + 64B） |
| 只读输入流 | `UsagePage 0xFFFF / Usage 0x0001`，32B |
| 有线端点 | `3151:502D` / `3151:502F` / `3151:5054`（Neon75 各修订） |

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

## 3. 2.4G 设置通道边界

- 该 Dongle 对 `0xF6`（选择键盘）+ `0x8F`（Identify）+ `0xF7` 轮询，16 个 target 均未返回 `reply_ready=1`，**不转发设置通道**。
- VGN 官方 VHUB 在 2.4G 下只展示设备页；sharkfin 对 Neon75 的说明也明确“Writes need the cable”。
- 因此 **灯光、按键映射、宏、磁轴行程等写入必须走 USB 数据线**，对应有线 PID `3151:502D/502F/5054`。2.4G 仅支持电量、充电与在线状态，不能为了“可调”盲写 `0x07/0x09/0x0A`。

## 4. 代码变更

| 文件 | 变更 |
|---|---|
| `src/Hardware/VgnKeyboardHelper.cs` | 新增：2.4G Dongle `0xF7` 电量/充电解析、有线 `0x8F` 型号识别、65/64 字节偏移兼容、注册表缓存 |
| `src/Hardware/DeviceManager.cs` | 注册 VGN Neon75 查询（2.45）、`VGN` 变体池、自定义设备合并/离线缓存/品牌与品类路由 |
| `src/Hardware/ValkyrieDeviceHelper.cs` | `3151:5038` 与有线 `502D/502F/5054` 从 Valkyrie 泛化分支排除 |
| `src/UI/Forms/AddDeviceWizardForm.cs` | 向导扫描 VGN Neon75，厂商名称区分 VGN / Valkyrie / ROYUAN OEM |
| `src/UI/Forms/MainForm.Hardware.cs` | 100% 键盘不再自动显示“充电中”；仅真实充电位、USB 直连或 NuPhy 既有浮充约定显示充电 |
| `tests/test_vgn_neon75_3151_5038.ps1` | 8 项回归断言（BOM/C#5、校验和、100% 样本、payload[9] 充电、64B 裸帧、非法类型、Valkyrie 隔离、型号映射） |
| `docs/AGENT_HANDBOOK.md` | 3.13 节更新为 VGN Neon75，记录品牌/方案商区分、充电位与有线设置边界 |
| `ARCHITECTURE.md` / `CHANGELOG.md` | 模块索引与未发布变更记录 |

## 5. 本地验证

```powershell
cd <repo>
cmd.exe /c "build.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File tests/test_vgn_neon75_3151_5038.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run_all_tests.ps1
```

实测 `DeviceManager.QueryAllDevices()`：

```text
VGN | VGN:3151:5038:KB | VGN Neon75 | batt=100 chg=False online=True | Keyboard | 2.4G 无线
```

## 6. 后续：键盘调节（需 USB 数据线）

当前提交只覆盖 2.4G 可验证部分。要让 FerrisPulse 支持 Neon75 的灯光/按键/磁轴调节，需要：

1. 用 USB-C **数据线**直接连接键盘（不是只插 2.4G 接收器）。
2. 确认键盘枚举为 `3151:502D/502F/5054` 之一，并读取 `0x8F` 返回的 device id（2781/2859/2904/2905/3237）确定具体修订。
3. 基于 ROYUAN gen2 协议实测 `0x87` 读灯光、`0x07` 写主灯、`0x08` 写侧灯、`0x11` 写休眠；按键/磁轴写入需逐项 round-trip，确认后才接入。

在拿到有线端点实测前，不应发布任何未经验证的键盘写入路径。

## 7. 提交方式

- **GitHub**：Fork https://github.com/Ferris-echo/FerrisPulse ，分支 `feat/vgn-neon75`，提交后发 PR。
- **Gitee**：Fork https://gitee.com/Ferris-echo/ferris-pulse ，同样发 PR/Issue。
- **邮件/文本**：将本摘要、`git diff --stat` 与 patch 发送到 `2949225840@qq.com` / `2949225840zpf@gmail.com`。

建议 PR 标题：

```text
feat: 适配 VGN Neon75 3151:5038 电量与充电状态
```
