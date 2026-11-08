using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopCat
{
    static class Art
    {
        public const int VW = 260, VH = 300;
        const float GROUND = 208f;

        // ---------- 基础工具 ----------
        static GraphicsPath Blob(RectangleF r, float k)
        {
            var p = new GraphicsPath();
            float w = r.Width, h = r.Height, cx = r.X + w / 2, cy = r.Y + h / 2;
            float rx = w / 2, ry = h / 2;
            float kx = rx * k, ky = ry * k;
            p.AddBezier(r.X, cy, r.X, cy - ky, cx - kx, r.Y, cx, r.Y);
            p.AddBezier(cx, r.Y, cx + kx, r.Y, r.Right, cy - ky, r.Right, cy);
            p.AddBezier(r.Right, cy, r.Right, cy + ky, cx + kx, r.Bottom, cx, r.Bottom);
            p.AddBezier(cx, r.Bottom, cx - kx, r.Bottom, r.X, cy + ky, r.X, cy);
            p.CloseFigure();
            return p;
        }


        static Color Lighten(Color c, int d)
        { return Color.FromArgb(c.A, Math.Min(255, c.R + d), Math.Min(255, c.G + d), Math.Min(255, c.B + d)); }

        static Color Darken(Color c, int d)
        { return Color.FromArgb(c.A, Math.Max(0, c.R - d), Math.Max(0, c.G - d), Math.Max(0, c.B - d)); }

        // 黏土感：中心偏上打亮 + 边缘压暗 + 一圈淡淡描边
        static void Clay(Graphics g, GraphicsPath path, Color baseC, float lightAmt = 24f, float rimAlpha = 42f)
        {
            var b = path.GetBounds();
            if (b.Width < 2 || b.Height < 2) return;
            using (var br = new PathGradientBrush(path))
            {
                br.CenterPoint = new PointF(b.X + b.Width * .44f, b.Y + b.Height * .32f);
                br.CenterColor = Lighten(baseC, (int)lightAmt);
                br.SurroundColors = new[] { Darken(baseC, 16) };
                br.FocusScales = new PointF(.52f, .52f);
                g.FillPath(br, path);
            }
            using (var pen = new Pen(Color.FromArgb((int)rimAlpha, Darken(baseC, 46)), 1.15f))
                g.DrawPath(pen, path);
        }
        static void Grad(Graphics g, GraphicsPath path, Color top, Color bottom, float angle = 90f)
        {
            var b = path.GetBounds();
            if (b.Width < 1 || b.Height < 1) return;
            using (var br = new LinearGradientBrush(
                new RectangleF(b.X - 2, b.Y - 2, b.Width + 4, b.Height + 4), top, bottom, angle))
                g.FillPath(br, path);
        }

        static void SoftShadow(Graphics g, float cx, float cy, float rx, float ry, int alpha)
        {
            for (int i = 3; i >= 0; i--)
            {
                float s = 1f + i * 0.10f;
                using (var b = new SolidBrush(Color.FromArgb(alpha / (i + 1), 108, 96, 84)))
                    g.FillEllipse(b, cx - rx * s, cy - ry * s, rx * 2 * s, ry * 2 * s);
            }
        }

        // 渐细尾巴：沿三次贝塞尔的变宽管
        static void TaperedTail(Graphics g, PointF p0, PointF p1, PointF p2, PointF p3, float w0, float w1, Color c)
        {
            const int N = 26;
            var left = new PointF[N + 1];
            var right = new PointF[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                float mt = 1 - t;
                float x = mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
                float y = mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y;
                // 切线
                float dx = 3 * mt * mt * (p1.X - p0.X) + 6 * mt * t * (p2.X - p1.X) + 3 * t * t * (p3.X - p2.X);
                float dy = 3 * mt * mt * (p1.Y - p0.Y) + 6 * mt * t * (p2.Y - p1.Y) + 3 * t * t * (p3.Y - p2.Y);
                float len = (float)Math.Sqrt(dx * dx + dy * dy); if (len < .0001f) len = 1;
                float nx = -dy / len, ny = dx / len;
                float w = w0 + (w1 - w0) * t;
                w *= (float)(1 - 0.35 * Math.Pow(Math.Max(0, t - 0.75) / 0.25, 2)); // 尾尖收圆
                left[i] = new PointF(x + nx * w / 2, y + ny * w / 2);
                right[i] = new PointF(x - nx * w / 2, y - ny * w / 2);
            }
            using (var path = new GraphicsPath())
            {
                path.AddCurve(left, 0.0f);
                var rev = new PointF[N + 1];
                for (int i = 0; i <= N; i++) rev[i] = right[N - i];
                path.AddCurve(rev, 0.0f);
                path.CloseFigure();
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
            }
        }

        // 耳朵：带弧底
        static void Ear(Graphics g, float x, float y, float w, float h, float tilt, Color outer, Color inner)
        {
            var p = new GraphicsPath();
            p.AddBezier(x, y, x + w * .18f, y - h * .85f, x + w * .55f, y - h * 1.02f, x + w * .62f, y - h);
            p.AddBezier(x + w * .62f, y - h, x + w * .92f, y - h * .55f, x + w, y - h * .18f, x + w, y);
            p.AddBezier(x + w, y, x + w * .5f, y + h * .10f, x, y + h * .10f, x, y);
            p.CloseFigure();
            using (var b = new SolidBrush(outer)) g.FillPath(b, p);
            var q = new GraphicsPath();
            q.AddBezier(x + w * .22f, y - h * .12f, x + w * .30f, y - h * .62f, x + w * .50f, y - h * .74f, x + w * .55f, y - h * .70f);
            q.AddBezier(x + w * .55f, y - h * .70f, x + w * .70f, y - h * .44f, x + w * .76f, y - h * .16f, x + w * .76f, y - h * .06f);
            q.AddBezier(x + w * .76f, y - h * .06f, x + w * .5f, y + h * .02f, x + w * .30f, y, x + w * .22f, y - h * .12f);
            q.CloseFigure();
            using (var b = new SolidBrush(inner)) g.FillPath(b, q);
        }

        // 杏仁眼
        static void AlmondEye(Graphics g, float cx, float cy, float w, float h, float lookX, float lookY,
            bool closed, bool sleepy)
        {
            if (closed || sleepy)
            {
                using (var pen = new Pen(Color.FromArgb(96, 68, 52), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, cx - w * .5f, cy - h * .35f, w, h * .8f, 12, 156);
                return;
            }
            // 眼白
            var sclera = new GraphicsPath();
            sclera.AddBezier(cx - w * .5f, cy, cx - w * .34f, cy - h * .62f, cx + w * .34f, cy - h * .62f, cx + w * .5f, cy);
            sclera.AddBezier(cx + w * .5f, cy, cx + w * .30f, cy + h * .58f, cx - w * .30f, cy + h * .58f, cx - w * .5f, cy);
            sclera.CloseFigure();
            using (var b = new SolidBrush(Color.FromArgb(255, 252, 246))) g.FillPath(b, sclera);

            var old = g.Clip;
            g.SetClip(sclera, CombineMode.Intersect);
            float ix = cx + lookX * w * .16f, iy = cy + lookY * h * .18f + h * .04f;
            using (var iris = new SolidBrush(Color.FromArgb(88, 62, 46)))
                g.FillEllipse(iris, ix - w * .30f, iy - h * .52f, w * .60f, h * 1.04f);
            using (var pupil = new SolidBrush(Color.FromArgb(36, 26, 20)))
                g.FillEllipse(pupil, ix - w * .13f, iy - h * .40f, w * .26f, h * .80f);
            using (var hl = new SolidBrush(Color.FromArgb(255, 255, 255)))
            {
                g.FillEllipse(hl, ix - w * .19f, iy - h * .40f, w * .16f, h * .22f);
                g.FillEllipse(hl, ix + w * .05f, iy + h * .12f, w * .07f, h * .09f);
            }
            g.Clip = old;

            using (var pen = new Pen(Color.FromArgb(120, 76, 56), 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawBezier(pen, cx - w * .5f, cy, cx - w * .34f, cy - h * .62f, cx + w * .34f, cy - h * .62f, cx + w * .5f, cy);
            }
        }

        static void Patches(Graphics g, GraphicsPath clip, Palette p, RectangleF[] blobs)
        {
            if (!p.Patches) return;
            var old = g.Clip;
            g.SetClip(clip, CombineMode.Intersect);
            for (int i = 0; i < blobs.Length; i++)
            {
                Color c = (i % 2 == 0) ? p.Patch1 : p.Patch2;
                if (c.A == 0) continue;
                using (var b = new SolidBrush(c))
                    g.FillEllipse(b, blobs[i]);
            }
            g.Clip = old;
        }

        static void Stripes(Graphics g, GraphicsPath clip, Color c, RectangleF[] bars)
        {
            var old = g.Clip;
            g.SetClip(clip, CombineMode.Intersect);
            using (var b = new SolidBrush(Color.FromArgb(58, c)))
                foreach (var r in bars) g.FillRectangle(b, r);
            g.Clip = old;
        }

        // ================= 坐姿 =================
        public static void DrawSitting(Graphics g, Palette pal, double t,
            float lookX, float lookY, float blink, float sleep, float yawn, float knead, bool faceRight)
        {
            bool asleep = sleep > .35f;
            float breathe = (float)(Math.Sin(t * 2.0) * 2.0);
            float drop = asleep ? 9f : 0f;

            SoftShadow(g, 100, GROUND + 4, 44, 9, 30);

            // 尾巴（在身后）
            double sway = Math.Sin(t * 1.4) * 7;
            TaperedTail(g, new PointF(126, 200), new PointF(178, 202),
                new PointF(194 + (float)sway, 176), new PointF(180 + (float)sway, 146),
                15f, 6f, pal.Dark);

            // 身体
            float bodyTop = 136 + breathe * .5f;
            var bodyRect = new RectangleF(56, bodyTop, 88, GROUND - bodyTop - 2);
            using (var body = Blob(bodyRect, .46f))
            {
                Clay(g, body, pal.Base);
                Patches(g, body, pal, new[] {
                    new RectangleF(46, bodyTop + 10, 52, 44),
                    new RectangleF(112, bodyTop + 34, 46, 40),
                    new RectangleF(78, bodyTop + 56, 40, 30) });
                if (pal.Stripes)
                    Stripes(g, body, pal.Dark, new[] {
                        new RectangleF(70, bodyTop + 16, 7, 34),
                        new RectangleF(92, bodyTop + 12, 7, 40),
                        new RectangleF(114, bodyTop + 20, 7, 30) });
                // 底部暗面
                var old = g.Clip;
                g.SetClip(body, CombineMode.Intersect);
                using (var shade = new LinearGradientBrush(
                    new RectangleF(56, GROUND - 40, 88, 42),
                    Color.FromArgb(0, 90, 60, 40), Color.FromArgb(34, 90, 60, 40), 90f))
                    g.FillRectangle(shade, 56, GROUND - 40, 88, 42);
                g.Clip = old;
            }

            // 胸口 / 肚子浅色
            using (var belPath = new GraphicsPath())
            {
                belPath.AddEllipse(100 - 20, 150, 40, 38);
                using (var belBrush = new PathGradientBrush(belPath))
                {
                    belBrush.CenterPoint = new PointF(94, 168);
                    belBrush.CenterColor = Lighten(pal.Belly, 10);
                    belBrush.SurroundColors = new[] { Darken(pal.Belly, 10) };
                    belBrush.FocusScales = new PointF(.6f, .6f);
                    g.FillPath(belBrush, belPath);
                }
            }

            // 前爪
            float kL = knead > 0 ? (float)Math.Sin(knead) : 0;
            for (int i = 0; i < 2; i++)
            {
                float dir = i == 0 ? -1 : 1;
                float px = 100 + dir * 19 + (kL * dir > 0 ? dir * 2 : 0);
                float py = 198 - (kL * dir > 0 ? Math.Abs(kL) * 4 : 0);
                var pawRect = new RectangleF(px - 12.5f, py - 10, 25, 19);
                using (var paw = Blob(pawRect, .55f))
                {
                    Clay(g, paw, pal.Belly, 14f, 30f);
                }
                using (var pen = new Pen(Color.FromArgb(46, 150, 118, 92), 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, px - 4.5f, py - 2, px - 4.5f, py + 6);
                    g.DrawLine(pen, px + 4.5f, py - 2, px + 4.5f, py + 6);
                }
            }

            // 头
            float hx = 100, hy = 104 + drop, hr = 49;
            var headRect = new RectangleF(hx - hr, hy - hr * .92f, hr * 2, hr * 1.86f);
            // 耳朵先画（压在头后）
            Ear(g, hx - 46, hy - 26, 34, 40, 0, pal.Base,
                pal == Palette.Calico ? Color.FromArgb(240, 176, 160) : Color.FromArgb(242, 176, 160));
            Ear(g, hx + 12, hy - 26, 34, 40, 0, pal.Base,
                pal == Palette.Calico ? Color.FromArgb(240, 176, 160) : Color.FromArgb(242, 176, 160));

            using (var head = Blob(headRect, .5f))
            {
                Clay(g, head, pal.Base);
                Patches(g, head, pal, new[] {
                    new RectangleF(hx - 46, hy - 46, 30, 26),
                    new RectangleF(hx + 20, hy - 30, 22, 18) });
                if (pal.Stripes)
                    Stripes(g, head, pal.Dark, new[] {
                        new RectangleF(hx - 26, hy - 46, 6, 20),
                        new RectangleF(hx - 6, hy - 48, 6, 22),
                        new RectangleF(hx + 14, hy - 46, 6, 20) });
                var old = g.Clip;
                g.SetClip(head, CombineMode.Intersect);
                using (var sh = new LinearGradientBrush(
                    new RectangleF(hx - hr, hy + 10, hr * 2, 44),
                    Color.FromArgb(0, 90, 60, 40), Color.FromArgb(26, 90, 60, 40), 90f))
                    g.FillRectangle(sh, hx - hr, hy + 10, hr * 2, 44);
                g.Clip = old;
            }

            // 眼睛
            float eyeY = hy + 4, eyeDX = 20;
            float open = asleep ? 0 : Math.Max(0, 1 - blink);
            if (open < .12f)
            {
                for (int i = 0; i < 2; i++)
                    AlmondEye(g, hx + (i == 0 ? -eyeDX : eyeDX), eyeY, 24, 20, 0, 0, true, false);
            }
            else
            {
                for (int i = 0; i < 2; i++)
                    AlmondEye(g, hx + (i == 0 ? -eyeDX : eyeDX), eyeY, 24, 20 * (0.55f + 0.45f * open), lookX, lookY, false, false);
            }

            // 口鼻浅色
            using (var muzzle = new SolidBrush(Color.FromArgb(150, pal.Belly)))
                g.FillEllipse(muzzle, hx - 17, hy + 10, 34, 22);

            // 鼻子
            using (var nose = new SolidBrush(Color.FromArgb(228, 146, 132)))
            {
                var np = new GraphicsPath();
                np.AddBezier(hx - 5.5f, hy + 18, hx - 2f, hy + 23.5f, hx + 2f, hy + 23.5f, hx + 5.5f, hy + 18);
                np.AddBezier(hx + 5.5f, hy + 18, hx + 3f, hy + 15f, hx - 3f, hy + 15f, hx - 5.5f, hy + 18);
                np.CloseFigure();
                g.FillPath(nose, np);
            }

            float mOpen = yawn * 15;
            if (mOpen > 1.5f)
            {
                using (var m = new SolidBrush(Color.FromArgb(178, 88, 80)))
                    g.FillEllipse(m, hx - 9, hy + 24, 18, mOpen);
                using (var tongue = new SolidBrush(Color.FromArgb(226, 150, 150)))
                    g.FillEllipse(tongue, hx - 5, hy + 30, 10, mOpen * .5f);
            }
            else
            {
                using (var pen = new Pen(Color.FromArgb(140, 92, 72), 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawBezier(pen, hx, hy + 24, hx - 4, hy + 30, hx - 9, hy + 30, hx - 11, hy + 27);
                    g.DrawBezier(pen, hx, hy + 24, hx + 4, hy + 30, hx + 9, hy + 30, hx + 11, hy + 27);
                }
            }

            // 胡须
            using (var pen = new Pen(Color.FromArgb(78, 120, 96, 78), 1.3f))
            {
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < 2; i++)
                        g.DrawLine(pen, hx + s * 24, hy + 22 + i * 6, hx + s * 52, hy + 16 + i * 12);
            }

            // Zzz
            if (asleep)
            {
                double ph = (t * 0.55) % 1.0;
                int a = (int)(190 * (1 - ph));
                using (var f1 = new Font("Segoe UI", 17f, FontStyle.Bold))
                using (var f2 = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (var b1 = new SolidBrush(Color.FromArgb(a, 118, 106, 96)))
                using (var b2 = new SolidBrush(Color.FromArgb((int)(a * .75), 118, 106, 96)))
                {
                    g.DrawString("z", f1, b1, hx + 34, hy - 34 - (float)(ph * 34));
                    g.DrawString("z", f2, b2, hx + 52, hy - 8 - (float)(ph * 24));
                }
            }
        }

        // ================= 走路（侧面，朝右） =================
        public static void DrawWalking(Graphics g, Palette pal, double t, double phase, bool right, float speedK)
        {
            float bob = (float)(Math.Sin(phase * 2 * Math.PI) * 2.2);
            float hipY = 158 + bob;

            SoftShadow(g, 100, GROUND + 2, 52, 8, 26);

            // 尾巴
            double sway = Math.Sin(phase * 2 * Math.PI + 0.6) * 6;
            TaperedTail(g, new PointF(62, 150 + bob), new PointF(34, 146 + bob),
                new PointF(20, 128 + (float)sway + bob), new PointF(30, 104 + (float)sway + bob),
                13f, 5.5f, pal.Dark);

            // 远侧腿（暗）
            for (int i = 0; i < 2; i++)
            {
                double off = i == 0 ? 0.5 : 0.0;
                float x0 = i == 0 ? 84 : 128;
                double swing = Math.Sin((phase + off) * 2 * Math.PI) * 13 * (0.55 + speedK * 0.5);
                Leg(g, x0, hipY + 8, x0 + (float)swing, GROUND - 6, pal.Dark, 9f);
            }

            // 身体
            var bodyRect = new RectangleF(52, 132 + bob, 96, 44);
            using (var body = Blob(bodyRect, .48f))
            {
                Clay(g, body, pal.Base);
                Patches(g, body, pal, new[] {
                    new RectangleF(50, 132 + bob, 46, 40),
                    new RectangleF(104, 146 + bob, 44, 34) });
                if (pal.Stripes)
                    Stripes(g, body, pal.Dark, new[] {
                        new RectangleF(72, 132 + bob, 6, 40),
                        new RectangleF(92, 130 + bob, 6, 44),
                        new RectangleF(112, 134 + bob, 6, 36) });
            }

            // 近侧腿（亮）
            for (int i = 0; i < 2; i++)
            {
                double off = i == 0 ? 0.0 : 0.5;
                float x0 = i == 0 ? 88 : 132;
                double swing = Math.Sin((phase + off) * 2 * Math.PI) * 13 * (0.55 + speedK * 0.5);
                Leg(g, x0, hipY + 10, x0 + (float)swing, GROUND - 3, pal.Base, 10.5f);
            }

            // 脖子
            using (var neck = Blob(new RectangleF(126, 140 + bob, 34, 30), .5f))
                Clay(g, neck, pal.Base, 18f, 30f);

            // 头
            float hx = 150, hy = 128 + bob, hr = 29;
            Ear(g, hx - 22, hy - 16, 22, 26, 0, pal.Base, Color.FromArgb(242, 176, 160));
            Ear(g, hx + 2, hy - 18, 22, 26, 0, pal.Base, Color.FromArgb(242, 176, 160));

            using (var head = Blob(new RectangleF(hx - hr, hy - hr * .92f, hr * 2, hr * 1.86f), .5f))
            {
                Clay(g, head, pal.Base);
                Patches(g, head, pal, new[] {
                    new RectangleF(hx - 30, hy - 26, 30, 26),
                    new RectangleF(hx + 4, hy - 22, 26, 24) });
                if (pal.Stripes)
                    Stripes(g, head, pal.Dark, new[] {
                        new RectangleF(hx - 12, hy - 28, 5, 14),
                        new RectangleF(hx + 2, hy - 28, 5, 14) });
            }

            // 脸
            AlmondEye(g, hx + 10, hy + 1, 17, 14, .6f, -.15f, false, false);
            using (var nose = new SolidBrush(Color.FromArgb(228, 146, 132)))
                g.FillEllipse(nose, hx + 25, hy + 8, 7, 5.5f);
            using (var pen = new Pen(Color.FromArgb(140, 92, 72), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(pen, hx + 28, hy + 13, hx + 27, hy + 18, hx + 22, hy + 19, hx + 19, hy + 17);
            using (var pen = new Pen(Color.FromArgb(78, 120, 96, 78), 1.2f))
            {
                g.DrawLine(pen, hx + 28, hy + 10, hx + 50, hy + 6);
                g.DrawLine(pen, hx + 28, hy + 14, hx + 50, hy + 16);
            }
        }


        // ================= 趴睡 =================
        public static void DrawSleeping(Graphics g, Palette pal, double t, float sleep)
        {
            float breathe = (float)(Math.Sin(t * 1.3) * 1.6);
            SoftShadow(g, 102, GROUND + 4, 56, 9, 28);

            // 尾巴绕到前爪
            TaperedTail(g, new PointF(62, 198), new PointF(34, 200), new PointF(30, 184), new PointF(52, 178), 13f, 5.5f, pal.Dark);

            // 身体（趴着：横长、扁）
            var bodyRect = new RectangleF(50, 168 - breathe * .5f, 104, 38 + breathe);
            using (var body = Blob(bodyRect, .5f))
            {
                Clay(g, body, pal.Base);
                Patches(g, body, pal, new[] {
                    new RectangleF(46, 166, 52, 36),
                    new RectangleF(108, 176, 44, 30) });
                if (pal.Stripes)
                    Stripes(g, body, pal.Dark, new[] {
                        new RectangleF(66, 166, 6, 40),
                        new RectangleF(86, 164, 6, 42),
                        new RectangleF(106, 168, 6, 36) });
            }

            // 前爪（伸在前头）
            for (int i = 0; i < 2; i++)
            {
                var r = new RectangleF(140 + i * 16, 192, 20, 14);
                using (var paw = Blob(r, .55f)) Clay(g, paw, pal.Belly, 12f, 26f);
            }

            // 头（枕在爪上）
            float hx = 128, hy = 176, hr = 27;
            Ear(g, hx - 24, hy - 14, 20, 24, 0, pal.Base, Color.FromArgb(242, 176, 160));
            Ear(g, hx + 4, hy - 16, 20, 24, 0, pal.Base, Color.FromArgb(242, 176, 160));
            using (var head = Blob(new RectangleF(hx - hr, hy - hr * .9f, hr * 2, hr * 1.8f), .5f))
            {
                Clay(g, head, pal.Base);
                Patches(g, head, pal, new[] {
                    new RectangleF(hx - 28, hy - 24, 28, 24),
                    new RectangleF(hx + 4, hy - 20, 24, 22) });
            }

            // 闭眼
            for (int i = 0; i < 2; i++)
                AlmondEye(g, hx + (i == 0 ? -11 : 11), hy + 2, 17, 14, 0, 0, true, false);
            using (var nose = new SolidBrush(Color.FromArgb(228, 146, 132)))
                g.FillEllipse(nose, hx - 3.5f, hy + 13, 7, 5.5f);
            using (var pen = new Pen(Color.FromArgb(140, 92, 72), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(pen, hx, hy + 18, hx - 3, hy + 23, hx - 8, hy + 23, hx - 10, hy + 20);
            using (var pen = new Pen(Color.FromArgb(78, 120, 96, 78), 1.2f))
            {
                g.DrawLine(pen, hx + 14, hy + 14, hx + 36, hy + 10);
                g.DrawLine(pen, hx + 14, hy + 18, hx + 36, hy + 20);
            }

            // Zzz
            double ph = (t * 0.55) % 1.0;
            int a = (int)(190 * (1 - ph));
            using (var f1 = new Font("Segoe UI", 17f, FontStyle.Bold))
            using (var f2 = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var b1 = new SolidBrush(Color.FromArgb(a, 118, 106, 96)))
            using (var b2 = new SolidBrush(Color.FromArgb((int)(a * .75), 118, 106, 96)))
            {
                g.DrawString("z", f1, b1, hx + 30, hy - 30 - (float)(ph * 34));
                g.DrawString("z", f2, b2, hx + 48, hy - 4 - (float)(ph * 24));
            }
        }


        // ================= 被拎起来拉长（年糕） =================
        public static void DrawStretched(Graphics g, Palette pal, PointF basePt, PointF headPt, double t, float squeeze)
        {
            float dx = headPt.X - basePt.X, dy = headPt.Y - basePt.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 60) len = 60;
            float ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);

            var st = g.Save();
            g.TranslateTransform(basePt.X, basePt.Y);
            g.RotateTransform(ang + 90f);     // 局部 -Y 指向头部

            float headR = 31f - Math.Min(5f, len / 160f);
            float neckY = -(len - headR * 0.62f);
            double sway = Math.Sin(t * 3.0) * Math.Min(7, len / 35);

            // ---- 身体：脚粗、中段鼓、脖子细，带一点左右摆 ----
            var leftPts = new List<PointF>();
            var rightPts = new List<PointF>();
            const int N = 22;
            for (int i = 0; i <= N; i++)
            {
                float tt = i / (float)N;
                float y = -6 + (neckY + 6) * tt;
                float w = 19f - 6f * tt + 6f * (float)Math.Sin(tt * Math.PI);
                w *= (float)Math.Max(.62, Math.Min(1.0, 1.0 - (len - 190) / 900f));   // 拉得越长越细
                w *= 1f + 0.06f * (float)Math.Sin(t * 5 + tt * 6);          // 呼吸/弹性
                float wob = (float)(Math.Sin(tt * Math.PI * 1.4 + t * 2.4) * 3.0 * Math.Sin(tt * Math.PI));
                leftPts.Add(new PointF(-w + wob, y));
                rightPts.Add(new PointF(w + wob, y));
            }
            var bodyPath = new GraphicsPath();
            bodyPath.AddCurve(leftPts.ToArray(), 0f);
            var rev = new List<PointF>();
            for (int i = rightPts.Count - 1; i >= 0; i--) rev.Add(rightPts[i]);
            bodyPath.AddCurve(rev.ToArray(), 0f);
            bodyPath.CloseFigure();

            Clay(g, bodyPath, pal.Base);
            {
                var old = g.Clip;
                g.SetClip(bodyPath, CombineMode.Intersect);
                Patches(g, bodyPath, pal, new[] {
                    new RectangleF(-34, neckY * .62f, 38, 34),
                    new RectangleF(4, neckY * .40f, 34, 40) });
                if (pal.Stripes)
                    Stripes(g, bodyPath, pal.Dark, new[] {
                        new RectangleF(-16, neckY * .74f, 6, 24),
                        new RectangleF(6, neckY * .58f, 6, 26),
                        new RectangleF(-10, neckY * .34f, 6, 20) });
                g.Clip = old;
            }
            // 肚皮（细长）
            using (var bel = new SolidBrush(Color.FromArgb(175, pal.Belly)))
                g.FillEllipse(bel, -6, neckY * .72f, 12, Math.Abs(neckY) * .30f);

            // ---- 悬着的后腿（画在身体之上，才看得见） ----
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -12 : 12;
                float kick = (float)Math.Sin(t * 4 + i * 1.7) * 3.5f;
                using (var pen = new Pen(i == 0 ? pal.Dark : pal.Base, 11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, sx * .6f, -2, sx + kick, 15);
                var pawRect = new RectangleF(sx + kick - 8.5f, 11, 17, 12);
                using (var paw = Blob(pawRect, .55f)) Clay(g, paw, pal.Belly, 12f, 24f);
            }

            // ---- 尾巴：垂在一侧，随晃动摆 ----
            TaperedTail(g, new PointF(8, -6), new PointF(30, 0), new PointF(36 + (float)sway * .4f, 22), new PointF(20 + (float)sway, 42),
                11f, 5f, pal.Dark);

            // ---- 头 ----
            float hy = -len;
            Ear(g, -headR - 6, hy - 14, 25, 29, 0, pal.Base, Color.FromArgb(242, 176, 160));
            Ear(g, headR - 19, hy - 14, 25, 29, 0, pal.Base, Color.FromArgb(242, 176, 160));

            var headRect = new RectangleF(-headR, hy - headR * .95f, headR * 2, headR * 1.9f);
            using (var head = Blob(headRect, .5f))
            {
                Clay(g, head, pal.Base);
                Patches(g, head, pal, new[] {
                    new RectangleF(-headR - 4, hy - 30, 30, 24),
                    new RectangleF(6, hy - 24, 24, 20) });
            }

            // ---- 脸：瞪眼张嘴（被拎起来的表情） ----
            for (int i = 0; i < 2; i++)
            {
                float ex = i == 0 ? -12.5f : 12.5f;
                using (var w = new SolidBrush(Color.FromArgb(255, 252, 246)))
                    g.FillEllipse(w, ex - 10, hy - 12, 20, 22);
                using (var iris = new SolidBrush(Color.FromArgb(88, 62, 46)))
                    g.FillEllipse(iris, ex - 6, hy - 7.5f, 12, 14);
                using (var pupil = new SolidBrush(Color.FromArgb(30, 22, 18)))
                    g.FillEllipse(pupil, ex - 3.2f, hy - 5, 6.4f, 9.5f);
                using (var hl = new SolidBrush(Color.White))
                    g.FillEllipse(hl, ex - 5, hy - 7, 3.6f, 3.6f);
            }
            using (var nose = new SolidBrush(Color.FromArgb(228, 146, 132)))
                g.FillEllipse(nose, -4.5f, hy + 13, 9, 6.5f);
            using (var mouth = new SolidBrush(Color.FromArgb(172, 86, 78)))
                g.FillEllipse(mouth, -5, hy + 20, 10, 9);
            using (var pen = new Pen(Color.FromArgb(78, 120, 96, 78), 1.3f))
            {
                g.DrawLine(pen, -15, hy + 16, -37, hy + 9);
                g.DrawLine(pen, 15, hy + 16, 37, hy + 9);
            }

            g.Restore(st);
        }

        static void Leg(Graphics g, float x0, float y0, float x1, float y1, Color c, float w)
        {
            using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, x0, y0, x1, y1);
            using (var paw = new SolidBrush(Light(c)))
                g.FillEllipse(paw, x1 - 7, y1 - 6, 14, 9);
        }

        static Color Light(Color c)
        {
            return Color.FromArgb(
                Math.Min(255, c.R + 34), Math.Min(255, c.G + 30), Math.Min(255, c.B + 26));
        }
    }

    class Palette
    {
        public string Name;
        public string Key;
        public Color Base, Dark, Belly, Patch1, Patch2;
        public bool Patches, Stripes;
        public int Alpha = 255;

        public static Palette Tabby = new Palette
        {
            Name = "橘猫",
            Key = "tabby",
            Base = Color.FromArgb(233, 165, 100),
            Dark = Color.FromArgb(205, 133, 68),
            Belly = Color.FromArgb(253, 240, 222),
            Patch1 = Color.FromArgb(0, 0, 0, 0),
            Patch2 = Color.FromArgb(0, 0, 0, 0),
            Stripes = true
        };
        public static Palette Cheese = new Palette
        {
            Name = "起司猫",
            Key = "cheese",
            Base = Color.FromArgb(247, 230, 194),
            Dark = Color.FromArgb(222, 199, 156),
            Belly = Color.FromArgb(255, 253, 247),
            Patch1 = Color.FromArgb(255, 252, 242),
            Patch2 = Color.FromArgb(0, 0, 0, 0),
            Patches = false
        };
        public static Palette Calico = new Palette
        {
            Name = "三花猫",
            Key = "calico",
            Base = Color.FromArgb(255, 250, 240),
            Dark = Color.FromArgb(233, 223, 208),
            Belly = Color.FromArgb(255, 254, 250),
            Patch1 = Color.FromArgb(231, 155, 88),
            Patch2 = Color.FromArgb(72, 58, 50),
            Patches = true
        };
        public static Palette[] All = { Tabby, Cheese, Calico };

        static readonly System.Collections.Generic.Dictionary<string, string> Names =
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "fox", "狐狸" }, { "rabbit", "兔子" }, { "dog", "小狗" }, { "cat", "猫" },
            { "otter", "水獭" }, { "redpanda", "小熊猫" }, { "capybara", "水豚" }, { "koala", "考拉" },
            { "tiger", "小老虎" }, { "snowy-owl", "雪鸮" }, { "sugar-glider", "蜜袋鼯" },
            { "penguin", "企鹅" }, { "phoenix", "凤凰" }, { "zombie", "小僵尸" },
            { "mummy", "绷带宝宝" }, { "stitched-monster", "缝合小怪人" },
            { "snake", "小蛇" }, { "dragon", "小龙" }
        };

        /// <summary>素材自动发现：exe 里只要有 {key}_sit.png 就自动当成一个可选角色，
        /// 加角色不用改代码，重新编译时多挂一个 /resource: 就行。</summary>
        public static Palette[] Discover()
        {
            var list = new System.Collections.Generic.List<Palette>();
            foreach (var p in All) list.Add(p);
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var p in All) seen.Add(p.Key);

            string[] names = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames();
            foreach (var n in names)
            {
                if (!n.EndsWith("_sit.png")) continue;
                string key = n.Substring(0, n.Length - 8);
                if (key.Length == 0 || seen.Contains(key)) continue;
                seen.Add(key);
                var p = new Palette();
                string cn;
                p.Name = Names.TryGetValue(key, out cn) ? cn : key;
                p.Key = key;
                p.Base = Color.White; p.Dark = Color.Gray; p.Belly = Color.White;
                p.Patch1 = Color.Transparent; p.Patch2 = Color.Transparent;
                list.Add(p);
            }
            All = list.ToArray();
            return All;
        }
    }
}
