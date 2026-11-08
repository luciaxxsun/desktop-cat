using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopCat
{
    // ---------------- 状态 ----------------
    enum PetState { Sit, Walk, Sleep, Drag, Fall, Climb, Ceil }

    // ---------------- 主窗口 ----------------
    class PetForm : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        // ---- 窗口当平台 ----
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int val, int size);

        class Plat
        {
            public IntPtr Hwnd;
            public int Left, Right, Top;
            public bool Floor;
        }

        readonly List<Plat> plats = new List<Plat>();
        static readonly StringBuilder platDump = new StringBuilder();
        IntPtr standHwnd = IntPtr.Zero;   // 当前站在哪个窗口上（0=桌面地面）
        float standOffsetX = 0f, standTop = 0f;
        double lastPlatScan = -9;

        const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;

        public Palette Palette = Palette.Tabby;
        static bool discovered = false;
        public float Zoom = 0.85f;
        public bool AutoWalk = true;

        PetState state = PetState.Sit;
        double t0 = 0, stateT = 0, walkPhase = 0, nextWalkAt = 6 + new Random().NextDouble() * 8;
        int wallSide = -1; double ceilT = 0, nextClimbAt = 10; bool forceClimb = false; bool climbUp = true;
        readonly Random rng = new Random();
        bool faceRight = true;
        float lookX = 0, lookY = 0, blink = 0, sleepAmt = 0, yawn = 0, kneadPhase = 0, grabHeadDY = 12f;
        double lastTyping = -99, lastInputCheck = 0;
        float vx = 0, vy = 0;
        Point groundPt;             // 当前落点（脚底）
        Point? dragOffset = null;
        PointF dragHead, dragBase;
        float grabOffX = 0f, squash = 0f;
        float x, y;                 // 窗口左上角（屏幕坐标，逻辑=实际）
        Timer timer;
        NotifyIcon tray;
        DateTime startTime = DateTime.Now;
        IntPtr hook = IntPtr.Zero;
        LowLevelProc hookProc;
        static double lastKeyTime = -99;
        static DateTime lastKeyStamp = DateTime.MinValue;
        static DateTime lastKneadBump = DateTime.MinValue;

        public PetForm()
        {
            if (!discovered) { discovered = true; Palette.Discover(); Palette = Palette.All[0]; }
            Text = "桌面猫";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Size = new Size((int)(Art.VW * Zoom), (int)(Art.VH * Zoom));

            var wa = Screen.PrimaryScreen.WorkingArea;
            x = wa.Left + wa.Width / 2 - Width / 2;
            y = wa.Bottom - Height;

            timer = new Timer { Interval = 33 };
            timer.Tick += OnTick;
            timer.Start();

            hookProc = HookCb;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
            BuildTray();
            ApplySize();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "桌面猫";
            tray.Visible = true;
            var menu = new ContextMenuStrip();

            var palMenu = new ToolStripMenuItem("选择花色");
            foreach (var p in Palette.All)
            {
                var it = new ToolStripMenuItem(p.Name) { Checked = p == Palette };
                var cap = p;
                it.Click += (s, e) => { Palette = cap; RebuildTrayChecks(); Config.Save(this); Render(); };
                palMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(palMenu);

            var sizeMenu = new ToolStripMenuItem("大小");
            foreach (var s in new[] { 0.7f, 0.85f, 1.0f, 1.35f })
            {
                var it = new ToolStripMenuItem(s.ToString("0.0") + "x") { Checked = Math.Abs(Zoom - s) < .01f };
                var cap = s;
                it.Click += (s2, e) => { Zoom = cap; ApplySize(); Config.Save(this); ResizeMenus(); };
                sizeMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(sizeMenu);

            var walk = new ToolStripMenuItem("自己走动") { Checked = AutoWalk, CheckOnClick = true };
            walk.CheckedChanged += (s, e) => { AutoWalk = walk.Checked; Config.Save(this); if (!AutoWalk && state == PetState.Walk) ToSit(); };
            menu.Items.Add(walk);

            var startup = new ToolStripMenuItem("开机自启") { Checked = Config.StartupEnabled(), CheckOnClick = true };
            startup.CheckedChanged += (s, e) => Config.SetStartup(startup.Checked);
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => Quit());

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => { ToSit(); };
        }

        ToolStripMenuItem palMenuItem, sizeMenuItem;

        void RebuildTrayChecks()
        {
            if (tray.ContextMenuStrip == null) return;
            var palMenu = tray.ContextMenuStrip.Items[0] as ToolStripMenuItem;
            if (palMenu == null) return;
            for (int i = 0; i < palMenu.DropDownItems.Count && i < Palette.All.Length; i++)
                ((ToolStripMenuItem)palMenu.DropDownItems[i]).Checked = Palette.All[i] == Palette;
        }

        void ResizeMenus()
        {
            if (tray.ContextMenuStrip == null) return;
            var sizeMenu = tray.ContextMenuStrip.Items[1] as ToolStripMenuItem;
            if (sizeMenu == null) return;
            float[] ss = { 0.7f, 1.0f, 1.35f };
            for (int i = 0; i < sizeMenu.DropDownItems.Count && i < ss.Length; i++)
                ((ToolStripMenuItem)sizeMenu.DropDownItems[i]).Checked = Math.Abs(ss[i] - Zoom) < .01f;
        }

        Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(Palette.Base))
                    {
                        g.FillEllipse(brush, 4, 6, 24, 22);
                        PointF[] e1 = { new PointF(5, 12), new PointF(8, 1), new PointF(15, 9) };
                        PointF[] e2 = { new PointF(27, 12), new PointF(24, 1), new PointF(17, 9) };
                        g.FillPolygon(brush, e1); g.FillPolygon(brush, e2);
                    }
                    using (var d = new SolidBrush(Color.FromArgb(70, 50, 40)))
                    { g.FillEllipse(d, 10, 15, 4, 5); g.FillEllipse(d, 18, 15, 4, 5); }
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        public void ApplySize()
        {
            var oldBottom = y + Height;
            SpriteArt.ZoomStretch = Zoom;
            Size = new Size((int)(Art.VW * Zoom), (int)(Art.VH * Zoom));
            var wa = Screen.PrimaryScreen.WorkingArea;
            y = Math.Min(oldBottom, wa.Bottom) - Height;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            Render();
        }

        IntPtr HookCb(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    lastKeyTime = (DateTime.Now - startTime).TotalSeconds;
                    lastKeyStamp = DateTime.Now;
                    // 每敲一下压一次、左右轮换；但最快 7 次/秒，别打成震动
                    if (kneadPhase > 0 && (DateTime.Now - lastKneadBump).TotalMilliseconds > 200)
                    {
                        kneadPhase += (float)Math.PI;
                        lastKneadBump = DateTime.Now;
                    }
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        double Now() { return (DateTime.Now - startTime).TotalSeconds; }

        public void MockState(string kind)
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            if (kind == "ceil")
            {
                state = PetState.Ceil; faceRight = true; ceilT = 0;
                x = wa.Left + (wa.Width - Width) / 2f; y = wa.Top;
            }
            else if (kind == "ground")
            {
                state = PetState.Sit; y = wa.Bottom - Height;
                x = wa.Left + (wa.Width - Width) / 2f;
            }
            else if (kind == "edge")
            {
                AutoWalk = true; forceClimb = true; nextClimbAt = 0;
                faceRight = false; x = wa.Left + 12; y = wa.Bottom - Height;
                state = PetState.Walk; stateT = 0;
            }
            else if (kind == "climb")
            {
                state = PetState.Climb; wallSide = -1; climbUp = true;
                x = wa.Left; y = wa.Bottom - Height - 260;
            }
            else if (kind == "climbdown")
            {
                state = PetState.Climb; wallSide = -1; climbUp = false;
                x = wa.Left; y = wa.Bottom - Height - 260;
            }
            else if (kind == "climbdownR")
            {
                state = PetState.Climb; wallSide = 1; climbUp = false;
                x = wa.Right - Width; y = wa.Bottom - Height - 260;
            }
            else if (kind == "climbR")
            {
                state = PetState.Climb; wallSide = 1; climbUp = true;
                x = wa.Right - Width; y = wa.Bottom - Height - 260;
            }
            else if (kind == "ceilL")
            {
                state = PetState.Ceil; faceRight = false; ceilT = 0;
                x = wa.Left + (wa.Width - Width) / 2f; y = wa.Top;
            }
            else if (kind == "ceilR")
            {
                state = PetState.Ceil; faceRight = true; ceilT = 0;
                x = wa.Left + (wa.Width - Width) / 2f; y = wa.Top;
            }
            ApplySize(); Render();
        }

        void ToSit()
        {
            state = PetState.Sit;
            stateT = 0;
            sleepAmt = 0; yawn = 0;
            nextWalkAt = Now() + 6 + new Random().NextDouble() * 9;
        }

        void RefreshPlatforms()
        {
            plats.Clear();
            var wa = Screen.PrimaryScreen.WorkingArea;
            var floor = new Plat();
            floor.Hwnd = IntPtr.Zero; floor.Left = wa.Left; floor.Right = wa.Right;
            floor.Top = wa.Bottom; floor.Floor = true;
            plats.Add(floor);

            IntPtr me = this.Handle;
            platDump.Clear();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (h == me) return true;
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                int ex = GetWindowLong(h, -20);           // GWL_EXSTYLE
                int style = GetWindowLong(h, -16);        // GWL_STYLE
                if ((ex & 0x80) != 0) return true;        // 工具窗口不要
                int cloaked = 0;
                if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                if (w < 160 || hh < 90) return true;
                if (r.Top < -500) return true;
                if (r.Top <= wa.Top + 2) return true;            // 贴着屏幕顶的窗口站不住（猫会被切）
                if (w > (wa.Width - 8) && r.Top < wa.Top + 30) return true;
                var sp = new Plat();
                sp.Hwnd = h; sp.Left = r.Left; sp.Right = r.Right; sp.Top = r.Top;
                plats.Add(sp);
                platDump.Append(h + " " + r.Left + "," + r.Top + "-" + r.Right + "," + r.Bottom);
                platDump.Append(Environment.NewLine);
                return true;
            }, IntPtr.Zero);
        }

        Plat FindPlat(IntPtr hwnd)
        {
            foreach (var p in plats) if (p.Hwnd == hwnd) return p;
            return null;
        }

        void OnTick(object sender, EventArgs e)
        {
            double now = Now(), dt = 1.0 / 30.0;
            stateT += dt;

            var wa = Screen.PrimaryScreen.WorkingArea;
            int groundY = wa.Bottom - Height;

            if (now - lastPlatScan > 0.28)
            {
                RefreshPlatforms(); lastPlatScan = now;
                if (DebugDir != null)
                {
                    try { File.WriteAllText(Path.Combine(DebugDir, "plat.txt"), platDump.ToString() +
                        "stand=" + standHwnd + " state=" + state + " x=" + x + " y=" + y + " H=" + Height); }
                    catch { }
                }
            }

            // 站在窗口顶上：跟着窗口走；窗口没了/隐藏了 → 掉下去
            if (state != PetState.Drag && state != PetState.Fall && state != PetState.Climb && state != PetState.Ceil && standHwnd != IntPtr.Zero)
            {
                var st = FindPlat(standHwnd);
                if (st == null) { standHwnd = IntPtr.Zero; vy = 0; state = PetState.Fall; stateT = 0; }
                else
                {
                    standTop = st.Top;
                    x = st.Left + standOffsetX - Width / 2f;
                    y = st.Top - Height;
                }
            }

            // ---- 全局输入 ----
            var cursor = Cursor.Position;
            bool typing = (DateTime.Now - lastKeyStamp).TotalMilliseconds < 600;

            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            GetLastInputInfo(ref lii);
            double idleSec = (uint)Environment.TickCount - lii.dwTime;
            if (idleSec < 0) idleSec += 4294967296.0;
            idleSec /= 1000.0;

            // ---- 状态机 ----
            if (pendingDrag)
            {
                bool stillDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (!stillDown) { pendingDrag = false; Capture = false; }
                else
                {
                    float mdx = cursor.X - downPt.X, mdy = cursor.Y - downPt.Y;
                    if (mdx * mdx + mdy * mdy > 64f) BeginDrag();
                }
            }
            if (state == PetState.Drag && (GetAsyncKeyState(0x01) & 0x8000) == 0)
            {
                ReleaseDrag();   // 鼠标在窗口外松开也能收尾
            }
            if (state == PetState.Drag)
            {
                float groundLine = wa.Bottom - 2;
                // 拉伸上限：最多拉到身高 1.6 倍，再拖也不会更长（视频里就是这么回事）
                float maxLen = 240f * Zoom;
                float headY = Math.Min(cursor.Y + grabHeadDY, groundLine - 60);
                dragHead = new PointF(cursor.X, headY);
                dragBase = new PointF(cursor.X - grabOffX, groundLine);
                var bb = SpriteArt.StretchBounds(Palette.Key, dragBase, dragHead);
                float left = bb.Left, right = bb.Right, top = bb.Top, bottom = bb.Bottom;
                x = left; y = top;
                Size = new Size((int)(right - left), (int)(bottom - top));
            }
            else if (state == PetState.Fall)
            {
                float prevFeet = y + Height;
                vy += 1400 * (float)dt;
                y += vy * (float)dt;
                float feet = y + Height;
                float fallCx = x + Width / 2f;

                Plat land = null;
                foreach (var p in plats)
                {
                    if (fallCx < p.Left || fallCx > p.Right) continue;
                    if (p.Top + 0.5f < prevFeet) continue;   // 只在脚下，不往头上穿
                    if (p.Top > feet) continue;              // 还没掉到
                    if (land == null || p.Top < land.Top) land = p;
                }
                if (land != null)
                {
                    y = land.Top - Height;
                    vy = 0; squash = 1f;
                    standHwnd = land.Hwnd;
                    standOffsetX = fallCx - land.Left;
                    standTop = land.Top;
                    ToSit();
                }
                else if (y >= groundY) { y = groundY; vy = 0; squash = 1f; standHwnd = IntPtr.Zero; ToSit(); }
            }
            else if (state == PetState.Walk)
            {
                float speed = 46f;
                float dx = faceRight ? speed : -speed;
                x += dx * (float)dt * 1f;
                walkPhase += dt * 2.0;
                if (standHwnd != IntPtr.Zero)
                {
                    // 在窗口顶上走：走出边缘就掉下去
                    y = standTop - Height;
                    float walkCx = x + Width / 2f;
                    var cur = FindPlat(standHwnd);
                    if (cur == null) { state = PetState.Fall; vy = 0; standHwnd = IntPtr.Zero; }
                    else if (walkCx < cur.Left + 4 || walkCx > cur.Right - 4)
                    {
                        state = PetState.Fall; vy = 0; standHwnd = IntPtr.Zero; stateT = 0;
                    }
                }
                else
                {
                    bool canClimb = AutoWalk && now > nextClimbAt;
                    if (x < wa.Left)
                    {
                        x = wa.Left;
                        if (canClimb && (forceClimb || rng.NextDouble() < 0.6)) { forceClimb = false; wallSide = -1; state = PetState.Climb; stateT = 0; nextClimbAt = now + 20 + rng.NextDouble() * 25; }
                        else faceRight = true;
                    }
                    else if (x + Width > wa.Right)
                    {
                        x = wa.Right - Width;
                        if (canClimb && (forceClimb || rng.NextDouble() < 0.6)) { forceClimb = false; wallSide = 1; state = PetState.Climb; stateT = 0; nextClimbAt = now + 20 + rng.NextDouble() * 25; }
                        else faceRight = false;
                    }
                }
                if (AutoWalk == false && stateT > .1) ToSit();
                if (stateT > 4 + new Random().NextDouble() * 4) { ToSit(); }
            }
            else if (state == PetState.Climb)
            {
                x = wallSide < 0 ? wa.Left : wa.Right - Width;
                walkPhase += dt * 2.4;
                if (climbUp)
                {
                    y -= 190f * (float)dt;
                    if (y <= wa.Top)
                    {
                        y = wa.Top; state = PetState.Ceil; stateT = 0; ceilT = 0;
                        faceRight = wallSide < 0 ? true : false;
                    }
                }
                else
                {
                    y += 150f * (float)dt;
                    if (y + Height >= wa.Bottom) { y = wa.Bottom - Height; standHwnd = IntPtr.Zero; ToSit(); }
                }
            }
            else if (state == PetState.Ceil)
            {
                x += (faceRight ? 85f : -85f) * (float)dt;
                y = wa.Top;
                walkPhase += dt * 2.2;
                if (x < wa.Left)
                {
                    x = wa.Left; state = PetState.Climb; climbUp = false; wallSide = -1; stateT = 0;
                }
                else if (x + Width > wa.Right)
                {
                    x = wa.Right - Width; state = PetState.Climb; climbUp = false; wallSide = 1; stateT = 0;
                }
                ceilT += dt;
                // 在顶上走着走着会松手掉下去（平均撑 3 秒左右）
                if (ceilT > 2.5 && rng.NextDouble() < 0.32 * dt) { state = PetState.Fall; vy = 0; stateT = 0; ceilT = 0; }
            }
            else
            {
                // Sit / Sleep
                // 坐久了 / 睡醒了 都起来走两步
                if (standHwnd == IntPtr.Zero) y = groundY;
                if (AutoWalk && stateT > nextWalkAt && now > 6)
                {
                    if (standHwnd == IntPtr.Zero) y = groundY;
                    state = PetState.Walk; stateT = 0; walkPhase = 0; yawn = 0;
                    faceRight = new Random().Next(2) == 0;
                }
                if (typing && state == PetState.Sit) { state = PetState.Sit; stateT = Math.Max(stateT, 0); }

                // 闲置 → 打哈欠 → 睡
                if (state == PetState.Sit)
                {
                    if (idleSec > 45 && yawn < 1) yawn = Math.Min(1f, yawn + (float)dt * 1.4f);
                    if (yawn >= 1 && idleSec > 55) { state = PetState.Sleep; stateT = 0; }
                }
                if (state == PetState.Sleep && idleSec < 0.3 && stateT > 0.6) { ToSit(); }
                if (state == PetState.Sit && yawn > 0 && idleSec < 3) yawn = Math.Max(0, yawn - (float)dt * 2f);
            }

            squash = Math.Max(0f, squash - (float)dt * 5f);
            sleepAmt = state == PetState.Sleep
                ? Math.Min(1f, sleepAmt + (float)dt * .9f)
                : Math.Max(0f, sleepAmt - (float)dt * 2f);

            // 踩奶：按住打字就持续，节奏随击键
            if (typing && state != PetState.Drag && state != PetState.Fall && state != PetState.Walk)
                kneadPhase += (float)dt * 3.2f;
            else kneadPhase = 0;

            // 眨眼
            blink -= (float)dt * 3.4f;
            if (blink < 0) blink = 0;
            if (blink == 0 && state != PetState.Sleep && new Random().Next(180) == 0) blink = 1;

            // 眼睛跟鼠标
            float cx = x + Width / 2f, cy = y + Height * .42f;
            float tx = Math.Max(-1, Math.Min(1, (cursor.X - cx) / 320f));
            float ty = Math.Max(-1, Math.Min(1, (cursor.Y - cy) / 260f));
            lookX += (tx - lookX) * Math.Min(1f, (float)dt * 6);
            lookY += (ty - lookY) * Math.Min(1f, (float)dt * 6);

            Render();
        }

        public static string DebugDir = null;
        static int dbgN = 0;
        static int dbgK = 0;
        void Render()
        {
            try { RenderCore(); }
            catch (Exception ex)
            {
                try
                {
                    string d = DebugDir ?? Path.Combine(Path.GetTempPath(), "DesktopCatErr");
                    Directory.CreateDirectory(d);
                    File.AppendAllText(Path.Combine(d, "paint_err.log"),
                        Now().ToString("0.00") + " state=" + state + " knead=" + kneadPhase.ToString("0.00")
                        + " :: " + ex.GetType().Name + " " + ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine);
                }
                catch { }
            }
        }

        void RenderCore()
        {
            if (!IsHandleCreated) return;
            if (DebugDir != null && dbgN < 800)
            {
                dbgN++;
                try
                {
                    var cur = Cursor.Position;
                    string line = "t=" + Now().ToString("0.00") + " state=" + state
                        + " typing=" + ((DateTime.Now - lastKeyStamp).TotalMilliseconds < 600)
                        + " lookX=" + lookX.ToString("0.00") + " lookY=" + lookY.ToString("0.00")
                        + " blink=" + blink.ToString("0.00") + " knead=" + kneadPhase.ToString("0.00")
                        + " cursor=" + cur.X + "," + cur.Y;
                    File.AppendAllText(Path.Combine(DebugDir, "trace.log"), line + Environment.NewLine);
                }
                catch { }
            }
            if (state == PetState.Drag) { RenderStretch(); return; }
            int w = Math.Max(1, (int)(Art.VW * Zoom)), h = Math.Max(1, (int)(Art.VH * Zoom));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // 3x 超采样后缩放，边缘更干净
                    int ss = 3;
                    using (var big = new Bitmap(Art.VW * ss, Art.VH * ss, PixelFormat.Format32bppArgb))
                    {
                        using (var bg = Graphics.FromImage(big))
                        {
                            bg.Clear(Color.Transparent);
                            bg.SmoothingMode = SmoothingMode.AntiAlias;
                            bg.ScaleTransform(ss, ss);
                            if (squash > 0.01f)
                            {
                                bg.TranslateTransform(Art.VW / 2f, Art.VH - 1);
                                bg.ScaleTransform(1f + squash * .16f, 1f - squash * .20f);
                                bg.TranslateTransform(-Art.VW / 2f, -(Art.VH - 1));
                            }
                            // 爬墙：不旋转、头朝上，贴着墙面往上走
                            bool wallLeft = (wallSide < 0);
                            if (state == PetState.Walk && !faceRight)
                            {
                                bg.TranslateTransform(Art.VW, 0);
                                bg.ScaleTransform(-1, 1);
                            }
                            bool drawn = false;
                            if (state == PetState.Ceil)
                                drawn = SpriteArt.DrawCeil(bg, Palette.Key, faceRight, Art.VW, 186f * Zoom, 244f * Zoom, Now(), walkPhase);
                            else if (state == PetState.Climb)
                                drawn = SpriteArt.DrawWall(bg, Palette.Key, wallLeft, climbUp, Art.VW, Art.VH, 186f * Zoom, 244f * Zoom, Now(), walkPhase);
                            else if (state == PetState.Walk)
                                drawn = SpriteArt.DrawWalking(bg, Palette.Key, Art.VW / 2f, Art.VH - 1, 186f * Zoom, Now(), walkPhase, faceRight, 244f * Zoom);
                            else if (sleepAmt > .5f)
                                drawn = SpriteArt.DrawSitting(bg, Palette.Key, Art.VW / 2f, Art.VH - 1, 186f * Zoom, Now(), squash, true, 240f * Zoom, lookX, lookY, blink, kneadPhase, false, ss);
                            else
                                drawn = SpriteArt.DrawSitting(bg, Palette.Key, Art.VW / 2f, Art.VH - 1, 186f * Zoom, Now(), squash, false, 240f * Zoom, lookX, lookY, blink, kneadPhase, kneadPhase > 0, ss);
                            if (!drawn)
                            {
                                if (state == PetState.Walk) Art.DrawWalking(bg, Palette, Now(), walkPhase, faceRight, 1f);
                                else if (sleepAmt > .5f) Art.DrawSleeping(bg, Palette, Now(), sleepAmt);
                                else Art.DrawSitting(bg, Palette, Now(), lookX, lookY, blink, sleepAmt, yawn, kneadPhase, faceRight);
                            }
                        }
                        g.DrawImage(big, new Rectangle(0, 0, w, h));
                    }
                }
                if (DebugDir != null && dbgN <= 3) bmp.Save(Path.Combine(DebugDir, "render" + dbgN + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                if (DebugDir != null && (DateTime.Now - lastKeyStamp).TotalMilliseconds < 600 && dbgK < 8)
                {
                    dbgK++;
                    bmp.Save(Path.Combine(DebugDir, "knead_render" + dbgK + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                PushLayered(bmp);
            }
        }

        Bitmap BuildStretch()
        {
            int w = Math.Max(2, Width), h = Math.Max(2, Height);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-x, -y);   // 世界坐标 → 窗口坐标
                if (!SpriteArt.DrawStretched(g, Palette.Key, dragBase, dragHead, Now()))
                    Art.DrawStretched(g, Palette, dragBase, dragHead, Now(), 1f);
            }
            return bmp;
        }

        static int stretchDumpN = 0;
        void RenderStretch()
        {
            using (var bmp = BuildStretch())
            {
                if (DebugDir != null && stretchDumpN < 40)
                {
                    stretchDumpN++;
                    try
                    {
                        bmp.Save(Path.Combine(DebugDir, "st" + stretchDumpN.ToString("00") + ".png"), ImageFormat.Png);
                        File.AppendAllText(Path.Combine(DebugDir, "st.log"),
                            "n=" + stretchDumpN + " win=" + x + "," + y + " " + Width + "x" + Height +
                            " base=" + dragBase.X + "," + dragBase.Y + " head=" + dragHead.X + "," + dragHead.Y +
                            Environment.NewLine);
                    }
                    catch { }
                }
                PushLayered(bmp);
            }
        }

        public void ExportSprites(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var p in Palette.All)
            {
                var key = p.Key;
                if (!SpriteArt.Has(key + "_sit")) continue;
                using (var bmp = new Bitmap(260, 260, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        SpriteArt.DrawSitting(g, key, 130f, 240f, 200f, 0.6, 0f, false);
                    }
                    bmp.Save(Path.Combine(dir, "sprite_sit_" + p.Name + ".png"), ImageFormat.Png);
                }
                using (var bmp = new Bitmap(320, 220, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        SpriteArt.DrawWalking(g, key, 160f, 200f, 168f, 1.0, .25, true);
                    }
                    bmp.Save(Path.Combine(dir, "sprite_walk_" + p.Name + ".png"), ImageFormat.Png);
                }
                using (var bmp = new Bitmap(300, 430, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        SpriteArt.DrawStretched(g, key, new PointF(150, 410), new PointF(150, 110), 1.0);
                    }
                    bmp.Save(Path.Combine(dir, "sprite_stretch_" + p.Name + ".png"), ImageFormat.Png);
                }
            }
        }

        public void AnimTest(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "eyes.txt"), SpriteArt.DebugEyes(Palette.Key));
            float[] lx = { 0f, -1f, 1f, 0f, 0f };
            float[] ly = { 0f, 0f, 0f, -1f, 1f };
            string[] nm = { "center", "left", "right", "up", "down" };
            for (int i = 0; i < lx.Length; i++)
            {
                using (var bmp = new Bitmap(220, 260, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        SpriteArt.DrawSitting(g, Palette.Key, 110f, 250f, 200f, 1.0 + i, 0, false, 200f, lx[i], ly[i], 0, 0, false, 3f);
                    }
                    bmp.Save(Path.Combine(dir, "eye_" + nm[i] + ".png"), ImageFormat.Png);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                using (var bmp = new Bitmap(220, 260, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        SpriteArt.DrawSitting(g, Palette.Key, 110f, 250f, 200f, 1.0 + i, 0, false, 200f, 0, 0, 0, i * 1.57f, true, 3f);
                    }
                    bmp.Save(Path.Combine(dir, "knead_" + i + ".png"), ImageFormat.Png);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                using (var bmp = new Bitmap(220, 260, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        SpriteArt.DrawSitting(g, Palette.Key, 110f, 250f, 200f, 1.0 + i, 0, false, 200f, 0, 0, i * 0.5f, 0, false, 3f);
                    }
                    bmp.Save(Path.Combine(dir, "blink_" + i + ".png"), ImageFormat.Png);
                }
            }
        }

        public void PalShots(string file)
        {
            int cols = 6, cw = 190, ch = 230;
            var all = Palette.All;
            int rows = (all.Length + cols - 1) / cols;
            using (var sheet = new Bitmap(cols * cw, rows * ch * 2, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(247, 244, 239));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    using (var font = new Font("Microsoft YaHei", 10f))
                    using (var br = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            int cx0 = (i % cols) * cw, cy0 = (i / cols) * ch * 2;
                            var pal = all[i];
                            SpriteArt.DrawSitting(g, pal.Key, cx0 + cw / 2f, cy0 + ch - 26, 150f, 0.4, 0f, false, 170f);
                            SpriteArt.DrawSitting(g, pal.Key, cx0 + cw / 2f, cy0 + ch * 2 - 30, 120f, 3.0, 0f, true, 170f);
                            g.DrawString(pal.Name, font, br, cx0 + 8, cy0 + ch * 2 - 22);
                        }
                    }
                }
                sheet.Save(file, ImageFormat.Png);
            }
            Console.WriteLine("share the shots: " + all.Length);
        }

        /// <summary>按程序真实参数出四种姿势，用来核对"大小一致"。</summary>
        public void PoseShot(string dir)
        {
            Directory.CreateDirectory(dir);
            int W = Art.VW, H = Art.VH;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    SpriteArt.DrawSitting(g, Palette.Key, W / 2f, H - 14, 186f, 0.6, 0f, false, 240f, 0f, 0f, 0f, 0f, false, 1f);
                }
                bmp.Save(Path.Combine(dir, "pose_sit.png"), ImageFormat.Png);
            }
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    SpriteArt.DrawWalking(g, Palette.Key, W / 2f, H - 14, 186f, 1.0, 0.25, true, 244f);
                }
                bmp.Save(Path.Combine(dir, "pose_walk.png"), ImageFormat.Png);
            }
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    SpriteArt.DrawSitting(g, Palette.Key, W / 2f, H - 14, 186f, 0.6, 0f, true, 240f, 0f, 0f, 0f, 0f, false, 1f);
                }
                bmp.Save(Path.Combine(dir, "pose_sleep.png"), ImageFormat.Png);
            }
            using (var bmp = new Bitmap(260, 420, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    SpriteArt.DrawStretched(g, Palette.Key, new PointF(130, 400), new PointF(130, 40), 1.0);
                }
                bmp.Save(Path.Combine(dir, "pose_stretch.png"), ImageFormat.Png);
            }
            Console.WriteLine("pose shots -> " + dir);
        }

        public void KneadShot(string dir)
        {
            Directory.CreateDirectory(dir);
            float[] ph = new float[] { 0f, 0.25f, 0.5f, 0.75f, 1.25f, 1.5f, 1.75f, 2.25f };
            int i = 0;
            foreach (float k in ph)
            {
                using (var bmp = new Bitmap(200, 220, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        SpriteArt.DrawSitting(g, Palette.Key, 100f, 210f, 196f, 0.6, 0f, false,
                            190f, 0f, 0f, 0f, k * 6.2831853f, true, 2f);
                    }
                    bmp.Save(Path.Combine(dir, "knead" + i + ".png"), ImageFormat.Png);
                    i++;
                }
            }
            Console.WriteLine("knead frames " + i);
        }

        public void DragShot(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (int len in new[] { 120, 240 })
            {
                dragBase = new PointF(400, 900);
                dragHead = new PointF(410, 900 - len);
                var bb = SpriteArt.StretchBounds(Palette.Key, dragBase, dragHead);
                float left = bb.Left, right = bb.Right, top = bb.Top, bottom = bb.Bottom;
                x = left; y = top;
                Size = new Size((int)(right - left), (int)(bottom - top));
                using (var bmp = BuildStretch())
                {
                    bmp.Save(Path.Combine(dir, "dragwin_" + len + ".png"), ImageFormat.Png);
                    Console.WriteLine("len=" + len + " win=" + Width + "x" + Height);
                }
            }
            dragBase = PointF.Empty; dragHead = PointF.Empty;
        }

        public void DragTest2(string dir)
        {
            Directory.CreateDirectory(dir);
            float[] lifts = { 110, 180, 240 };
            for (int i = 0; i < lifts.Length; i++)
            {
                using (var bmp = new Bitmap((int)(240 * Zoom) + 120, (int)lifts[i] + 160, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        var b = new PointF(120, lifts[i] + 150);
                        var h = new PointF(120 + i * 12, lifts[i] + 150 - lifts[i]);
                        SpriteArt.DrawStretched(g, Palette.Key, b, h, 1.0);
                    }
                    bmp.Save(Path.Combine(dir, "sd" + i + ".png"), ImageFormat.Png);
                }
            }
        }

        public void DragTest(string dir)
        {
            Directory.CreateDirectory(dir);
            var wa = Screen.PrimaryScreen.WorkingArea;
            float[] lifts = { 100, 200, 300 };
            for (int i = 0; i < lifts.Length; i++)
            {
                state = PetState.Drag;
                dragBase = new PointF(wa.Left + 420, wa.Bottom - 2);
                dragHead = new PointF(wa.Left + 420 + i * 45, wa.Bottom - 2 - lifts[i]);
                using (var bmp = BuildStretch())
                    bmp.Save(Path.Combine(dir, "drag" + i + ".png"), ImageFormat.Png);
            }
            state = PetState.Sit;
        }

        void PushLayered(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBitmap);
            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT { x = 0, y = 0 };
                var dst = new POINT { x = (int)x, y = (int)y };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        bool pendingDrag = false;
        Point downPt = Point.Empty;

        void BeginDrag()
        {
            pendingDrag = false;
            grabOffX = Cursor.Position.X - (x + Width / 2f);
            // 头相对鼠标的抓取偏移：抓肚子时头在鼠标上方，但不能跑太远（否则看不见）
            float headScreenY = y + 46f;
            grabHeadDY = Math.Max(-100f, Math.Min(50f, headScreenY - Cursor.Position.Y));
            dragHead = new PointF(Cursor.Position.X, Cursor.Position.Y);
            dragBase = new PointF(Cursor.Position.X - grabOffX, Screen.PrimaryScreen.WorkingArea.Bottom - 2);
            state = PetState.Drag; stateT = 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Show(Cursor.Position);
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                dragOffset = new Point(Cursor.Position.X - (int)x, Cursor.Position.Y - (int)y);
                pendingDrag = true;
                downPt = Cursor.Position;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // 拖动中的位置由 OnTick 统一计算（拉伸形态），这里不做跟随
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (state == PetState.Drag) ReleaseDrag();
            else { pendingDrag = false; Capture = false; }
        }

        void ReleaseDrag()
        {
            Capture = false;
            dragOffset = null;
            Size = new Size((int)(Art.VW * Zoom), (int)(Art.VH * Zoom));
            x = dragHead.X - Width / 2f;
            y = Math.Max(0, dragHead.Y - Height * 0.55f);
            state = PetState.Fall; stateT = 0; vy = 0;
            standHwnd = IntPtr.Zero;
        }

        void Quit()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            tray.Visible = false;
            timer.Stop();
            Application.Exit();
        }

        public void ExportFrames(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var p in Palette.All)
            {
                Palette = p;
                string tag = p.Name;
                using (var bmp = new Bitmap(Art.VW, Art.VH, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Art.DrawSitting(g, p, 0.6, .3f, -.2f, 0f, 0f, 0f, 0f, true);
                    }
                    bmp.Save(Path.Combine(dir, "sit_" + tag + ".png"), ImageFormat.Png);
                }
                using (var bmp = new Bitmap(Art.VW, Art.VH, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Art.DrawSleeping(g, p, 2.0, 1f);
                    }
                    bmp.Save(Path.Combine(dir, "sleep_" + tag + ".png"), ImageFormat.Png);
                }
                using (var bmp = new Bitmap(Art.VW, Art.VH, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Art.DrawWalking(g, p, 1.0, .25, true, 1f);
                    }
                    bmp.Save(Path.Combine(dir, "walk_" + tag + ".png"), ImageFormat.Png);
                }
                using (var bmp = new Bitmap(260, 420, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Art.DrawStretched(g, p, new PointF(130, 400), new PointF(130, 80), 1.0, 1f);
                    }
                    bmp.Save(Path.Combine(dir, "stretch_" + tag + ".png"), ImageFormat.Png);
                }
            }
        }
    }

    // ---------------- 配置 ----------------
    static class Config
    {
        static string PathFile
        {
            get
            {
                var d = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopCat");
                Directory.CreateDirectory(d);
                return System.IO.Path.Combine(d, "config.ini");
            }
        }

        public static void Save(PetForm f)
        {
            var sb = new StringBuilder();
            sb.AppendLine("palette=" + f.Palette.Name);
            sb.AppendLine("scale=" + f.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("walk=" + (f.AutoWalk ? "1" : "0"));
            File.WriteAllText(PathFile, sb.ToString(), Encoding.UTF8);
        }

        public static void Load(PetForm f)
        {
            try
            {
                if (!File.Exists(PathFile)) return;
                foreach (var line in File.ReadAllLines(PathFile))
                {
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    var k = line.Substring(0, i).Trim();
                    var v = line.Substring(i + 1).Trim();
                    if (k == "palette") { foreach (var p in Palette.All) if (p.Name == v) f.Palette = p; }
                    else if (k == "scale") { float s; if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) f.Zoom = s; }
                    else if (k == "walk") f.AutoWalk = v == "1";
                }
                f.ApplySize();
            }
            catch { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartupEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("DesktopCat") != null;
            }
            catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("DesktopCat", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("DesktopCat", false);
                }
            }
            catch { }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--mock")
            {
                PetForm.DebugDir = args[1];
                Directory.CreateDirectory(args[1]);
                var fm = new PetForm { AutoWalk = false };
                fm.MockState(args[2]);
                Application.Run(fm); return;
            }
            if (args.Length >= 2 && args[0] == "--debugdir")
            {
                PetForm.DebugDir = args[1];
                Directory.CreateDirectory(args[1]);
            }
            if (args.Length >= 2 && args[0] == "--reslist")
            {
                var sb = new System.Text.StringBuilder();
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var n in asm.GetManifestResourceNames())
                {
                    var bmp = SpriteArt.Get(n.EndsWith(".png") ? n.Substring(0, n.Length - 4) : n);
                    sb.AppendLine(n + " -> " + (bmp == null ? "NULL" : bmp.Width + "x" + bmp.Height));
                }
                sb.AppendLine("Dir=" + SpriteArt.Dir + " enabled=" + SpriteArt.Enabled);
                File.WriteAllText(args[1], sb.ToString());
                return;
            }
            if (args.Length >= 2 && args[0] == "--exportsprites")
            {
                var f = new PetForm();
                f.ExportSprites(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--animtest")
            {
                var fa = new PetForm(); fa.AnimTest(args[1]); return;
            }
            if (args.Length >= 2 && args[0] == "--palshots")
            {
                Application.EnableVisualStyles();
                using (var fp = new PetForm()) { fp.PalShots(args[1]); }
                return;
            }
            if (args.Length >= 2 && args[0] == "--poseshot")
            {
                var fp = new PetForm();
                if (args.Length > 2) foreach (var pp in Palette.All) if (pp.Name == args[2] || pp.Key == args[2]) fp.Palette = pp;
                fp.PoseShot(args[1]); return;
            }
            if (args.Length >= 2 && args[0] == "--kneadshot")
            {
                var fk = new PetForm();
                if (args.Length > 2) foreach (var pp in Palette.All) if (pp.Name == args[2] || pp.Key == args[2]) fk.Palette = pp;
                fk.KneadShot(args[1]); return;
            }
            if (args.Length >= 2 && args[0] == "--dragshot")
            {
                var fs2 = new PetForm(); fs2.DragShot(args[1]); return;
            }
            if (args.Length >= 2 && args[0] == "--dragtest2")
            {
                var f2 = new PetForm(); f2.DragTest2(args[1]); return;
            }
            if (args.Length >= 2 && args[0] == "--dragtest")
            {
                var f = new PetForm();
                f.DragTest(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--export")
            {
                var f = new PetForm();
                f.ExportFrames(args[1]);
                return;
            }
            if (args.Length >= 1 && args[0] == "--selftest")
            {
                var f = new PetForm();
                Console.WriteLine("selftest: window=" + f.Width + "x" + f.Height);
                return;
            }

            bool mutexOwned;
            using (var m = new System.Threading.Mutex(true, "DesktopCat_SingleInstance", out mutexOwned))
            {
                if (!mutexOwned) return;   // 已经在跑
                Application.EnableVisualStyles();
                var form = new PetForm();
                Config.Load(form);
                Application.Run(form);
            }
        }
    }
}
