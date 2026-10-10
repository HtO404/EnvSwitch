# -*- coding: utf-8 -*-
"""对比度体检：读两份主题字典里的色值，按 WCAG 算实际对比度，逐条判达标。
用法: python 对比度体检.py ..\主题-白天.xaml ..\主题-夜晚.xaml
"""
import re
import sys

# (说明, 前景 token, 背景 token, 最低要求)
PAIRS = [
    ("正文", "Ink", "Bg", 4.5),
    ("次要文字", "Ink2", "Bg", 4.5),
    ("弱化文字（未变更行/未运行）", "Ink3", "Bg", 3.0),
    ("卡片正文", "Ink", "Surface", 4.5),
    ("卡片选中态正文", "Ink", "BrandSoft", 4.5),
    ("主色文字/链接", "Brand", "Bg", 4.5),
    ("主色文字在新值高亮底上", "Brand", "BrandSoft", 4.5),
    ("主按钮文字", "OnBrand", "Brand", 4.5),
    ("主按钮悬停", "OnBrand", "BrandHover", 4.5),
    ("主按钮按下", "OnBrand", "BrandPress", 4.5),
    ("线上徽章文字", "OnDanger", "DangerFill", 4.5),
    ("报错文字", "DangerInk", "Surface", 4.5),
    ("线上徽章淡底上的警示字", "DangerInk", "BrandSoft", 4.5),
    ("状态方块·绿（图形）", "Ok", "Surface", 3.0),
    ("状态方块·红（图形）", "Danger", "Surface", 3.0),
    ("线上徽章红（图形）", "Danger", "Bg", 3.0),
]


def load(path):
    text = open(path, encoding="utf-8").read()
    out = {}
    for m in re.finditer(r'<SolidColorBrush x:Key="([^"]+)"\s+Color="(#[0-9A-Fa-f]{6})"', text):
        out[m.group(1)] = m.group(2)
    return out


def lum(hexcolor):
    c = [int(hexcolor[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
    c = [(v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4) for v in c]
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def main(paths):
    fails = 0
    for p in paths:
        theme = "白天" if "白天" in p else "夜晚"
        t = load(p)
        print("=" * 82)
        print("%s 主题  (%s，共 %d 个色 token)" % (theme, p, len(t)))
        print("  %-30s %-18s %-6s %s" % ("用途", "前景/背景", "对比度", "判定"))
        for name, fg, bg, need in PAIRS:
            if fg not in t or bg not in t:
                print("  %-30s %-18s %-6s 缺 token" % (name, fg + "/" + bg, "-"))
                fails += 1
                continue
            r = ratio(t[fg], t[bg])
            ok = r >= need
            if not ok:
                fails += 1
            print("  %-30s %-18s %5.2f  %s（要求 ≥%.1f）" % (name, t[fg] + " on " + t[bg], r, "达标" if ok else "★不达标", need))
    print("=" * 82)
    print("结论：" + ("全部达标" if fails == 0 else "有 %d 条不达标，需要改色" % fails))
    return 0 if fails == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
