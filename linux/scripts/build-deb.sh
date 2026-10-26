#!/usr/bin/env bash
# 构建 ferrispulse-linux 的 .deb（不依赖 debhelper，直接 dpkg-deb）
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LINUX_DIR="$(dirname "$HERE")"
VERSION="$(python3 -c "import sys; sys.path.insert(0,'$LINUX_DIR'); from ferrispulse_linux import __version__; print(__version__)")"
ARCH="all"
find "$LINUX_DIR/ferrispulse_linux" -name __pycache__ -type d -prune -exec rm -rf {} + 2>/dev/null || true
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
PKG="$STAGE/ferrispulse-linux"

install -d "$PKG/DEBIAN" \
  "$PKG/usr/local/bin" \
  "$PKG/usr/local/lib/ferrispulse-linux" \
  "$PKG/usr/share/applications" \
  "$PKG/usr/share/icons/hicolor/scalable/apps" \
  "$PKG/etc/udev/rules.d"

cp -a "$LINUX_DIR/ferrispulse_linux" "$PKG/usr/local/lib/ferrispulse-linux/"
install -m755 "$LINUX_DIR/bin/ferrispulse-linux" "$PKG/usr/local/bin/ferrispulse-linux"
install -m644 "$LINUX_DIR/data/ferrispulse-linux.desktop" "$PKG/usr/share/applications/"
install -m644 "$LINUX_DIR/data/ferrispulse-linux.svg" "$PKG/usr/share/icons/hicolor/scalable/apps/"
install -m644 "$LINUX_DIR/udev/99-ferrispulse-linux.rules" "$PKG/etc/udev/rules.d/"

cat > "$PKG/DEBIAN/control" <<CONTROL
Package: ferrispulse-linux
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${ARCH}
Maintainer: Qingyushan <qingyushan@users.noreply.github.com>
Depends: python3 (>= 3.10), python3-gi, gir1.2-gtk-3.0, gir1.2-ayatanaappindicator3-0.1, gir1.2-notify-0.7
Description: FerrisPulse Linux (灵脉) — 2.4G 鼠标电量托盘
 Linux 原生移植版（Python 3 + GTK3 + AppIndicator），
 首个适配设备为 Acer OMR331 / XCTECH 249A:5C2F。
CONTROL

cat > "$PKG/DEBIAN/postinst" <<'POST'
#!/bin/sh
set -e
udevadm control --reload-rules || true
udevadm trigger --subsystem-match=hidraw || true
gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
exit 0
POST
chmod 755 "$PKG/DEBIAN/postinst"

install -d "$LINUX_DIR/dist"
OUT="$LINUX_DIR/dist/ferrispulse-linux_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "$PKG" "$OUT"
echo "==> 生成：$OUT"
