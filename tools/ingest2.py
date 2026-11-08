"""入库 v2：先对齐再裁切。

上一版的问题：每张按各自 alpha bbox 裁切，抬爪帧轮廓不对称 → 角色整体左右跳。

v2 流程（针对坐姿组 6 张）：
1. 以 sit 为基准，用降采样互相关把 kneadA/kneadB 对齐到 sit（整数像素偏移）；
2. 6 张共用「对齐后的联合裁切框」+ 同一缩放；底边/中心自然一致；
3. 量对齐后差异覆盖率：
   < 12%  → 那两张确实是「只抬了爪」，生成 pawA/pawB 覆盖贴片（只替换变化区域，身体不动、不重影）；
   >=12% → 判为「整体重画」，不生成覆盖贴片，程序继续用整只轻压（不会抖）。

walk/sleep/stretch 各自裁切缩放到上限。
"""
import glob
import os
import sys

from PIL import Image, ImageChops, ImageFilter, ImageStat

ROOT = r"D:\Hermes\DesktopCat"
ASSETS = os.path.join(ROOT, "assets")
SIT_H = 420
OTHER_MAX = 460
PAD = 12
COVER_LIMIT = 12.0     # 差异覆盖率阈值（%）

SIT_POSES = ["sit", "sit_left", "sit_right", "sit_closed", "sit_kneadA", "sit_kneadB"]
OTHER_POSES = ["walk", "sleep", "stretch"]
KNEAD = ["sit_kneadA", "sit_kneadB"]


def alpha_bbox(im, th=12):
    return im.split()[3].point(lambda v: 255 if v > th else 0).getbbox()


def find_shift(ref, mov, work=320):
    """粗搜 + 细调，返回全分辨率下的 (dx, dy)。"""
    sw = work / float(max(ref.size))
    r = ref.resize((max(1, int(ref.width * sw)), max(1, int(ref.height * sw))), Image.LANCZOS)
    m = mov.resize(r.size, Image.LANCZOS)
    step = max(1, int(work / 80))          # 320 宽时 step=4
    best = None
    for dy in range(-6, 7, 2):
        for dx in range(-16, 17, 2):
            mm = ImageChops.offset(m, dx, dy)
            mask = ImageChops.multiply(r.split()[3], mm.split()[3]).point(lambda v: 255 if v > 200 else 0)
            st = ImageStat.Stat(ImageChops.difference(r.convert("RGB"), mm.convert("RGB")), mask=mask)
            v = sum(st.mean) / 3.0
            if best is None or v < best[0]:
                best = (v, dx, dy)
    v0, dx0, dy0 = best
    for dy in range(dy0 - 2, dy0 + 3):
        for dx in range(dx0 - 2, dx0 + 3):
            mm = ImageChops.offset(m, dx, dy)
            mask = ImageChops.multiply(r.split()[3], mm.split()[3]).point(lambda v: 255 if v > 200 else 0)
            st = ImageStat.Stat(ImageChops.difference(r.convert("RGB"), mm.convert("RGB")), mask=mask)
            v = sum(st.mean) / 3.0
            if v < best[0]:
                best = (v, dx, dy)
    k = 1.0 / sw
    return int(round(best[1] * k)), int(round(best[2] * k)), best[0]


def coverage(ref, mov):
    mask = ImageChops.multiply(ref.split()[3], mov.split()[3]).point(lambda v: 255 if v > 200 else 0)
    d = ImageChops.difference(ref.convert("RGB"), mov.convert("RGB")).convert("L")
    hi = ImageChops.multiply(d.point(lambda x: 255 if x > 30 else 0), mask)
    n1 = sum(1 for v in mask.getdata() if v > 0)
    n2 = sum(1 for v in hi.getdata() if v > 0)
    return 100.0 * n2 / max(1, n1)


def paw_overlay(ref, mov, scale, box, canvas_size):
    """把 mov 相对 ref 的变化区域抠成一张贴片（放到统一画布坐标）。"""
    d = ImageChops.difference(ref.convert("RGB"), mov.convert("RGB")).convert("L")
    m = d.point(lambda x: 255 if x > 18 else 0).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(2.0))
    m = ImageChops.multiply(m, mov.split()[3])
    out = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    # 变化区域裁到 box 内再贴
    x0, y0 = int(round(-box[0] * scale)), int(round(-box[1] * scale))
    crop = mov.crop(box)
    w = max(1, int(round(crop.width * scale)))
    h = max(1, int(round(crop.height * scale)))
    crop = crop.resize((w, h), Image.LANCZOS)
    mk = m.crop(box).resize((w, h), Image.LANCZOS)
    crop.putalpha(ImageChops.multiply(crop.split()[3], mk))
    out.alpha_composite(crop, (x0, y0))
    return out


def prep(name, src_dir):
    imgs = {}
    for p in SIT_POSES + OTHER_POSES:
        f = os.path.join(src_dir, "%s_%s.png" % (name, p))
        if os.path.exists(f):
            imgs[p] = Image.open(f).convert("RGBA")
        else:
            print("  MISSING", f)
    if "sit" not in imgs:
        print("  no sit, skip")
        return

    ref = imgs["sit"]
    shifts = {}
    cover = {}
    for p in KNEAD:
        if p not in imgs:
            continue
        dx, dy, v = find_shift(ref, imgs[p])
        shifts[p] = (dx, dy)
        imgs[p] = ImageChops.offset(imgs[p], dx, dy)
        cover[p] = coverage(ref, imgs[p])

    # 联合裁切框
    boxes = [alpha_bbox(imgs[p]) for p in SIT_POSES if p in imgs]
    box = (min(b[0] for b in boxes), min(b[1] for b in boxes),
           max(b[2] for b in boxes), max(b[3] for b in boxes))
    scale = SIT_H / float(box[3] - box[1])
    cw = int(round((box[2] - box[0]) * scale)) + PAD * 2
    ch = SIT_H + PAD * 2

    for p in SIT_POSES:
        if p not in imgs:
            continue
        crop = imgs[p].crop(box)
        crop = crop.resize((max(1, int(round(crop.width * scale))), SIT_H), Image.LANCZOS)
        canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        canvas.alpha_composite(crop, ((cw - crop.width) // 2, PAD))
        canvas.save(os.path.join(ASSETS, "%s_%s.png" % (name, p)), optimize=True)

    # 差异集中（真的只是抬爪转身）→ 打标记给程序做「硬切换」抬爪动画。
    # 不做半透明叠加：两张图的五官/轮廓也有 1px 级差异，叠加会出现重影。
    made = []
    worst = max(cover.values()) if cover else 999
    for tag in ["A", "B"]:
        f = os.path.join(ASSETS, "%s_hit%s.png" % (name, tag))
        if os.path.exists(f):
            os.remove(f)
        f2 = os.path.join(ASSETS, "%s_paw%s.png" % (name, tag))
        if os.path.exists(f2):
            os.remove(f2)
    if cover and worst < COVER_LIMIT:
        for p, tag in zip(KNEAD, ["A", "B"]):
            if p not in imgs:
                continue
            Image.open(os.path.join(ASSETS, "%s_%s.png" % (name, p))).save(
                os.path.join(ASSETS, "%s_hit%s.png" % (name, tag)), optimize=True)
            made.append(tag)

    for p in OTHER_POSES:
        if p not in imgs:
            continue
        b = alpha_bbox(imgs[p])
        crop = imgs[p].crop(b)
        s = min(1.0, OTHER_MAX / float(max(crop.size)))
        if s < 1.0:
            crop = crop.resize((max(1, int(crop.width * s)), max(1, int(crop.height * s))), Image.LANCZOS)
        canvas = Image.new("RGBA", (crop.width + PAD * 2, crop.height + PAD * 2), (0, 0, 0, 0))
        canvas.alpha_composite(crop, (PAD, PAD))
        canvas.save(os.path.join(ASSETS, "%s_%s.png" % (name, p)), optimize=True)

    cv = ", ".join("%s %.1f%%" % (k.replace("sit_", ""), v) for k, v in cover.items())
    print("%-18s 对齐 %s  差异 %s  -> %s" % (
        name,
        " ".join("%s(%d,%d)" % (k[-1], v[0], v[1]) for k, v in shifts.items()) or "-",
        cv or "-",
        ("抬爪 " + "+".join(made)) if made else "整只轻压（帧差异太大）"))


def main(names):
    for name in names:
        src = os.path.join(ROOT, "incoming", name)
        if not os.path.isdir(src):
            print(name, "-> no incoming dir")
            continue
        prep(name, src)


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        args = [d for d in sorted(os.listdir(os.path.join(ROOT, "incoming")))
                if os.path.isdir(os.path.join(ROOT, "incoming", d)) and not d.startswith("_")]
    main(args)
