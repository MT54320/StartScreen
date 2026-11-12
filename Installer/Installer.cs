using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Setup for the Windows 8.1 Start screen ("A Project Made By MT").
// A borderless full-screen wizard in the style of Windows 8.1 setup: it opens on "pick a colour",
// installs per-user (no admin needed), and copies itself into the install folder as Uninstall.exe.
namespace StartScreenSetup
{
    static class Info
    {
        public const string Name = "Start screen";
        public const string Version = "1.0";
        public const string Publisher = "MT";
        public const string AppExe = "StartScreen.exe";
        public static readonly Color LogoGray = Color.FromArgb(96, 96, 96);   // the logo's accent (matches the icon)
        public static readonly Color ModifyBlue = Color.FromArgb(30, 102, 178);   // the blue of the Windows "Choose an option" screen
        public static readonly Color TileBlue = Color.FromArgb(78, 167, 225);     // its option tiles
        public const string RunName = "StartScreen8";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StartScreen8";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "StartScreen"); }
        }
        public static string StartMenuDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
        public static string UserData { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StartScreen8"); } }
    }

    static class Hsv
    {
        public static Color From(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        // Same background shade the app uses for its first shade choice (hue < 0 = grey).
        public static Color Background(double hue) { return hue < 0 ? From(0, 0, 0.34) : From(hue, 1.0, 0.44); }
    }

    static class Worker
    {
        public static void StopApp()
        {
            foreach (var p in Process.GetProcessesByName("StartScreen"))
            {
                try { p.Kill(); p.WaitForExit(4000); } catch { }
            }
        }

        public static void Shortcut(string lnk, string target, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            dynamic s = sh.CreateShortcut(lnk);
            s.TargetPath = target;
            s.WorkingDirectory = Path.GetDirectoryName(target);
            s.Description = desc;
            s.IconLocation = target + ",0";
            s.Save();
        }

        public static void Install(string dir, bool desktop, bool startMenu, bool runAtSignIn, double? hue, string pattern)
        {
            StopApp();
            Directory.CreateDirectory(dir);
            string appPath = Path.Combine(dir, Info.AppExe);
            using (var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(Info.AppExe))
            {
                if (rs == null) throw new InvalidOperationException("The setup file is damaged (the app is missing from it).");
                using (var fs = File.Create(appPath)) rs.CopyTo(fs);
            }
            string uninst = Path.Combine(dir, "Uninstall.exe");
            string selfPath = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(selfPath), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))   // repairing from Uninstall.exe itself: already in place
                File.Copy(selfPath, uninst, true);

            string startLnk = Path.Combine(Info.StartMenuDir, Info.Name + ".lnk");
            string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Info.Name + ".lnk");
            if (startMenu) Shortcut(startLnk, appPath, "Windows 8.1 style Start screen"); else TryDelete(startLnk);
            if (desktop) Shortcut(deskLnk, appPath, "Windows 8.1 style Start screen"); else TryDelete(deskLnk);

            long kb = (new FileInfo(appPath).Length + new FileInfo(uninst).Length) / 1024;
            using (var k = Registry.CurrentUser.CreateSubKey(Info.UninstallKey))
            {
                k.SetValue("DisplayName", Info.Name + " (Windows 8.1 style)");
                k.SetValue("DisplayVersion", Info.Version);
                k.SetValue("Publisher", Info.Publisher);
                k.SetValue("DisplayIcon", appPath);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /quiet");
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(Info.RunKey))
            {
                if (runAtSignIn) k.SetValue(Info.RunName, "\"" + appPath + "\" /background");
                else k.DeleteValue(Info.RunName, false);
            }

            // Hand the colour / background chosen in setup to the app. "-" = left as it was (a reinstall
            // never overwrites colours you already had unless you picked something new).
            try
            {
                Directory.CreateDirectory(Info.UserData);
                string choice = (hue.HasValue ? hue.Value.ToString("0.#", CultureInfo.InvariantCulture) : "-") + "\r\n" + (pattern ?? "-");
                if (hue.HasValue || pattern != null) File.WriteAllText(Path.Combine(Info.UserData, "setup-colour.txt"), choice);
            }
            catch { }
        }
        // Where Start screen is installed (null if it isn't): the Windows Apps-list entry exists.
        public static string InstalledDir()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Info.UninstallKey))
                {
                    string loc = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(loc)) return loc;   // registered = installed, even if files went missing (that is what Repair is for)
                }
            }
            catch { }
            return null;
        }

        public static string InstalledVersion()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Info.UninstallKey)) return k == null ? null : k.GetValue("DisplayVersion") as string; }
            catch { return null; }
        }

        // Repair: put the app files, Uninstall.exe and shortcuts back. Colours, layout and the sign-in setting are left as they are.
        public static void Repair()
        {
            string dir = InstalledDir();
            if (dir == null) throw new InvalidOperationException("Start screen isn't installed here, so there is nothing to repair.");
            bool desktop = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Info.Name + ".lnk"));
            bool run;
            using (var k = Registry.CurrentUser.OpenSubKey(Info.RunKey)) run = k != null && k.GetValue(Info.RunName) != null;
            Install(dir, desktop, true, run, null, null);
            Preload(dir);
        }

        // Starts the app hidden and waits (up to 45s) until it reports that every app and icon is loaded, so the
        // first time Start is opened it is already complete.
        public static void Preload(string dir)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(dir, Info.AppExe), "/background") { UseShellExecute = false });
                var sw = Stopwatch.StartNew();
                EventWaitHandle ev = null;
                while (sw.Elapsed.TotalSeconds < 45)
                {
                    if (ev == null) EventWaitHandle.TryOpenExisting(@"Local\StartScreen8.Loaded", out ev);
                    if (ev != null) { if (ev.WaitOne(250)) break; }
                    else Thread.Sleep(250);
                }
            }
            catch { }
        }

        public static void Uninstall(bool removeSettings)
        {
            StopApp();
            string dir = null;
            using (var k = Registry.CurrentUser.OpenSubKey(Info.UninstallKey))
                if (k != null) dir = k.GetValue("InstallLocation") as string;
            if (string.IsNullOrEmpty(dir)) dir = Info.DefaultDir;

            TryDelete(Path.Combine(Info.StartMenuDir, Info.Name + ".lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Info.Name + ".lnk"));
            using (var k = Registry.CurrentUser.CreateSubKey(Info.RunKey)) k.DeleteValue(Info.RunName, false);
            try { Registry.CurrentUser.DeleteSubKeyTree(Info.UninstallKey, false); } catch { }
            if (removeSettings) { try { if (Directory.Exists(Info.UserData)) Directory.Delete(Info.UserData, true); } catch { } }
            // Main() relaunches Uninstall.exe from %TEMP% first, so the install folder isn't locked by us.
            // Windows can keep StartScreen.exe locked for a moment after the app exits (or while it is scanned), so one try
            // sometimes fails and silently leaves the folder behind. Keep trying for a few seconds.
            for (int attempt = 0; attempt < 25 && Directory.Exists(dir); attempt++)
            {
                try { Directory.Delete(dir, true); }
                catch { Thread.Sleep(400); }
            }
        }

        static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }

    // ------------------------------------------------------------------ controls
    //
    // Every visible element is drawn by us (text as vector outlines) so that:
    //  - the first inked pixel of every text line sits exactly on the element's left edge, so text, tick
    //    boxes, the swatch row and buttons share one precise left edge, and
    //  - each element can fade and slide independently for smooth page transitions.

    abstract class Fx : Control
    {
        public const int MaxPad = 48;              // most slack any element gets either side (for sliding)
        public int Pad = MaxPad;                   // this element's slack; buttons use 0 so they never overlap a neighbour
        public float Alpha = 1, Dx = 0;
        float fromA, toA, fromD, toD; int t0, dur, delay; bool run;
        public static readonly FontFamily Light = new FontFamily("Segoe UI Light");
        public static readonly FontFamily Regular = new FontFamily("Segoe UI");

        protected Fx()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        public int ContentW { get { return Width - 2 * Pad; } }
        public void Place(int x, int y, int contentW, int h) { Bounds = new Rectangle(x - Pad, y, contentW + 2 * Pad, h); }

        public void Set(float a, float dx) { Alpha = a; Dx = dx; run = false; Invalidate(); }
        public void AnimTo(float a, float dx, int ms, int delayMs)
        {
            fromA = Alpha; fromD = Dx; toA = a; toD = dx; t0 = Environment.TickCount; dur = Math.Max(1, ms); delay = delayMs; run = true;
        }
        public bool Step()
        {
            if (!run) return false;
            int el = unchecked(Environment.TickCount - t0) - delay;
            if (el < 0) return true;
            double u = Math.Min(1, (double)el / dur), e = 1 - Math.Pow(1 - u, 3);
            Alpha = fromA + (toA - fromA) * (float)e; Dx = fromD + (toD - fromD) * (float)e;
            Invalidate();
            if (u >= 1) run = false;
            return run;
        }

        protected bool Inside(MouseEventArgs e) { return e.X >= Pad && e.X < Width - Pad; }
        protected static Color W(float a) { return Color.FromArgb((int)(255 * Math.Max(0, Math.Min(1, a))), 255, 255, 255); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Alpha <= 0.003f) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(Pad + Dx, 0);
            Draw(g, Math.Max(0, Math.Min(1, Alpha)));
        }
        protected abstract void Draw(Graphics g, float a);

        // text outline at a given em height (pixels), laid out to maxW; returns ink bounds
        public static GraphicsPath TextPath(string text, FontFamily f, float px, float maxW, out RectangleF ink)
        {
            var p = new GraphicsPath();
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
                p.AddString(text ?? "", f, (int)FontStyle.Regular, px, new RectangleF(0, 0, maxW, 6000), sf);
            ink = p.PointCount > 0 ? p.GetBounds() : new RectangleF(0, 0, 0, px);
            return p;
        }
        // vertical centre of capital letters for a font, so labels centre on caps, not on descenders
        public static float CapCentre(FontFamily f, float px)
        {
            RectangleF b; using (TextPath("H", f, px, 1000, out b)) { }
            return b.Y + b.Height / 2;
        }
    }

    class Txt : Fx
    {
        string text; FontFamily fam; float px, dim; int maxW;
        GraphicsPath path; RectangleF ink;
        public Txt(string text, FontFamily fam, float px, int maxW, float dim, int x, int y)
        {
            this.fam = fam; this.px = px; this.maxW = maxW; this.dim = dim;
            SetText(text);
            Place(x, y, maxW, Height);
        }
        public void SetText(string t, float? newPx = null)
        {
            text = t; if (newPx.HasValue) px = newPx.Value;
            if (path != null) path.Dispose();
            path = TextPath(text, fam, px, maxW, out ink);
            Height = (int)Math.Ceiling(ink.Bottom) + 6;
            Invalidate();
        }
        protected override void Draw(Graphics g, float a)
        {
            g.TranslateTransform(-ink.X, 0);
            using (var br = new SolidBrush(W(a * dim))) g.FillPath(br, path);
        }
    }

    class Btn : Fx
    {
        readonly string text; readonly Action click; readonly float px; float hover, hoverTo;
        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 15 };
        public static Color Ink = Color.Black;
        public Btn(string text, float px, int x, int y, int w, int h, Action click)
        {
            this.text = text; this.px = px; this.click = click;
            Pad = 0; Place(x, y, w, h); Cursor = Cursors.Hand;
            tm.Tick += (s, e) =>
            {
                hover += (hoverTo - hover) * 0.28f;
                if (Math.Abs(hoverTo - hover) < 0.01f) { hover = hoverTo; tm.Stop(); }
                Invalidate();
            };
        }
        protected override void OnMouseEnter(EventArgs e) { hoverTo = 1; tm.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverTo = 0; tm.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Inside(e) && ClientRectangle.Contains(e.Location)) click(); base.OnMouseUp(e); }
        protected override void Dispose(bool d) { if (d) tm.Dispose(); base.Dispose(d); }
        protected override void Draw(Graphics g, float a)
        {
            int w = ContentW, h = Height;
            using (var br = new SolidBrush(Color.FromArgb((int)(60 * a), 0, 0, 0))) g.FillRectangle(br, 1, 1, w - 2, h - 2);
            using (var br = new SolidBrush(W(a * hover))) g.FillRectangle(br, 1, 1, w - 2, h - 2);
            using (var p = new Pen(W(a), 2)) g.DrawRectangle(p, 1, 1, w - 2, h - 2);
            RectangleF ink;
            using (var path = TextPath(text, Regular, px, 1000, out ink))
            {
                float cap = CapCentre(Regular, px);
                g.TranslateTransform((w - ink.Width) / 2f - ink.X, h / 2f - cap);
                var c = Color.FromArgb((int)(255 * a), (int)(255 + (Ink.R - 255) * hover), (int)(255 + (Ink.G - 255) * hover), (int)(255 + (Ink.B - 255) * hover));
                using (var br = new SolidBrush(c)) g.FillPath(br, path);
            }
        }
    }

    class Tick : Fx
    {
        public bool On; readonly string text; readonly float px; readonly int box; readonly Action<bool> set;
        float v; readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 15 };
        public Tick(string text, bool on, float px, int box, int x, int y, int w, Action<bool> set)
        {
            this.text = text; On = on; this.px = px; this.box = box; this.set = set; v = on ? 1 : 0;
            Place(x, y, w, box + 4); Cursor = Cursors.Hand;
            tm.Tick += (s, e) =>
            {
                float t = On ? 1 : 0; v += (t - v) * 0.3f;
                if (Math.Abs(t - v) < 0.02f) { v = t; tm.Stop(); }
                Invalidate();
            };
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Inside(e)) { On = !On; set(On); tm.Start(); }
            base.OnMouseUp(e);
        }
        protected override void Dispose(bool d) { if (d) tm.Dispose(); base.Dispose(d); }
        protected override void Draw(Graphics g, float a)
        {
            float cy = Height / 2f, by = cy - box / 2f;
            using (var p = new Pen(W(a), 2)) g.DrawRectangle(p, 1, by + 1, box - 2, box - 2);
            if (v > 0.01f)
            {
                float inset = 5 + (1 - v) * (box / 2f - 5), s = box - 2 * inset;
                using (var br = new SolidBrush(W(a * v))) g.FillRectangle(br, inset, by + inset, s, s);
            }
            RectangleF ink;
            using (var path = TextPath(text, Regular, px, 2000, out ink))
            {
                g.TranslateTransform(box + box * 0.55f - ink.X, cy - CapCentre(Regular, px));
                using (var br = new SolidBrush(W(a))) g.FillPath(br, path);
            }
        }
    }

    // 8.1 setup's colour row: a strip of swatches with a marker above and below the chosen one.
    class Swatches : Fx
    {
        public readonly double[] Hues;
        public int Index;
        public event Action Changed;
        readonly int margin; float pos;
        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 15 };
        bool down;

        public Swatches(int count, int x, int y, int w, int barH, int margin, double startHue)
        {
            Hues = new double[count];
            Hues[0] = -1;                                           // grey
            for (int i = 1; i < count; i++) Hues[i] = (i - 1) * 360.0 / (count - 1);
            this.margin = margin;
            Place(x, y, w, barH + 2 * margin); Cursor = Cursors.Hand;
            int best = 1; double bd = 1e9;
            for (int i = 1; i < count; i++) { double d = Math.Abs(Hues[i] - startHue); if (d < bd) { bd = d; best = i; } }
            Index = best; pos = best;
            tm.Tick += (s, e) =>
            {
                pos += (Index - pos) * 0.26f;
                if (Math.Abs(Index - pos) < 0.005f) { pos = Index; tm.Stop(); }
                Invalidate();
            };
        }
        public double Hue { get { return Hues[Index]; } }

        void Pick(MouseEventArgs e)
        {
            double cw = ContentW / (double)Hues.Length;
            int i = (int)Math.Floor((e.X - Pad) / cw);
            i = Math.Max(0, Math.Min(Hues.Length - 1, i));
            if (i == Index) return;
            Index = i; tm.Start();
            var c = Changed; if (c != null) c();
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Capture = true; Pick(e); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (down) Pick(e); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Capture = false; base.OnMouseUp(e); }
        protected override void Dispose(bool d) { if (d) tm.Dispose(); base.Dispose(d); }

        protected override void Draw(Graphics g, float a)
        {
            g.SmoothingMode = SmoothingMode.None;
            int n = Hues.Length, w = ContentW, barH = Height - 2 * margin;
            float cw = w / (float)n;
            for (int i = 0; i < n; i++)
            {
                int x0 = (int)Math.Round(i * cw), x1 = (int)Math.Round((i + 1) * cw);
                var col = Hues[i] < 0 ? Hsv.From(0, 0, 0.5) : Hsv.From(Hues[i], 0.92, 0.78);
                using (var br = new SolidBrush(Color.FromArgb((int)(255 * a), col))) g.FillRectangle(br, x0, margin, x1 - x0, barH);
                float s = cw * 0.34f;
                using (var br = new SolidBrush(Color.FromArgb((int)(70 * a), 255, 255, 255))) g.FillRectangle(br, x0 + cw * 0.10f, margin + barH * 0.14f, s, s);
                using (var br = new SolidBrush(Color.FromArgb((int)(55 * a), 0, 0, 0))) g.FillRectangle(br, x0 + cw * 0.50f, margin + barH * 0.14f + s * 0.55f, s, s);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = (pos + 0.5f) * cw, t = Math.Max(5f, margin * 0.55f);
            using (var br = new SolidBrush(W(a)))
            {
                g.FillPolygon(br, new[] { new PointF(cx - t, 1), new PointF(cx + t, 1), new PointF(cx, margin - 3) });
                g.FillPolygon(br, new[] { new PointF(cx - t, Height - 1), new PointF(cx + t, Height - 1), new PointF(cx, Height - margin + 3) });
            }
        }
    }

    // 8.1-style "working" dots that glide across and ease out.
    class Dots : Fx
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 16 };
        public Dots(int x, int y, int w, int h)
        {
            Place(x, y, w, h);
            tm.Tick += (s, e) => Invalidate();
            tm.Start();
        }
        protected override void Dispose(bool d) { if (d) tm.Dispose(); base.Dispose(d); }
        protected override void Draw(Graphics g, float a)
        {
            double cycle = 3.4, t = (clock.Elapsed.TotalSeconds % cycle) / cycle;
            int d = Height;
            using (var br = new SolidBrush(W(a)))
                for (int i = 0; i < 5; i++)
                {
                    double u = t - i * 0.06;
                    if (u < 0 || u > 1) continue;
                    double s2 = u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2;
                    g.FillEllipse(br, (float)(s2 * (ContentW - d)), 0, d, d);
                }
        }
    }

    // Start-screen backgrounds, drawn with GDI+ the way the app draws them (simplified), tinted with
    // the colour chosen on the first screen. Names match the app's own background names.
    static class Art
    {
        public static readonly string[] Names = { "none", "chevrons", "zigzag", "circles", "waves", "dots", "grid", "diamonds", "hex", "crosses", "arcs", "blocks", "wallpaper" };
        public static readonly string[] Titles = { "Plain", "Chevrons", "Zigzag", "Rings", "Waves", "Halftone", "Grid", "Diamonds", "Honeycomb", "Crosses", "Arcs", "Blocks", "Desktop picture" };

        static Image desk; static bool deskTried;
        public static Image Desktop()
        {
            if (deskTried) return desk;
            deskTried = true;
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(p)) desk = Image.FromStream(new MemoryStream(File.ReadAllBytes(p)));
            }
            catch { desk = null; }
            return desk;
        }

        public static Bitmap Thumb(string name, Color bg, Color ink, int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(bg);
                if (name == "wallpaper")
                {
                    var d = Desktop();
                    if (d != null)
                    {
                        double s = Math.Max(w / (double)d.Width, h / (double)d.Height);
                        int dw = (int)(d.Width * s), dh = (int)(d.Height * s);
                        g.DrawImage(d, (w - dw) / 2, (h - dh) / 2, dw, dh);
                        using (var br = new SolidBrush(Color.FromArgb(0x77, 0, 0, 0))) g.FillRectangle(br, 0, 0, w, h);
                    }
                    return bmp;
                }
                const float W = 700f, H = 394f;
                g.ScaleTransform(w / W, h / H);
                int seed = 17; foreach (char ch in name) seed = seed * 31 + ch;
                var rnd = new Random(seed);
                Func<double, Brush> B = a => new SolidBrush(Color.FromArgb((int)(255 * Math.Max(0, Math.Min(1, a * 1.8))), ink));
                Func<double, float, Pen> P = (a, wd) => new Pen(Color.FromArgb((int)(255 * Math.Max(0, Math.Min(1, a * 1.8))), ink), wd) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                switch (name)
                {
                    case "chevrons":
                        {
                            float step = 56, amp = 28;
                            using (var p = P(0.20, 7))
                                for (float y = -amp; y < H + step; y += step)
                                {
                                    var pts = new List<PointF>(); bool up = true;
                                    for (float x = -step; x <= W + step * 2; x += step, up = !up) pts.Add(new PointF(x, y + (up ? 0 : amp)));
                                    g.DrawLines(p, pts.ToArray());
                                }
                            break;
                        }
                    case "zigzag":
                        for (float y = 30; y < H + 40; y += 64)
                        {
                            var pts = new List<PointF>(); int k = 0;
                            for (float x = -40; x < W + 80; x += 40, k++) pts.Add(new PointF(x, y + (k % 2 == 0 ? -22 : 22)));
                            double depth = y / H;
                            using (var p = P(0.10 + depth * 0.12, (float)(3 + depth * 6))) g.DrawLines(p, pts.ToArray());
                        }
                        break;
                    case "circles":
                        for (int i = 0; i < 11; i++)
                        {
                            double r = 20 + Math.Pow(rnd.NextDouble(), 2) * 190;
                            double cx = rnd.NextDouble() * W, cy = rnd.NextDouble() * H;
                            var rect = new RectangleF((float)(cx - r), (float)(cy - r), (float)(2 * r), (float)(2 * r));
                            if (rnd.NextDouble() < 0.3) using (var b = B(0.05 + rnd.NextDouble() * 0.12)) g.FillEllipse(b, rect);
                            else using (var p = P(0.14 + rnd.NextDouble() * 0.35, (float)(2 + rnd.NextDouble() * 9))) g.DrawEllipse(p, rect);
                        }
                        break;
                    case "waves":
                        for (int i = 0; i < 7; i++)
                        {
                            double baseY = H * (0.40 + 0.075 * i), amp = 18 + rnd.NextDouble() * 34, freq = 2 * Math.PI / (260 + rnd.NextDouble() * 360), ph = rnd.NextDouble() * 6;
                            var pts = new List<PointF> { new PointF(0, H) };
                            for (float x = 0; x <= W + 10; x += 10) pts.Add(new PointF(x, (float)(baseY + Math.Sin(x * freq + ph) * amp)));
                            pts.Add(new PointF(W + 10, H));
                            using (var b = B(0.08 + i * 0.012)) g.FillPolygon(b, pts.ToArray());
                        }
                        break;
                    case "dots":
                        using (var b = B(0.34))
                            for (float x = 9; x < W; x += 18)
                                for (float y = 9; y < H; y += 18)
                                {
                                    double v = 0.5 + 0.5 * Math.Sin(x / 260.0 + Math.Cos(y / 150.0) * 1.6);
                                    double r = v * v * 7.5 * (0.3 + 0.7 * y / H);
                                    if (r < 0.9) continue;
                                    g.FillEllipse(b, (float)(x - r), (float)(y - r), (float)(2 * r), (float)(2 * r));
                                }
                        break;
                    case "grid":
                        {
                            using (var thin = P(0.09, 1)) using (var thick = P(0.22, 2))
                            {
                                for (float x = 0; x < W; x += 24) g.DrawLine(((int)(x / 24)) % 5 == 0 ? thick : thin, x, 0, x, H);
                                for (float y = 0; y < H; y += 24) g.DrawLine(((int)(y / 24)) % 5 == 0 ? thick : thin, 0, y, W, y);
                            }
                            for (int i = 0; i < 12; i++) using (var b = B(0.07 + rnd.NextDouble() * 0.14)) g.FillRectangle(b, rnd.Next((int)(W / 24)) * 24, rnd.Next((int)(H / 24)) * 24, 24, 24);
                            break;
                        }
                    case "diamonds":
                        {
                            float s = 28, step = 42; int row = 0;
                            for (float y = 0; y < H + step; y += step / 2, row++)
                                for (float x = (row % 2) * step / 2; x < W + step; x += step)
                                {
                                    double wave = 0.5 + 0.5 * Math.Sin(x / 240.0 + y / 160.0);
                                    int k = Math.Min(4, (int)(wave * rnd.NextDouble() * 5.5));
                                    using (var b = B(0.03 + k * 0.06))
                                        g.FillPolygon(b, new[] { new PointF(x, y - s / 2), new PointF(x + s / 2, y), new PointF(x, y + s / 2), new PointF(x - s / 2, y) });
                                }
                            break;
                        }
                    case "hex":
                        {
                            float s = 34, hw = (float)(Math.Sqrt(3) * s); int row = 0;
                            using (var p = P(0.18, 1.5f))
                                for (float y = 0; y < H + s * 2; y += s * 1.5f, row++)
                                    for (float x = (row % 2) * hw / 2; x < W + hw; x += hw)
                                    {
                                        var pts = new PointF[6];
                                        for (int i = 0; i < 6; i++) { double ang = Math.PI / 180 * (60 * i - 30); pts[i] = new PointF((float)(x + s * 0.92 * Math.Cos(ang)), (float)(y + s * 0.92 * Math.Sin(ang))); }
                                        if (rnd.NextDouble() < 0.12) using (var b = B(0.22)) g.FillPolygon(b, pts); else g.DrawPolygon(p, pts);
                                    }
                            break;
                        }
                    case "crosses":
                        for (float x = 14; x < W; x += 28)
                            for (float y = 14; y < H; y += 28)
                                using (var p = P(0.08 + rnd.Next(4) * 0.08, 2)) { g.DrawLine(p, x - 5, y, x + 5, y); g.DrawLine(p, x, y - 5, x, y + 5); }
                        break;
                    case "arcs":
                        for (float ox = 0; ox < W + 300; ox += 310)
                        {
                            bool top = ((int)(ox / 310)) % 2 == 1; float cy = top ? 0 : H;
                            using (var p = P(0.18, 3))
                                for (float r = 25; r < 320; r += 18) g.DrawEllipse(p, ox - r, cy - r, 2 * r, 2 * r);
                        }
                        break;
                    case "blocks":
                        {
                            float u = 34;
                            for (int i = 0; i < 70; i++)
                            {
                                float bw = u * (1 + rnd.Next(3)), bh = u * (1 + rnd.Next(3));
                                float x = (float)Math.Floor(rnd.NextDouble() * W / u) * u, y = (float)Math.Floor(rnd.NextDouble() * H / u) * u;
                                double edge = Math.Abs(y / H - 0.5) * 2;
                                if (rnd.NextDouble() > edge * 1.1) continue;
                                var rc = new RectangleF(x + 2, y + 2, bw - 4, bh - 4);
                                if (rnd.NextDouble() < 0.6) using (var b = B(0.05 + rnd.NextDouble() * 0.2)) g.FillRectangle(b, rc.X, rc.Y, rc.Width, rc.Height);
                                else using (var p = P(0.25, 1.5f)) g.DrawRectangle(p, rc.X, rc.Y, rc.Width, rc.Height);
                            }
                            break;
                        }
                }
            }
            return bmp;
        }
    }

    // Grid of background previews; the chosen one gets a white frame that fades in.
    class Thumbs : Fx
    {
        readonly List<string> names = new List<string>(), titles = new List<string>();
        readonly List<Bitmap> bmps = new List<Bitmap>();
        readonly List<float> selA = new List<float>();
        const int M = 8;
        readonly int cols, tw, th, gap, rowH; readonly float labelPx; int hover = -1, sel;
        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 15 };
        public event Action Changed;
        public string Selected { get { return names[sel]; } }

        public Thumbs(int x, int y, int totalW, int gap, int cols, float labelPx, Color bg, Color ink, string current, int u)
        {
            this.cols = cols; this.gap = gap; this.labelPx = labelPx;
            tw = (totalW - gap * (cols - 1)) / cols; th = tw * 9 / 16; rowH = th + (int)(labelPx * 2.4f) + gap / 2;
            for (int i = 0; i < Art.Names.Length; i++)
            {
                if (Art.Names[i] == "wallpaper" && Art.Desktop() == null) continue;
                names.Add(Art.Names[i]); titles.Add(Art.Titles[i]);
                bmps.Add(Art.Thumb(Art.Names[i], bg, ink, tw, th));
                selA.Add(0);
            }
            sel = Math.Max(0, names.IndexOf(current)); selA[sel] = 1;
            int rows = (names.Count + cols - 1) / cols;
            Place(x, y - M, totalW, rows * rowH + 2 * M);
            Cursor = Cursors.Hand;
            tm.Tick += (s, e) =>
            {
                bool more = false;
                for (int i = 0; i < selA.Count; i++)
                {
                    float t = i == sel ? 1 : 0; selA[i] += (t - selA[i]) * 0.3f;
                    if (Math.Abs(t - selA[i]) < 0.02f) selA[i] = t; else more = true;
                }
                if (!more) tm.Stop();
                Invalidate();
            };
        }

        int HitTest(MouseEventArgs e)
        {
            int px = e.X - Pad;
            if (px < 0) return -1;
            int ey = e.Y - M; if (ey < 0) return -1;
            int c = px / (tw + gap), r = ey / rowH;
            if (c >= cols || px - c * (tw + gap) > tw || ey - r * rowH > th) return -1;
            int i = r * cols + c;
            return i < names.Count ? i : -1;
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = HitTest(e); if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = HitTest(e);
            if (e.Button == MouseButtons.Left && i >= 0 && i != sel) { sel = i; tm.Start(); var c = Changed; if (c != null) c(); }
            base.OnMouseUp(e);
        }
        protected override void Dispose(bool d) { if (d) { tm.Dispose(); foreach (var b in bmps) b.Dispose(); } base.Dispose(d); }

        protected override void Draw(Graphics g, float a)
        {
            for (int i = 0; i < names.Count; i++)
            {
                int c = i % cols, r = i / cols, x = c * (tw + gap), y = r * rowH + M;
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = a };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(bmps[i], new Rectangle(x, y, tw, th), 0, 0, tw, th, GraphicsUnit.Pixel, ia);
                }
                float f = selA[i];
                if (f > 0.01f) using (var p = new Pen(W(a * f), 3)) g.DrawRectangle(p, x + 1, y + 1, tw - 3, th - 3);
                else if (i == hover) using (var p = new Pen(W(a * 0.55f), 2)) g.DrawRectangle(p, x + 1, y + 1, tw - 3, th - 3);
                RectangleF ink;
                using (var path = TextPath(titles[i], Regular, labelPx, 600, out ink))
                {
                    var st = g.Save();
                    g.TranslateTransform(x - ink.X, y + th + labelPx * 0.9f);
                    using (var br = new SolidBrush(W(a * (0.75f + 0.25f * f)))) g.FillPath(br, path);
                    g.Restore(st);
                }
            }
        }
    }

    // The logo: the Start-screen tile layout on the gray accent (the same artwork as the icon).
    static class Logo
    {
        static readonly double[][] Tiles =
        {
            new[] { 2, 2, 7, 7, 1.00 }, new[] { 10, 2, 4, 3, 0.72 }, new[] { 10, 6, 4, 3, 0.50 }, new[] { 2, 10, 12, 4, 0.86 }
        };
        public static void Draw(Graphics g, Rectangle r, float alpha)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            int a = (int)(255 * Math.Max(0, Math.Min(1, alpha)));
            var gray = Info.LogoGray;
            using (var br = new SolidBrush(Color.FromArgb(a, gray))) g.FillRectangle(br, r);
            double u = r.Width / 16.0;
            foreach (var t in Tiles)
            {
                int x0 = r.X + (int)Math.Round(t[0] * u), y0 = r.Y + (int)Math.Round(t[1] * u);
                int x1 = r.X + (int)Math.Round((t[0] + t[2]) * u), y1 = r.Y + (int)Math.Round((t[1] + t[3]) * u);
                var c = Color.FromArgb(a, (int)(gray.R + (255 - gray.R) * t[4]), (int)(gray.G + (255 - gray.G) * t[4]), (int)(gray.B + (255 - gray.B) * t[4]));
                using (var br = new SolidBrush(c)) g.FillRectangle(br, x0, y0, x1 - x0, y1 - y0);
            }
            g.SmoothingMode = old;
        }
    }

    // The "turning on" screen: black, the logo, and a ring of dots that chase round, like the Windows start-up screen.
    class Splash : Control
    {
        public float LogoAlpha, SpinAlpha;
        readonly float k; readonly Stopwatch clock = Stopwatch.StartNew();
        public Splash(float scale)
        {
            k = scale;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e) { PaintTo(e.Graphics, Width, Height, clock.Elapsed.TotalSeconds); }

        public void PaintTo(Graphics g, int w, int h, double t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int size = (int)(112 * k);
            var logo = new Rectangle((w - size) / 2, (int)(h * 0.40) - size / 2, size, size);
            if (LogoAlpha > 0.003f) Logo.Draw(g, logo, LogoAlpha);
            if (SpinAlpha <= 0.003f) return;
            // The Windows ring: five dots set off from the top one after another, speed up round the circle, then slow down
            // and bunch together at the top again before the next lap. Each dot fades in at the start and out at the end.
            float cx = w / 2f, cy = h * 0.83f, rad = 22 * k, dot = 3.2f * k;
            const double Cycle = 2.85, Travel = 2.0, Stagger = 0.19;   // the next lap starts as the last dots finish: no empty gap
            double phase = t % Cycle;
            for (int i = 0; i < 5; i++)
            {
                double local = phase - i * Stagger;
                if (local < 0 || local > Travel) continue;
                double u = local / Travel;
                double eased = u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;     // ease in-out: slow, fast, slow
                double ang = (-90 + 360 * eased) * Math.PI / 180;
                double fade = Math.Max(0, Math.Min(1, Math.Min(u / 0.07, (1 - u) / 0.07)));
                using (var br = new SolidBrush(Color.FromArgb((int)(255 * SpinAlpha * fade), 255, 255, 255)))
                    g.FillEllipse(br, (float)(cx + rad * Math.Cos(ang) - dot), (float)(cy + rad * Math.Sin(ang) - dot), dot * 2, dot * 2);
            }
        }
    }

    // One entry on the Modify screen, laid out like Windows' "Choose an option" screen: a light square tile with a white
    // icon, then the option's name, then a small line explaining it underneath.
    class RecoveryOption : Fx
    {
        readonly string glyph, title, desc; readonly Action click; readonly int tile; readonly float titlePx, descPx;
        float hover, hoverTo;
        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer { Interval = 15 };
        static FontFamily icons;
        static FontFamily Icons
        {
            get
            {
                if (icons != null) return icons;
                foreach (var n in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }) { try { icons = new FontFamily(n); return icons; } catch { } }
                icons = Regular; return icons;
            }
        }

        public RecoveryOption(string glyph, string title, string desc, int tile, float titlePx, float descPx, int x, int y, int w, Action click)
        {
            this.glyph = glyph; this.title = title; this.desc = desc; this.tile = tile; this.titlePx = titlePx; this.descPx = descPx; this.click = click;
            Pad = 0; Place(x, y, w, tile); Cursor = Cursors.Hand;
            tm.Tick += (s, e) =>
            {
                hover += (hoverTo - hover) * 0.3f;
                if (Math.Abs(hoverTo - hover) < 0.01f) { hover = hoverTo; tm.Stop(); }
                Invalidate();
            };
        }
        protected override void OnMouseEnter(EventArgs e) { hoverTo = 1; tm.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverTo = 0; tm.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) click(); base.OnMouseUp(e); }
        protected override void Dispose(bool d) { if (d) tm.Dispose(); base.Dispose(d); }

        protected override void Draw(Graphics g, float a)
        {
            // tile: the Windows light blue, brightening and getting a white frame under the mouse
            var t0 = Info.TileBlue; float lift = 0.16f * hover;
            var tc = Color.FromArgb((int)(255 * a), (int)(t0.R + (255 - t0.R) * lift), (int)(t0.G + (255 - t0.G) * lift), (int)(t0.B + (255 - t0.B) * lift));
            using (var br = new SolidBrush(tc)) g.FillRectangle(br, 0, 0, tile, tile);
            if (hover > 0.01f) using (var p = new Pen(W(a * hover), 2)) g.DrawRectangle(p, 1, 1, tile - 2, tile - 2);

            RectangleF b;
            using (var path = TextPath(glyph, Icons, tile * 0.46f, 400, out b))
            {
                var st = g.Save(); g.TranslateTransform((tile - b.Width) / 2f - b.X, (tile - b.Height) / 2f - b.Y);
                using (var br = new SolidBrush(W(a))) g.FillPath(br, path);
                g.Restore(st);
            }

            float tx = tile + tile * 0.24f, textW = ContentW - tx, top = tile * 0.02f;
            float titleBottom;
            using (var path = TextPath(title, Light, titlePx, textW, out b))
            {
                var st = g.Save(); g.TranslateTransform(tx - b.X, top - b.Y);
                using (var br = new SolidBrush(W(a))) g.FillPath(br, path);
                g.Restore(st);
                titleBottom = top + b.Height;
            }
            using (var path = TextPath(desc, Regular, descPx, textW, out b))
            {
                var st = g.Save(); g.TranslateTransform(tx - b.X, titleBottom + descPx * 0.5f - b.Y);
                using (var br = new SolidBrush(W(a * 0.92f))) g.FillPath(br, path);
                g.Restore(st);
            }
        }
    }

    // ------------------------------------------------------------------ the wizard

    class SetupForm : Form
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static void EnableDpi() { try { SetProcessDPIAware(); } catch { } }

        bool uninstallMode, modifyMode, repairMode, bootActive, skipBoot;
        readonly bool boot; readonly int startPage; Splash splash; string repairDir;
        double hue = 120;
        Color shown, target;
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 15 };
        readonly Panel host = new Panel { BackColor = Color.Transparent };
        readonly float k;                                   // dpi scale
        int step;
        bool busy, transitioning, instant;
        string dir = Info.DefaultDir;
        string pattern = "none"; bool hueTouched, patTouched;
        bool optStart = true, optDesktop = false, optRun = true, optLaunch = true, optSettings = false;

        // type sizes (pixels at 100% scale)
        float Title { get { return 40 * k; } }
        float Sub { get { return 16 * k; } }
        float Body { get { return 13 * k; } }
        int U(double v) { return (int)Math.Round(v * k); }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }   // WS_EX_COMPOSITED: no flicker while elements fade
        }

        public SetupForm(bool uninstall, bool boot = true)
        {
            uninstallMode = uninstall;
            this.boot = boot;
            // already installed? then opening setup again offers Modify (repair / uninstall) instead of a fresh install
            modifyMode = !uninstall && Worker.InstalledDir() != null;
            startPage = modifyMode ? 6 : 0;
            k = Math.Max(1f, DeviceDpi / 96f);
            Text = uninstall ? "Uninstall Start screen" : "Start screen";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            var b = Screen.PrimaryScreen.Bounds;
            Bounds = b;
            TopMost = true;
            DoubleBuffered = true;
            ForeColor = Color.White;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            shown = target = Base;
            BackColor = shown; Btn.Ink = Darker(shown);
            clock.Tick += (s, e) => Tick_();
            clock.Start();

            // content area, plus Fx.Pad of slack on the right so sliding elements near the edge aren't clipped
            host.Bounds = new Rectangle((int)(b.Width * 0.12), (int)(b.Height * 0.17), (int)(b.Width * 0.76) + Fx.MaxPad, (int)(b.Height * 0.66));
            Controls.Add(host);

            KeyDown += (s, e) =>
            {
                if (bootActive) { skipBoot = true; return; }       // any key skips the start-up screen
                if (e.KeyCode == Keys.Escape && !busy) Cancel();
            };

            if (boot)
            {
                // start black and see-through; the start-up sequence fades the screen to black, shows the logo, then boots into setup
                Opacity = 0;
                shown = target = Color.Black; BackColor = Color.Black; Btn.Ink = Color.Black;
                host.Visible = false;
                splash = new Splash(k) { Dock = DockStyle.Fill };
                Controls.Add(splash);
            }
            else Show_(startPage);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); if (boot) StartBoot(); }

        static double EaseOut(double x) { x = Math.Max(0, Math.Min(1, x)); return 1 - Math.Pow(1 - x, 3); }

        // 0.0s  the whole screen fades to black   1.0s  the logo fades in   1.5s  the dots start turning
        // 4.4s  logo and dots fade out as the black fades into the setup colour   4.75s  the first page appears
        void StartBoot()
        {
            bootActive = true;
            var sw = Stopwatch.StartNew();
            double offset = 0; bool began = false, shownHost = false;
            var tm = new System.Windows.Forms.Timer { Interval = 15 };
            tm.Tick += (s, e) =>
            {
                double t = sw.Elapsed.TotalMilliseconds + offset;
                if (skipBoot && t < 4300) { offset = 4300 - sw.Elapsed.TotalMilliseconds; t = 4300; skipBoot = false; }
                Opacity = EaseOut(t / 900);
                float logoA = (float)EaseOut((t - 1000) / 600), spinA = (float)EaseOut((t - 1500) / 500);
                if (t >= 4400)
                {
                    if (!began) { began = true; target = Base; }
                    float fade = (float)(1 - EaseOut((t - 4400) / 600));
                    logoA *= fade; spinA *= fade;
                }
                splash.LogoAlpha = logoA; splash.SpinAlpha = spinA; splash.Invalidate();
                if (t >= 4750 && !shownHost) { shownHost = true; host.Visible = true; Show_(startPage); }
                if (t >= 5300)
                {
                    tm.Stop(); tm.Dispose();
                    Controls.Remove(splash); splash.Dispose(); splash = null;
                    bootActive = false;
                }
            };
            tm.Start();
        }

        // the screen colour for the current mode: Windows blue on the Modify screens, otherwise the colour picked in setup
        Color Base { get { return modifyMode ? Info.ModifyBlue : Hsv.Background(hue); } }

        static Color Darker(Color c) { return Color.FromArgb(c.R * 7 / 10, c.G * 7 / 10, c.B * 7 / 10); }

        void Tick_()
        {
            // smooth background fade
            if (shown != target)
            {
                int dr = target.R - shown.R, dg = target.G - shown.G, db = target.B - shown.B;
                if (Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db) < 3) shown = target;
                else shown = Color.FromArgb(shown.R + (int)Math.Round(dr * 0.14), shown.G + (int)Math.Round(dg * 0.14), shown.B + (int)Math.Round(db * 0.14));
                BackColor = shown; Btn.Ink = Darker(shown);
            }
            foreach (Control c in host.Controls) { var f = c as Fx; if (f != null) f.Step(); }
        }

        void SetColour(Color c) { target = c; }

        void Cancel()
        {
            if (step >= 100 || (modifyMode && step == 6)) { Close(); return; }   // nothing to confirm when you leave Modify
            var r = MessageBox.Show(this, uninstallMode ? "Cancel and keep Start screen installed?" : "Exit setup? Nothing has been installed yet.", "Start screen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) Close();
        }

        // ---- page plumbing

        T Add<T>(T c) where T : Control { host.Controls.Add(c); return c; }
        int CW { get { return host.Width - Fx.MaxPad; } }            // usable content width
        Txt Head(string t) { return Add(new Txt(t, Fx.Light, Title, CW, 1f, 0, 0)); }
        Txt Lead(string t, int maxW) { return Add(new Txt(t, Fx.Light, Sub, maxW, 1f, 0, U(66))); }
        Btn Next(string text, Action a) { return Add(new Btn(text, Body, CW - U(96), host.Height - U(32), U(96), U(32), a)); }
        Btn Prev(string text, Action a) { return Add(new Btn(text, Body, CW - U(96) - U(16) - U(96), host.Height - U(32), U(96), U(32), a)); }

        // fade the current page out, then build and fade in the next one
        void Go(int n)
        {
            if (transitioning) return;
            if (instant) { Show_(n); return; }
            transitioning = true;
            foreach (Control c in host.Controls) { var f = c as Fx; if (f != null) f.AnimTo(0, -26, 170, 0); else c.Visible = false; }
            var t = new System.Windows.Forms.Timer { Interval = 200 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); transitioning = false; Show_(n); };
            t.Start();
        }

        void Show_(int n)
        {
            step = n;
            host.SuspendLayout();
            foreach (Control c in host.Controls) c.Dispose();
            host.Controls.Clear();
            int w = CW, h = host.Height;
            if (n == 0) PageColour(w, h);
            else if (n == 1) PageLicense(w, h);
            else if (n == 2) PageSettings(w, h);
            else if (n == 5) PageWallpaper(w, h);
            else if (n == 3) PageWorking(w, h);
            else if (n == 6) PageModify(w, h);
            else PageDone(w, h);
            host.ResumeLayout();
            if (instant) return;
            int i = 0, lastDelay = 0;
            foreach (Control c in host.Controls)
            {
                var f = c as Fx;
                if (f != null) { f.Set(0, 34); lastDelay = 40 + i * 55; f.AnimTo(1, 0, 420, lastDelay); i++; }
                else
                {
                    c.Visible = false; var cc = c;
                    var t = new System.Windows.Forms.Timer { Interval = 40 + i * 55 + 380 };
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!cc.IsDisposed) cc.Visible = true; };
                    t.Start();
                }
            }
        }

        // 1. pick a colour (where 8.1 setup starts)
        void PageColour(int w, int h)
        {
            if (uninstallMode)
            {
                Head("Remove Start screen");
                Lead("This closes Start screen and removes it from your PC, including its shortcuts and its sign-in entry.", Math.Min(w, U(620)));
                Add(new Tick("Also delete my saved layout and settings", optSettings, Body, U(20), 0, U(130), U(520), v => optSettings = v));
                Next("Uninstall", () => { Go(3); RunWork(); });
                Prev("Cancel", () => { if (modifyMode) { uninstallMode = false; Go(6); } else Close(); });   // from Modify, Cancel goes back to it
                return;
            }
            Head("Personalize");
            Lead("Pick a colour you like. You'll be able to change it any time in Settings.", Math.Min(w, U(700)));
            var sw = Add(new Swatches(28, 0, U(128), Math.Min(w, U(700)), U(34), U(12), hue));
            sw.Changed += () => { hue = sw.Hue; hueTouched = true; SetColour(Hsv.Background(hue)); };
            Add(new Txt("Start screen  ·  a project made by MT", Fx.Regular, Body * 0.92f, Math.Min(w, U(700)), 0.8f, 0, U(128) + U(34) + U(24) + U(30)));
            Next("Next", () => Go(5));
            Prev("Exit", Cancel);
        }

        // Modify: shown instead of the install pages when Start screen is already installed.
        void PageModify(int w, int h)
        {
            // laid out like the Windows "Choose an option" recovery screen: blue, a title, then icon tiles with a line each
            string ver = Worker.InstalledVersion();
            Head("Choose an option");
            Lead("Start screen" + (ver != null ? " " + ver : "") + " is already installed on this PC.", Math.Min(w, U(700)));
            int tile = U(66), row = U(94), y0 = U(134), colW = U(440), gapX = U(60);
            float tpx = Sub * 1.3f, dpx = Body * 0.82f;
            Add(new RecoveryOption("", "Repair", "Put Start screen's files and shortcuts back. Your layout and settings are kept.", tile, tpx, dpx, 0, y0, colW, () => { repairMode = true; Go(3); RunWork(); }));
            Add(new RecoveryOption("", "Uninstall", "Remove Start screen from this PC.", tile, tpx, dpx, 0, y0 + row, colW, () => { uninstallMode = true; Go(0); }));
            Add(new RecoveryOption("", "Exit", "Close setup without changing anything.", tile, tpx, dpx, colW + gapX, y0, colW, Close));
        }

        // 1b. start background (drawn in the colour just chosen)
        void PageWallpaper(int w, int h)
        {
            Head("Background");
            Lead("Choose a background for your Start screen. It uses the colour you picked.", Math.Min(w, U(700)));
            Color bg = Hsv.Background(hue), ink = hue < 0 ? Color.White : Hsv.From(hue, 1.0, 0.96);
            var th = Add(new Thumbs(0, U(112), Math.Min(w, U(780)), U(14), 5, Body * 0.92f, bg, ink, pattern, U(1)));
            th.Changed += () => { pattern = th.Selected; patTouched = true; };
            Next("Next", () => Go(1));
            Prev("Back", () => Go(0));
        }

        // 2. licence
        void PageLicense(int w, int h)
        {
            Head("License terms");
            Lead("Please read and accept these terms to continue.", Math.Min(w, U(700)));
            var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(Math.Min(255, shown.R + 22), Math.Min(255, shown.G + 22), Math.Min(255, shown.B + 22)),
                ForeColor = Color.White, Font = new Font("Segoe UI", 10f), Location = new Point(0, U(110)), Size = new Size(Math.Min(w, U(760)), h - U(110) - U(56)),
                Text = "START SCREEN  -  a project made by MT\r\n\r\n" +
                       "This is an original, independent recreation of the idea of a full-screen Start screen. It is not made by, endorsed by, or affiliated with Microsoft. " +
                       "\"Windows\" is a trademark of Microsoft Corporation; no Microsoft code or artwork is included.\r\n\r\n" +
                       "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
                       "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY ARISING FROM " +
                       "THE USE OF THE SOFTWARE.\r\n\r\n" +
                       "What it does on your PC:\r\n" +
                       " - Installs for your account only (no administrator rights needed).\r\n" +
                       " - Listens for the Windows key and for clicks on the taskbar Start button so it can open itself instead of the Windows Start menu. Both can be switched off from its tray icon.\r\n" +
                       " - Can uninstall apps you ask it to, using each app's own uninstaller.\r\n" +
                       " - Saves your tile layout and colours in your AppData folder. Nothing is sent over the internet.\r\n"
            };
            Add(box);
            Next("I accept", () => Go(2));
            Prev("Back", () => Go(5));
        }

        // 3. settings
        void PageSettings(int w, int h)
        {
            Head("Settings");
            Lead("Choose how Start screen is set up.", Math.Min(w, U(700)));
            int tw = Math.Min(w - U(120), U(560));
            Add(new Txt("Install to", Fx.Regular, Body, U(300), 0.85f, 0, U(112)));
            var tb = new TextBox { Text = dir, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f), AutoSize = false };
            tb.Bounds = new Rectangle(0, U(138), tw, U(28));
            tb.TextChanged += (s, e) => dir = tb.Text.Trim();
            Add(tb);
            Add(new Btn("Browse…", Body, tb.Right + U(12), tb.Top, U(92), tb.Height, () =>
            {
                using (var d = new FolderBrowserDialog { Description = "Choose where to install Start screen", SelectedPath = Directory.Exists(dir) ? dir : Info.DefaultDir })
                    if (d.ShowDialog(this) == DialogResult.OK) tb.Text = Path.Combine(d.SelectedPath, "StartScreen");
            }));
            int y = U(196), gap = U(40), bw = Math.Min(w, U(700));
            Add(new Tick("Add to the Start menu programs list", optStart, Body, U(20), 0, y, bw, v => optStart = v));
            Add(new Tick("Create a desktop shortcut", optDesktop, Body, U(20), 0, y + gap, bw, v => optDesktop = v));
            Add(new Tick("Start automatically when I sign in (needed for the Windows key and Start button)", optRun, Body, U(20), 0, y + gap * 2, bw, v => optRun = v));
            Add(new Tick("Open Start screen when setup finishes", optLaunch, Body, U(20), 0, y + gap * 3, bw, v => optLaunch = v));
            Next("Install", () =>
            {
                if (string.IsNullOrEmpty(dir) || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(dir))
                { MessageBox.Show(this, "Please choose a valid folder.", "Start screen", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Go(3); RunWork();
            });
            Prev("Back", () => Go(1));
        }

        // 4. working (the "Hi ... getting things ready" screen)
        Txt workMsg;
        void PageWorking(int w, int h)
        {
            int y = (int)(h * 0.32);
            workMsg = Add(new Txt("Hi", Fx.Light, Title * 1.15f, w, 1f, 0, y));
            Add(new Dots(0, y + U(110), Math.Min(w, U(640)), U(8)));
        }

        // 5. done
        void PageDone(int w, int h)
        {
            if (repairMode)
            {
                int yr = (int)(h * 0.30);
                Add(new Txt("All done", Fx.Light, Title * 1.3f, w, 1f, 0, yr));
                Add(new Txt("Start screen has been repaired.", Fx.Light, Sub * 1.15f, Math.Min(w, U(640)), 1f, 0, yr + U(84)));
                Next("Start", () =>
                {
                    try { Process.Start(new ProcessStartInfo(Path.Combine(repairDir ?? dir, Info.AppExe)) { UseShellExecute = true }); } catch { }
                    Close();
                });
                return;
            }
            if (uninstallMode)
            {
                int y0 = (int)(h * 0.30);
                Add(new Txt("All done", Fx.Light, Title * 1.3f, w, 1f, 0, y0));
                Add(new Txt("Start screen has been removed from your PC.", Fx.Light, Sub * 1.15f, Math.Min(w, U(640)), 1f, 0, y0 + U(84)));
                Next("Close", Close);
                return;
            }
            int y = (int)(h * 0.26);
            Add(new Txt("You're all set", Fx.Light, Title * 1.3f, w, 1f, 0, y));
            Add(new Txt("Press the Windows key or click the taskbar Start button to open your Start screen. " +
                        "You can turn either off from the tray icon, and change your colour in Settings.", Fx.Light, Sub * 1.15f, Math.Min(w, U(640)), 1f, 0, y + U(84)));
            Next(optLaunch ? "Start" : "Finish", () =>
            {
                if (optLaunch) { try { Process.Start(new ProcessStartInfo(Path.Combine(dir, Info.AppExe)) { UseShellExecute = true }); } catch { } }
                Close();
            });
        }

        void RunWork()
        {
            busy = true;
            string[] msgs = uninstallMode
                ? new[] { "Goodbye for now", "Removing Start screen…", "Tidying up…" }
                : repairMode
                    ? new[] { "Hi", "Repairing Start screen…", "Putting everything back…", "Almost there" }
                    : new[] { "Hi", "We're getting everything ready for you", "This might take a few moments", "Almost there" };
            if (repairMode) repairDir = Worker.InstalledDir();
            var started = Stopwatch.StartNew();
            Exception failure = null; bool finished = false;
            var worker = new Thread(() =>
            {
                try
                {
                    if (uninstallMode) Worker.Uninstall(optSettings);
                    else if (repairMode) Worker.Repair();
                    else { bool fresh = !File.Exists(Path.Combine(Info.UserData, "layout.json")); Worker.Install(dir, optDesktop, optStart, optRun, (fresh || hueTouched) ? (double?)hue : null, (fresh || patTouched) ? pattern : null); Worker.Preload(dir); }
                }
                catch (Exception ex) { failure = ex; }
                finished = true;
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();

            int shownIdx = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (s, e) =>
            {
                int idx = Math.Min(msgs.Length - 1, (int)(started.Elapsed.TotalSeconds / 2.6));
                if (idx != shownIdx && workMsg != null && !workMsg.IsDisposed && !transitioning)
                {
                    shownIdx = idx;
                    workMsg.SetText(msgs[idx]);
                    workMsg.Set(0, 30); workMsg.AnimTo(1, 0, 450, 0);
                }
                if (finished && started.Elapsed.TotalSeconds > 3.6 && !transitioning)
                {
                    timer.Stop(); busy = false;
                    if (failure != null)
                    {
                        MessageBox.Show(this, failure.Message, "Setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Go(uninstallMode ? 0 : repairMode ? 6 : 2);
                    }
                    else { step = 100; Go(4); }
                }
            };
            timer.Start();
        }

        // /snapshot <folder>: render each page off-screen to PNG (for checking the design; installs nothing).
        public void Snap(string folder)
        {
            instant = true;
            Directory.CreateDirectory(folder);
            StartPosition = FormStartPosition.Manual; TopMost = false; ShowInTaskbar = false;
            var real = Bounds; Location = new Point(-9000, 0);
            Show();
            Action<string> save = name =>
            {
                Application.DoEvents();
                foreach (Control c in host.Controls) { var f = c as Fx; if (f != null) f.Set(1, 0); }
                Tick_();
                using (var bmp = new Bitmap(real.Width, real.Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, real.Width, real.Height));
                    bmp.Save(Path.Combine(folder, name + ".png"));
                }
            };
            if (!uninstallMode)
            {
                Show_(0); save("1-colour");
                hue = 280; target = shown = Hsv.Background(hue); BackColor = shown; Btn.Ink = Darker(shown); Show_(0); save("1b-purple");
                Show_(1); save("2-license");
                Show_(5); save("2b-background");
                Show_(2); save("3-settings");
                Show_(3); save("4-working");
                step = 100; Show_(4); save("5-done");
            }
            else { Show_(0); save("u1-confirm"); step = 100; Show_(4); save("u2-done"); }

            // the Modify screen, the repair finish, and the start-up screen (logo + dots)
            modifyMode = true; uninstallMode = false; repairMode = false; step = 0; target = shown = Base; BackColor = shown; Btn.Ink = Darker(shown);
            Show_(6); save("m1-modify");
            repairMode = true; step = 100; Show_(4); save("m2-repaired"); repairMode = false;
            double[] times = { 0.3, 0.9, 1.5, 2.1, 2.7, 3.1 };     // several moments of the dot ring's lap
            for (int fi = 0; fi < times.Length; fi++)
                using (var bmp = new Bitmap(real.Width, real.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    var sp = new Splash(k) { LogoAlpha = 1, SpinAlpha = 1 };
                    sp.PaintTo(g, real.Width, real.Height, times[fi]);
                    bmp.Save(Path.Combine(folder, "b" + (fi + 1) + "-boot.png"));
                }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (busy) e.Cancel = true; base.OnFormClosing(e); }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool uninstall = false, quiet = false;
            foreach (var a in args)
            {
                if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (a.Equals("/quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
            }
            // Uninstall.exe lives inside the folder it can delete or repair, so run a temp copy of it instead.
            string self = Assembly.GetExecutingAssembly().Location;
            string temp = Path.Combine(Path.GetTempPath(), "StartScreenUninstall.exe");
            string installedAt = Worker.InstalledDir();
            bool insideInstall = installedAt != null && self.StartsWith(installedAt, StringComparison.OrdinalIgnoreCase);
            if ((uninstall || insideInstall) && !self.Equals(temp, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(self, temp, true);
                    Process.Start(new ProcessStartInfo(temp, string.Join(" ", args)) { UseShellExecute = false });
                    return;
                }
                catch { }
            }
            if (uninstall && quiet) { Worker.Uninstall(Array.Exists(args, a => a.Equals("/removesettings", StringComparison.OrdinalIgnoreCase))); return; }

            // /repair: the Modify screen's Repair without the UI (also used for testing).
            if (Array.Exists(args, a => a.Equals("/repair", StringComparison.OrdinalIgnoreCase))) { Worker.Repair(); return; }

            // /silent [/dir <folder>] [/hue <0-360>] [/desktop] [/norun]: install without the UI (also used for testing).
            if (Array.Exists(args, a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase)))
            {
                string d = Info.DefaultDir, pat = null; double? hu = null;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i].Equals("/dir", StringComparison.OrdinalIgnoreCase)) d = args[i + 1];
                    if (args[i].Equals("/pattern", StringComparison.OrdinalIgnoreCase)) pat = args[i + 1];
                    if (args[i].Equals("/hue", StringComparison.OrdinalIgnoreCase)) { double hv; if (double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out hv)) hu = hv; }
                }
                bool desk = Array.Exists(args, a => a.Equals("/desktop", StringComparison.OrdinalIgnoreCase));
                bool run = !Array.Exists(args, a => a.Equals("/norun", StringComparison.OrdinalIgnoreCase));
                Worker.Install(d, desk, true, run, hu, pat);
                if (Array.Exists(args, a => a.Equals("/preload", StringComparison.OrdinalIgnoreCase))) Worker.Preload(d);   // same step the full-screen setup runs
                return;
            }

            SetupForm.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int si = Array.FindIndex(args, a => a.Equals("/snapshot", StringComparison.OrdinalIgnoreCase));
            if (si >= 0 && si + 1 < args.Length)
            {
                var f = new SetupForm(uninstall, false);
                f.Snap(args[si + 1]);
                return;
            }
            // double-clicking setup plays the start-up screen first; /noboot skips it
            bool noboot = Array.Exists(args, a => a.Equals("/noboot", StringComparison.OrdinalIgnoreCase));
            Application.Run(new SetupForm(uninstall, !noboot));
        }
    }
}
