"""设备注册表（对应原项目 DeviceManager / 各 *DeviceHelper）。

Linux 版先覆盖 Acer OMR331 / XCTECH 249A:5C2F；新增设备时只需实现
一个带有 find()/read() 的后端，并加入 BACKENDS。
"""

from .acer import AcerOMR331

BACKENDS = [
    AcerOMR331(),
]


def read_state():
    """轮询所有后端，返回第一个成功读取到的 BatteryState；都失败返回 None。"""
    for backend in BACKENDS:
        try:
            state = backend.read()
        except Exception:
            state = None
        if state is not None:
            return state, backend
    return None, None
