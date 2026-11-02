using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Theme colours, fonts and the app icon.
static class Th
{
    public static Color Bg, Side, Card, Field, Border, Fg, Dim, Accent, OnAccent, Hover, Good, Bad;
    public static bool Dark = true;
    static float dpi = 1f;
    static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

    public static void Apply(Settings s)
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;
        Dark = s.Theme == "Dark" || (s.Theme == "System" && SystemIsDark());
        Accent = ParseColor(s.Accent);
        // Picks whichever text colour stays readable on the chosen accent.
        double lum = (0.2126 * Linear(Accent.R) + 0.7152 * Linear(Accent.G) + 0.0722 * Linear(Accent.B));
        OnAccent = lum > 0.18 ? Hex("#07111F") : Color.White;
        if (Dark)
        {
            Bg = Hex("#111317"); Side = Hex("#0B0D10"); Card = Hex("#1A1D23"); Field = Hex("#22262E");
            Border = Hex("#2F343D"); Fg = Hex("#EDEFF3"); Dim = Hex("#9DA4B1"); Hover = Hex("#2B303A");
            Good = Hex("#4CC38A"); Bad = Hex("#F2777A");
        }
        else
        {
            Bg = Hex("#F3F4F7"); Side = Hex("#E9EBEF"); Card = Hex("#FFFFFF"); Field = Hex("#F6F7F9");
            Border = Hex("#D5D8DE"); Fg = Hex("#14161B"); Dim = Hex("#596170"); Hover = Hex("#E4E7EC");
            Good = Hex("#13703F"); Bad = Hex("#BC2F33");
        }
    }

    static double Linear(int c) { double v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }

    static bool SystemIsDark()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch { return true; }
    }

    static Color Hex(string h) { return ColorTranslator.FromHtml(h); }
    static Color ParseColor(string h) { try { return Hex(h); } catch { return Hex("#4C9DFF"); } }

    public static int S(int n) { return (int)Math.Round(n * dpi); }

    static Font Get(string family, float size, FontStyle style)
    {
        var key = family + size + style;
        Font f;
        if (!fonts.TryGetValue(key, out f)) { f = new Font(family, size, style); fonts[key] = f; }
        return f;
    }

    public static Font F(float size, FontStyle style = FontStyle.Regular)
    {
        return style == FontStyle.Bold ? Get("Segoe UI Semibold", size, FontStyle.Regular) : Get("Segoe UI", size, style);
    }

    public static Font Icon(float size) { return Get("Segoe MDL2 Assets", size, FontStyle.Regular); }

    public static Icon AppIcon(string file, int size)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(file))
            return new Icon(stream, size, size);
    }
}

class ThemedForm : Form
{
    public ThemedForm()
    {
        BackColor = Th.Bg;
        ForeColor = Th.Fg;
        Font = Th.F(9.5f);
        Icon = Th.AppIcon("app.ico", 32);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = Th.Dark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, 20, ref on, 4);
        Native.DwmSetWindowAttribute(Handle, 19, ref on, 4);
    }
}

// A rounded card that holds a group of settings. Sizes its height to its content.
class Section : FlowLayoutPanel
{
    public Section()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Th.S(18), Th.S(14), Th.S(18), Th.S(16));
        Margin = new Padding(0, 0, 0, Th.S(14));
        BackColor = Th.Card;
        Tag = "section";
        DoubleBuffered = true;
    }

    public void SetWidth(int w)
    {
        MinimumSize = new Size(w, 0);
        MaximumSize = new Size(w, 0);
        foreach (Control c in Controls) Ui.Fit(c, w - Padding.Horizontal - c.Margin.Horizontal);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (MaximumSize.Width > 0) Ui.Fit(e.Control, MaximumSize.Width - Padding.Horizontal - e.Control.Margin.Horizontal);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.Back(Parent));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var p = Widget.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Th.S(10)))
        using (var b = new SolidBrush(Th.Card))
        using (var pen = new Pen(Th.Border))
        {
            e.Graphics.FillPath(b, p);
            e.Graphics.DrawPath(pen, p);
        }
    }
}

// Scrolling page column. Content stops growing at a readable width.
class Stack : FlowLayoutPanel
{
    Section current;

    public Stack()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(Th.S(24), Th.S(4), Th.S(16), Th.S(16));
        BackColor = Th.Bg;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.DarkScroll(this); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }
    protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); FitOne(e.Control); }

    int ContentWidth() { return Math.Min(ClientSize.Width - Padding.Horizontal - Th.S(4), Th.S(1040)); }

    void Reflow() { SuspendLayout(); foreach (Control c in Controls) FitOne(c); ResumeLayout(); }

    void FitOne(Control c)
    {
        int w = ContentWidth() - c.Margin.Horizontal;
        if (w >= Th.S(50)) Ui.Fit(c, w);
    }

    // Starts a new card. What's added next goes inside it.
    public Section Card()
    {
        current = new Section();
        current.SuspendLayout(); // laid out once at the end, which keeps long pages fast to open
        Controls.Add(current);
        return current;
    }

    public T Add<T>(T c) where T : Control
    {
        if (current != null) current.Controls.Add(c); else Controls.Add(c);
        return c;
    }

    public Label Title(string text)
    {
        Card();
        var l = Ui.Lbl(text, Th.F(11f, FontStyle.Bold), Th.Fg);
        l.Margin = new Padding(0, 0, 0, Th.S(6));
        return Add(l);
    }

    public Label Hint(string text) { var l = Ui.Lbl(text, Th.F(9f), Th.Dim); l.Margin = new Padding(0, Th.S(1), 0, Th.S(8)); return Add(l); }
    public Label Field(string text) { var l = Ui.Lbl(text, Th.F(9f, FontStyle.Bold), Th.Dim); l.Margin = new Padding(0, Th.S(10), 0, Th.S(4)); return Add(l); }
}

// A switch that paints itself, used instead of the dated checkbox.
class Toggle : Control
{
    bool on;
    public event Action Changed;
    public bool Checked { get { return on; } set { on = value; Invalidate(); } }

    public Toggle(string text, bool value)
    {
        Text = text;
        on = value;
        Tag = "natural";
        Font = Th.F(9.5f);
        ForeColor = Th.Fg;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        var size = TextRenderer.MeasureText(text, Font);
        Size = new Size(size.Width + Th.S(52), Th.S(28));
        Margin = new Padding(0, Th.S(2), 0, Th.S(2));
        Click += (o, e) => Flip();
        KeyDown += (o, e) => { if (e.KeyCode == Keys.Space) { e.Handled = true; Flip(); } };
        GotFocus += (o, e) => Invalidate();
        LostFocus += (o, e) => Invalidate();
    }

    void Flip() { on = !on; Invalidate(); if (Changed != null) Changed(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Back(Parent));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int h = Th.S(18), w = Th.S(34), y = (Height - h) / 2;
        using (var p = Widget.Rounded(new Rectangle(0, y, w, h), h / 2))
        using (var b = new SolidBrush(on ? Th.Accent : Th.Border)) g.FillPath(b, p);
        int d = h - Th.S(6);
        using (var knob = new SolidBrush(on ? Th.OnAccent : Th.Dim))
            g.FillEllipse(knob, on ? w - d - Th.S(3) : Th.S(3), y + Th.S(3), d, d);
        if (Focused)
            using (var pen = new Pen(Th.Accent))
            using (var p = Widget.Rounded(new Rectangle(0, y - Th.S(2), w, h + Th.S(4)), h / 2 + Th.S(2))) g.DrawPath(pen, p);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(w + Th.S(8), 0, Width - w - Th.S(8), Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }
}

class PillButton : Button
{
    public bool Primary;
    bool hover, down;

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(Math.Max(MinimumSize.Width, t.Width + Th.S(24)), Math.Max(MinimumSize.Height, t.Height + Th.S(12)));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Back(Parent));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color fill, text, edge;
        if (!Enabled) { fill = Th.Field; text = Th.Border; edge = Th.Border; }
        else if (Primary)
        {
            fill = down ? ControlPaint.Dark(Th.Accent, 0.05f) : hover ? ControlPaint.Light(Th.Accent, 0.2f) : Th.Accent;
            text = Th.OnAccent;
            edge = fill;
        }
        else { fill = down ? Th.Border : hover ? Th.Hover : Th.Field; text = Th.Fg; edge = Th.Border; }
        using (var p = Widget.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Th.S(6)))
        using (var b = new SolidBrush(fill))
        using (var pen = new Pen(Focused && ShowFocusCues ? Th.Accent : edge))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

// Text box with a themed border that lights up on focus, inner margins and a scrollbar only when needed.
class FlatBox : TextBox
{
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int m = BorderStyle == BorderStyle.None ? 0 : Th.S(7);
        SendMessage(Handle, 0xD3, (IntPtr)3, (IntPtr)((m << 16) | m)); // EM_SETMARGINS
        Ui.DarkScroll(this);
        UpdateBar();
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); UpdateBar(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateBar(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); DrawBorder(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); DrawBorder(); }

    void UpdateBar()
    {
        if (!Multiline || !IsHandleCreated) return;
        int lines = (int)SendMessage(Handle, 0xBA, IntPtr.Zero, IntPtr.Zero); // EM_GETLINECOUNT
        ShowScrollBar(Handle, 1, lines * Font.Height > ClientSize.Height);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x85 || m.Msg == 0xF) DrawBorder(); // WM_NCPAINT, WM_PAINT
    }

    void DrawBorder()
    {
        if (BorderStyle == BorderStyle.None || !IsHandleCreated) return;
        var dc = GetWindowDC(Handle);
        using (var g = Graphics.FromHdc(dc))
        using (var pen = new Pen(Focused ? Th.Accent : Th.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        ReleaseDC(Handle, dc);
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool ShowScrollBar(IntPtr h, int bar, bool show);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
}

class OwnerList : ListBox
{
    public Func<object, string> Sub;
    public string Empty = "";

    public OwnerList(bool twoLines)
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = twoLines ? Th.S(52) : Th.S(34);
        BorderStyle = BorderStyle.None;
        BackColor = Th.Card;
        ForeColor = Th.Fg;
        Font = Th.F(9.5f);
        IntegralHeight = false;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.DarkScroll(this); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0xF && Items.Count == 0 && Empty != "")
            using (var g = CreateGraphics())
                TextRenderer.DrawText(g, Empty, Th.F(9f), new Rectangle(Th.S(12), Th.S(12), Width - Th.S(24), Height - Th.S(24)), Th.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);
        if (sel)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(e.Bounds.X + Th.S(4), e.Bounds.Y + Th.S(2), e.Bounds.Width - Th.S(8), e.Bounds.Height - Th.S(4));
            using (var p = Widget.Rounded(r, Th.S(6)))
            using (var b = new SolidBrush(Th.Hover)) g.FillPath(b, p);
            using (var b = new SolidBrush(Th.Accent)) g.FillRectangle(b, r.X + Th.S(1), r.Y + Th.S(7), Th.S(3), r.Height - Th.S(14));
        }
        var item = Items[e.Index];
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int x = e.Bounds.X + Th.S(14), w = e.Bounds.Width - Th.S(22);
        if (Sub == null) TextRenderer.DrawText(g, item.ToString(), Font, new Rectangle(x, e.Bounds.Y, w, e.Bounds.Height), Th.Fg, flags);
        else
        {
            int half = e.Bounds.Height / 2;
            TextRenderer.DrawText(g, item.ToString(), Font, new Rectangle(x, e.Bounds.Y + Th.S(4), w, half), Th.Fg, flags);
            TextRenderer.DrawText(g, Sub(item), Th.F(8.5f), new Rectangle(x, e.Bounds.Y + half - Th.S(1), w, half), Th.Dim, flags);
        }
    }
}

// The stock dropdown ignores dark colours in its list style, so this paints the closed state itself.
class DarkCombo : ComboBox
{
    public DarkCombo(bool editable)
    {
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Th.Field;
        ForeColor = Th.Fg;
        Font = Th.F(9.5f);
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Th.S(24);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.DarkScroll(this); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var b = new SolidBrush(hot ? Th.Hover : Th.Field)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index >= 0)
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + Th.S(4), e.Bounds.Y, e.Bounds.Width - Th.S(4), e.Bounds.Height), Th.Fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0xF) return; // WM_PAINT
        using (var g = Graphics.FromHwnd(Handle))
        {
            int w = SystemInformation.VerticalScrollBarWidth + Th.S(2);
            var arrow = new Rectangle(Width - w, 0, w, Height);
            using (var b = new SolidBrush(Th.Field)) g.FillRectangle(b, arrow);
            TextRenderer.DrawText(g, "", Th.Icon(8f), arrow, Th.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Focused ? Th.Accent : Th.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}

// Thin bar that shows the prompt score.
class Meter : Control
{
    int value;
    Color color = Th.Accent;

    public Meter()
    {
        DoubleBuffered = true;
        Height = Th.S(5);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void Set(int v, Color c) { value = v; color = c; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.Back(Parent));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var p = Widget.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
        using (var b = new SolidBrush(Th.Border)) e.Graphics.FillPath(b, p);
        if (value <= 0) return;
        using (var p = Widget.Rounded(new Rectangle(0, 0, Math.Max(Height, (Width - 1) * value / 100), Height - 1), Height / 2))
        using (var b = new SolidBrush(color)) e.Graphics.FillPath(b, p);
    }
}

// A filled rounded panel, for Studio's two editors.
class Pane : Panel
{
    public Pane()
    {
        DoubleBuffered = true;
        BackColor = Th.Card;
        Padding = new Padding(Th.S(14), Th.S(10), Th.S(14), Th.S(12));
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.Back(Parent));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var p = Widget.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Th.S(10)))
        using (var b = new SolidBrush(Th.Card))
        using (var pen = new Pen(Th.Border))
        {
            e.Graphics.FillPath(b, p);
            e.Graphics.DrawPath(pen, p);
        }
    }
}

class MenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected { get { return Th.Hover; } }
    public override Color MenuItemBorder { get { return Th.Hover; } }
    public override Color MenuBorder { get { return Th.Border; } }
    public override Color ToolStripDropDownBackground { get { return Th.Card; } }
    public override Color ImageMarginGradientBegin { get { return Th.Card; } }
    public override Color ImageMarginGradientMiddle { get { return Th.Card; } }
    public override Color ImageMarginGradientEnd { get { return Th.Card; } }
    public override Color SeparatorDark { get { return Th.Border; } }
    public override Color SeparatorLight { get { return Th.Card; } }
    public override Color CheckBackground { get { return Th.Hover; } }
    public override Color CheckSelectedBackground { get { return Th.Hover; } }
    public override Color CheckPressedBackground { get { return Th.Hover; } }
}

class MenuRenderer : ToolStripProfessionalRenderer
{
    public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Th.Dim; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, "", Th.Icon(9f), e.ImageRectangle, Th.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

static class Ui
{
    public static void ResumeCards(Control c)
    {
        foreach (Control child in c.Controls) ResumeCards(child);
        if (c is Section) c.ResumeLayout(true);
    }

    public static void DarkScroll(Control c)
    {
        if (Th.Dark) Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
    }

    // The nearest real background behind a control, for painting smooth corners.
    public static Color Back(Control c)
    {
        for (; c != null; c = c.Parent)
            if (c.BackColor.A == 255) return c.BackColor;
        return Th.Bg;
    }

    // Stretches a child to the column width, in the way that suits its type.
    public static void Fit(Control c, int w)
    {
        if (w <= 0) return;
        var s = c as Section;
        if (s != null) s.SetWidth(w);
        else if (c is Label) c.MaximumSize = new Size(w, 0);
        else if ("wrap".Equals(c.Tag)) c.MaximumSize = new Size(w, 0);
        else if (!"natural".Equals(c.Tag)) c.Width = w;
    }

    public static Label Lbl(string text, Font font = null, Color? color = null)
    {
        return new Label { Text = text, AutoSize = true, Font = font ?? Th.F(9.5f), ForeColor = color ?? Th.Fg, UseMnemonic = false, BackColor = Color.Transparent };
    }

    public static PillButton Btn(string text, Action click, bool primary = false)
    {
        var b = new PillButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, UseMnemonic = false, Tag = "natural", Primary = primary,
            MinimumSize = new Size(Th.S(68), Th.S(32)), Margin = new Padding(0, Th.S(3), Th.S(6), Th.S(3)), Font = Th.F(9.5f), Cursor = Cursors.Hand, BackColor = Color.Transparent };
        if (click != null) b.Click += (o, e) => click();
        return b;
    }

    public static TextBox Box(string text = "", int lines = 1, bool password = false)
    {
        var t = new FlatBox { Text = text ?? "", BorderStyle = BorderStyle.FixedSingle, BackColor = Th.Field, ForeColor = Th.Fg, Font = Th.F(10f), Margin = new Padding(0, 0, 0, Th.S(4)) };
        if (lines > 1)
        {
            t.Multiline = true;
            t.AcceptsReturn = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.Height = Th.S(10) + lines * t.Font.Height;
        }
        t.UseSystemPasswordChar = password;
        return t;
    }

    // Grey hint text inside an empty single-line box.
    public static void Cue(TextBox t, string text)
    {
        EventHandler set = (o, e) => SendMessage(t.Handle, 0x1501, (IntPtr)1, text);
        if (t.IsHandleCreated) set(null, null); else t.HandleCreated += set;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

    public static ComboBox Combo(IEnumerable<object> items, object selected, Action<object> changed, int width = 0)
    {
        var c = new DarkCombo(false) { Margin = new Padding(0, Th.S(3), Th.S(6), Th.S(3)) };
        c.Items.AddRange(items.ToArray());
        if (selected != null) c.SelectedItem = selected;
        if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0;
        if (width > 0) { c.Width = Th.S(width); c.Tag = "natural"; }
        if (changed != null) c.SelectedIndexChanged += (o, e) => changed(c.SelectedItem);
        return c;
    }

    // Controls in a line that wraps when the window is narrow.
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Tag = "wrap", Margin = new Padding(0, Th.S(2), 0, Th.S(2)), BackColor = Color.Transparent };
        r.Controls.AddRange(controls);
        return r;
    }

    public static TableLayoutPanel Split(Control left, Control right, int leftWidth)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Th.Bg };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Th.S(leftWidth)));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Dock = DockStyle.Fill;
        right.Dock = DockStyle.Fill;
        t.Controls.Add(left, 0, 0);
        t.Controls.Add(right, 1, 0);
        return t;
    }

    public static ContextMenuStrip Menu()
    {
        var m = new ContextMenuStrip { Renderer = new MenuRenderer(), BackColor = Th.Card, ForeColor = Th.Fg, Font = Th.F(9.5f), ShowImageMargin = true, ShowCheckMargin = false };
        return m;
    }

    public static ToolStripMenuItem Sub(string text)
    {
        var s = new ToolStripMenuItem(text) { ForeColor = Th.Fg, BackColor = Th.Card };
        s.DropDown.BackColor = Th.Card;
        ((ToolStripDropDownMenu)s.DropDown).Renderer = new MenuRenderer();
        return s;
    }

    public static ToolStripMenuItem Item(ToolStrip owner, string text, Action act, bool check = false)
    {
        var i = new ToolStripMenuItem(text) { ForeColor = Th.Fg, BackColor = Th.Card, Checked = check };
        i.Click += (o, e) => act();
        owner.Items.Add(i);
        return i;
    }
}
