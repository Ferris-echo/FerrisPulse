"""
Acer OMR331（XCTECH 249A:5C2F）Linux 适配（对应原项目 AcerDeviceHelper.cs）

协议来源：上游 PR #33 / 本仓库 feat/acer-omr331-and-vk87-max-v2
  * 厂商通道：Usage Page 0xFF01 / Usage 0x10
  * 无 Feature Report，只能走 Output/Input Report
  * Linux hidraw 的报文不含 Report ID，负载为 32 字节
  * TX：10 00 00 ...（32 字节）
  * RX：10 00 01 0B "R332" 85 01 19 01 00 64 01 ... EF
        [0]=0x10 命令回显，[1]=0x00 成功，[3]=0x0B 负载长度
        [4..7]="R332" 设备 ID，[8..9]=固件（显示为 01.85）
        [12]=充电标志（0/1），[13]=电量（充电时固件会填 100 作为哨兵值）
        [31]=sum([4..30]) & 0xFF
"""

from .hidraw import HidrawError, find_vendor_channel
from .models import BatteryState

VID_WIRELESS = 0x249A
PID_DONGLE = 0x5C2F
VID_WIRED = 0x248A
PIDS_WIRED = (0x5D2E, 0x5E2E, 0x5C2F)

VENDOR_USAGE_PAGE = 0xFF01
VENDOR_USAGE = 0x0010
REPORT_LENGTH = 32
COMMAND_DEVICE_INFO = 0x10
READ_TIMEOUT = 0.32
ATTEMPTS = 4

BATTERY_SENTINEL = 100
MODEL_MAP = {
    "E546": "Acer OMW143 无线鼠标",
    "E512": "Acer OMR212 无线鼠标",
    "M120": "Acer OMW120 无线鼠标",
    "M129": "Acer OMR248 无线鼠标",
    "T220": "Acer OMW112 无线鼠标",
    "N139": "Acer OMR139 无线鼠标",
    "M117": "Acer OMW110 电竞版无线鼠标",
    "E513": "Acer OMW110 无线鼠标",
    "N820": "Acer OMR238 无线鼠标",
    "M116": "Acer OMR255 无线鼠标",
    "M041": "Acer OMW120 屏显版无线鼠标",
    "M216": "Acer OMR216 无线鼠标",
    "M226": "Acer OMR216 无线鼠标 (二代)",
    "M300": "Acer OMR255 无线鼠标",
    "M111": "Acer OMR247 无线鼠标",
    "R120": "Acer OMW120-XC10000 无线鼠标",
    "E547": "Acer OMR319 无线鼠标",
    "E5M5": "Acer OMW110 旗舰版无线鼠标",
    "E548": "Acer OMW143 无线鼠标",
    "E549": "Acer OMW143 无线鼠标",
    "R331": "Acer OMR331 标准版无线鼠标",
    "R332": "Acer OMR331 旗舰版无线鼠标",
    "ZH41": "Acer OMR332 无线鼠标",
    "LT36": "Acer OMR323 无线鼠标",
    "T524": "Acer OMR320 标准版无线鼠标",
    "T504": "Acer OMR320 电竞版无线鼠标",
    "M318": "Acer OMR318 无线鼠标",
    "MM34": "Acer OMR272 无线鼠标",
    "G821": "Acer OMR329 无线鼠标",
    "G823": "Acer OMR330 无线鼠标",
    "E558": "Acer OMW143 无线鼠标",
    "M319": "Acer OMW120 无线鼠标",
    "G349": "Acer OMR333 无线鼠标",
    "M043": "Acer OMW120 无线鼠标",
    "2349": "Acer OMW325 无线鼠标",
    "R632": "Acer OMR319 PRO 无线鼠标",
}


def model_name(dev_id):
    key = (dev_id or "").strip().upper()
    return MODEL_MAP.get(key, "Acer 无线鼠标（%s）" % (key or "未知"))


def build_query():
    """构建 0x10 设备信息查询帧（Linux hidraw：32 字节负载，无 Report ID）。"""
    frame = bytearray(REPORT_LENGTH)
    frame[0] = COMMAND_DEVICE_INFO
    return bytes(frame)


def parse_response(rx):
    """解析 32 字节应答；失败返回 None。"""
    if rx is None or len(rx) < REPORT_LENGTH:
        return None
    command_index = -1
    for i in range(3):
        if rx[i] == COMMAND_DEVICE_INFO and rx[i + 1] == 0x00:
            command_index = i
            break
    if command_index < 0:
        return None
    if rx[command_index + 3] < 11:
        return None
    dev_id = rx[command_index + 4:command_index + 8].decode("ascii", "replace")
    if any(ord(c) < 0x20 or ord(c) > 0x7E for c in dev_id):
        return None
    battery = rx[command_index + 13]
    if battery < 0 or battery > 100:
        return None
    firmware = "%02X.%02X" % (rx[command_index + 9], rx[command_index + 8])
    if firmware.startswith("0"):
        firmware = firmware.lstrip("0")
    return BatteryState(
        device_id=dev_id,
        device_name=model_name(dev_id),
        battery=battery,
        charging=rx[command_index + 12] != 0,
        firmware=firmware,
        raw=rx,
    )


class AcerOMR331:
    """Acer OMR 系列 2.4G / 有线鼠标后端。"""

    brand = "Acer"
    name = "Acer OMR331"

    def __init__(self):
        self._last_real_battery = -1

    def find(self):
        candidates = [(VID_WIRELESS, PID_DONGLE)]
        candidates += [(VID_WIRED, pid) for pid in PIDS_WIRED]
        for vid, pid in candidates:
            dev = find_vendor_channel(vid, pid, VENDOR_USAGE_PAGE, VENDOR_USAGE)
            if dev is not None:
                return dev
        return None

    def read(self, dev=None):
        own = dev is None
        if own:
            dev = self.find()
        if dev is None:
            return None
        try:
            if own:
                dev.open()
            dev.drain()
            dev.write(build_query())
            for _ in range(ATTEMPTS):
                rx = dev.read_report(READ_TIMEOUT, size=64)
                state = parse_response(rx)
                if state is None:
                    continue
                if not state.charging:
                    self._last_real_battery = state.battery
                else:
                    state.displayed_battery = self._display_battery(state)
                return state
        except (HidrawError, OSError):
            return None
        finally:
            if own:
                dev.close()
        return None

    def _display_battery(self, state):
        """充电时固件把电量填成 100 哨兵值，退回最近一次非充电实测值。"""
        if state.charging and state.battery >= BATTERY_SENTINEL and 0 <= self._last_real_battery < BATTERY_SENTINEL:
            return self._last_real_battery
        return state.battery
