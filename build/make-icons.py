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


def build_tilt():
    """Tilt handle of the build menu: the panel seen from the side on its pivot, with a two-headed arc."""
    n = 96
    img, d = canvas(n, n)
    round_button(d, n)
    px, py = s(34), s(72)                        # pivot at the lower edge; the viewer is on the left
    a = math.radians(-62)
    d.line((px, py, px + math.cos(a) * s(32), py + math.sin(a) * s(32)), fill=INK, width=s(8))
    d.ellipse((px - s(6), py - s(6), px + s(6), py + s(6)), fill=INK)
    r = s(44)
    a0, a1 = -112, -36
    d.arc((px - r, py - r, px + r, py + r), a0 + 6, a1 - 6, fill=ORANGE, width=s(6))
    hl = s(13)
    for end, sign in ((a0, -1), (a1, 1)):
        e = math.radians(end)
        ex, ey = px + math.cos(e) * r, py + math.sin(e) * r
        tang = e + sign * math.pi / 2
        tip = (ex + math.cos(tang) * hl * 0.6, ey + math.sin(tang) * hl * 0.6)
        l = (ex + math.cos(tang + 2.3) * hl, ey + math.sin(tang + 2.3) * hl)
        rr = (ex + math.cos(tang - 2.3) * hl, ey + math.sin(tang - 2.3) * hl)
        d.polygon([tip, l, rr], fill=ORANGE)
    save(img, "build_tilt", n, n)

def marble(d, cx, cy, r):
    # glass marble: shaded blue sphere (light from the upper left), one orange swirl, white gloss spot
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(40, 95, 175, 255))
    for i in range(1, 9):
        k = 1 - i * 0.09
        ox, oy = -r * 0.035 * i, -r * 0.035 * i
        c = (int(40 + 10 * i), int(95 + 10 * i), int(175 + 8 * i), 255)
        d.ellipse((cx + ox - r * k, cy + oy - r * k, cx + ox + r * k, cy + oy + r * k), fill=c)
    d.arc((cx - r * 1.1, cy - r * 0.35, cx + r * 0.5, cy + r * 1.25), 280, 20, fill=ORANGE, width=int(r * 0.22))
    d.arc((cx - r * 0.5, cy - r * 1.25, cx + r * 1.1, cy + r * 0.35), 100, 200, fill=ORANGE, width=int(r * 0.22))
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=INK, width=s(3))
    d.ellipse((cx - r * 0.58, cy - r * 0.62, cx - r * 0.18, cy - r * 0.3), fill=(255, 255, 255, 230))


def icon_marble(on):
    n = 128
    img, d = canvas(n, n)
    m = s(10)
    d.ellipse((m, m + s(6), s(n) - m, s(n) - m + s(4)), fill=SHADOW)
    d.ellipse((m, m, s(n) - m, s(n) - m - s(2)), fill=(88, 190, 120, 255) if on else CREAM, outline=(60, 150, 90, 255) if on else RING, width=s(7))
    marble(d, s(64), s(62), s(30))
    if on:
        # little drop lines below: "rolling"
        for i, w in enumerate((s(26), s(16))):
            y = s(101) + i * s(8)
            d.line((s(64) - w / 2, y, s(64) + w / 2, y), fill=WHITE, width=s(4))
    save(img, "btn_marble_on" if on else "btn_marble", n, n)


def gauge():
    # throw-speed dial: cream plate, coloured arc (slow = teal ... fast = red) with ticks in the upper half,
    # pivot of the needle at the centre, room for the number in the lower half
    n = 128
    img, d = canvas(n, n)
    c = s(64)
    R = s(58)
    d.pieslice((c - R, c - R + s(4), c + R, c + R + s(4)), 180, 360, fill=SHADOW)
    d.rounded_rectangle((c - R, c - s(2), c + R, c + s(44)), radius=s(16), fill=SHADOW)
    d.pieslice((c - R, c - R, c + R, c + R), 180, 360, fill=CREAM)
    d.rounded_rectangle((c - R, c - s(6), c + R, c + s(40)), radius=s(16), fill=CREAM)
    d.arc((c - R, c - R, c + R, c + R), 180, 360, fill=RING, width=s(5))
    stops = [(64, 178, 174), (120, 200, 110), (250, 205, 70), (240, 146, 58), (220, 70, 50)]
    steps = 36
    r0 = R - s(9)
    for i in range(steps):
        f = i / (steps - 1)
        k = min(int(f * (len(stops) - 1)), len(stops) - 2)
        u = f * (len(stops) - 1) - k
        col = tuple(int(stops[k][j] + (stops[k + 1][j] - stops[k][j]) * u) for j in range(3)) + (255,)
        a0 = 180 + 180 * i / steps
        d.arc((c - r0, c - r0, c + r0, c + r0), a0, a0 + 180 / steps + 0.8, fill=col, width=s(11))
    for i in range(9):
        ang = math.radians(180 + 180 * i / 8)
        r1, r2 = r0 - s(14), r0 - s(20 if i % 2 == 0 else 17)
        d.line((c + r1 * math.cos(ang), c + r1 * math.sin(ang), c + r2 * math.cos(ang), c + r2 * math.sin(ang)), fill=INK, width=s(3))
    save(img, "gauge", n, n)


def gauge_needle():
    # needle pointing up from the centre of the square, hub at the centre
    n = 128
    img, d = canvas(n, n)
    c = s(64)
    d.polygon([(c - s(4.5), c), (c, c - s(46)), (c + s(4.5), c)], fill=(200, 50, 40, 255))
    d.ellipse((c - s(8), c - s(8), c + s(8), c + s(8)), fill=INK)
    d.ellipse((c - s(3), c - s(3), c + s(3), c + s(3)), fill=CREAM)
    save(img, "gauge_needle", n, n)


# ---------------------------------------------------------------- UI pieces in the game's dialog style
# (white shapes are tinted in the game; the header carries its own colours)

def ui_round_rect():
    # white rounded rectangle for 9-slicing (border 40 px), used for panels, pills and buttons
    n = 128
    img, d = canvas(n, n)
    d.rounded_rectangle((0, 0, s(n) - 1, s(n) - 1), radius=s(36), fill=WHITE)
    save(img, "ui_round_rect", n, n)


def ui_header():
    # yellow header band like the game's dialogs: sun rays, stars and dots in a darker yellow, rounded top corners
    w, h = 1024, 128
    img, d = canvas(w, h)
    base, dark = (250, 196, 45, 255), (238, 168, 30, 255)
    d.rounded_rectangle((0, 0, s(w) - 1, s(h) + s(60)), radius=s(36), fill=base)
    import random
    rnd = random.Random(7)
    # big sun on the right with rays
    cx, cy, r = s(880), s(52), s(26)
    for i in range(16):
        ang = math.radians(i * 22.5)
        d.line((cx + math.cos(ang) * r * 1.3, cy + math.sin(ang) * r * 1.3, cx + math.cos(ang) * r * 2.3, cy + math.sin(ang) * r * 2.3), fill=dark, width=s(4))
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=dark)
    # little bursts, stars and dots spread over the band
    def star(x, y, rr, col):
        pts = []
        for i in range(10):
            ang = math.radians(-90 + i * 36)
            rad = rr if i % 2 == 0 else rr * 0.45
            pts.append((x + math.cos(ang) * rad, y + math.sin(ang) * rad))
        d.polygon(pts, fill=col)
    for i in range(14):
        x, y = s(rnd.randint(20, 800)), s(rnd.randint(10, 118))
        if rnd.random() < 0.45:
            star(x, y, s(rnd.randint(7, 13)), dark)
        else:
            for k in range(8):
                ang = math.radians(k * 45 + rnd.randint(0, 30))
                rr = s(rnd.randint(8, 14))
                d.line((x + math.cos(ang) * rr * 0.4, y + math.sin(ang) * rr * 0.4, x + math.cos(ang) * rr, y + math.sin(ang) * rr), fill=dark, width=s(3))
    for i in range(40):
        x, y, rr = s(rnd.randint(5, 1015)), s(rnd.randint(5, 123)), s(rnd.choice((2, 3, 4)))
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=dark)
    save(img, "ui_header", w, h)


def ui_arrow():
    n = 64
    img, d = canvas(n, n)
    d.polygon([(s(20), s(12)), (s(50), s(32)), (s(20), s(52))], fill=WHITE)
    save(img, "ui_arrow", n, n)


def ui_check():
    n = 64
    img, d = canvas(n, n)
    d.line((s(12), s(33), s(26), s(47), s(52), s(17)), fill=WHITE, width=s(9), joint="curve")
    for (x, y) in ((s(12), s(33)), (s(52), s(17))):
        d.ellipse((x - s(4.5), y - s(4.5), x + s(4.5), y + s(4.5)), fill=WHITE)
    save(img, "ui_check", n, n)


def ui_headset():
    # VR goggles glyph (white)
    n = 128
    img, d = canvas(n, n)
    d.rounded_rectangle((s(10), s(36), s(118), s(92)), radius=s(22), fill=WHITE)
    d.ellipse((s(50), s(70), s(78), s(98)), fill=(0, 0, 0, 0))                # nose cut-out
    d.rectangle((s(54), s(84), s(74), s(96)), fill=(0, 0, 0, 0))
    d.rectangle((s(2), s(52), s(12), s(66)), fill=WHITE)                       # strap stubs
    d.rectangle((s(116), s(52), s(126), s(66)), fill=WHITE)
    save(img, "ui_headset", n, n)


def ui_monitor():
    n = 128
    img, d = canvas(n, n)
    d.rounded_rectangle((s(12), s(18), s(116), s(88)), radius=s(10), outline=WHITE, width=s(10))
    d.rectangle((s(56), s(88), s(72), s(104)), fill=WHITE)
    d.rounded_rectangle((s(36), s(102), s(92), s(112)), radius=s(5), fill=WHITE)
    save(img, "ui_monitor", n, n)


# ---------------------------------------------------------------- controller decals
# White glyphs on transparent ground (tinted in the game), a round sticker plate, 7-segment digits.

G = 128
W_ = (255, 255, 255, 255)
LW = 13  # stroke width at 128 px


def gcanvas():
    return canvas(G, G)


def P(x, y):
    return (s(x), s(y))


def line(d, pts, w=LW):
    pts = [P(*q) for q in pts]
    d.line(pts, fill=W_, width=s(w), joint="curve")
    for q in (pts[0], pts[-1]):
        r = s(w) / 2
        d.ellipse((q[0] - r, q[1] - r, q[0] + r, q[1] + r), fill=W_)


def poly(d, pts):
    d.polygon([P(*q) for q in pts], fill=W_)


def arc(d, cx, cy, r, a0, a1, w=LW):
    d.arc((s(cx - r), s(cy - r), s(cx + r), s(cy + r)), a0, a1, fill=W_, width=s(w))


def disc(d, cx, cy, r, fill=W_):
    d.ellipse((s(cx - r), s(cy - r), s(cx + r), s(cy + r)), fill=fill)


def arrow_head(d, x, y, ang, size=22):
    import math as m
    a = m.radians(ang)
    tip = (x + m.cos(a) * size * 0.6, y + m.sin(a) * size * 0.6)
    l = (x + m.cos(a + 2.4) * size * 0.75, y + m.sin(a + 2.4) * size * 0.75)
    r = (x + m.cos(a - 2.4) * size * 0.75, y + m.sin(a - 2.4) * size * 0.75)
    poly(d, [tip, l, r])


def glyph(name, draw):
    img, d = gcanvas()
    draw(d)
    save(img, "g_" + name, G, G)


def glyphs():
    glyph("up", lambda d: (line(d, [(64, 104), (64, 40)]), arrow_head(d, 64, 34, -90, 46)))
    glyph("down", lambda d: (line(d, [(64, 24), (64, 88)]), arrow_head(d, 64, 94, 90, 46)))
    glyph("left", lambda d: (line(d, [(104, 64), (40, 64)]), arrow_head(d, 34, 64, 180, 46)))
    glyph("right", lambda d: (line(d, [(24, 64), (88, 64)]), arrow_head(d, 94, 64, 0, 46)))
    # undo / redo: hooked arrows
    glyph("undo", lambda d: (arc(d, 68, 70, 30, 270, 450), line(d, [(68, 100), (44, 100)]), line(d, [(68, 40), (44, 40)]), arrow_head(d, 34, 40, 180, 42)))
    glyph("redo", lambda d: (arc(d, 60, 70, 30, 90, 270), line(d, [(60, 100), (84, 100)]), line(d, [(60, 40), (84, 40)]), arrow_head(d, 94, 40, 0, 42)))
    glyph("back", lambda d: (line(d, [(100, 96), (100, 70), (90, 56), (40, 56)]), arrow_head(d, 32, 56, 180, 46)))
    glyph("fly", lambda d: ([line(d, l) for l in ([(64, 22), (64, 106)], [(22, 64), (106, 64)])], [arrow_head(d, *q, 34) for q in ((64, 16, -90), (64, 112, 90), (16, 64, 180), (112, 64, 0))]))
    glyph("pause", lambda d: (line(d, [(46, 34), (46, 94)], 18), line(d, [(82, 34), (82, 94)], 18)))
    glyph("rotate", lambda d: (arc(d, 64, 64, 36, 200, 500 - 40), arrow_head(d, 34, 50, -100, 40)))
    glyph("turn", lambda d: (arc(d, 64, 74, 40, 200, 340), arrow_head(d, 26, 82, 110, 34), arrow_head(d, 102, 82, 70, 34)))
    glyph("trash", lambda d: (line(d, [(30, 38), (98, 38)]), line(d, [(52, 38), (54, 26), (74, 26), (76, 38)], 10),
                              line(d, [(38, 46), (44, 106), (84, 106), (90, 46)]), line(d, [(56, 58), (57, 92)], 9), line(d, [(72, 58), (71, 92)], 9)))
    glyph("trash_marbles", lambda d: (line(d, [(30, 38), (98, 38)]), line(d, [(38, 46), (44, 106), (84, 106), (90, 46)]), disc(d, 52, 22, 11), disc(d, 76, 22, 11), disc(d, 64, 72, 15)))
    glyph("play", lambda d: poly(d, [(40, 26), (104, 64), (40, 102)]))
    glyph("plus", lambda d: (line(d, [(64, 26), (64, 102)], 18), line(d, [(26, 64), (102, 64)], 18)))
    glyph("minus", lambda d: line(d, [(26, 64), (102, 64)], 18))
    glyph("copy", lambda d: (d.rounded_rectangle((s(22), s(36), s(78), s(104)), radius=s(10), outline=W_, width=s(11)),
                             line(d, [(48, 22), (98, 22), (106, 30), (106, 84)], 11)))
    glyph("paste", lambda d: (d.rounded_rectangle((s(28), s(28), s(100), s(108)), radius=s(10), outline=W_, width=s(11)),
                              d.rounded_rectangle((s(46), s(18), s(82), s(38)), radius=s(6), fill=W_), line(d, [(46, 66), (82, 66)], 10), line(d, [(46, 86), (74, 86)], 10)))
    glyph("cut", lambda d: (d.ellipse((s(18), s(76), s(50), s(108)), outline=W_, width=s(10)), d.ellipse((s(78), s(76), s(110), s(108)), outline=W_, width=s(10)),
                            line(d, [(42, 80), (92, 20)], 10), line(d, [(86, 80), (36, 20)], 10)))
    glyph("magic", lambda d: (line(d, [(26, 104), (82, 48)], 14), poly(d, [(92, 14), (98, 30), (114, 36), (98, 42), (92, 58), (86, 42), (70, 36), (86, 30)])))
    glyph("magnet", lambda d: (arc(d, 64, 60, 34, 0, 180, 22), line(d, [(30, 60), (30, 28)], 22), line(d, [(98, 60), (98, 28)], 22)))
    glyph("recenter", lambda d: (d.ellipse((s(30), s(30), s(98), s(98)), outline=W_, width=s(11)), disc(d, 64, 64, 12),
                                 line(d, [(64, 8), (64, 26)], 10), line(d, [(64, 102), (64, 120)], 10), line(d, [(8, 64), (26, 64)], 10), line(d, [(102, 64), (120, 64)], 10)))
    glyph("pointer", lambda d: poly(d, [(34, 16), (34, 102), (56, 82), (72, 114), (88, 106), (72, 76), (102, 76)]))
    glyph("hand", lambda d: hand(d, s(64), s(70), s(110), W_))
    glyph("marble", lambda d: (d.ellipse((s(22), s(22), s(106), s(106)), outline=W_, width=s(11)), arc(d, 64, 64, 26, 200, 330, 10), disc(d, 46, 46, 8)))
    glyph("world", lambda d: (d.ellipse((s(18), s(18), s(110), s(110)), outline=W_, width=s(10)), d.ellipse((s(44), s(18), s(84), s(110)), outline=W_, width=s(9)),
                              line(d, [(20, 64), (108, 64)], 9)))
    glyph("carry", lambda d: (hand(d, s(64), s(80), s(84), W_), d.rounded_rectangle((s(30), s(8), s(98), s(36)), radius=s(6), fill=W_)))
    glyph("cycle", lambda d: (arc(d, 64, 64, 38, 20, 160), arc(d, 64, 64, 38, 200, 340), arrow_head(d, 28, 72, 100, 36), arrow_head(d, 100, 56, -80, 36)))
    glyph("shift", lambda d: line(d, [(64, 18), (106, 62), (84, 62), (84, 104), (44, 104), (44, 62), (22, 62), (64, 18)], 10))
    glyph("props", lambda d: ([line(d, [(24, y), (104, y)], 9) for y in (34, 64, 94)], disc(d, 48, 34, 13), disc(d, 84, 64, 13), disc(d, 56, 94, 13)))
    glyph("menu_open", lambda d: (d.rounded_rectangle((s(18), s(24), s(110), s(104)), radius=s(10), outline=W_, width=s(10)), line(d, [(50, 26), (50, 102)], 10),
                                  arrow_head(d, 92, 64, 0, 34), line(d, [(64, 64), (84, 64)], 10)))
    glyph("menu_close", lambda d: (d.rounded_rectangle((s(18), s(24), s(110), s(104)), radius=s(10), outline=W_, width=s(10)), line(d, [(50, 26), (50, 102)], 10),
                                   arrow_head(d, 70, 64, 180, 34), line(d, [(80, 64), (96, 64)], 10)))
    glyph("toolbox", lambda d: (d.rounded_rectangle((s(16), s(44), s(112), s(104)), radius=s(10), outline=W_, width=s(10)), line(d, [(46, 44), (46, 30), (82, 30), (82, 44)], 10),
                                line(d, [(16, 70), (112, 70)], 9)))
    glyph("click", lambda d: (poly(d, [(46, 40), (46, 112), (64, 96), (78, 122), (90, 116), (76, 90), (102, 90)]), line(d, [(30, 30), (20, 20)], 9), line(d, [(46, 20), (46, 8)], 9), line(d, [(62, 30), (72, 20)], 9)))
    glyph("check", lambda d: line(d, [(24, 66), (52, 94), (104, 34)], 16))
    glyph("menu", lambda d: [line(d, [(24, y), (104, y)], 15) for y in (34, 64, 94)])
    glyph("gear", lambda d: gear_glyph(d))
    glyph("menu_center", lambda d: (d.rounded_rectangle((s(30), s(26), s(98), s(102)), radius=s(10), outline=W_, width=s(10)),
                                    line(d, [(46, 46), (82, 46)], 9), line(d, [(46, 64), (82, 64)], 9), line(d, [(46, 82), (70, 82)], 9),
                                    arrow_head(d, 12, 64, 0, 30), arrow_head(d, 116, 64, 180, 30)))
    glyph("camera", lambda d: (d.rounded_rectangle((s(14), s(38), s(114), s(104)), radius=s(14), fill=W_), d.rounded_rectangle((s(42), s(24), s(86), s(44)), radius=s(6), fill=W_),
                               disc(d, 64, 71, 22, (0, 0, 0, 0)), disc(d, 64, 71, 12)))
    glyph("flag", lambda d: (line(d, [(30, 18), (30, 112)], 12), poly(d, [(36, 20), (106, 34), (84, 52), (106, 70), (36, 70)])))
    glyph("manual", lambda d: (poly(d, [(14, 30), (60, 38), (60, 104), (14, 96)]), poly(d, [(68, 38), (114, 30), (114, 96), (68, 104)])))
    glyph("layers", lambda d: (poly(d, [(64, 20), (112, 46), (64, 72), (16, 46)]), line(d, [(16, 66), (64, 92), (112, 66)], 10), line(d, [(16, 86), (64, 112), (112, 86)], 10)))


def gear_glyph(d):
    cx, cy = s(64), s(64)
    for i in range(8):
        a = 2 * math.pi * i / 8
        pts = [rot(-s(10), -s(36), cx, cy, a), rot(s(10), -s(36), cx, cy, a), rot(s(8), -s(52), cx, cy, a), rot(-s(8), -s(52), cx, cy, a)]
        d.polygon(pts, fill=W_)
    d.ellipse((cx - s(40), cy - s(40), cx + s(40), cy + s(40)), fill=W_)
    d.ellipse((cx - s(18), cy - s(18), cx + s(18), cy + s(18)), fill=(0, 0, 0, 0))


def dpad_cross():
    # plus-shaped plate under the four D-pad labels (tinted dark grey in the game)
    n = 128
    img, d = canvas(n, n)
    d.rounded_rectangle((s(44), s(4), s(84), s(124)), radius=s(14), fill=WHITE)
    d.rounded_rectangle((s(4), s(44), s(124), s(84)), radius=s(14), fill=WHITE)
    save(img, "dpad_cross", n, n)


def sticker_plate():
    # round plate for decals: white face (tinted), slightly darker rim, light bevel on top
    n = 128
    img, d = canvas(n, n)
    d.ellipse((s(2), s(2), s(126), s(126)), fill=(205, 205, 205, 255))
    d.ellipse((s(8), s(6), s(120), s(118)), fill=WHITE)
    d.arc((s(14), s(12), s(114), s(112)), 200, 330, fill=(255, 255, 255, 255), width=s(4))
    save(img, "plate", n, n)


SEG = {  # a b c d e f g
    "0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc",
}


def lcd(ch):
    # tiny 7-segment display: dark bezel, lit segments in amber, unlit ones faintly visible
    w, h = 96, 128
    img, d = canvas(w, h)
    d.rounded_rectangle((0, 0, s(w) - 1, s(h) - 1), radius=s(16), fill=(40, 34, 30, 255))
    d.rounded_rectangle((s(8), s(8), s(w - 8), s(h - 8)), radius=s(10), fill=(24, 20, 18, 255))
    x0, x1, y0, y1, ym, th = 26, 70, 22, 106, 64, 11
    segs = {
        "a": ((x0 + 6, y0), (x1 - 6, y0)), "g": ((x0 + 6, ym), (x1 - 6, ym)), "d": ((x0 + 6, y1), (x1 - 6, y1)),
        "f": ((x0, y0 + 6), (x0, ym - 6)), "b": ((x1, y0 + 6), (x1, ym - 6)),
        "e": ((x0, ym + 6), (x0, y1 - 6)), "c": ((x1, ym + 6), (x1, y1 - 6)),
    }
    on = SEG[ch]
    for k, (p0, p1) in segs.items():
        col = (255, 170, 60, 255) if k in on else (70, 52, 40, 255)
        d.line((s(p0[0]), s(p0[1]), s(p1[0]), s(p1[1])), fill=col, width=s(th))
        for q in (p0, p1):
            r = s(th) / 2
            d.ellipse((s(q[0]) - r, s(q[1]) - r, s(q[0]) + r, s(q[1]) + r), fill=col)
    save(img, "lcd_" + ch, w, h)


if __name__ == "__main__":
    glyphs()
    sticker_plate()
    dpad_cross()
    for ch in "0123456":
        lcd(ch)
    ui_round_rect()
    ui_header()
    ui_arrow()
    ui_check()
    ui_headset()
    ui_monitor()
    gauge()
    gauge_needle()
    icon_marble(False)
    icon_marble(True)
    icon_vr()
    icon_manual()
    toggle(False)
    toggle(True)
    grab_bar()
    resize()
    build_tilt()
    print("icons written to", os.path.normpath(OUT))
