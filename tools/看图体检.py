# -*- coding: utf-8 -*-
"""对截图做像素级体检（当前模型看不了图，只能量化验证）：
   - 布局分带（找发丝线分割行）
   - 主色普查（验证 token 是否真的落到界面上）
   - 粗网格色块图（用文字"看"版面结构）
   - 关键区域定点探针（品牌色按钮、告警徽章、diff 高亮）
用法: python 看图体检.py 白天.png 夜晚.png
"""
import collections
import struct
import sys
import zlib


def load(path):
    with open(path, "rb") as f:
        b = f.read()
    assert b[:8] == b"\x89PNG\r\n\x1a\n"
    i, idat, ihdr = 8, [], None
    while i < len(b) - 8:
        ln = struct.unpack(">I", b[i:i + 4])[0]
        typ = b[i + 4:i + 8]
        data = b[i + 8:i + 8 + ln]
        i += 12 + ln
        if typ == b"IHDR":
            ihdr = struct.unpack(">IIBBBBB", data)
        elif typ == b"IDAT":
            idat.append(data)
        elif typ == b"IEND":
            break
    w, h, bd, ct, cm, fl, il = ihdr
    raw = zlib.decompress(b"".join(idat))
    ch = {0: 1, 2: 3, 3: 1, 6: 4}[ct]
    stride = w * ch
    out, prev, pos = bytearray(), bytearray(stride), 0
    for _ in range(h):
        ft = raw[pos]; pos += 1
        line = bytearray(raw[pos:pos + stride]); pos += stride
        for x in range(stride):
            a = line[x - ch] if x >= ch else 0
            bq = prev[x]
            c = prev[x - ch] if x >= ch else 0
            if ft == 1:
                line[x] = (line[x] + a) & 255
            elif ft == 2:
                line[x] = (line[x] + bq) & 255
            elif ft == 3:
                line[x] = (line[x] + ((a + bq) >> 1)) & 255
            elif ft == 4:
                p = a + bq - c
                pa, pb, pc = abs(p - a), abs(p - bq), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (bq if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        out += line
        prev = line
    return w, h, ct, bytes(out)


def alpha_at(px, ct, w, x, y):
    if ct != 6:
        return 255
    return px[(y * w + x) * 4 + 3]


def rgb(px, ct, w, x, y):
    if ct == 2:
        i = (y * w + x) * 3
        return px[i], px[i + 1], px[i + 2]
    if ct == 6:
        i = (y * w + x) * 4
        return px[i], px[i + 1], px[i + 2]
    v = px[y * w + x]
    return v, v, v


def near(c, t, tol=10):
    return abs(c[0] - t[0]) <= tol and abs(c[1] - t[1]) <= tol and abs(c[2] - t[2]) <= tol


def classify(c, theme):
    r, g, b = c
    mx, mn = max(c), min(c)
    sat = mx - mn
    if theme == "day":
        if near(c, (0xFE, 0xFE, 0xFE), 6) or near(c, (0xFF, 0xFF, 0xFF), 6):
            return "."
        if near(c, (0xE6, 0xE6, 0xE6), 8) or near(c, (0xF0, 0xF1, 0xF1), 6):
            return "-"
        if r > 140 and g < 130 and b < 90 and r - b > 60:
            return "B"          # 橙
        if r > 180 and g < 130 and b < 130:
            return "D"          # 告警红
        if g > r and g > b and g - b > 25:
            return "G"
        if mx < 120:
            return "k"          # 深色文字
        if mx < 205:
            return "t"          # 次要文字
        return "o"
    if mx < 24:
        return "."
    if near(c, (0x12, 0x16, 0x15), 8) or near(c, (0x17, 0x1E, 0x1C), 8):
        return "-"
    if near(c, (0x22, 0x30, 0x29), 10):
        return "-"
    if g > 150 and g - r > 40:
        return "B"              # 湖绿
    if r > 170 and r - g > 40:
        return "D"
    if mx > 190:
        return "k"
    if mx > 110:
        return "t"
    return "o"


def report(path, theme):
    w, h, ct, px = load(path)
    print("=" * 78)
    print("%s   %dx%d" % (path, w, h))
    tot = w * h
    census = collections.Counter()
    step = max(1, min(w, h) // 400)
    trans = 0
    n = 0
    for y in range(0, h, step):
        for x in range(0, w, step):
            n += 1
            if alpha_at(px, ct, w, x, y) < 250:
                trans += 1
                census[(-1, -1, -1)] += 1
                continue
            c = rgb(px, ct, w, x, y)
            census[(c[0] >> 3 << 3, c[1] >> 3 << 3, c[2] >> 3 << 3)] += 1
    print("  透明像素占比: %.1f%%" % (100.0 * trans / max(1, n)))
    print("  主色 top10:")
    for c, k in census.most_common(10):
        if c == (-1, -1, -1):
            print("    (透明)      %5.1f%%" % (100.0 * k / n))
        else:
            print("    #%02X%02X%02X  %5.1f%%" % (c[0], c[1], c[2], 100.0 * k / n))

    # 发丝线分割行
    line = (0xE6, 0xE6, 0xE6) if theme == "day" else (0x22, 0x30, 0x29)
    rows = []
    for y in range(h):
        cnt = 0
        for x in range(0, w, 3):
            if near(rgb(px, ct, w, x, y), line, 14):
                cnt += 1
        if cnt > (w // 3) * 0.75:
            rows.append(y)
    merged = []
    for y in rows:
        if merged and y - merged[-1][-1] <= 2:
            merged[-1].append(y)
        else:
            merged.append([y])
    print("  横向发丝线 y =", [m[0] for m in merged])

    # 粗网格色块图
    cols, rws = 78, 30
    cw, chh = w / cols, h / rws
    print("  版面色块图（. 底 / - 线 / B 主色 / D 告警 / G 绿 / k 深字 / t 次字 / o 其他）:")
    for r in range(rws):
        linebuf = []
        for c2 in range(cols):
            cnt = collections.Counter()
            y0, y1 = int(r * chh), max(int(r * chh) + 1, int((r + 1) * chh))
            x0, x1 = int(c2 * cw), max(int(c2 * cw) + 1, int((c2 + 1) * cw))
            for y in range(y0, min(y1, h), max(1, (y1 - y0) // 6)):
                for x in range(x0, min(x1, w), max(1, (x1 - x0) // 6)):
                    if alpha_at(px, ct, w, x, y) < 250:
                        cnt["x"] += 1
                    else:
                        cnt[classify(rgb(px, ct, w, x, y), theme)] += 1
            order = [".", "-", "x", "B", "D", "G", "k", "t", "o"]
            best, bestn = None, -1
            for s in order:
                if cnt[s] > bestn and cnt[s] > 0 and (s not in (".", "-", "x") or cnt[s] > 0.55 * sum(cnt.values())):
                    best, bestn = s, cnt[s]
            linebuf.append(best or ".")
        print("    |" + "".join(linebuf) + "|")

    # 定点探针
    brand = (0xC2, 0x57, 0x0F) if theme == "day" else (0x3E, 0xD0, 0xAE)
    danger = (0xF9, 0x51, 0x51) if theme == "day" else (0xF0, 0x70, 0x6F)
    for name, target, tol in (("主色", brand, 60), ("告警", danger, 60)):
        xs, ys = [], []
        for y in range(0, h, 2):
            for x in range(0, w, 2):
                c = rgb(px, ct, w, x, y)
                if near(c, target, tol):
                    xs.append(x)
                    ys.append(y)
        if xs:
            print("  %s像素: %d 个，x %d–%d，y %d–%d" % (name, len(xs), min(xs), max(xs), min(ys), max(ys)))
        else:
            print("  %s像素: 0 个（注意）" % name)


if __name__ == "__main__":
    for p in sys.argv[1:]:
        report(p, "day" if "白天" in p else "night")
