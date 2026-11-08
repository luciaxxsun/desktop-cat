import re

p = "src/SpriteArt.cs"
s = open(p, encoding="utf-8").read()

start = s.index("        /// <summary>爬墙：整只按指定朝向摆放")
end = s.index("        static readonly Dictionary<string, int> TopPadCache")
new_block = '''        /// <summary>
        /// 爬墙：朝向跟着走向走（Lucia 定的对应表）
        ///   往上爬：左墙=不转+镜像，右墙=不转
        ///   往下爬：左墙=顺时针90，右墙=顺时针90+镜像
        /// 身体那一侧贴着墙线，垂直方向居中，只加一点上下踮动。
        /// </summary>
        public static bool DrawWall(Graphics g, string key, bool wallLeft, bool goingUp, float frameW, float frameH,
            float targetH, float maxW, double t, double phase)
        {
            float ang = goingUp ? 0f : 90f;                 // GDI+ 正角度=顺时针
            bool flip = goingUp ? wallLeft : !wallLeft;
            var rs = BuildRot(key, "walk", ang, flip, targetH, maxW);
            if (rs == null) return false;
            float bob = (float)Math.Sin(phase * 2 * Math.PI) * 2.5f;
            float x = wallLeft ? -rs.Left : (frameW - rs.Right - 1);
            float y = (frameH - (rs.Bottom - rs.Top)) / 2f - rs.Top + bob;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rs.Bmp, new RectangleF(x, y, rs.Bmp.Width, rs.Bmp.Height));
            return true;
        }

        /// <summary>
        /// 天花板：整只倒过来，脚贴着屏幕顶边。
        ///   往右走=180+镜像，往左走=180（Lucia 定的）
        /// </summary>
        public static bool DrawCeil(Graphics g, string key, bool movingRight, float frameW, float targetH, float maxW,
            double t, double phase)
        {
            var rs = BuildRot(key, "walk", 180f, movingRight, targetH, maxW);
            if (rs == null) return false;
            float bob = (float)Math.Sin(phase * 2 * Math.PI) * 2.0f;
            float x = frameW / 2f - (rs.Left + rs.Right) / 2f;
            float y = -rs.Top + bob;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rs.Bmp, new RectangleF(x, y, rs.Bmp.Width, rs.Bmp.Height));
            return true;
        }

'''
s = s[:start] + new_block + s[end:]

# BuildRot：先镜像（在图片自身坐标系）再旋转，避免镜像轴跟着转
old_start = s.index("        static RotSprite BuildRot(")
old_end = s.index("        /// <summary>\n        /// 爬墙：朝向跟着走向走")
build = '''        static RotSprite BuildRot(string key, string pose, float ang, bool flip, float targetH, float maxW)
        {
            string id = key + "|" + pose + "|" + ang.ToString("0") + "|" + flip + "|" + targetH.ToString("0.0");
            RotSprite rs;
            if (RotCache.TryGetValue(id, out rs)) return rs;

            var bmp = Get(key + "_" + pose);
            if (bmp == null) return null;

            float sA = AreaScale(key, key + "_" + pose, bmp, targetH);
            float s = Math.Min(sA, Math.Min(maxW / bmp.Width, targetH * 1.35f / bmp.Height));
            int w = Math.Max(1, (int)(bmp.Width * s)), h = Math.Max(1, (int)(bmp.Height * s));

            // 1) 缩放；需要镜像就在图片自身坐标里先翻好
            using (var src = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var sg = Graphics.FromImage(src))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    if (flip)
                    {
                        sg.TranslateTransform(w, 0);
                        sg.ScaleTransform(-1, 1);
                    }
                    sg.DrawImage(bmp, new Rectangle(0, 0, w, h));
                }

                // 2) 再整体旋转
                double rad = ang * Math.PI / 180.0;
                int cw = (int)Math.Ceiling(Math.Abs(w * Math.Cos(rad)) + Math.Abs(h * Math.Sin(rad))) + 4;
                int ch = (int)Math.Ceiling(Math.Abs(w * Math.Sin(rad)) + Math.Abs(h * Math.Cos(rad))) + 4;
                var rot = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                using (var rg = Graphics.FromImage(rot))
                {
                    rg.Clear(Color.Transparent);
                    rg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    rg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    rg.SmoothingMode = SmoothingMode.AntiAlias;
                    if (Math.Abs(ang) > 0.01f)
                    {
                        rg.TranslateTransform(cw / 2f, ch / 2f);
                        rg.RotateTransform(ang);
                        rg.DrawImage(src, new RectangleF(-w / 2f, -h / 2f, w, h));
                    }
                    else
                    {
                        rg.DrawImage(src, new RectangleF((cw - w) / 2f, (ch - h) / 2f, w, h));
                    }
                }

                // 3) 量不透明边界，用来贴墙/贴顶对齐
                int l = cw, t = ch, r = -1, bo = -1;
                for (int yy = 0; yy < ch; yy++)
                    for (int xx = 0; xx < cw; xx++)
                        if (rot.GetPixel(xx, yy).A > 40)
                        {
                            if (xx < l) l = xx;
                            if (xx > r) r = xx;
                            if (yy < t) t = yy;
                            if (yy > bo) bo = yy;
                        }
                if (r < 0) { l = t = 0; r = cw - 1; bo = ch - 1; }
                rs = new RotSprite { Bmp = rot, Left = l, Top = t, Right = r, Bottom = bo };
                RotCache[id] = rs;
                return rs;
            }
        }

'''
s = s[:old_start] + build + s[old_end:]
open(p, "w", encoding="utf-8", newline="\n").write(s)
print("spriteart ok")
