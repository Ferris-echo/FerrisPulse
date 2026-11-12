# -*- coding: utf-8 -*-
"""皂品驱动界面美化 —— 照抄罗技 G HUB 深色主题
画布 #1d1d1d / 表面 #262626 / 边框 #3a3a3a / 白字 / 强调青 #00b8fc
尺寸/结构/线条位置与原图严格一致。
"""
from PIL import Image, ImageDraw, ImageFilter
import colorsys, os

SRC = r'D:/Software/皂品驱动/Zaopin Wireless Mouse/res_backup_原始界面'
DST = r'D:/Software/皂品驱动/Zaopin Wireless Mouse/res'

CANVAS = (29, 29, 29)        # 罗技 G HUB 画布 #1d1d1d
BORDER = (58, 58, 58, 255)   # 边框 #3a3a3a
CYAN = (255, 106, 0)         # 强调色：皂品品牌橙 #FF6A00
SS = 4

def panel(w, h, fill_top, fill_bot, border, radius, inner_lines=None, solid=True):
    W, H = w * SS, h * SS
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = radius * SS
    if solid:
        for y in range(H):
            t = y / max(H - 1, 1)
            c = tuple(int(fill_top[i] + (fill_bot[i] - fill_top[i]) * t) for i in range(3)) + (255,)
            d.line([(0, y), (W, y)], fill=c)
        mask = Image.new('L', (W, H), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, H - 1], r, fill=255)
        im.putalpha(mask)
    d.rounded_rectangle([0, 0, W - 1, H - 1], r, outline=border, width=SS)
    if inner_lines:
        for (x0, x1) in inner_lines:
            d.line([(x0 * SS, SS), (x0 * SS, H - SS)], fill=border, width=max(1, (x1 - x0 + 1) * SS))
    return im.resize((w, h), Image.LANCZOS)

# ---------- 主背景：罗技纯色画布 + 设备区克制浅光 ----------
def make_background():
    src = Image.open(os.path.join(SRC, '1HomePage', 'background.png')).convert('RGB')
    w, h = 1020, 700
    im = Image.new('RGB', (w, h), CANVAS)          # 纯 #1d1d1d，与罗技一致
    d = ImageDraw.Draw(im)
    # 标题栏 0-96（logo 无缝保留）
    d.rectangle([0, 0, w, 96], fill=(35, 35, 35))
    d.line([(0, 97), (w, 97)], fill=(18, 18, 18))
    # 标题栏底部罗技青强调线（左实右渐隐）
    glow_w = 340
    for x in range(glow_w):
        a = (1 - x / glow_w) ** 1.6
        c = tuple(int(CYAN[i] * a + 35 * (1 - a)) for i in range(3))
        d.line([(x, 96), (x, 96)], fill=c)
    logo = src.crop((20, 32, 210, 72))
    im.paste(logo, (20, 32))
    return im

bg = make_background()
bg.save(os.path.join(DST, '1HomePage', 'background.png'))
bg.save(os.path.join(DST, '2Button', 'background.png'))
print('background done')

# ---------- 面板（罗技卡片层次：画布 #1d1d1d < 面板 #232323 < 控件 #2f2f2f）----------
BORDER2 = (69, 69, 69, 255)    # 边框 #454545（提亮，保证在深底上清晰）
# 按键/宏功能区面板：表面填充 + 亮边框 + 竖分割线（两个功能区边界清晰）
p = panel(862, 216, (35, 35, 35), (31, 31, 31), BORDER2, 0, inner_lines=[(636, 637)], solid=True)
p.save(os.path.join(DST, '2Button', 'key_func_bg.png'))
p = panel(192, 194, (38, 38, 38), (34, 34, 34), BORDER2, 6)
p.save(os.path.join(DST, '2Button', 'key_sub_func_bg.png'))
# 按键列表行：低调填充（仅比面板微亮）+ 细边框，靠边框区分而非亮度
p = panel(262, 26, (36, 36, 36), (33, 33, 33), (61, 61, 61, 255), 4, solid=True)
p.save(os.path.join(DST, '2Button', 'key_list_nr.png'))
p = panel(300, 110, (44, 44, 44), (36, 36, 36), (76, 76, 76, 255), 8)
p.save(os.path.join(DST, '7General', 'message_bg.png'))
p = panel(548, 302, (44, 44, 44), (36, 36, 36), (84, 84, 84, 255), 10)
p.save(os.path.join(DST, '6Setting', 'setting_bg.png'))
p = panel(458, 242, (44, 44, 44), (36, 36, 36), (84, 84, 84, 255), 8)
p.save(os.path.join(DST, '6Setting', 'pair_bg.png'))
print('panels done')

# ---------- 橙色 UI 强调色 → 罗技青（5Light 灯光色板除外）----------
ORANGE_ICONS = [
    '1HomePage/repair.png',
    '2Button/adv_ld.png', '2Button/charing.png', '2Button/exit_down.png', '2Button/exit_enter.png',
    '2Button/key_func_down(arr).png', '2Button/key_func_down.png', '2Button/key_index.png',
    '2Button/key_list_down.png', '2Button/low.png', '2Button/mouse_down.png',
    '2Button/mouse_key_down.png', '2Button/mouse_key_enter.png', '2Button/mouse_key_nr.png',
    '2Button/restore_down.png', '2Button/tab_enter.png', '2Button/tab_select.png',
    '3Sensor/current_DPI_enter.png', '3Sensor/current_DPI_nr.png', '3Sensor/ReportRate_Select.png',
    '4Macro/new_macro_down.png', '4Macro/start_record_enter.png', '4Macro/stop_record_enter.png',
    '6Setting/pair_fail.png', '6Setting/pair_success.png', '6Setting/updating.png',
    '7General/add.png', '7General/check_select.png', '7General/ok_down.png',
    '7General/on.png', '7General/radio_select.png', '7General/sub.png',
]

def recolor_orange_to_cyan(path):
    im = Image.open(os.path.join(SRC, path)).convert('RGBA')
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            if r > 90 and r > g > b:
                hh, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
                if 0.02 < hh < 0.14 and s > 0.3:
                    h2 = (hh + 0.475) % 1.0
                    rr, gg, bb = colorsys.hsv_to_rgb(h2, s, v)
                    px[x, y] = (int(rr * 255), int(gg * 255), int(bb * 255), a)
    im.save(os.path.join(DST, path))
    print('recolor', path)

# 强调色已改为品牌橙：直接从备份恢复原始橙色图标（不再着色）
import shutil
for icon in ORANGE_ICONS:
    try:
        shutil.copyfile(os.path.join(SRC, icon), os.path.join(DST, icon))
        print('restore', icon)
    except PermissionError:
        print('SKIP (locked):', icon)

# ---------- 鼠标主体提亮为纯白（罗技白色渲染图风格）----------
MOUSE_IMGS = (
    ['1HomePage/dev%donline.png' % i for i in range(1, 9)] +
    ['1HomePage/dev%doffline.png' % i for i in range(1, 9)] +
    ['8Calibration/cal_mouse.png']
)

def brighten_mouse(path):
    im = Image.open(os.path.join(SRC, path)).convert('RGBA')
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 200:              # 只处理不透明鼠标主体，保留半透明阴影
                continue
            hh, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            v2 = min(1.0, v + (1.0 - v) * 0.15)   # 温和提亮，保留明暗层次
            s2 = s                                 # 保留原始饱和度
            rr, gg, bb = colorsys.hsv_to_rgb(hh, s2, v2)
            px[x, y] = (int(rr * 255), int(gg * 255), int(bb * 255), a)
    im.save(os.path.join(DST, path))
    print('brighten', path)

for m in MOUSE_IMGS:
    brighten_mouse(m)

print('ALL DONE')
