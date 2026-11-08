p = "src/SpriteArt.cs"
s = open(p, encoding="utf-8").read()

helper = '''
        // ───────── 爬墙 / 天花板：把姿势图整体转成所需朝向，一次算好缓存起来 ─────────
        class RotSprite
        {
            public Bitmap Bmp;
            public int Left, Top, Right, Bottom;   // 相对整块画布的不透明边界
        }

        static readonly Dictionary<string, RotSprite> RotCache = new Dictionary<string, RotSprite>();

        static RotSprite BuildRot(string key, string pose, float ang, bool flip, float targetH, float maxW)
        {
            string id = key + "|" + pose + "|" + ang.ToString("0") + "|" + flip + "|" + targetH.ToString("0.0");
            RotSprite rs;
            if (RotCache.TryGetValue(id, out rs)) return rs;

            var bmp = Get(key + "_" + pose);
            if (bmp == null) return null;

            float sA = AreaScale(key, key + "_" + pose, bmp, targetH);
            float s = Math.Min(sA, Math.Min(maxW / bmp.Width, targetH * 1.35f / bmp.Height));
            int w = Math.Max(1, (int)(bmp.Width * s)), h = Math.Max(1, (int)(bmp.Height * s));

            using (var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var sg = Graphics.FromImage(scaled))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    sg.DrawImage(bmp, new Rectangle(0, 0, w, h));
                }
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
                    rg.TranslateTransform(cw / 2f, ch / 2f);
                    rg.RotateTransform(ang);
                    if (flip) rg.ScaleTransform(-1, 1);
                    rg.DrawImage(scaled, new RectangleF(-w / 2f, -h / 2f, w, h));
                }
                int l = cw, t = ch, r = -1, bo = -1;
                for (int yy = 0; yy < ch; yy++)
                    for (int xx = 0; xx < cw; xx++)
                        if (rot.GetPixel(xx, yy).A > 40)
                        {
                            if (xx < l) l = xx; if (xx > r) r = xx;
                            if (yy < t) t = yy; if (yy > bo) bo = yy;
                        }
                if (r < 0) { l = t = 0; r = cw - 1; bo = ch - 1; }
                rs = new RotSprite { Bmp = rot, Left = l, Top = t, Right = r, Bottom = bo };
                RotCache[id] = rs;
                return rs;
            }
        }

        /// <summary>爬墙：整只按指定朝向摆放，身体那一侧贴着墙线，垂直方向居中。</summary>
        public static bool DrawWall(Graphics g, string key, bool wallLeft, float frameW, float frameH,
            float targetH, float maxW, double t, double phase)
        {
            // 墙上的朝向：把站立图逆时针转 90° 再镜像（头朝内、四肢贴墙），左墙用镜像版、右墙用另一版
            var rs = BuildRot(key, "walk", -90f, wallLeft, targetH, maxW);
            if (rs == null) return false;
            float bob = (float)Math.Sin(phase * 2 * Math.PI) * 2.5f;
            float x = wallLeft ? -rs.Left : (frameW - rs.Right);
            float y = (frameH - (rs.Bottom - rs.Top)) / 2f - rs.Top + bob;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rs.Bmp, new RectangleF(x, y, rs.Bmp.Width, rs.Bmp.Height));
            return true;
        }

        /// <summary>天花板：整只倒过来，脚贴着屏幕顶边。</summary>
        public static bool DrawCeil(Graphics g, string key, bool fromLeft, float frameW, float targetH, float maxW,
            double t, double phase)
        {
            var rs = BuildRot(key, "walk", 180f, fromLeft, targetH, maxW);
            if (rs == null) return false;
            float bob = (float)Math.Sin(phase * 2 * Math.PI) * 2.5f;
            float x = frameW / 2f - (rs.Left + rs.Right) / 2f + bob;
            float y = -rs.Top;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rs.Bmp, new RectangleF(x, y, rs.Bmp.Width, rs.Bmp.Height));
            return true;
        }

        static readonly Dictionary<string, int> TopPadCache = new Dictionary<string, int>();'''

anchor = "        static readonly Dictionary<string, int> TopPadCache = new Dictionary<string, int>();"
assert anchor in s
s = s.replace(anchor, helper, 1)
open(p, "w", encoding="utf-8", newline="\n").write(s)
print("spriteart ok")
