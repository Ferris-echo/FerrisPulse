# FerrisPulse 外设适配贡献摘要

## 一、适配了什么

**皂品 Z2 游戏鼠标（Zaopin Z2）** —— 带精准电池电量上报。

| 项 | 值 |
| :--- | :--- |
| 品牌/型号 | 皂品 Z2（Zaopin Z2） |
| VID | `0x3554`（CompX 方案） |
| PID 变体 | `0xF527` = **4K 底座式接收器**、`0xF526` = **Nano 1K 直插接收器** |
| 接收器芯片 | NORDIC nRF52810（4K 底座式，带线带指示灯）；鼠标主控 NORDIC nRF52840 |
| 设备标识 | `cid=7 mid=2` |
| 传感器 | PixArt PAW3395（原相 3395，最高 26000 DPI；1K/4K 版均用此传感器，8K Max 版才用 PAW3950） |
| 电池 | 500mAh 锂电池（可充电） |
| 连接 | 2.4G / 蓝牙 5.1 / 有线 Type-C 三模 |
| 回报率 | 125-250-500-1000-2000-4000Hz（4K，需 4K 底座接收器） |

**实测电池读取成功**（2.4G 模式）：
- `PID F527`（4K 底座接收器）→ 电量 100%、满电；
- `PID F526`（Nano 1K 接收器）→ 电量 95%、充电中。

## 二、修改了哪些文件

- **修改** `src/Hardware/VgnDeviceHelper.cs` — 皂品 Z2 识别（`cid=7/mid=2` + `pid 0xF526/0xF527`）、品牌动态派生（Zaopin）、DPI 6 档枚举、**DPI 档位切换（`SetVgnDpiStage`）、自定义 DPI（`SetVgnDpiValue`）、回报率切换（`SetVgnPollingRate`，上限 4K）、实体键档位同步读取（`FastQueryVgnDpiStage`）与批量档位写入（`UpdateVgnHardwareStages`）**；
- **修改** `src/UI/Forms/MainForm.Hardware.cs` — `SetDpiFromUI`/`SetPollingRateFromUI` 新增 Zaopin/VGN 分发分支，主页性能卡片纳入 `isZaopinMouse` 分支（显示 DPI 与回报率档位按钮），回报率档位按 `MaxPollingRate`（皂品 4000Hz）裁剪杜绝 8K，并接入**实体 DPI 键同步监听**（`DpiMonitorWorker` 周期读 `0x04` 档位，变化即高亮主页按钮 + OSD）；
- **修改** `src/UI/Forms/MainForm.UI.cs` — `OpenDpiManagementDialog` 白名单纳入 Zaopin/皂品/VGN，新增皂品 DPI 能力配置（PAW3395，50-26000，50 步进），硬件持久化接入 `UpdateVgnHardwareStages`；
- **修改** `src/UI/Forms/DpiManagementDialog.cs` — 新增 `SupportsXy` 开关，皂品隐藏 X/Y 轴栏（硬件协议无双轴）；
- **修改** `src/Hardware/DeviceManager.cs` — `GetDeviceIdVariants` 纳入 `Zaopin` 前缀；
- **修改** `src/Models/MouseBatteryInfo.cs` — 增加 Zaopin（皂品）品牌配色与「皂品 Z2」友好命名；
- **修改** `ARCHITECTURE.md`、`CHANGELOG.md` — 同步文档；
- **新增** `tests/test_zaopin_z2.ps1` — 6 项自动化回归测试。

## 三、报文与硬件细节（CompX 方案 FF02 通道）

- **通信端点**：UsagePage `0xFF02` / Usage `0x0002`，Input/Output 各 17 字节；
- **报告格式**：Report ID `0x08` + 16 字节载荷，末尾校验字节 `= 77 - sum(前15字节)`；
- **命令序列（电量）**：
  1. `Command 1`（0x01，加密握手）→ 响应 `[10]=cid(7) [11]=mid(2)`；
  2. `Command 2`（0x02，PC 驱动状态，参数 `{1}`）；
  3. `Command 3`（0x03，在线探测）→ 响应 `[6]=1` 表示在线；
  4. `Command 4`（0x04，电池查询）→ 响应 `[6]=电量(0-100) [7]=充电状态(1=充电)`。

实测响应示例：
```
F526: 08 04 00 00 00 02 5F 01 10 69 ...  → 电量 0x5F=95，充电中
F527: 08 04 00 00 00 02 64 00 10 2A ...  → 电量 0x64=100，未充电
```

### 配置读写协议（抓包实测，逐字节对齐官方驱动）

**帧结构**（17 字节报告，Report ID `0x08`）：

```
08 <cmd> 00 00 <addr> <len> <payload…> <pad 0x00…> <cksum>
```

- `cksum = (0x55 - Σ(bytes[1..15])) & 0xFF`，报告末字节 = `cksum - 0x08`
- `cmd` 分两类：
  - **`0x08` = 数据落盘写 / 读**：DPI 档位值写入、读取各配置值
  - **`0x07` = 控制写 / 预览**：切档位、回报率切换、拖拽预览（预览不持久化）

**地址表**：

| 地址 | 用途 | cmd | payload |
| :--- | :--- | :--- | :--- |
| `0x00` | 回报率 | `0x07` 写 / `0x08` 读 | `[code, 0x55-code]` |
| `0x02` | DPI 档位数 | `0x08` 读 | — |
| `0x04` | 当前档位 | `0x07` 写 / `0x08` 读 | `[index, 0x55-index]` |
| `0x0c + 4i` | 第 i 档 DPI 值 | `0x08` 写 / `0x08` 读 | `[enc, enc, 0x00, ck]` |

**编码**：

- **DPI**：`enc = dpi/50 - 1`，落盘 payload `[enc, enc, 0x00, ck]`，`ck = 0x55 - 2*enc`
- **回报率**：`125→8 / 250→4 / 500→2 / 1000→1 / 2000→16 / 4000→32`（**上限 4000Hz，无 8K**）

**实测值**：

- DPI 默认 6 档：`400 / 1000 / 1600 / 2400 / 3200 / 6400`（enc `07/13/1F/2F/3F/7F`）
- 自定义 DPI 写 `1000` 读回验证成功；回报率 `1000↔2000` 切换验证成功
- 早期误将 DPI 落盘用 `0x07`（实际只做预览、不持久化），已改为 `0x08`

### 实体 DPI 键同步（事件驱动）

- **机制**：按鼠标实体 DPI 键时，FF02 通道主动上报事件报告 `08 0A 00 00 00 0A 01 00 ...`（Report ID `0x08`，命令 `0x0A`，事件码 `0x01`），软件据此读地址 `0x04` 同步当前档位；
- **实现**：`FastQueryVgnDpiStage()` 读 `0x04`（value + 奇偶校验 `0x55-value`），主界面 `DpiMonitorWorker` 后台循环检测档位变化，两帧防抖确认后高亮主页档位按钮并弹出 OSD；
- **实测**：按实体 DPI 键连续切档，档位 index 依次 1→2→3→4→5→0 循环，每次按键均触发事件且软件同步成功。

## 四、贡献方式

- **方式 A（GitHub PR/Issue）**：https://github.com/Ferris-echo/FerrisPulse
- **方式 B（Gitee PR/Issue）**：https://gitee.com/Ferris-echo/ferris-pulse
- **方式 C（邮件/patch）**：`git diff > zaopin_z2_adaptation.patch` 随邮件发送。
