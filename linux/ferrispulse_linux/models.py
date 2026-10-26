"""数据模型（对应原项目 Models/MouseBatteryInfo.cs）。"""

from dataclasses import dataclass, field


@dataclass
class BatteryState:
    device_id: str = ""
    device_name: str = "Acer 2.4G 无线鼠标"
    battery: int = -1
    charging: bool = False
    firmware: str = ""
    raw: bytes = b""
    # 充电时设备回报 100 哨兵值，displayed_battery 用来保存“最近一次真实电量”
    displayed_battery: int = field(default=-1)

    @property
    def percent(self):
        if self.displayed_battery is not None and self.displayed_battery >= 0:
            return self.displayed_battery
        return self.battery

    @property
    def level(self):
        if self.percent < 0:
            return "unknown"
        if self.percent <= 15:
            return "critical"
        if self.percent <= 40:
            return "low"
        return "ok"
