"""核验高清返工包：数量/尺寸/通道/透明边/哈希/清晰度/比例，并和老 assets 对比。"""
import hashlib
import json
import os
import sys

from PIL import Image, ImageFilter

PKG = r"D:\Hermes\DesktopCat\incoming\高清返工-20261107"
OLD = r"D:\Hermes\DesktopCat\assets"
POSES = ["sit", "sit_left", "sit_right", "sit_closed", "sit_kneadA", "sit_kneadB", "walk", "sleep", "stretch"]


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def sharp(im):
    """边缘能量：alpha>200 区域的梯度均值，越大越清晰。"""
    a = im.split()[3]
    L = im.convert("L").filter(ImageFilter.FIND_EDGES).load()
    ap = a.load()
    tot = cnt = 0
    for y in range(0, im.height, 2):
        for x in range(0, im.width, 2):
            if ap[x, y] > 200:
                tot += L[x, y]
                cnt += 1
    return tot / max(1, cnt)


man = json.load(open(os.path.join(PKG, "manifest.json"), encoding="utf-8"))
by_key = {(m["species"], m["pose"]): m for m in man}

problems = []
names = sorted({m["species"] for m in man})
print("角色数", len(names), "清单条数", len(man))

# 1 哈希 & 尺寸 & 透明边
hash_bad = size_bad = edge_bad = 0
for m in man:
    p = os.path.join(PKG, m["file"])
    if not os.path.exists(p):
        problems.append("缺文件 %s" % m["file"])
        continue
    if sha(p) != m["sha256"]:
        hash_bad += 1
        problems.append("哈希不符 %s" % m["file"])
    im = Image.open(p)
    if im.size != (1024, 1024) or im.mode != "RGBA":
        size_bad += 1
        problems.append("尺寸/通道 %s %s %s" % (m["file"], im.size, im.mode))
    a = im.split()[3]
    w, h = im.size
    bad = 0
    for y in range(h):
        for x in list(range(8)) + list(range(w - 8, w)):
            if a.getpixel((x, y)) > 0:
                bad += 1
    for x in range(w):
        for y in list(range(8)) + list(range(h - 8, h)):
            if a.getpixel((x, y)) > 0:
                bad += 1
    if bad:
        edge_bad += 1
        problems.append("外圈8px不透明 %s (%d px)" % (m["file"], bad))
print("哈希不符 %d / 尺寸通道不符 %d / 外圈不透明 %d" % (hash_bad, size_bad, edge_bad))

# 2 每只 9 姿势齐全
for n in names:
    for p in POSES:
        if (n, p) not in by_key:
            problems.append("缺姿势 %s_%s" % (n, p))

# 3 清晰度 & 比例 & 与旧版对比
print("\n%-16s %-6s %-6s %-6s %-6s %-6s" % ("角色", "清晰度", "旧清", "睡比", "提比", "坐宽差"))
soft = []
for n in names:
    vals = {}
    for p in POSES:
        m = by_key.get((n, p))
        if not m:
            continue
        im = Image.open(os.path.join(PKG, m["file"])).convert("RGBA")
        vals[p] = (im, sharp(im), m["bounds"])
    s = vals["sit"][1]
    old_p = os.path.join(OLD, "%s_sit.png" % n)
    old_s = sharp(Image.open(old_p).convert("RGBA")) if os.path.exists(old_p) else 0
    sl = vals["sleep"][0]
    st = vals["stretch"][0]
    sleep_ratio = sl.width / float(sl.height)
    stretch_ratio = st.height / float(st.width)
    widths = [vals[p][2]["right"] - vals[p][2]["left"] for p in ["sit", "sit_left", "sit_right", "sit_closed"]]
    wdif = max(widths) - min(widths)
    flag = "" if s >= 7.0 else "  <-- 偏软"
    if s < 7.0:
        soft.append((n, round(s, 2)))
    print("%-16s %6.2f %6.2f %6.2f %6.2f %6d%s" % (n, s, old_s, sleep_ratio, stretch_ratio, wdif, flag))

print("\n问题合计", len(problems))
for p in problems[:20]:
    print(" -", p)
print("偏软角色", soft)
