"""按程序当前的绘制高度，把各姿势并排画出来（同一基线），看体量差多少。"""
import os
import sys

from PIL import Image

A = r"D:\Hermes\DesktopCat\assets"
OUT = r"D:\Hermes\DesktopCat\frames"
# 程序当前用的显示高度（Zoom=1）
TARGET = {"sit": 186, "sit_left": 186, "sit_right": 186, "sit_closed": 186,
          "walk": 150, "sleep": 186, "stretch": 230}
PAD = 10


def render(name, poses, h_override=None):
    ims = []
    for p in poses:
        f = os.path.join(A, "%s_%s.png" % (name, p))
        if not os.path.exists(f):
            continue
        im = Image.open(f).convert("RGBA")
        h = (h_override or TARGET).get(p, 186)
        s = h / float(im.height)
        im = im.resize((max(1, int(im.width * s)), h), Image.LANCZOS)
        ims.append((p, im))
    W = sum(i.width + PAD for _, i in ims) + PAD
    H = max(i.height for _, i in ims) + PAD * 2
    out = Image.new("RGB", (W, H), (247, 244, 239))
    x = PAD
    for p, im in ims:
        out.paste(im, (x, H - PAD - im.height), im)
        x += im.width + PAD
    f = os.path.join(OUT, "pose_%s.png" % name)
    out.save(f)
    return f, out.size


if __name__ == "__main__":
    names = sys.argv[1:] or ["penguin", "fox", "cat"]
    for n in names:
        f, sz = render(n, ["sit", "walk", "sleep", "stretch"])
        print(n, f, sz)
