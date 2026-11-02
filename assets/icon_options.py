"""Renders candidate icon concepts side by side at real sizes."""
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SS = 8


def base(size, color):
    s = size * SS
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    inset = 0 if size <= 24 else int(s * 0.03)
    d.rounded_rectangle([inset, inset, s - inset - 1, s - inset - 1], radius=int(s * 0.23), fill=color)
    return img, d, s / 256


def speech_p(size):
    """A letter P that is also a speech bubble, with a text cursor inside."""
    ink, lime = (16, 18, 22, 255), (200, 255, 61, 255)
    img, d, k = base(size, ink)
    p = lambda *a: [v * k for v in a]
    d.rounded_rectangle(p(62, 44, 200, 158), radius=52 * k, fill=lime)
    d.rectangle(p(62, 44, 110, 158), fill=lime)
    d.polygon([(62 * k, 150 * k), (110 * k, 150 * k), (110 * k, 176 * k), (62 * k, 218 * k)], fill=lime)
    d.rectangle(p(132, 74, 150, 128), fill=ink)
    return img


def needle(size):
    """A pilot's compass needle on warm paper. Red half points the way."""
    paper, ink, red = (243, 239, 228, 255), (20, 20, 22, 255), (226, 52, 40, 255)
    img, d, k = base(size, paper)
    if size >= 32:
        w = 13 * k
        d.ellipse([34 * k, 34 * k, 222 * k, 222 * k], outline=ink, width=int(w))
    c, a, b = 128 * k, 72 * k, 22 * k
    tip = (c + a, c - a)
    tail = (c - a, c + a)
    left = (c - b, c - b)
    right = (c + b, c + b)
    d.polygon([tip, left, right], fill=red)
    d.polygon([tail, left, right], fill=ink)
    return img


def chevron_p(size):
    """A P whose bowl is the command-line prompt chevron."""
    ink, mint = (12, 28, 30, 255), (45, 226, 197, 255)
    img, d, k = base(size, ink)
    pts = lambda *a: [(a[i] * k, a[i + 1] * k) for i in range(0, len(a), 2)]
    d.rectangle([64 * k, 46 * k, 108 * k, 212 * k], fill=mint)
    d.polygon(pts(108, 46, 150, 46, 208, 102, 150, 158, 108, 158, 108, 118, 134, 118, 150, 102, 134, 86, 108, 86), fill=mint)
    return img


def rewrite(size):
    """Rough uneven lines becoming one clean line: the thing the app does."""
    yellow, ink = (255, 208, 0, 255), (17, 17, 17, 255)
    img, d, k = base(size, yellow)
    r = 11 * k
    d.rounded_rectangle([52 * k, 62 * k, 142 * k, 88 * k], radius=r, fill=ink)
    d.rounded_rectangle([52 * k, 112 * k, 112 * k, 138 * k], radius=r, fill=ink)
    d.rounded_rectangle([52 * k, 162 * k, 204 * k, 196 * k], radius=r, fill=ink)
    # Four-point spark
    cx, cy, a, b = 176 * k, 88 * k, 40 * k, 9 * k
    d.polygon([(cx, cy - a), (cx + b, cy - b), (cx + a, cy), (cx + b, cy + b), (cx, cy + a), (cx - b, cy + b), (cx - a, cy), (cx - b, cy - b)], fill=ink)
    return img


CONCEPTS = [('A  Speech-bubble P', speech_p), ('B  Compass needle', needle), ('C  Prompt-chevron P', chevron_p), ('D  Rough to clean', rewrite)]
SIZES = [128, 48, 32, 24, 16]

if __name__ == '__main__':
    sheet = Image.new('RGB', (1040, 300), (246, 246, 248))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype('segoeuib.ttf', 15)
    for i, (name, fn) in enumerate(CONCEPTS):
        x0 = 20 + i * 256
        draw.text((x0, 14), name, fill=(20, 20, 20), font=font)
        big = fn(128).resize((128, 128), Image.LANCZOS)
        sheet.paste(big, (x0, 42), big)
        draw.rectangle([x0, 186, x0 + 236, 286], fill=(24, 26, 31))
        x = x0 + 10
        for s in SIZES[1:]:
            im = fn(s).resize((s, s), Image.LANCZOS)
            sheet.paste(im, (x, 196), im)
            x += s + 12
        draw.rectangle([x0, 250, x0 + 236, 286], fill=(238, 240, 243))
        x = x0 + 10
        for s in [24, 16]:
            im = fn(s).resize((s, s), Image.LANCZOS)
            sheet.paste(im, (x, 256), im)
            x += s + 12
    sheet.save(os.path.join(HERE, 'icon-options.png'))
    print('ok')
