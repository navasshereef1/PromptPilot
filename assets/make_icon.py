"""Builds app.ico and app-off.ico from a concept in icon_options.py. Usage: make_icon.py [A|B|C|D]"""
import io
import os
import struct
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import icon_options as opt  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
fn = dict(zip('ABCD', [c[1] for c in opt.CONCEPTS]))[(sys.argv[1] if len(sys.argv) > 1 else 'A').upper()]


def dib(img):
    w, h = img.size
    data = img.tobytes('raw', 'BGRA', 0, -1)
    row = ((w + 31) // 32) * 4
    return struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, len(data) + row * h, 0, 0, 0, 0) + data + b'\0' * (row * h)


def write_ico(path, images):
    entries, blobs = [], []
    offset = 6 + 16 * len(images)
    for img in images:
        if img.width >= 256:
            buf = io.BytesIO()
            img.save(buf, 'PNG', optimize=True)
            blob = buf.getvalue()
        else:
            blob = dib(img)
        wh = 0 if img.width >= 256 else img.width
        entries.append(struct.pack('<BBBBHHII', wh, wh, 0, 0, 1, 32, len(blob), offset))
        blobs.append(blob)
        offset += len(blob)
    with open(path, 'wb') as f:
        f.write(struct.pack('<HHH', 0, 1, len(images)) + b''.join(entries) + b''.join(blobs))


def grey(img):
    r, g, b, a = img.split()
    v = Image.merge('RGB', (r, g, b)).convert('L')
    return Image.merge('RGBA', (v, v, v, a))


on = [fn(s).resize((s, s), Image.LANCZOS) for s in SIZES]
write_ico(os.path.join(HERE, 'app.ico'), on)
write_ico(os.path.join(HERE, 'app-off.ico'), [grey(i) for i in on])
print('ok')
