import re
import shutil

CP = "src/CatPet.cs"
SP = "src/SpriteArt.cs"
shutil.copy(CP, CP + ".bak16")
shutil.copy(SP, SP + ".bak16")

s = open(SP, encoding="utf-8").read()

# 1) 拎起不再纵向拉伸（Lucia: 拉长了反而不好看）
old = """            float k = 0.94f + (len - 180f) / 1400f;
            if (k < 0.94f) k = 0.94f;
            if (k > 1.30f) k = 1.30f;
            g.H = naturalH * k;"""
new = """            // 不变形：拎起来就用原版比例（拉长反而不好看）
            g.H = naturalH;"""
assert old in s
s = s.replace(old, new)

# 2) 脚底像素补正：把素材底部的透明留白补掉，落地才严丝合缝
helper = """
        static readonly Dictionary<string, int> PadCache = new Dictionary<string, int>();

        /// <summary>素材底部透明留白（像素行数）：绘制时补回来，脚才能真的踩在线上。</summary>
        static int BottomPad(Bitmap bmp)
        {
            string id = bmp.Width + "x" + bmp.Height + "#" + bmp.GetHashCode();
            int v;
            if (PadCache.TryGetValue(id, out v)) return v;
            int last = 0;
            for (int yy = bmp.Height - 1; yy >= 0; yy--)
            {
                bool any = false;
                for (int xx = 0; xx < bmp.Width; xx += 2)
                    if (bmp.GetPixel(xx, yy).A > 60) { any = true; break; }
                if (any) { last = yy; break; }
            }
            v = bmp.Height - 1 - last;
            PadCache[id] = v;
            return v;
        }
"""
anchor = "        struct StretchGeo { public float W, H, HeadY, Ang; }"
assert anchor in s
s = s.replace(anchor, helper.strip("\n") + "\n\n" + anchor, 1)

old = "                var dest = new RectangleF(cx - dw / 2f, groundY - dh, dw, dh);"
new = ("                float padPx = BottomPad(bmp) * (dh / bmp.Height);\n"
       "                var dest = new RectangleF(cx - dw / 2f, groundY - dh + padPx, dw, dh);")
assert old in s
s = s.replace(old, new)

old = "            DrawImg(g, bmp, new RectangleF(-w / 2f, -h + bob, w, h));"
new = ("            float wpad = BottomPad(bmp) * (h / bmp.Height);\n"
       "            DrawImg(g, bmp, new RectangleF(-w / 2f, -h + bob + wpad, w, h));")
assert old in s
s = s.replace(old, new)

open(SP, "w", encoding="utf-8", newline="\n").write(s)

# ---------------- CatPet.cs ----------------
c = open(CP, encoding="utf-8").read()

c = c.replace("enum PetState { Sit, Walk, Sleep, Drag, Fall }",
              "enum PetState { Sit, Walk, Sleep, Drag, Fall, Climb, Ceil }")
c = c.replace("public float Zoom = 1.0f;", "public float Zoom = 0.85f;")
c = c.replace("foreach (var s in new[] { 0.7f, 1.0f, 1.35f })",
              "foreach (var s in new[] { 0.7f, 0.85f, 1.0f, 1.35f })")

# 尺寸变了，拎起来的素材也跟着变
old = """        void ApplySize()
        {
            var oldBottom = y + Height;
            Size = new Size((int)(Art.VW * Zoom), (int)(Art.VH * Zoom));"""
new = """        void ApplySize()
        {
            var oldBottom = y + Height;
            SpriteArt.ZoomStretch = Zoom;
            Size = new Size((int)(Art.VW * Zoom), (int)(Art.VH * Zoom));"""
assert old in c
c = c.replace(old, new)

# 绘制基线：原来留 14px，导致站在窗口上悬空
c = c.replace("Art.VH - 14", "Art.VH - 1")

# 新字段
old = "        double t0 = 0, stateT = 0, walkPhase = 0, nextWalkAt = 6 + new Random().NextDouble() * 8;"
new = ("        double t0 = 0, stateT = 0, walkPhase = 0, nextWalkAt = 6 + new Random().NextDouble() * 8;\n"
       "        int wallSide = -1; double ceilT = 0, nextClimbAt = 10;\n"
       "        readonly Random rng = new Random();")
assert old in c
c = c.replace(old, new)

# 爬墙/天花板时不跟着 standHwnd 复位
old = "if (state != PetState.Drag && state != PetState.Fall && standHwnd != IntPtr.Zero)"
new = "if (state != PetState.Drag && state != PetState.Fall && state != PetState.Climb && state != PetState.Ceil && standHwnd != IntPtr.Zero)"
assert old in c
c = c.replace(old, new)

# 地面走到边上：有机会扒着墙往上爬
old = """                    if (x < wa.Left - 10) { x = wa.Left - 10; faceRight = true; }
                    if (x + Width > wa.Right + 10) { x = wa.Right + 10 - Width; faceRight = false; }"""
new = """                    bool canClimb = AutoWalk && now > nextClimbAt;
                    if (x < wa.Left)
                    {
                        x = wa.Left;
                        if (canClimb && rng.NextDouble() < 0.6) { wallSide = -1; state = PetState.Climb; stateT = 0; nextClimbAt = now + 20 + rng.NextDouble() * 25; }
                        else faceRight = true;
                    }
                    else if (x + Width > wa.Right)
                    {
                        x = wa.Right - Width;
                        if (canClimb && rng.NextDouble() < 0.6) { wallSide = 1; state = PetState.Climb; stateT = 0; nextClimbAt = now + 20 + rng.NextDouble() * 25; }
                        else faceRight = false;
                    }"""
assert old in c
c = c.replace(old, new)

# 走完 Walk 分支后插入 Climb / Ceil
old = """                if (AutoWalk == false && stateT > .1) ToSit();
                if (stateT > 4 + new Random().NextDouble() * 4) { ToSit(); }
            }"""
new = """                if (AutoWalk == false && stateT > .1) ToSit();
                if (stateT > 4 + new Random().NextDouble() * 4) { ToSit(); }
            }
            else if (state == PetState.Climb)
            {
                y -= 95f * (float)dt;
                x = wallSide < 0 ? wa.Left : wa.Right - Width;
                walkPhase += dt * 2.4;
                if (y <= wa.Top)
                {
                    y = wa.Top; state = PetState.Ceil; stateT = 0; ceilT = 0;
                    faceRight = wallSide < 0;
                }
            }
            else if (state == PetState.Ceil)
            {
                x += (faceRight ? 40f : -40f) * (float)dt;
                y = wa.Top;
                walkPhase += dt * 2.2;
                if (x < wa.Left) { x = wa.Left; faceRight = true; }
                else if (x + Width > wa.Right) { x = wa.Right - Width; faceRight = false; }
                ceilT += dt;
                // 在顶上走着走着会松手掉下去（平均撑 3 秒左右）
                if (ceilT > 2.5 && rng.NextDouble() < 0.32 * dt) { state = PetState.Fall; vy = 0; stateT = 0; ceilT = 0; }
            }"""
assert old in c
c = c.replace(old, new)

# 渲染：爬墙转 90°，天花板转 180°
old = """                        g.DrawImage(big, new Rectangle(0, 0, w, h));"""
new = """                        if (state == PetState.Ceil || state == PetState.Climb)
                        {
                            using (var rot = new Bitmap(Art.VW * ss, Art.VH * ss, PixelFormat.Format32bppArgb))
                            {
                                using (var rg = Graphics.FromImage(rot))
                                {
                                    rg.Clear(Color.Transparent);
                                    rg.SmoothingMode = SmoothingMode.AntiAlias;
                                    rg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    rg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    rg.TranslateTransform(Art.VW * ss / 2f, Art.VH * ss / 2f);
                                    rg.RotateTransform(state == PetState.Ceil ? 180f : (wallSide < 0 ? 90f : -90f));
                                    rg.ScaleTransform(0.88f, 0.88f);
                                    rg.TranslateTransform(-Art.VW * ss / 2f, -Art.VH * ss / 2f);
                                    rg.DrawImage(big, 0, 0);
                                }
                                g.DrawImage(rot, new Rectangle(0, 0, w, h));
                            }
                        }
                        else g.DrawImage(big, new Rectangle(0, 0, w, h));"""
assert old in c
c = c.replace(old, new)

# 天花板上是倒着走的，镜像要反过来
old = """                            if (state == PetState.Walk && !faceRight)
                            {
                                bg.TranslateTransform(Art.VW, 0);
                                bg.ScaleTransform(-1, 1);
                            }"""
new = """                            bool mirrored = (state == PetState.Ceil) ? faceRight : !faceRight;
                            if ((state == PetState.Walk || state == PetState.Ceil || state == PetState.Climb) && mirrored)
                            {
                                bg.TranslateTransform(Art.VW, 0);
                                bg.ScaleTransform(-1, 1);
                            }"""
assert old in c
c = c.replace(old, new)

# Climb / Ceil 也画走路帧
old = """                            if (state == PetState.Walk)
                                drawn = SpriteArt.DrawWalking("""
new = """                            if (state == PetState.Walk || state == PetState.Ceil || state == PetState.Climb)
                                drawn = SpriteArt.DrawWalking("""
assert old in c
c = c.replace(old, new)

# 命令行：--mock <dir> ceil|climb，用来出图核验
old = """            if (args.Length >= 2 && args[0] == "--debugdir")"""
new = """            if (args.Length >= 3 && args[0] == "--mock")
            {
                PetForm.DebugDir = args[1];
                Directory.CreateDirectory(args[1]);
                var fm = new PetForm { AutoWalk = false };
                fm.MockState(args[2]);
                Application.Run(fm); return;
            }
            if (args.Length >= 2 && args[0] == "--debugdir")"""
assert old in c
c = c.replace(old, new)

old = """        void ToSit()"""
new = """        public void MockState(string kind)
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            if (kind == "ceil")
            {
                state = PetState.Ceil; faceRight = true; ceilT = 0;
                x = wa.Left + (wa.Width - Width) / 2f; y = wa.Top;
            }
            else if (kind == "climb")
            {
                state = PetState.Climb; wallSide = -1;
                x = wa.Left; y = wa.Bottom - Height - 260;
            }
            ApplySize(); Render();
        }

        void ToSit()"""
assert old in c
c = c.replace(old, new, 1)

open(CP, "w", encoding="utf-8", newline="\n").write(c)
print("patched ok")
