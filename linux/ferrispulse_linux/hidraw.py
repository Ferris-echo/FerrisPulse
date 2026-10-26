"""
hidraw 访问层（Linux 版对应原项目的 HidTransport.cs）

职责：
  * 通过 /sys/class/hidraw 找到目标 VID/PID 的厂商通道；
  * 打开 /dev/hidrawN，发送 Output Report、读取 Input Report；
  * 不依赖 root：安装 udev 规则后由 logind 的 uaccess ACL 授权当前登录用户。

与 Windows 的差异：
  * Windows HID API 的 Report 缓冲区首字节是 Report ID（无编号设备也补 0x00）；
  * Linux hidraw 的 read()/write() 缓冲区**不含** Report ID，因此本层看到的
    32 字节就是厂商通道的 32 字节负载（对应 Windows 的 33 字节）。
"""

import glob
import os
import re
import select
import time

HIDRAW_ROOT = "/sys/class/hidraw"
HID_ID_RE = re.compile(
    r"HID_ID=[0-9A-Fa-f]{4}:([0-9A-Fa-f]{4,8}):([0-9A-Fa-f]{4,8})"
)


class HidrawError(RuntimeError):
    pass


class HidrawDevice:
    """一个已打开的 hidraw 设备。"""

    def __init__(self, path, vid, pid, name):
        self.path = path
        self.vid = vid
        self.pid = pid
        self.name = name
        self._fd = -1

    def open(self):
        try:
            self._fd = os.open(self.path, os.O_RDWR | os.O_NONBLOCK)
        except PermissionError as exc:
            raise HidrawError(
                "没有权限打开 %s；请先安装 udev 规则（scripts/install.sh）"
                "或把当前用户加入 input 组后重新插拔接收器" % self.path
            ) from exc
        except OSError as exc:
            raise HidrawError("打开 %s 失败：%s" % (self.path, exc)) from exc
        return self

    def close(self):
        if self._fd >= 0:
            try:
                os.close(self._fd)
            finally:
                self._fd = -1

    def __enter__(self):
        return self.open()

    def __exit__(self, *exc):
        self.close()

    def drain(self, rounds=8, timeout=0.02):
        """丢弃设备里残留的输入报文，避免把上一次的应答当成本次结果。"""
        for _ in range(rounds):
            r, _, _ = select.select([self._fd], [], [], timeout)
            if not r:
                return
            try:
                os.read(self._fd, 64)
            except OSError:
                return

    def write(self, payload):
        if self._fd < 0:
            raise HidrawError("设备未打开")
        os.write(self._fd, payload)

    def read_report(self, timeout=0.32, size=64):
        r, _, _ = select.select([self._fd], [], [], timeout)
        if not r:
            return None
        try:
            return os.read(self._fd, size)
        except OSError:
            return None


def _read_text(path):
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as fh:
            return fh.read()
    except OSError:
        return ""


def _read_bytes(path):
    try:
        with open(path, "rb") as fh:
            return fh.read()
    except OSError:
        return b""


def enumerate_hidraw(vid, pid):
    """返回匹配 VID/PID 的 hidraw 设备列表（含厂商页判断所需的信息）。"""
    devices = []
    for path in sorted(glob.glob(HIDRAW_ROOT + "/hidraw*")):
        name = os.path.basename(path)
        uevent = _read_text(os.path.join(path, "device", "uevent"))
        match = HID_ID_RE.search(uevent)
        if not match:
            continue
        d_vid, d_pid = int(match.group(1), 16), int(match.group(2), 16)
        if d_vid != vid or d_pid != pid:
            continue
        descriptor = _read_bytes(os.path.join(path, "device", "report_descriptor"))
        devices.append(
            {
                "path": "/dev/" + name,
                "vid": d_vid,
                "pid": d_pid,
                "descriptor": descriptor,
                "hid_name": _read_text(os.path.join(path, "device", "name")).strip(),
            }
        )
    return devices


def find_vendor_channel(vid, pid, usage_page=0xFF01, usage=0x10):
    """
    在匹配 VID/PID 的接口中找出厂商通道。

    判据：Report Descriptor 中同时出现 Usage Page(0xFF01) 与 Usage(0x10)，
    且该接口的 Input/Output 报文长度够存放协议帧（33 字节 = 32 字节负载 + Report ID）。
    """
    page_le = bytes([0x06, usage_page & 0xFF, (usage_page >> 8) & 0xFF])  # 06 01 FF
    usage_marker = bytes([0x09, usage & 0xFF])                            # 09 10
    for dev in enumerate_hidraw(vid, pid):
        desc = dev["descriptor"]
        if page_le not in desc or usage_marker not in desc:
            continue
        return HidrawDevice(dev["path"], vid, pid, dev["hid_name"] or "HID")
    return None


def sleep_ms(ms):
    time.sleep(ms / 1000.0)
