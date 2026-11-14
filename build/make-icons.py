# Draws the mod's own menu icons (round buttons, mode toggle, panel handles) into src/Plugin/Icons.
# The PNGs are embedded into the plugin by build.ps1; rerun this script only to change the artwork.
#   python build/make-icons.py
import math
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Plugin", "Icons")
SS = 4  # supersampling

CREAM = (247, 243, 235, 255)
RING = (214, 204, 190, 255)
SHADOW = (60, 45, 30, 70)
INK = (78, 70, 64, 255)
INK_SOFT = (78, 70, 64, 140)
TEAL = (64, 178, 174, 255)
ORANGE = (240, 146, 58, 255)
WHITE = (255, 255, 255, 255)
LENS = (38, 34, 32, 255)


def canvas(w, h):
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def save(img, name, w, h):
    os.makedirs(OUT, exist_ok=True)
    img.resize((w, h), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


def s(v):
    return v * SS


def round_button(d, size):
    m = s(10)
    d.ellipse((m, m + s(6), s(size) - m, s(size) - m + s(4)), fill=SHADOW)
    d.ellipse((m, m, s(size) - m, s(size) - m - s(2)), fill=CREAM, outline=RING, width=s(7))


def rot(px, py, cx, cy, a):
    ca, sa = math.cos(a), math.sin(a)
    return (cx + px * ca - py * sa, cy + px * sa + py * ca)


def gear(d, cx, cy, r_body, r_tip, r_hole, teeth, color, bg):
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        w0, w1 = r_tip * 0.22, r_tip * 0.15
        # one tooth pointing up, rotated around the centre
        pts = [rot(-w0, -r_body + s(4), cx, cy, a), rot(w0, -r_body + s(4), cx, cy, a), rot(w1, -r_tip, cx, cy, a), rot(-w1, -r_tip, cx, cy, a)]
        d.polygon(pts, fill=color)
    d.ellipse((cx - r_body, cy - r_body, cx + r_body, cy + r_body), fill=color)
    d.ellipse((cx - r_hole, cy - r_hole, cx + r_hole, cy + r_hole), fill=bg)


def sunglasses(d, cx, cy, scale):
    lw, lh, gap = s(30) * scale, s(22) * scale, s(5) * scale
    outline = s(4) * scale
    lenses = [(cx - gap - lw, cy - lh / 2, cx - gap, cy + lh / 2), (cx + gap, cy - lh / 2, cx + gap + lw, cy + lh / 2)]
    # cream halo separates the glasses from the gear behind them
    for (x0, y0, x1, y1) in lenses:
        d.rounded_rectangle((x0 - outline, y0 - outline, x1 + outline, y1 + outline), radius=s(9) * scale, fill=CREAM)
    d.rectangle((cx - gap - outline, cy - lh / 2 - outline, cx + gap + outline, cy - lh / 2 + s(6) * scale + outline), fill=CREAM)
    for (x0, y0, x1, y1) in lenses:
        d.rounded_rectangle((x0, y0, x1, y1), radius=s(8) * scale, fill=LENS)
    d.rectangle((cx - gap - s(1), cy - lh / 2, cx + gap + s(1), cy - lh / 2 + s(5) * scale), fill=LENS)
    # glare
    for (x0, y0, x1, y1) in lenses:
        d.line((x0 + lw * 0.22, y0 + lh * 0.62, x0 + lw * 0.45, y0 + lh * 0.25), fill=(255, 255, 255, 210), width=int(s(3) * scale))


def icon_vr():
    n = 128
    img, d = canvas(n, n)
    round_button(d, n)
    c = s(n / 2)
    gear(d, c, c, s(34), s(45), s(22), 8, INK, CREAM)
    sunglasses(d, c, c + s(2), 0.78)
    save(img, "btn_vr", n, n)


def icon_manual():
    n = 128
    img, d = canvas(n, n)
    round_button(d, n)
    cx, cy = s(64), s(66)
    # cover
    d.polygon([(cx - s(40), cy - s(24)), (cx, cy - s(18)), (cx + s(40), cy - s(24)), (cx + s(40), cy + s(26)), (cx, cy + s(32)), (cx - s(40), cy + s(26))], fill=TEAL)
    # pages
    left = [(cx - s(35), cy - s(30)), (cx - s(2), cy - s(24)), (cx - s(2), cy + s(24)), (cx - s(35), cy + s(18))]
    right = [(cx + s(2), cy - s(24)), (cx + s(35), cy - s(30)), (cx + s(35), cy + s(18)), (cx + s(2), cy + s(24))]
    for page in (left, right):
        d.polygon(page, fill=WHITE, outline=INK, width=s(3))
    # text lines
    for i in range(4):
        y = cy - s(16) + s(9) * i
        d.line((cx - s(28), y - s(3), cx - s(9), y), fill=INK_SOFT, width=s(3))
        d.line((cx + s(9), y, cx + s(28), y - s(3)), fill=INK_SOFT, width=s(3))
    d.line((cx, cy - s(22), cx, cy + s(26)), fill=INK, width=s(3))
    save(img, "btn_manual", n, n)


def cursor(d, cx, cy, h, color, outline):
    pts = [(0, 0), (0, 420), (100, 330), (170, 480), (235, 450), (165, 305), (290, 305)]
    k = h / 480.0
    ox, oy = cx - 145 * k, cy - 240 * k
    pts = [(ox + x * k, oy + y * k) for (x, y) in pts]
    d.polygon(pts, fill=color, outline=outline, width=s(3) if outline else 0)


def hand(d, cx, cy, h, color):
    k = h / 100.0
    def R(x0, y0, x1, y1, r):
        d.rounded_rectangle((cx + x0 * k, cy + y0 * k, cx + x1 * k, cy + y1 * k), radius=r * k, fill=color)
    R(-24, -6, 26, 46, 14)                                    # palm
    for i, (top) in enumerate((-46, -54, -50, -38)):          # fingers
        x = -24 + i * 13
        R(x, top, x + 11, 10, 5.5)
    # thumb, tilted outwards
    t0, t1 = (cx - 20 * k, cy + 22 * k), (cx - 38 * k, cy - 4 * k)
    d.line((t0, t1), fill=color, width=int(12 * k))
    for (x, y) in (t0, t1):
        d.ellipse((x - 6 * k, y - 6 * k, x + 6 * k, y + 6 * k), fill=color)


def toggle(hand_active):
    w, h = 256, 128
    img, d = canvas(w, h)
    m = s(8)
    d.rounded_rectangle((m, m + s(6), s(w) - m, s(h) - m + s(4)), radius=s(h / 2) - m, fill=SHADOW)
    d.rounded_rectangle((m, m, s(w) - m, s(h) - m - s(2)), radius=s(h / 2) - m, fill=CREAM, outline=RING, width=s(7))
    k = s(10)
    knob = (s(w / 2) + k - s(4), m + k, s(w) - m - k, s(h) - m - k - s(2)) if hand_active else (m + k, m + k, s(w / 2) - k + s(4), s(h) - m - k - s(2))
    d.rounded_rectangle(knob, radius=(knob[3] - knob[1]) / 2, fill=ORANGE if hand_active else TEAL)
    cy = s(h / 2) - s(1)
    cursor(d, s(w * 0.25) + s(2), cy, s(62), INK_SOFT if hand_active else WHITE, None)
    hand(d, s(w * 0.75) - s(2), cy + s(2), s(64), WHITE if hand_active else INK_SOFT)
    save(img, "toggle_hand" if hand_active else "toggle_mouse", w, h)


def grab_bar():
    w, h = 320, 64
    img, d = canvas(w, h)
    m = s(5)
    d.rounded_rectangle((m, m + s(4), s(w) - m, s(h) - m + s(3)), radius=s(h / 2) - m, fill=SHADOW)
    d.rounded_rectangle((m, m, s(w) - m, s(h) - m - s(2)), radius=s(h / 2) - m, fill=CREAM, outline=RING, width=s(5))
    r = s(4.5)
    for row in (-1, 1):
        for i in range(-3, 4):
            x, y = s(w / 2) + i * s(17), s(h / 2) - s(1) + row * s(8)
            d.ellipse((x - r, y - r, x + r, y + r), fill=INK_SOFT)
    save(img, "grab_bar", w, h)


def resize():
    n = 96
    img, d = canvas(n, n)
    round_button(d, n)
    a, b = s(30), s(65)
    d.line((a + s(4), a + s(4), b - s(4), b - s(4)), fill=INK, width=s(7))
    hl = s(17)
    d.polygon([(a - s(2), a - s(2)), (a - s(2) + hl, a - s(2)), (a - s(2), a - s(2) + hl)], fill=INK)
    d.polygon([(b + s(2), b + s(2)), (b + s(2) - hl, b + s(2)), (b + s(2), b + s(2) - hl)], fill=INK)
    save(img, "resize", n, n)


if __name__ == "__main__":
    icon_vr()
    icon_manual()
    toggle(False)
    toggle(True)
    grab_bar()
    resize()
    print("icons written to", os.path.normpath(OUT))
