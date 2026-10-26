# FerrisPulse Linux · 灵脉（Linux 版）

> 原项目：[Ferris-echo/FerrisPulse](https://github.com/Ferris-echo/FerrisPulse)（Windows 10/11，.NET Framework 4.0 + Win32）
>
> 本目录是**独立实现的 Linux 发行版**：沿用原项目的分层思路（Hardware → Devices → UI），
> 用 Python 3 + GTK3 + AppIndicator 重写，不依赖 Wine / .NET / Windows API。
> 首个适配设备：**Acer OMR331 旗舰版（XCTECH 2.4G 接收器 249A:5C2F）**，
> 协议来自上游 PR #33（作者：Qingyushan）。

## 支持的设备

| 设备 | VID:PID | 通道 | 状态 |
| --- | --- | --- | --- |
| Acer OMR331 旗舰版 / 标准版（R331/R332） | `249a:5c2f` | Usage Page 0xFF01 / Usage 0x10，32B Output/Input | ✅ 电量、充电、固件、设备 ID |
| Acer 同方案有线变体 | `248a:5d2e` / `248a:5e2e` / `248a:5c2f` | 同上 | ✅ 协议相同，未实机验证 |

新增设备：在 `ferrispulse_linux/` 下实现一个带 `find()` / `read()` 的后端，加入
`devices.py` 的 `BACKENDS` 列表即可。

## 架构（对应原项目）

| 原项目（C#） | Linux 版（Python） | 职责 |
| --- | --- | --- |
| `Hardware/HidTransport.cs` | `ferrispulse_linux/hidraw.py` | 枚举 hidraw、打开厂商通道、Output/Input Report |
| `Hardware/AcerDeviceHelper.cs` | `ferrispulse_linux/acer.py` | Acer 0x10 查询帧、字段解析、充电哨兵值 |
| `Hardware/DeviceManager.cs` | `ferrispulse_linux/devices.py` | 后端注册与轮询 |
| `Models/MouseBatteryInfo.cs` | `ferrispulse_linux/models.py` | 电量状态模型 |
| `UI/MainForm.cs` + 托盘 | `ferrispulse_linux/app.py` | GTK3 窗口 + AppIndicator 托盘 |
| `tools/*.ps1` / `build.bat` | `scripts/install.sh` | 安装、udev、桌面入口 |

## 协议（Acer OMR331 / XCTECH）

Linux hidraw 的报文**不含 Report ID**，因此下面都是 32 字节负载；Windows HID API 的
33 字节缓冲区 = 1 字节 Report ID(0x00) + 这 32 字节。

```
TX: 10 00 00 00 ... (32B, [0]=0x10 设备信息查询)
RX: 10 00 01 0B "R332" 85 01 19 01 00 64 01 ... EF
    [0]     0x10 命令回显
    [1]     0x00 成功
    [3]     0x0B 负载长度
    [4..7]  设备 ID（R332 = OMR331 旗舰版）
    [8..9]  固件（显示为 01.85）
    [12]    充电标志（0/1）
    [13]    电量百分比
    [31]    sum([4..30]) & 0xFF
```

注意事项：

* 充电时固件把 `[13]` 填成 `100` 作为“已接电源”的哨兵值，不代表真实电量；
  本实现会退回最近一次非充电实测值（与原项目 `ResolveDisplayedBattery` 一致）。
* 该接收器**没有 Feature Report**，只能走 Output/Input Report；不要尝试
  `HIDIOCSFEATURE`（会报 `Broken pipe`）。

## 安装

```bash
# 1) 先给 hidraw 授权（安装 udev 规则，需要 root）
sudo linux/scripts/install.sh

# 2) 验证命令行
ferrispulse-linux --cli
# Acer OMR331 旗舰版无线鼠标（R332） 电量 100%  电池供电  固件 1.85

# 3) 启动托盘
ferrispulse-linux            # 显示窗口 + 托盘
ferrispulse-linux --hidden   # 只显示托盘
```

如果托盘图标不出现：启用 Ubuntu 自带的 AppIndicator 扩展：

```bash
gnome-extensions enable ubuntu-appindicators@ubuntu.com
```

## 使用

| 命令 | 说明 |
| --- | --- |
| `ferrispulse-linux --cli` | 打印一次电量（适合脚本/waybar） |
| `ferrispulse-linux --json` | JSON 输出，字段：`device_id/device_name/battery/charging/firmware` |
| `ferrispulse-linux --watch 5` | 每 5 秒打印一次 |
| `ferrispulse-linux` | GTK 窗口 + 托盘（每 5 秒轮询） |

托盘菜单：显示主窗口 / 立即刷新 / 退出；关闭窗口只是隐藏。

## 权限

非 root 读 hidraw 依赖 udev 的 `uaccess`：

```udev
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="249a", ATTRS{idProduct}=="5c2f", TAG+="uaccess", MODE="0660", GROUP="plugdev"
```

安装后重新插拔接收器，或执行 `sudo udevadm trigger --subsystem-match=hidraw`。

> 默认用 `plugdev` 组（Ubuntu 桌面用户默认已在其中）；如果你的发行版没有 plugdev，
> 可改成 `input` 并把用户加入该组。

## 与原版的差异（当前范围）

| 功能 | 状态 |
| --- | --- |
| 电量 / 充电 / 固件 / 设备型号 | ✅ |
| 托盘图标 + 百分比标签 | ✅ |
| 低电量通知（≤15%） | ✅ |
| 8000Hz 回报率调度、DPI、OSD、多设备拖拽 | ❌（原版 Windows 专属，Linux 侧后续逐项移植） |
| Valkyrie / Logitech / Razer 等其它品牌 | ❌（可按相同模式扩展） |

## 开发与测试

```bash
cd linux
python3 -m pytest -q tests/           # 离线解析测试（无需设备）
python3 -m ferrispulse_linux --cli    # 需要接收器 + udev 权限
```

## 许可

与原项目一致：GPL v3.0。协议来自上游 PR #33，感谢原作者 Ferris-echo 与 PR 作者 Qingyushan。
