#!/usr/bin/env bash
# 安装 FerrisPulse Linux 到本机（需要 root）
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LINUX_DIR="$(dirname "$HERE")"
PREFIX="${PREFIX:-/usr/local}"
APP_DIR="$PREFIX/lib/ferrispulse-linux"

echo "==> 安装 Python 包到 $APP_DIR"
install -d "$APP_DIR"
rm -rf "$APP_DIR/ferrispulse_linux"
cp -a "$LINUX_DIR/ferrispulse_linux" "$APP_DIR/"
install -Dm755 "$LINUX_DIR/bin/ferrispulse-linux" "$PREFIX/bin/ferrispulse-linux"

echo "==> 安装 udev 规则（hidraw 免 root 访问）"
install -Dm644 "$LINUX_DIR/udev/99-ferrispulse-linux.rules" /etc/udev/rules.d/99-ferrispulse-linux.rules
udevadm control --reload-rules
udevadm trigger --subsystem-match=hidraw || true

echo "==> 安装桌面入口与图标"
install -Dm644 "$LINUX_DIR/data/ferrispulse-linux.desktop" /usr/share/applications/ferrispulse-linux.desktop
install -Dm644 "$LINUX_DIR/data/ferrispulse-linux.svg" /usr/share/icons/hicolor/scalable/apps/ferrispulse-linux.svg
gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true

echo "==> 完成。运行："
echo "    ferrispulse-linux --cli     # 命令行看电量"
echo "    ferrispulse-linux           # GUI + 托盘"
echo "提示：若托盘图标不显示，请确认 GNOME 扩展 ubuntu-appindicators@ubuntu.com 已启用。"
