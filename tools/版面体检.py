# -*- coding: utf-8 -*-
"""版面体检：把截图解析成「文字行清单 + 分带内容密度」，用来在没有视觉能力时核对版面。
用法: python 版面体检.py 白天.png
输出每一条：y 范围 / 墨迹像素数 / x 范围 —— 可以据此看出每个区块有没有内容、有没有越界或重叠。
"""
import sys

sys.path.insert(0, ".")
from 看图体检 import load, rgb  # noqa: E402


def main(path, theme):
    w, h, ct, px = load(path)
    bg = (0xFE, 0xFE, 0xFE) if theme == "day" else (0x0A, 0x0D, 0x0C)
    brand = (0xC2, 0x57, 0x0F) if theme == "day" else (0x3E, 0xD0, 0xAE)
    rows = []
    for y in range(h):
        n = 0
        x0, x1 = None, None
        nb = 0
        for x in range(w):
            c = rgb(px, ct, w, x, y)
            if max(abs(c[0] - bg[0]), abs(c[1] - bg[1]), abs(c[2] - bg[2])) > 16:
                n += 1
                if x0 is None:
                    x0 = x
                x1 = x
                if abs(c[0] - brand[0]) < 45 and abs(c[1] - brand[1]) < 45 and abs(c[2] - brand[2]) < 45:
                    nb += 1
        rows.append((n, x0, x1, nb))
    print("%s  %dx%d" % (path, w, h))
    print("y 起-止     墨迹   x 范围        主色像素")
    i = 0
    while i < h:
        if rows[i][0] > 12:
            j = i
            while j + 1 < h and rows[j + 1][0] > 12:
                j += 1
            ink = sum(rows[k][0] for k in range(i, j + 1))
            x0 = min(r[1] for r in rows[i:j + 1] if r[1] is not None)
            x1 = max(r[2] for r in rows[i:j + 1] if r[2] is not None)
            nb = sum(r[3] for r in rows[i:j + 1])
            print("%4d-%-4d %7d   %4d–%-4d   %5d" % (i, j, ink, x0, x1, nb))
            i = j + 1
        else:
            i += 1


if __name__ == "__main__":
    for p in sys.argv[1:]:
        main(p, "day" if "白天" in p else "night")
