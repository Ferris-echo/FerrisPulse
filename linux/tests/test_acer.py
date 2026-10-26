"""离线解析测试：用 PR #33 抓到的真实帧验证字段偏移。"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from ferrispulse_linux.acer import parse_response, model_name  # noqa: E402

# 2026-10-26 本机实测帧（Acer OMR331 旗舰版，电量 100%，未充电）
FRAME = bytes.fromhex(
    "10 00 01 0b 52 33 33 32 85 01 19 01 00 64 01 00 "
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 ef"
)


def test_parse_captured_frame():
    state = parse_response(FRAME)
    assert state is not None
    assert state.device_id == "R332"
    assert state.device_name == "Acer OMR331 旗舰版无线鼠标"
    assert state.battery == 100
    assert state.charging is False
    assert state.firmware == "1.85"
    assert state.percent == 100


def test_checksum_is_valid_for_captured_frame():
    assert sum(FRAME[4:31]) & 0xFF == FRAME[31]


def test_model_mapping_fallback():
    assert model_name("R332") == "Acer OMR331 旗舰版无线鼠标"
    assert model_name("ZZZZ").startswith("Acer 无线鼠标")


def test_reject_short_frame():
    assert parse_response(FRAME[:20]) is None


if __name__ == "__main__":
    test_parse_captured_frame()
    test_checksum_is_valid_for_captured_frame()
    test_model_mapping_fallback()
    test_reject_short_frame()
    print("all Acer parser tests passed")
