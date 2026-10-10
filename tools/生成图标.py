# -*- coding: utf-8 -*-
"""生成 EnvSwitch 的图标：左右双色方块（左=白天橙，右=夜晚湖绿）。
只用标准库（zlib/struct），可重复运行。用法：
    python 生成图标.py ..\src\图标.ico
"""
import os
import struct
import sys
import zlib

ORANGE = (0xC2, 0x57, 0x0F)
LAKE = (0x3E, 0xD0, 0xAE)
SS = 4  # 4 倍超采样，边缘抗锯齿


def in_rrect(x, y, x0, y0, w, h, rad):
    if x < x0 or y < y0 or x >= x0 + w or y >= y0 + h:
        return False
    if rad <= 0:
        return True
    cx = min(max(x, x0 + rad - 0.5), x0 + w - rad - 0.5)
    cy = min(max(y, y0 + rad - 0.5), y0 + h - rad - 0.5)
    dx, dy = x - cx, y - cy
    return dx * dx + dy * dy <= (rad - 0.5) * (rad - 0.5) + 0.25


def render(size):
    """返回 top-down RGBA bytes。"""
    big = size * SS
    margin = max(SS, round(big * 0.10 / SS) * SS)
    gap = max(SS, round(big * 0.09 / SS) * SS)
    rad = max(SS, round(big * 0.16 / SS) * SS)
    inner_w = big - 2 * margin
    inner_h = big - 2 * margin
    lw = (inner_w - gap) // 2
    rw = inner_w - gap - lw
    acc = [0] * (size * size * 4)
    for by in range(big):
        for bx in range(big):
            if in_rrect(bx, by, margin, margin, lw, inner_h, rad):
                c = ORANGE
            elif in_rrect(bx, by, margin + lw + gap, margin, rw, inner_h, rad):
                c = LAKE
            else:
                continue
            i = ((by // SS) * size + (bx // SS)) * 4
            acc[i] += c[0]
            acc[i + 1] += c[1]
            acc[i + 2] += c[2]
            acc[i + 3] += 255
    n = SS * SS
    out = bytearray(size * size * 4)
    for p in range(size * size):
        a = acc[p * 4 + 3]
        if a:
            out[p * 4] = acc[p * 4] // n
            out[p * 4 + 1] = acc[p * 4 + 1] // n
            out[p * 4 + 2] = acc[p * 4 + 2] // n
            out[p * 4 + 3] = a // n
    return bytes(out)


def png_bytes(rgba, w, h):
    raw = b"".join(b"\x00" + rgba[y * w * 4:(y + 1) * w * 4] for y in range(h))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def bmp_bytes(rgba, w, h):
    hdr = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    rows = []
    for y in range(h - 1, -1, -1):
        row = bytearray()
        for x in range(w):
            i = (y * w + x) * 4
            row += bytes((rgba[i + 2], rgba[i + 1], rgba[i], rgba[i + 3]))
        rows.append(bytes(row))
    and_mask = b"\x00" * (((w + 31) // 32) * 4 * h)
    return hdr + b"".join(rows) + and_mask


def build_ico(path):
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = []
    for s in sizes:
        rgba = render(s)
        images.append((s, png_bytes(rgba, s, s) if s >= 128 else bmp_bytes(rgba, s, s)))
    out = bytearray(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    for s, data in images:
        out += struct.pack("<BBBBHHII", 0 if s >= 256 else s, 0 if s >= 256 else s,
                           0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in images:
        out += data
    with open(path, "wb") as f:
        f.write(bytes(out))
    return len(out)


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "图标.ico"
    target = os.path.abspath(target)
    n = build_ico(target)
    print("已生成 %s（%d 字节）" % (target, n))
