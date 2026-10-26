"""CLI 入口：用于无 GUI 环境下验证设备协议。"""

import argparse
import json
import sys
import time

from .devices import read_state


def _print_human(state, backend):
    charging = "充电中" if state.charging else "电池供电"
    print(
        "%s（%s） 电量 %d%%  %s  固件 %s"
        % (state.device_name, state.device_id or "?", state.percent, charging, state.firmware or "?")
    )


def main(argv=None):
    parser = argparse.ArgumentParser(prog="ferrispulse-linux", description="FerrisPulse Linux（灵脉）")
    parser.add_argument("--cli", action="store_true", help="只打印电量，不启动 GUI")
    parser.add_argument("--json", action="store_true", help="CLI 输出 JSON")
    parser.add_argument("--watch", type=float, default=0, help="每 N 秒轮询一次（0 = 只读一次）")
    parser.add_argument("--hidden", action="store_true", help="GUI 启动时只显示托盘图标")
    args, unknown = parser.parse_known_args(argv)

    if args.cli or args.json:
        while True:
            state, backend = read_state()
            if state is None:
                if args.json:
                    print(json.dumps({"ok": False, "error": "device not found"}))
                else:
                    print("未找到受支持的设备（检查接收器是否插好、udev 规则是否生效）")
                if not args.watch:
                    return 1
            else:
                if args.json:
                    print(
                        json.dumps(
                            {
                                "ok": True,
                                "device_id": state.device_id,
                                "device_name": state.device_name,
                                "battery": state.percent,
                                "raw_battery": state.battery,
                                "charging": state.charging,
                                "firmware": state.firmware,
                            },
                            ensure_ascii=False,
                        )
                    )
                else:
                    _print_human(state, backend)
                if not args.watch:
                    return 0
            time.sleep(args.watch or 5)
    return None
