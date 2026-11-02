using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

class WItem
{
    public Rectangle R;
    public string Key, Text, Glyph, Tip;
    public bool Accent;
}

// A per-pixel layered window: smooth corners and a soft shadow. No child controls can take
// focus, and it only redraws when something changes, so it costs nothing while idle.
class Widget : Form
{
    public static readonly string[] Styles = { "Bar", "Vertical", "Icons", "Compact", "Edge", "Hidden" };
    public static readonly Dictionary<string, string> StyleNames = new Dictionary<string, string> {
        { "Bar", "Horizontal bar" }, { "Vertical", "Vertical bar" }, { "Icons", "Mini icons" },
        { "Compact", "Compact bubble" }, { "Edge", "Edge tab" }, { "Hidden", "Hotkeys only" } };
    public static readonly Dictionary<string, string> StyleTips = new Dictionary<string, string> {
        { "Bar", "Labelled buttons in a row." }, { "Vertical", "Labelled buttons in a column. Good at a screen edge." },
        { "Icons", "Smallest bar with letters only." }, { "Compact", "One bubble that opens when you hover over it." },
        { "Edge", "A slim tab on the screen edge that slides open on hover." }, { "Hidden", "Nothing on screen until a hotkey is working." } };

    public const int WM_OPEN = 0x8001; // sent by a second launch of the app

    readonly App app;
    readonly ToolTip tip = new ToolTip { InitialDelay = 500 };
    readonly Timer pulse = new Timer { Interval = 66 };
    List<WItem> items = new List<WItem>();
    WItem hover, down;
    bool active, expanded;
    int sweep;
    Point dragFrom;
    bool dragging;
    public string Status { get; private set; }
    public bool Busy;
    public bool MenuOpen;

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x80000; // NOACTIVATE, TOOLWINDOW, TOPMOST, LAYERED
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x21, WM_HOTKEY = 0x312, MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        if (m.Msg == WM_HOTKEY) app.OnHotkey(m.WParam.ToInt32());
        if (m.Msg == WM_OPEN) app.OpenMain("Studio");
        base.WndProc(ref m);
    }

    public Widget(App a)
    {
        app = a;
        Text = "PromptPilot Bar";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        pulse.Tick += (o, e) => { sweep = (sweep + 4) % 100; Render(); };
        var handle = Handle; // hotkeys need the window handle before the first Show
    }

    Settings S { get { return app.S; } }
    bool Collapsible { get { return S.Style == "Compact" || S.Style == "Edge"; } }
    public bool IsActive { get { return active; } }

    public void SetActive(bool on)
    {
        active = on;
        Refresh2();
    }

    // Rebuilds the layout. Call when style, status or menu state changes.
    public void Refresh2()
    {
        Size size;
        items = BuildLayout(S, S.Style, expanded || Status != null, Status, app.CanUndo, app.ProjectChip, out size);
        bool show = S.Style == "Hidden" ? Status != null : active;
        if (Size != size) Size = size;
        Render();
        if (show) Place();
        if (Visible != show) Visible = show;
    }

    void Render()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        using (var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Draw(g, S, ClientSize, items, hover, sweep, Busy);
            }
            var screen = GetDC(IntPtr.Zero);
            var mem = CreateCompatibleDC(screen);
            var hbm = bmp.GetHbitmap(Color.FromArgb(0));
            var old = SelectObject(mem, hbm);
            var size = new SIZE { cx = Width, cy = Height };
            var src = new POINT();
            var dst = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = (byte)(Math.Max(30, Math.Min(100, S.Opacity)) * 255 / 100), AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            SelectObject(mem, old);
            DeleteObject(hbm);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, rad * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Font WidgetFont(Settings s) { return Th.F(s.Size == 0 ? 8.5f : s.Size == 2 ? 11f : 9.5f); }

    // The edge tab sits flush against the screen, so it has no room for a shadow.
    static int Shadow(string style) { return style == "Edge" ? 0 : Th.S(9); }

    public static List<WItem> BuildLayout(Settings s, string style, bool open, string status, bool canUndo, string project, out Size size)
    {
        var list = new List<WItem>();
        var font = WidgetFont(s);
        int unit = TextRenderer.MeasureText("Hg", font).Height + Th.S(12);
        int pad = Th.S(3);
        Func<string, int> w = t => TextRenderer.MeasureText(t, font).Width + Th.S(16);

        if (status != null)
        {
            int sw = w(status) + Th.S(26);
            list.Add(new WItem { Key = "status", Text = status, Tip = "Click to cancel", R = new Rectangle(pad, pad, sw, unit) });
            size = new Size(sw + pad * 2, unit + pad * 2);
        }
        else if (style == "Compact" && !open)
        {
            int d = unit + Th.S(6);
            list.Add(new WItem { Key = "bubble", Glyph = "", Accent = true, Tip = "PromptPilot", R = new Rectangle(0, 0, d, d) });
            size = new Size(d, d);
        }
        else if (style == "Edge" && !open)
        {
            list.Add(new WItem { Key = "tab", Accent = true, Tip = "PromptPilot", R = new Rectangle(0, 0, Th.S(10), Th.S(84)) });
            size = new Size(Th.S(10), Th.S(84));
        }
        else
        {
            var modes = s.Modes.Where(m => m.Pinned).ToList();
            bool vertical = style == "Vertical" || style == "Edge";
            bool letters = style == "Icons";
            var row = new List<WItem>();
            row.Add(new WItem { Key = "grip", Glyph = "", Tip = "Drag to move" });
            foreach (var m in modes)
            {
                var hk = string.IsNullOrEmpty(m.Hotkey) ? "" : " (" + m.Hotkey + ")";
                row.Add(new WItem { Key = "mode:" + m.Id, Text = letters ? m.Name.Substring(0, 1) : m.Name, Accent = m.Id == "engineer", Tip = m.Name + hk + ". Shift+click to preview first." });
            }
            if (canUndo) row.Add(new WItem { Key = "undo", Glyph = "", Text = letters ? null : "Undo", Tip = "Put your original text back" });
            if (!string.IsNullOrEmpty(project) && !letters) row.Add(new WItem { Key = "more", Glyph = "", Text = Trim(project, 14), Tip = "Active project. Click for options." });
            row.Add(new WItem { Key = "more", Glyph = "", Tip = "More modes, context and settings" });

            Func<WItem, int> itemW = it => letters || it.Text == null ? unit : (it.Glyph != null ? Th.S(20) : 0) + w(it.Text);
            if (vertical)
            {
                int width = row.Max(itemW);
                int y = pad;
                foreach (var it in row)
                {
                    int h = it.Key == "grip" ? Th.S(18) : unit;
                    it.R = new Rectangle(pad, y, width, h);
                    y += h + 1;
                    list.Add(it);
                }
                size = new Size(width + pad * 2, y + pad - 1);
            }
            else
            {
                int x = pad;
                foreach (var it in row)
                {
                    int width = it.Key == "grip" ? Th.S(20) : itemW(it);
                    it.R = new Rectangle(x, pad, width, unit);
                    x += width + 1;
                    list.Add(it);
                }
                size = new Size(x + pad - 1, unit + pad * 2);
            }
        }

        int m2 = Shadow(style);
        foreach (var it in list) it.R.Offset(m2, m2);
        size = new Size(size.Width + m2 * 2, size.Height + m2 * 2);
        return list;
    }

    static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

    static readonly StringFormat Center = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None };

    static void Text2(Graphics g, string text, Font font, Rectangle r, Color c)
    {
        using (var b = new SolidBrush(c)) g.DrawString(text, font, b, r, Center);
    }

    public static void Draw(Graphics g, Settings s, Size size, List<WItem> items, WItem hover, int sweep, bool busy)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var font = WidgetFont(s);
        var iconFont = Th.Icon(s.Size == 2 ? 12f : 10f);
        int m = Shadow(s.Style);
        // Sample thumbnails draw other styles, so take the margin from the items when present.
        if (items.Count > 0 && items[0].Key != "tab") m = items[0].Key == "bubble" ? items[0].R.X : Math.Max(0, items[0].R.X - Th.S(3));
        else m = 0;
        var body = new Rectangle(m, m, size.Width - m * 2, size.Height - m * 2);
        bool solid = items.Count > 0 && (items[0].Key == "tab" || items[0].Key == "bubble");
        int rad = body.Width == body.Height ? body.Width / 2 : Math.Min(Th.S(11), Math.Min(body.Width, body.Height) / 2);

        for (int i = m; i > 0; i--)
        {
            var ring = body;
            ring.Inflate(i, i);
            ring.Offset(0, Th.S(1));
            using (var p = Rounded(ring, rad + i))
            using (var b = new SolidBrush(Color.FromArgb(Math.Max(2, 26 - i * 26 / m), 0, 0, 0))) g.FillPath(b, p);
        }

        using (var p = Rounded(new Rectangle(body.X, body.Y, body.Width - 1, body.Height - 1), rad))
        using (var b = new SolidBrush(solid ? Th.Accent : Th.Card))
        using (var edge = new Pen(Color.FromArgb(Th.Dark ? 40 : 60, Th.Dark ? 255 : 0, Th.Dark ? 255 : 0, Th.Dark ? 255 : 0)))
        {
            g.FillPath(b, p);
            if (!solid) g.DrawPath(edge, p);
        }

        foreach (var it in items)
        {
            if (it.Key == "tab") continue;
            if (it.Key == "bubble") { Text2(g, it.Glyph, Th.Icon(13f), it.R, Th.OnAccent); continue; }
            if (it == hover && it.Key != "status" && it.Key != "grip")
                using (var p = Rounded(it.R, Th.S(7)))
                using (var b = new SolidBrush(Th.Hover)) g.FillPath(b, p);
            var color = it.Key == "grip" ? Th.Dim : it.Accent ? Th.Accent : Th.Fg;
            if (it.Glyph != null && it.Text != null)
            {
                var gr = new Rectangle(it.R.X + Th.S(6), it.R.Y, Th.S(16), it.R.Height);
                Text2(g, it.Glyph, iconFont, gr, Th.Dim);
                Text2(g, it.Text, font, new Rectangle(gr.Right, it.R.Y, it.R.Right - gr.Right, it.R.Height), color);
            }
            else if (it.Glyph != null) Text2(g, it.Glyph, iconFont, it.R, Th.Dim);
            else Text2(g, it.Text, font, it.R, color);
        }

        if (busy)
        {
            int w = body.Width / 3;
            int x = body.X + (int)((body.Width + w) * sweep / 100.0) - w;
            using (var clip = Rounded(body, rad))
            {
                g.SetClip(clip);
                using (var b = new SolidBrush(Th.Accent)) g.FillRectangle(b, x, body.Bottom - Th.S(3), w, Th.S(3));
                g.ResetClip();
            }
        }
    }

    public void ShowStatus(string text, bool animate)
    {
        Status = text;
        Busy = animate;
        pulse.Enabled = animate; // the only timer in the app, and it runs only during a rewrite
        Refresh2();
    }

    int flashGen;
    public async void Flash(string text)
    {
        int gen = ++flashGen;
        ShowStatus(text, false);
        await Task.Delay(Math.Max(1400, Math.Min(5000, text.Length * 70)));
        if (gen == flashGen && !Busy) ShowStatus(null, false);
    }

    public void ClearFlash() { flashGen++; }

    WItem Hit(Point p) { return items.FirstOrDefault(i => i.R.Contains(p)); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            var np = new Point(Left + e.X - dragFrom.X, Top + e.Y - dragFrom.Y);
            if (S.Style == "Edge") np.X = Left;
            Location = np;
            return;
        }
        var h = Hit(e.Location);
        if (h != hover)
        {
            hover = h;
            tip.SetToolTip(this, h == null ? null : h.Tip);
            Render();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (Collapsible && !expanded) { expanded = true; Refresh2(); }
        base.OnMouseEnter(e);
    }

    protected override async void OnMouseLeave(EventArgs e)
    {
        hover = null;
        Render();
        base.OnMouseLeave(e);
        if (!Collapsible) return;
        await Task.Delay(450);
        CollapseIfOutside();
    }

    public void CollapseIfOutside()
    {
        if (expanded && !MenuOpen && !Bounds.Contains(Cursor.Position)) { expanded = false; Refresh2(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        down = Hit(e.Location);
        if (e.Button == MouseButtons.Left && (down == null || down.Key == "grip" || down.Key == "bubble"))
        {
            dragging = true;
            dragFrom = e.Location;
            startPos = Location;
        }
    }

    Point startPos;

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (dragging)
        {
            dragging = false;
            if (Location != startPos) { RememberPosition(); return; }
        }
        var up = Hit(e.Location);
        if (e.Button == MouseButtons.Right) { app.ShowMenu(this, e.Location); return; }
        if (up == null || up != down) return;
        app.WidgetClick(up.Key, this, new Point(up.R.Left, up.R.Bottom), (ModifierKeys & Keys.Shift) != 0);
    }

    void RememberPosition()
    {
        if (S.Placement == "FollowWindow" && S.Style != "Edge")
        {
            Native.RECT r;
            if (Native.GetWindowRect(app.Foreground, out r))
            {
                S.FollowDx = Left + Width / 2 - (r.L + r.R) / 2;
                S.FollowDy = Top - r.B;
            }
        }
        else S.Positions[S.Style] = new[] { Left, Top };
        S.Save();
    }

    // Works out where the widget belongs and keeps it on screen.
    public void Place()
    {
        if (dragging) return;
        var target = app.Foreground;
        var screen = target != IntPtr.Zero ? Screen.FromHandle(target) : Screen.PrimaryScreen;
        var area = screen.WorkingArea;
        Point p;
        int[] saved;
        bool hasSaved = S.Positions.TryGetValue(S.Style, out saved) && saved.Length == 2;
        if (S.Style == "Edge")
        {
            int y = hasSaved ? saved[1] : area.Top + area.Height / 2 - Height / 2;
            p = new Point(area.Right - Width, y);
        }
        else if (S.Placement == "FollowWindow" && target != IntPtr.Zero)
        {
            Native.RECT r;
            Native.GetWindowRect(target, out r);
            p = new Point((r.L + r.R) / 2 - Width / 2 + S.FollowDx, r.B + S.FollowDy);
        }
        else if (hasSaved && Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(new Point(saved[0], saved[1]))))
        {
            p = new Point(saved[0], saved[1]);
            area = Screen.FromPoint(p).WorkingArea;
        }
        else if (S.Style == "Vertical") p = new Point(area.Right - Width - Th.S(12), area.Top + area.Height / 2 - Height / 2);
        else p = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - Th.S(90));

        p.X = Math.Max(area.Left, Math.Min(p.X, area.Right - Width));
        p.Y = Math.Max(area.Top, Math.Min(p.Y, area.Bottom - Height));
        if (Location != p) Location = p;
    }

    // Thumbnail for the Appearance page. Same layout and paint code as the real widget.
    public static Bitmap Sample(Settings s, string style)
    {
        if (style == "Hidden")
        {
            var b0 = new Bitmap(Th.S(170), Th.S(40));
            using (var g = Graphics.FromImage(b0))
            {
                g.Clear(Color.Transparent);
                TextRenderer.DrawText(g, "Ctrl + Alt + 1 / 2 / 3", Th.F(9.5f), new Rectangle(0, 0, b0.Width, b0.Height), Th.Dim, Th.Side, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            return b0;
        }
        Size size;
        var items = BuildLayout(s, style, false, null, false, null, out size);
        var b = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent);
            Draw(g, s, size, items, null, 0, false);
        }
        return b;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
}
