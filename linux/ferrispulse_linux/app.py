"""
GTK3 主界面与托盘（对应原项目 UI/MainForm + 托盘图标）。

设计取舍：
  * 原版是 Win11 Mica + 任务栏；Linux 侧用 AppIndicator（Ubuntu 自带
    ubuntu-appindicators 扩展）+ GTK3 窗口，不依赖 WebKit/Electron；
  * 托盘标签直接显示百分比，窗口只做详情与“立即刷新”；
  * 关闭窗口只隐藏，不退出，符合原版托盘常驻的行为。
"""

import os
import time

import gi

gi.require_version("Gtk", "3.0")
gi.require_version("AyatanaAppIndicator3", "0.1")
from gi.repository import AyatanaAppIndicator3, Gdk, Gio, GLib, Gtk  # noqa: E402

try:
    gi.require_version("Notify", "0.7")
    from gi.repository import Notify  # noqa: E402
except (ValueError, ImportError):  # pragma: no cover
    Notify = None

from .devices import read_state  # noqa: E402

APP_ID = "org.ferrispulse.linux"
CACHE_DIR = os.path.join(GLib.get_user_cache_dir(), "ferrispulse-linux")

CSS = b"""
window, .fp-root {
  background: #171821;
}
.fp-card {
  background: #21222f;
  border-radius: 18px;
  padding: 22px;
}
.fp-title {
  color: #f2f4ff;
  font-size: 20px;
  font-weight: 700;
}
.fp-sub {
  color: #9aa0b8;
  font-size: 12px;
}
.fp-big {
  color: #f2f4ff;
  font-size: 64px;
  font-weight: 800;
}
.fp-state {
  color: #8b93ad;
  font-size: 13px;
}
.fp-charging {
  color: #6ee7b7;
}
.fp-low {
  color: #fbbf24;
}
.fp-critical {
  color: #f87171;
}
.fp-btn {
  background: #2c2e40;
  color: #e8eaff;
  border-radius: 10px;
  padding: 7px 14px;
}
.fp-btn:hover {
  background: #3a3d55;
}
"""


def _color_for(percent, charging):
    if charging:
        return (0.43, 0.91, 0.72)
    if percent <= 15:
        return (0.98, 0.44, 0.44)
    if percent <= 40:
        return (0.98, 0.75, 0.14)
    return (0.55, 0.85, 0.62)


def draw_battery_icon(percent, charging):
    """生成托盘图标 PNG，返回绝对路径。"""
    import cairo

    os.makedirs(CACHE_DIR, exist_ok=True)
    path = os.path.join(
        CACHE_DIR, "battery-%s-%s.png" % (percent if percent >= 0 else "na", 1 if charging else 0)
    )
    size = 64
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    ctx = cairo.Context(surface)
    ctx.set_source_rgba(0, 0, 0, 0)
    ctx.paint()

    # 电池外壳
    x, y, w, h = 10, 18, 40, 28
    ctx.set_line_width(4)
    ctx.set_source_rgb(0.85, 0.87, 0.95)
    ctx.rectangle(x, y, w, h)
    ctx.stroke()
    ctx.rectangle(x + w + 2, y + 9, 5, 10)
    ctx.fill()

    if percent >= 0:
        fill_w = max(2, int((w - 8) * min(100, percent) / 100.0))
        r, g, b = _color_for(percent, charging)
        ctx.set_source_rgb(r, g, b)
        ctx.rectangle(x + 4, y + 4, fill_w, h - 8)
        ctx.fill()

    if charging:
        # 简单闪电
        ctx.set_source_rgb(1, 0.95, 0.55)
        ctx.move_to(34, 14)
        ctx.line_to(24, 36)
        ctx.line_to(33, 36)
        ctx.line_to(27, 50)
        ctx.line_to(44, 28)
        ctx.line_to(35, 28)
        ctx.close_path()
        ctx.fill()

    surface.write_to_png(path)
    return path


class MainWindow(Gtk.ApplicationWindow):
    def __init__(self, app):
        super().__init__(application=app, title="FerrisPulse Linux · 灵脉")
        self.set_default_size(340, 300)
        self.set_border_width(14)
        self.set_icon_name("battery-good-symbolic")

        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        root.get_style_context().add_class("fp-root")
        self.add(root)

        card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        card.get_style_context().add_class("fp-card")
        root.pack_start(card, True, True, 0)

        self.title_label = Gtk.Label(label="正在检测设备…", xalign=0)
        self.title_label.get_style_context().add_class("fp-title")
        card.pack_start(self.title_label, False, False, 0)

        self.big_label = Gtk.Label(label="—", xalign=0)
        self.big_label.get_style_context().add_class("fp-big")
        card.pack_start(self.big_label, False, False, 0)

        self.state_label = Gtk.Label(label="", xalign=0)
        self.state_label.get_style_context().add_class("fp-state")
        card.pack_start(self.state_label, False, False, 0)

        self.progress = Gtk.ProgressBar()
        self.progress.set_margin_top(10)
        card.pack_start(self.progress, False, False, 0)

        self.detail_label = Gtk.Label(label="", xalign=0)
        self.detail_label.get_style_context().add_class("fp-sub")
        card.pack_start(self.detail_label, False, False, 0)

        btn_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        btn_box.set_margin_top(8)
        refresh = Gtk.Button(label="立即刷新")
        refresh.get_style_context().add_class("fp-btn")
        refresh.connect("clicked", lambda *_: self.refresh())
        btn_box.pack_start(refresh, False, False, 0)
        close = Gtk.Button(label="隐藏到托盘")
        close.get_style_context().add_class("fp-btn")
        close.connect("clicked", lambda *_: self.hide())
        btn_box.pack_start(close, False, False, 0)
        card.pack_start(btn_box, False, False, 0)

        self.connect("delete-event", self._on_delete)
        self._notified_low = False

    def _on_delete(self, *_):
        self.hide()
        return True

    def update(self, state):
        if state is None:
            self.title_label.set_text("未检测到设备")
            self.big_label.set_text("—")
            self.state_label.set_text("请检查 2.4G 接收器 / udev 规则")
            self.progress.set_fraction(0)
            self.detail_label.set_text("")
            return

        self.title_label.set_text(state.device_name)
        self.big_label.set_text("%d%%" % state.percent)
        ctx = self.state_label.get_style_context()
        for cls in ("fp-charging", "fp-low", "fp-critical"):
            ctx.remove_class(cls)
        if state.charging:
            ctx.add_class("fp-charging")
            self.state_label.set_text("充电中")
        elif state.percent <= 15:
            ctx.add_class("fp-critical")
            self.state_label.set_text("电量极低")
        elif state.percent <= 40:
            ctx.add_class("fp-low")
            self.state_label.set_text("电量偏低")
        else:
            self.state_label.set_text("电池供电")
        self.progress.set_fraction(max(0.0, min(1.0, state.percent / 100.0)))
        self.detail_label.set_text(
            "设备 ID %s · 固件 %s · 原始电量 %d%%%s"
            % (state.device_id or "?", state.firmware or "?", state.battery,
               "（充电哨兵值）" if state.charging and state.battery == 100 else "")
        )

    def refresh(self):
        state, _ = read_state()
        self.update(state)
        return state


class FerrisPulseApp(Gtk.Application):
    def __init__(self, hidden=False):
        super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.FLAGS_NONE)
        self.hidden = hidden
        self.window = None
        self.indicator = None
        self.backend = None
        self._low_notified = False

    def do_activate(self):
        if self.window is None:
            self.window = MainWindow(self)
            provider = Gtk.CssProvider()
            provider.load_from_data(CSS)
            Gtk.StyleContext.add_provider_for_screen(
                Gdk.Screen.get_default(), provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
            )
            self._build_indicator()
            self.refresh()
            GLib.timeout_add_seconds(5, self._on_tick)
        if self.hidden:
            self.window.hide()
        else:
            self.window.show_all()

    def _build_indicator(self):
        self.indicator = AyatanaAppIndicator3.Indicator.new(
            "ferrispulse-linux",
            draw_battery_icon(-1, False),
            AyatanaAppIndicator3.IndicatorCategory.HARDWARE,
        )
        self.indicator.set_status(AyatanaAppIndicator3.IndicatorStatus.ACTIVE)
        self.indicator.set_label("—", "")

        menu = Gtk.Menu()
        show_item = Gtk.MenuItem(label="显示主窗口")
        show_item.connect("activate", lambda *_: self._show_window())
        menu.append(show_item)
        refresh_item = Gtk.MenuItem(label="立即刷新")
        refresh_item.connect("activate", lambda *_: self.refresh())
        menu.append(refresh_item)
        menu.append(Gtk.SeparatorMenuItem())
        quit_item = Gtk.MenuItem(label="退出")
        quit_item.connect("activate", lambda *_: self.quit())
        menu.append(quit_item)
        menu.show_all()
        self.indicator.set_menu(menu)

    def _show_window(self):
        if self.window:
            self.window.show_all()
            self.window.present()

    def refresh(self):
        state, backend = read_state()
        self.backend = backend
        if self.window:
            self.window.update(state)
        if self.indicator:
            if state is None:
                self.indicator.set_icon_full(draw_battery_icon(-1, False), "battery")
                self.indicator.set_label("—", "")
            else:
                self.indicator.set_icon_full(draw_battery_icon(state.percent, state.charging), "battery")
                self.indicator.set_label("%d%%" % state.percent, "")
                self._maybe_notify_low(state)
        return state

    def _maybe_notify_low(self, state):
        if Notify is None:
            return
        if state.percent <= 15 and not state.charging and not self._low_notified:
            try:
                Notify.init("FerrisPulse Linux")
                note = Notify.Notification.new(
                    "鼠标电量低", "%s 剩余 %d%%，请及时充电。" % (state.device_name, state.percent)
                )
                note.show()
                self._low_notified = True
            except Exception:
                pass
        elif state.percent > 20:
            self._low_notified = False

    def _on_tick(self):
        self.refresh()
        return True

    def do_shutdown(self):
        if self.indicator:
            self.indicator.set_status(AyatanaAppIndicator3.IndicatorStatus.PASSIVE)
        Gtk.Application.do_shutdown(self)
