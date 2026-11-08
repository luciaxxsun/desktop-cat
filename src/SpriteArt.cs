using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;

namespace DesktopCat
{
    /// <summary>
    /// 贴图渲染：素材是 Seedream 生成 + 蓝幕抠图得到的透明 PNG。
    /// 缺素材时返回 false，调用方退回程序画法。
    /// </summary>
    static class SpriteArt
    {
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();
        public static string Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
        public static bool Enabled = true;

        public static Bitmap Get(string name)
        {
            if (!Enabled) return null;
            Bitmap b;
            if (Cache.TryGetValue(name, out b)) return b;
            try
            {
                // 先找内嵌资源（单文件 exe 才不会丢素材），再找同目录 assets
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var es = asm.GetManifestResourceStream(name + ".png"))
                {
                    if (es != null)
                    {
                        using (var t = Image.FromStream(es)) b = new Bitmap(t);
                        Cache[name] = b;
                        return b;
                    }
                }
                var p = Path.Combine(Dir, name + ".png");
                if (!File.Exists(p)) { Cache[name] = null; return null; }
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                using (var tmp = new Bitmap(fs))
                    b = new Bitmap(tmp);
            }
            catch { b = null; }
            Cache[name] = b;
            return b;
        }

        public static bool Has(string name) { return Get(name) != null; }

        static void DrawImg(Graphics g, Bitmap bmp, RectangleF dest)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bmp, dest);
        }

        // ---------------- 眼睛（程序化：瞳孔跟鼠标 + 眨眼） ----------------
        class EyeInfo
        {
            public float X, Y, R, IrisR;
            public Color Iris, Fur;
            public Color[] Ring;
        }
        static readonly Dictionary<string, EyeInfo[]> EyeCache = new Dictionary<string, EyeInfo[]>();

        static int Luma(Color c) { return (c.R * 299 + c.G * 587 + c.B * 114) / 1000; }

        static EyeInfo[] GetEyes(string key)
        {
            EyeInfo[] res;
            if (EyeCache.TryGetValue(key, out res)) return res;
            var bmp = Get(key + "_sit");
            if (bmp == null) { EyeCache[key] = null; return null; }
            int w = bmp.Width, h = bmp.Height;
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[bd.Stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);
            int stride = bd.Stride;
            Func<int, int, Color> px = (x, y) =>
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return Color.Transparent;
                int o = y * stride + x * 4;
                return Color.FromArgb(buf[o + 3], buf[o + 2], buf[o + 1], buf[o]);
            };

            int y0 = (int)(h * 0.10f), y1 = (int)(h * 0.58f);
            var dark = new List<Point>();
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = y * stride + x * 4;
                    if (buf[o + 3] > 200 && (buf[o] * 299 + buf[o + 1] * 587 + buf[o + 2] * 114) / 1000 < 45)
                        dark.Add(new Point(x, y));
                }
            if (dark.Count < 60) { EyeCache[key] = null; return null; }

            float midX = 0; foreach (var q in dark) midX += q.X; midX /= dark.Count;
            var Lp = dark.FindAll(q => q.X <= midX);
            var Rp = dark.FindAll(q => q.X > midX);

            Func<List<Point>, EyeInfo> mk = pts =>
            {
                if (pts.Count < 20) return null;
                int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = 0, by1 = 0;
                foreach (var q in pts)
                {
                    if (q.X < bx0) bx0 = q.X;
                    if (q.Y < by0) by0 = q.Y;
                    if (q.X > bx1) bx1 = q.X;
                    if (q.Y > by1) by1 = q.Y;
                }
                float cx = (bx0 + bx1) / 2f, cy = (by0 + by1) / 2f;
                float r = (Math.Min(bx1 - bx0, by1 - by0) / 2f);
                r = Math.Max(3f, Math.Min(r, h * 0.03f));

                // 往外扫，找瞳孔→虹膜的颜色跃变，得到虹膜半径
                float irisR = r * 1.7f;
                for (float k = 1.0f; k < 2.6f; k += 0.05f)
                {
                    long rr = 0, gg = 0, bb = 0; int n = 0;
                    for (int a = 0; a < 360; a += 10)
                    {
                        int xx = (int)(cx + Math.Cos(a * Math.PI / 180) * r * k);
                        int yy = (int)(cy + Math.Sin(a * Math.PI / 180) * r * k);
                        Color c = px(xx, yy);
                        if (c.A < 200) continue;
                        rr += c.R; gg += c.G; bb += c.B; n++;
                    }
                    if (n < 8) continue;
                    Color avg = Color.FromArgb((int)(rr / n), (int)(gg / n), (int)(bb / n));
                    if (Luma(avg) > 78) { irisR = r * k; break; }
                }

                // 虹膜按半径分 5 圈取色，画出渐变而不是平涂
                var ring = new Color[5];
                for (int i = 0; i < 5; i++)
                {
                    float k0 = 0.98f + i * 0.30f;
                    long rr = 0, gg = 0, bb = 0; int n = 0;
                    for (int a = 0; a < 360; a += 5)
                        for (int kk = 0; kk < 3; kk++)
                        {
                            float k = k0 + kk * 0.08f;
                            int xx = (int)(cx + Math.Cos(a * Math.PI / 180) * r * k);
                            int yy = (int)(cy + Math.Sin(a * Math.PI / 180) * r * k);
                            Color c = px(xx, yy);
                            if (c.A < 210 || Luma(c) < 40) continue;
                            rr += c.R; gg += c.G; bb += c.B; n++;
                        }
                    ring[i] = n > 8 ? Color.FromArgb(255, (int)(rr / n), (int)(gg / n), (int)(bb / n))
                                    : (i > 0 ? ring[i - 1] : Color.FromArgb(255, 200, 150, 80));
                }

                long fr = 0, fg = 0, fb = 0; int m = 0;
                for (int a = 0; a < 360; a += 5)
                    for (int k = 0; k < 4; k++)
                    {
                        int xx = (int)(cx + Math.Cos(a * Math.PI / 180) * r * (2.0f + k * 0.18f));
                        int yy = (int)(cy + Math.Sin(a * Math.PI / 180) * r * (2.0f + k * 0.18f));
                        Color c = px(xx, yy);
                        if (c.A < 220) continue;
                        fr += c.R; fg += c.G; fb += c.B; m++;
                    }

                return new EyeInfo
                {
                    X = cx, Y = cy, R = r, IrisR = irisR,
                    Iris = ring[0], Fur = m > 10 ? Color.FromArgb(255, (int)(fr / m), (int)(fg / m), (int)(fb / m)) : Color.FromArgb(255, 240, 230, 215),
                    Ring = ring
                };
            };

            var e1 = mk(Lp); var e2 = mk(Rp);
            if (e1 == null || e2 == null) { EyeCache[key] = null; return null; }
            res = new[] { e1, e2 };
            EyeCache[key] = res;
            return res;
        }

        static void DrawEyes(Graphics sg, string key, float s, float lookX, float lookY, float blink)
        {
            var eyes = GetEyes(key);
            if (eyes == null) return;
            foreach (var e in eyes)
            {
                float cx = e.X * s, cy = e.Y * s, r = e.R * s, ir = e.IrisR * s;
                if (blink > 0.02f)
                {
                    float cover = Math.Min(1f, blink) * 2.15f * ir;
                    using (var br = new SolidBrush(e.Fur))
                        sg.FillRectangle(br, cx - ir * 1.15f, cy - ir * 1.06f, ir * 2.3f, cover);
                    if (blink > 0.75f)
                        using (var pen = new Pen(Color.FromArgb(80, 40, 30, 20), Math.Max(1.2f, r * 0.24f)))
                            sg.DrawArc(pen, cx - ir * 0.8f, cy - ir * 0.5f, ir * 1.6f, ir * 1.0f, 15, 150);
                    continue;
                }

                float maxOff = ir * 0.34f;
                float ox = Math.Max(-1f, Math.Min(1f, lookX)) * maxOff;
                float oy = Math.Max(-1f, Math.Min(1f, lookY)) * maxOff * 0.8f;

                // 用原虹膜的同心色环把原瞳孔盖掉（不是平涂一色）
                for (int i = 4; i >= 0; i--)
                {
                    float rr2 = r * (1.02f + i * 0.30f);
                    if (rr2 > ir * 1.02f) rr2 = ir * 1.02f;
                    using (var br = new SolidBrush(e.Ring[Math.Min(i, e.Ring.Length - 1)]))
                        sg.FillEllipse(br, cx - rr2, cy - rr2, rr2 * 2, rr2 * 2);
                }

                // 新瞳孔：大小和原来一致，只挪位置
                using (var br = new SolidBrush(Color.FromArgb(255, 28, 22, 18)))
                    sg.FillEllipse(br, cx + ox - r, cy + oy - r, r * 2, r * 2);
                using (var br = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                    sg.FillEllipse(br, cx + ox - r * 0.55f, cy + oy - r * 0.62f, r * 0.30f, r * 0.26f);
            }
        }

        // ---------------- 眼神三态：正/左/右 + 闭眼，靠 AI 变体切换 ----------------
        class LookState { public string Cur, Prev; public double T0 = -99; }
        static readonly Dictionary<string, LookState> LookCache = new Dictionary<string, LookState>();

        static string LookKey(string key, float lookX, float blink, bool typing)
        {
            if (blink > 0.15f) return key + "_sit_closed";
            if (typing) return key + "_sit";
            bool wasLeft = false, wasRight = false;
            LookState cur;
            if (LookCache.TryGetValue(key, out cur))
            {
                wasLeft = cur.Cur == key + "_sit_left";
                wasRight = cur.Cur == key + "_sit_right";
            }
            // 迟滞：进入 0.22、退出 0.10，避免边缘抖动
            if (lookX < (wasLeft ? -0.10f : -0.22f)) return key + "_sit_left";
            if (lookX > (wasRight ? 0.10f : 0.22f)) return key + "_sit_right";
            return key + "_sit";
        }

        static void DrawImgAlpha(Graphics g, Bitmap bmp, RectangleF dest, float alpha)
        {
            if (alpha >= 0.99f) { g.DrawImage(bmp, dest); return; }
            if (alpha <= 0.01f) return;
            var cm = new ColorMatrix();
            cm.Matrix33 = alpha;
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(bmp,
                    new PointF[] { new PointF(dest.X, dest.Y), new PointF(dest.Right, dest.Y), new PointF(dest.X, dest.Bottom) },
                    new RectangleF(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel, ia);
            }
        }

        static string prevNameOf(string key, string cur)
        {
            return key + "_sit";
        }

        public static string DebugEyes(string key) { return "n/a"; }

        // ---------------- 踩奶：在坐姿上叠一张「抬爪」帧，按节奏淡入淡出 ----------------
        static void DrawKneading(Graphics sg, string key, int w, int h, float knead, double t)
        {
            var sit = Get(key + "_sit");
            if (sit == null) return;

            double cyc = knead / (2 * Math.PI);
            bool left = ((int)Math.Floor(cyc)) % 2 == 0;
            float phase = (float)(cyc - Math.Floor(cyc));      // 一次按键 0→1

            // ① 有真抬爪帧（Codex 单独生成的左/右抬爪）→ 硬切换：抬起→放下
            var hitA = Get(key + "_hitA");
            var hitB = Get(key + "_hitB");
            if (hitA != null && hitB != null)
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (phase > 0.16f && phase < 0.84f)
                {
                    var f = left ? hitA : hitB;
                    sg.DrawImage(f, new RectangleF(0, -h * 0.006f, w, h));
                }
                else
                {
                    sg.DrawImage(sit, new RectangleF(0, 0, w, h));
                }
                return;
            }

            // ② 没有抬爪帧 → 整只左右压按（不会抖，但只是"压一下"）
            var st = sg.Save();
            sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.SmoothingMode = SmoothingMode.AntiAlias;
            float push = (float)Math.Sin(Math.PI * phase);
            float rdy = -push * h * 0.026f;
            float sx = 1f + push * 0.012f;
            float sy = 1f + push * 0.020f;
            float rot = (left ? -1.5f : 1.5f) * push;
            sg.TranslateTransform(w / 2f, h);
            sg.RotateTransform(rot);
            sg.ScaleTransform(sx, sy);
            sg.TranslateTransform(-w / 2f, -h + rdy);
            sg.DrawImage(sit, new RectangleF(0, 0, w, h));
            sg.Restore(st);
        }

        /// <summary>坐着：呼吸 + 压扁 + 踩奶 + 眼神切换（左/右/闭眼由 AI 变体提供）。</summary>
        public static bool DrawSitting(Graphics g, string key, float cx, float groundY, float targetH,
            double t, float squash, bool sleeping, float maxW = 190f,
            float lookX = 0, float lookY = 0, float blink = 0, float knead = 0, bool typing = false,
            float supersample = 1f)
        {
            bool isSleep = sleeping;
            string name = sleeping ? key + "_sleep" : LookKey(key, lookX, blink, typing);
            var bmp = Get(name);
            if (bmp == null) { isSleep = false; name = key + "_sit"; bmp = Get(name); }
            if (bmp == null) return false;

            LookState st;
            if (!LookCache.TryGetValue(key, out st)) { st = new LookState { Cur = name, Prev = name }; LookCache[key] = st; }
            if (st.Cur != name) { st.Prev = st.Cur; st.Cur = name; st.T0 = t; }
            float p = (float)Math.Min(1.0, Math.Max(0.0, (t - st.T0) / 0.16));
            Bitmap prevBmp = p < 1f ? Get(st.Prev) : null;

            float s;
            if (isSleep)
            {
                // 趴着：按剪影面积对齐坐姿，不然躺平后不是显大就是显小
                s = AreaScale(key, name, bmp, targetH);
                if (bmp.Width * s > maxW) s = maxW / bmp.Width;
            }
            else
            {
                s = Math.Min(targetH / bmp.Height, maxW / bmp.Width);
            }
            float w = bmp.Width * s, h = bmp.Height * s;
            float breathe = (float)Math.Sin(t * (isSleep ? 1.3 : 2.0)) * (isSleep ? 1.2f : 1.8f);
            float sy = 1f + squash * 0.10f - breathe / h * 2f;
            float sx = 1f + squash * 0.14f + breathe / h;
            float dw = w * sx, dh = h * sy;

            float ss = Math.Max(1f, supersample);
            int bw = Math.Max(1, (int)(w * ss)), bh = Math.Max(1, (int)(h * ss));
            using (var stage = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
            {
                using (var sg = Graphics.FromImage(stage))
                {
                    sg.Clear(Color.Transparent);
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    if (!isSleep && typing) DrawKneading(sg, key, bw, bh, knead, t);
                    else
                    {
                        if (prevBmp != null && p < 1f) sg.DrawImage(prevBmp, new Rectangle(0, 0, bw, bh));
                        DrawImgAlpha(sg, bmp, new RectangleF(0, 0, bw, bh), p);
                    }
                }
                var dest = new RectangleF(cx - dw / 2f, groundY - dh, dw, dh);
                float lean = Math.Max(-1f, Math.Min(1f, lookX)) * 2.2f;
                if (Math.Abs(lean) > 0.2f && !isSleep)
                {
                    var save = g.Save();
                    g.TranslateTransform(cx, groundY);
                    g.RotateTransform(lean);
                    g.TranslateTransform(-cx, -groundY);
                    g.DrawImage(stage, dest);
                    g.Restore(save);
                }
                else g.DrawImage(stage, dest);
            }
            return true;
        }

        /// <summary>走路：贴图上下轻颠 + 轻微摇摆（贴图本身是侧面迈步帧）。</summary>
        public static bool DrawWalking(Graphics g, string key, float cx, float groundY, float targetH,
            double t, double phase, bool right, float maxW = 200f)
        {
            var bmp = Get(key + "_walk");
            if (bmp == null) return false;

            float sA = AreaScale(key, key + "_walk", bmp, targetH);
            float s = Math.Min(sA, Math.Min(maxW / bmp.Width, targetH * 1.35f / bmp.Height));
            float w = bmp.Width * s, h = bmp.Height * s;
            float bob = (float)(Math.Sin(phase * 2 * Math.PI) * 2.6);
            float tilt = (float)(Math.Sin(phase * 2 * Math.PI) * 1.6);

            var st = g.Save();
            g.TranslateTransform(cx, groundY);
            if (!right) g.ScaleTransform(-1, 1);
            g.RotateTransform(tilt);
            DrawImg(g, bmp, new RectangleF(-w / 2f, -h + bob, w, h));
            g.Restore(st);
            return true;
        }

        static readonly Dictionary<string, Bitmap[]> PieceCache = new Dictionary<string, Bitmap[]>();

        static Bitmap CropFeather(Bitmap src, int y0, int h, int fadeRows)
        {
            h = Math.Min(h, src.Height - y0);
            if (h <= 0) return null;
            var crop = new Bitmap(src.Width, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(crop))
                g.DrawImage(src, new Rectangle(0, 0, src.Width, h), new Rectangle(0, y0, src.Width, h), GraphicsUnit.Pixel);
            if (fadeRows > 0)
            {
                // 底边做 alpha 渐隐，接缝就看不出来
                fadeRows = Math.Min(fadeRows, h);
                for (int y = h - fadeRows; y < h; y++)
                {
                    float k = (y - (h - fadeRows)) / (float)fadeRows;
                    for (int x = 0; x < crop.Width; x++)
                    {
                        var c = crop.GetPixel(x, y);
                        crop.SetPixel(x, y, Color.FromArgb((int)(c.A * (1 - k)), c.R, c.G, c.B));
                    }
                }
            }
            return crop;
        }

        static Bitmap[] GetPieces(string key)
        {
            Bitmap[] pcs;
            if (PieceCache.TryGetValue(key, out pcs)) return pcs;
            var bmp = Get(key + "_sit");
            if (bmp == null) { PieceCache[key] = null; return null; }
            int w = bmp.Width, h = bmp.Height;

            // 逐行量不透明宽度，最细的那一行就是脖子 —— 新素材头大，不能用固定比例切
            int[] rowW = new int[h];
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[bd.Stride * h];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < h; y++)
                {
                    int c = 0, off = y * bd.Stride;
                    for (int x = 0; x < w; x++) if (buf[off + x * 4 + 3] > 40) c++;
                    rowW[y] = c;
                }
            }
            finally { bmp.UnlockBits(bd); }

            int neck = (int)(h * 0.46f), best = int.MaxValue;
            for (int y = (int)(h * 0.28f); y < (int)(h * 0.64f); y++)
            {
                int avg = (rowW[y] + (y > 0 ? rowW[y - 1] : rowW[y]) + (y + 1 < h ? rowW[y + 1] : rowW[y])) / 3;
                if (avg < best) { best = avg; neck = y; }
            }

            int pawTop = (int)(h * 0.78f);
            var head = CropFeather(bmp, 0, neck, (int)(h * 0.06f));
            var body = CropFeather(bmp, Math.Max(0, neck - (int)(h * 0.06f)), pawTop - Math.Max(0, neck - (int)(h * 0.06f)), 0);
            var paws = CropFeather(bmp, pawTop, h - pawTop, 0);
            pcs = new[] { head, body, paws };
            PieceCache[key] = pcs;
            return pcs;
        }

        static readonly Dictionary<string, float> CatWCache = new Dictionary<string, float>();

        /// <summary>量贴图里不透明部分的实际宽度（不含透明留白），用于跨素材等比对齐。</summary>
        static readonly Dictionary<string, float> AreaCache = new Dictionary<string, float>();

        /// <summary>剪影面积（alpha>60 的像素数）——用来做「视觉体量」对齐。</summary>
        static float CatArea(string key, Bitmap b)
        {
            float v;
            if (AreaCache.TryGetValue(key, out v)) return v;
            long n = 0;
            var bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                var buf = new byte[stride * b.Height];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < b.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < b.Width; x++) if (buf[row + x * 4 + 3] > 60) n++;
                }
            }
            finally { b.UnlockBits(bd); }
            v = n > 200 ? n : b.Width * b.Height * 0.3f;
            AreaCache[key] = v;
            return v;
        }

        /// <summary>让某个姿势的「视觉体量」等于坐姿时的缩放系数。</summary>
        static float AreaScale(string key, string poseKey, Bitmap poseBmp, float sitH)
        {
            var sit = Get(key + "_sit");
            if (sit == null) return 1f;
            float sitScreenArea = CatArea(key + "_sit", sit) * (float)Math.Pow(sitH / sit.Height, 2);
            return (float)Math.Sqrt(sitScreenArea / CatArea(poseKey, poseBmp));
        }

        /// <summary>坐姿在屏幕上的「最宽处」像素——所有姿势的体量基准。</summary>
        static float SitCatW(string key, float baseH)
        {
            var sit = Get(key + "_sit");
            if (sit == null) return 120f;
            return CatWidth(key + "_sit", sit) * (baseH / sit.Height);
        }

        static float CatWidth(string key, Bitmap b)
        {
            float v;
            if (CatWCache.TryGetValue(key, out v)) return v;
            int maxRun = 0;
            var bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                var buf = new byte[stride * b.Height];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < b.Height; y++)
                {
                    int run = 0;
                    int row = y * stride;
                    for (int x = 0; x < b.Width; x++)
                    {
                        if (buf[row + x * 4 + 3] > 24) { run++; if (run > maxRun) maxRun = run; }
                        else run = 0;
                    }
                }
            }
            finally { b.UnlockBits(bd); }
            float w = maxRun > 8 ? maxRun : b.Width * 0.5f;
            CatWCache[key] = w;
            return w;
        }

        struct StretchGeo { public float W, H, HeadY, Ang; }

        /// <summary>算拉伸姿态的几何：头贴鼠标、身子往下垂，脚不许穿地。</summary>
        static StretchGeo Geo(string key, PointF basePt, PointF headPt)
        {
            var g = new StretchGeo();
            var bmp = Get(key + "_stretch");
            var sit = Get(key + "_sit");
            if (bmp == null) { g.W = 150; g.H = 200; g.HeadY = headPt.Y; g.Ang = 0; return g; }

            float dx = headPt.X - basePt.X, dy = headPt.Y - basePt.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 60) len = 60;
            g.Ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90f;

            // 提起图现在是"自然体型"（不是细长条），所以按剪影面积对齐坐姿 —— 和走路/睡觉同一套规则。
            // 再用 k 做纵向拉伸（拉得越远越长），上限 1.30，避免拉成面条。
            float s = AreaScale(key, key + "_stretch", bmp, 186f * ZoomStretch);
            // 面积对齐之外再给一条下限：猫身宽不小于坐姿的 0.80（早期 fal 的提起图是细长条，
            // 只按面积算会瘦成一条，看着像"缩水"）
            var sitB = Get(key + "_sit");
            if (sitB != null)
            {
                float sitW = CatWidth(key + "_sit", sitB) * (186f * ZoomStretch / sitB.Height);
                float floorS = (sitW * 0.80f) / CatWidth(key + "_stretch", bmp);
                if (floorS > s) s = floorS;
            }
            g.W = bmp.Width * s;
            float naturalH = bmp.Height * s;
            float k = 0.94f + (len - 180f) / 1400f;
            if (k < 0.94f) k = 0.94f;
            if (k > 1.30f) k = 1.30f;
            g.H = naturalH * k;

            // 头跟着鼠标，但整只猫的脚不能穿到地面以下
            float lowestHead = basePt.Y - g.H * 0.96f;
            g.HeadY = Math.Min(headPt.Y, lowestHead);
            return g;
        }

        /// <summary>拉伸时窗口该多大（含旋转余量），保证头不会被切。</summary>
        public static RectangleF StretchBounds(string key, PointF basePt, PointF headPt)
        {
            var g = Geo(key, basePt, headPt);
            float rad = (float)(g.Ang * Math.PI / 180.0);
            float cos = Math.Abs((float)Math.Cos(rad)), sin = Math.Abs((float)Math.Sin(rad));
            float bw = g.W * cos + g.H * sin;
            float bh = g.W * sin + g.H * cos;
            return new RectangleF(headPt.X - bw / 2f - 30f, g.HeadY - 30f, bw + 60f, bh + 70f);
        }

        /// <summary>被拎起来：头锚在鼠标，身子往下垂；拉得越远越长，压到地面就停住。</summary>
        public static bool DrawStretched(Graphics g, string key, PointF basePt, PointF headPt, double t)
        {
            var bmp = Get(key + "_stretch");
            if (bmp == null) return false;
            var geo = Geo(key, basePt, headPt);
            float sway = (float)Math.Sin(t * 2.2) * 2.0f;

            var st = g.Save();
            g.TranslateTransform(headPt.X, geo.HeadY);
            g.RotateTransform(geo.Ang + sway);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bmp, new RectangleF(-geo.W / 2f, -geo.H * 0.04f, geo.W, geo.H));
            g.Restore(st);
            return true;
        }

        public static float ZoomStretch = 1f;

        /// <summary>落地压扁时也要能画（复用坐着）。</summary>
        public static void ClearCache() { Cache.Clear(); }
    }
}
