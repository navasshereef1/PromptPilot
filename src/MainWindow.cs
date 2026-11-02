using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class NavItem : Control
{
    readonly string glyph;
    readonly ToolTip tip = new ToolTip();
    public bool Selected, Compact;
    bool hover;

    public NavItem(string text, string icon, Action click)
    {
        Text = text;
        glyph = icon;
        Dock = DockStyle.Top;
        Height = Th.S(42);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Click += (o, e) => click();
        KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; click(); } };
        GotFocus += (o, e) => Invalidate();
        LostFocus += (o, e) => Invalidate();
        MouseEnter += (o, e) => { hover = true; Invalidate(); };
        MouseLeave += (o, e) => { hover = false; Invalidate(); };
    }

    public void SetCompact(bool on)
    {
        Compact = on;
        tip.SetToolTip(this, on ? Text : null);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Th.Side);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var pill = new Rectangle(Th.S(8), Th.S(2), Width - Th.S(16), Height - Th.S(4));
        if (Selected || hover || Focused)
            using (var p = Widget.Rounded(pill, Th.S(8)))
            using (var b = new SolidBrush(Selected ? Th.Hover : Th.Card)) g.FillPath(b, p);
        if (Selected) using (var b = new SolidBrush(Th.Accent)) g.FillRectangle(b, pill.X, pill.Y + Th.S(10), Th.S(3), pill.Height - Th.S(20));
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var iconColor = Selected ? Th.Accent : Th.Dim;
        if (Compact)
        {
            TextRenderer.DrawText(g, glyph, Th.Icon(12f), ClientRectangle, iconColor, flags | TextFormatFlags.HorizontalCenter);
            return;
        }
        TextRenderer.DrawText(g, glyph, Th.Icon(11f), new Rectangle(Th.S(20), 0, Th.S(24), Height), iconColor, flags);
        TextRenderer.DrawText(g, Text, Th.F(9.5f, Selected ? FontStyle.Bold : FontStyle.Regular), new Rectangle(Th.S(50), 0, Width - Th.S(50), Height), Selected ? Th.Fg : Th.Dim, flags);
    }
}

partial class MainWindow : ThemedForm
{
    readonly App app;
    readonly Panel side = new Panel();
    readonly Panel content = new Panel();
    readonly Dictionary<string, NavItem> nav = new Dictionary<string, NavItem>();
    string current;

    Settings S { get { return app.S; } }

    public MainWindow(App a)
    {
        app = a;
        Text = "PromptPilot";
        Size = new Size(Th.S(1120), Th.S(740));
        MinimumSize = new Size(Th.S(700), Th.S(520));
        StartPosition = FormStartPosition.CenterScreen;

        side.Dock = DockStyle.Left;
        side.Width = Th.S(190);
        content.Dock = DockStyle.Fill;
        Controls.Add(content);
        Controls.Add(side);
        BuildSide();
        FormClosing += (o, e) => S.Save();
        Resize += (o, e) => FitSide();
        FitSide();
    }

    // Double-buffers the whole window, so resizing doesn't flicker.
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; if (!Silent) cp.ExStyle |= 0x02000000; return cp; }
    }

    public static bool Silent; // renders for testing without taking focus from the user
    protected override bool ShowWithoutActivation { get { return Silent; } }

    bool compact;

    // A narrow window collapses the sidebar to icons, leaving the room for content.
    void FitSide()
    {
        bool want = ClientSize.Width < Th.S(960);
        if (want == compact && side.Width > 0) return;
        compact = want;
        side.Width = Th.S(want ? 62 : 200);
        foreach (var n in nav.Values) n.SetCompact(want);
        if (brandText != null) brandText.Visible = !want;
        if (foot != null) foot.Visible = !want;
    }

    static readonly string[][] Pages = {
        new[] { "Studio", "\uE70F", "Studio" }, new[] { "Context", "\uE77B", "Context" }, new[] { "Modes", "\uE945", "Modes" },
        new[] { "Templates", "\uE8F1", "Templates" }, new[] { "History", "\uE81C", "History" }, new[] { "Providers", "\uE99A", "AI Providers" },
        new[] { "Appearance", "\uE771", "Appearance" }, new[] { "System", "\uE713", "Privacy & System" }, new[] { "Insights", "\uE9D9", "Insights" },
    };

    Label brandText;
    Panel foot;

    void BuildSide()
    {
        side.Controls.Clear();
        nav.Clear();
        side.BackColor = Th.Side;
        foreach (var p in Pages.Reverse())
        {
            var key = p[0];
            var item = new NavItem(p[2], p[1], () => Go(key));
            nav[key] = item;
            side.Controls.Add(item);
        }

        var brand = new Panel { Dock = DockStyle.Top, Height = Th.S(64), BackColor = Th.Side };
        var logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Th.S(28), Th.S(28)), Location = new Point(Th.S(17), Th.S(18)), BackColor = Th.Side };
        using (var ic = Th.AppIcon("app.ico", 48)) logo.Image = ic.ToBitmap();
        brandText = new Label { Text = "PromptPilot", Location = new Point(Th.S(52), Th.S(18)), AutoSize = true, Font = Th.F(12.5f, FontStyle.Bold), ForeColor = Th.Fg, BackColor = Th.Side };
        brand.Controls.Add(logo);
        brand.Controls.Add(brandText);
        side.Controls.Add(brand);

        foot = new Panel { Dock = DockStyle.Bottom, Height = Th.S(84), BackColor = Th.Side, Padding = new Padding(Th.S(16), Th.S(4), Th.S(8), Th.S(10)) };
        var always = new Toggle("Always on", S.Enabled) { Dock = DockStyle.Top };
        always.Changed += () => app.SetEnabled(always.Checked);
        var ready = S.Providers.FirstOrDefault(x => x.Usable());
        var aiLine = new Label { Dock = DockStyle.Top, Height = Th.S(40), Font = Th.F(8.5f), ForeColor = ready == null ? Th.Bad : Th.Dim, BackColor = Th.Side, UseMnemonic = false,
            Text = ready == null ? "No AI key yet" : "AI: " + ready.Name + (S.Providers.Count(x => x.Usable()) > 1 ? " + " + (S.Providers.Count(x => x.Usable()) - 1) + " backup" : "") + "\nSaves automatically" };
        foot.Controls.Add(aiLine);
        foot.Controls.Add(always);
        side.Controls.Add(foot);
        compact = !compact;
        FitSide();
    }

    public string Page { get { return current; } }

    public void Go(string page)
    {
        current = page;
        if (Visible && ActiveForm == this) app.OwnWindowFront(true);
        foreach (var kv in nav) { kv.Value.Selected = kv.Key == page; kv.Value.Invalidate(); }
        content.SuspendLayout();
        var old = content.Controls.Cast<Control>().ToList();
        content.Controls.Clear();
        foreach (var c in old) c.Dispose();
        content.BackColor = Th.Bg;
        BackColor = Th.Bg;
        Control view;
        switch (page)
        {
            case "Context": view = ContextPage(); break;
            case "Modes": view = ModesPage(); break;
            case "Templates": view = TemplatesPage(); break;
            case "History": view = HistoryPage(); break;
            case "Providers": view = ProvidersPage(); break;
            case "Appearance": view = AppearancePage(); break;
            case "System": view = SystemPage(); break;
            case "Insights": view = InsightsPage(); break;
            default: view = StudioPage(); break;
        }
        view.Dock = DockStyle.Fill;
        Ui.ResumeCards(view);
        content.Controls.Add(view);
        content.ResumeLayout();
    }

    void Restyle()
    {
        app.ApplyLook();
        ForeColor = Th.Fg;
        int on = Th.Dark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, 20, ref on, 4);
        BuildSide();
        Go(current);
    }

    static Control Header(string title, string sub, Control body)
    {
        var page = new Panel { BackColor = Th.Bg };
        var head = new Panel { Dock = DockStyle.Top, Height = Th.S(string.IsNullOrEmpty(sub) ? 58 : 80), BackColor = Th.Bg, Padding = new Padding(Th.S(24), Th.S(16), Th.S(20), 0) };
        var t = new Label { Text = title, Dock = DockStyle.Top, Height = Th.S(32), Font = Th.F(16f, FontStyle.Bold), ForeColor = Th.Fg, UseMnemonic = false };
        var s = new Label { Text = sub, Dock = DockStyle.Top, Height = Th.S(24), Font = Th.F(9.5f), ForeColor = Th.Dim, UseMnemonic = false };
        head.Controls.Add(s);
        head.Controls.Add(t);
        body.Dock = DockStyle.Fill;
        page.Controls.Add(body);
        page.Controls.Add(head);
        return page;
    }

    // ---------- Studio ----------

    static string studioDraft = "";
    static string studioMode = "engineer";
    CancellationTokenSource studioCts;

    Control StudioPage()
    {
        Func<Mode> currentMode = () => S.FindMode(studioMode) ?? S.Modes[0];
        var chips = new List<KeyValuePair<string, PillButton>>();
        ComboBox more = null;
        Action syncChips = () =>
        {
            foreach (var c in chips) { c.Value.Primary = c.Key == studioMode; c.Value.Invalidate(); }
        };
        var chipRow = Ui.Row();
        chipRow.WrapContents = false;
        foreach (var pm in S.Modes.Where(x => x.Pinned))
        {
            var id = pm.Id;
            var chip = Ui.Btn(pm.Name, () => { studioMode = id; if (more != null) more.SelectedIndex = 0; syncChips(); });
            chips.Add(new KeyValuePair<string, PillButton>(id, chip));
            chipRow.Controls.Add(chip);
        }
        var others = S.Modes.Where(x => !x.Pinned).ToList();
        more = Ui.Combo(new object[] { "More modes" }.Concat(others.Cast<object>()), null, null, 150);
        var otherNow = others.FirstOrDefault(x => x.Id == studioMode);
        if (otherNow != null) more.SelectedItem = otherNow;
        more.SelectedIndexChanged += (o, e) =>
        {
            var picked = more.SelectedItem as Mode;
            if (picked != null) { studioMode = picked.Id; syncChips(); }
        };
        syncChips();
        var targetCombo = Ui.Combo(new object[] { "Auto", "ChatGPT", "Claude", "Gemini", "Any AI" }, "Auto", null, 100);

        var draft = Ui.Box(studioDraft, 10);
        draft.BorderStyle = BorderStyle.None;
        draft.BackColor = Th.Card;
        draft.Dock = DockStyle.Fill;
        var output = new RichTextBox { BorderStyle = BorderStyle.None, BackColor = Th.Card, ForeColor = Th.Fg, Font = Th.F(10f), DetectUrls = false, Dock = DockStyle.Fill };
        output.HandleCreated += (o, e) => Ui.DarkScroll(output);

        var draftHint = new Label { Text = "Type or paste a rough prompt here.", AutoSize = true, ForeColor = Th.Dim, BackColor = Th.Card, Font = Th.F(10f), Location = new Point(0, Th.S(2)) };
        draftHint.Click += (o, e) => draft.Focus();
        draft.Controls.Add(draftHint);
        var outHint = new Label { Text = "Your rewritten prompt appears here.\nPress Ctrl+Enter in the draft to run.", AutoSize = true, ForeColor = Th.Dim, BackColor = Th.Card, Font = Th.F(10f), Location = new Point(0, Th.S(2)) };
        output.Controls.Add(outHint);
        output.TextChanged += (o, e) => outHint.Visible = output.TextLength == 0;

        var meter = new Meter { Dock = DockStyle.Top };
        var score = new Label { Dock = DockStyle.Top, Height = Th.S(24), Font = Th.F(9f, FontStyle.Bold), ForeColor = Th.Fg, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        var tips = new Label { Dock = DockStyle.Top, Height = Th.S(36), Font = Th.F(8.5f), ForeColor = Th.Dim, BackColor = Color.Transparent, UseMnemonic = false };
        var status = new Label { Dock = DockStyle.Right, Width = Th.S(230), TextAlign = ContentAlignment.MiddleRight, Font = Th.F(8.5f), ForeColor = Th.Dim, BackColor = Color.Transparent, AutoEllipsis = true, UseMnemonic = false };
        var stop = Ui.Btn("Stop", () => { if (studioCts != null) studioCts.Cancel(); });
        stop.Enabled = false;
        PillButton run = null;

        Action rescore = () =>
        {
            studioDraft = draft.Text;
            draftHint.Visible = draft.TextLength == 0;
            List<ScoreCheck> checks;
            int v = Scorer.Score(draft.Text, out checks);
            bool empty = draft.Text.Trim() == "";
            var c = empty ? Th.Border : v >= 70 ? Th.Good : v >= 40 ? Th.Accent : Th.Bad;
            meter.Set(v, c);
            score.Text = empty ? "Prompt score" : "Prompt score " + v + " / 100   \u00B7   about " + Scorer.Tokens(draft.Text) + " tokens";
            var missing = checks.Where(x => !x.Pass).Take(2).Select(x => x.Tip).ToList();
            tips.Text = empty ? "Scored on your PC as you type. No AI or internet used." : missing.Count == 0 ? "All checks passed." : string.Join("  ", missing);
        };
        draft.TextChanged += (o, e) => rescore();

        Func<Task> go = async () =>
        {
            var mode = currentMode();
            if (draft.Text.Trim() == "") { status.Text = "Type a draft first."; draft.Focus(); return; }
            if (!Ai.HasProvider(S)) { Go("Providers"); return; }
            if (studioCts != null) studioCts.Cancel();
            var mine = studioCts = new CancellationTokenSource();
            run.Enabled = false;
            stop.Enabled = true;
            output.Clear();
            status.Text = "Writing\u2026";
            var targetName = (string)targetCombo.SelectedItem;
            var ctx = app.BuildContext(targetName == "Auto" ? app.LastTarget : targetName == "Any AI" ? "" : targetName);
            var input = draft.Text;
            var mask = S.Guard == "Off" ? new MaskResult { Text = input } : Guard.Mask(input);
            if (S.Guard != "Off")
            {
                Guard.Mask(ctx.About, mask); ctx.About = mask.Text;
                Guard.Mask(ctx.ProjectText, mask); ctx.ProjectText = mask.Text;
                Guard.Mask(input, mask);
            }
            try
            {
                var r = await Ai.RunStream(S, Ai.Build(mode, mask.Text, ctx), piece =>
                {
                    if (output.IsHandleCreated && !output.IsDisposed) output.BeginInvoke((Action)(() => { if (mine == studioCts) output.AppendText(piece); }));
                }, mine.Token);
                if (output.IsDisposed) return;
                output.Text = Guard.Unmask(r.Text, mask);
                status.Text = r.Provider + " in " + (r.Ms / 1000.0).ToString("0.0") + "s" + (mask.Map.Count > 0 ? ", " + mask.Map.Count + " masked" : "");
                if (S.HistoryOn)
                    History.Add(new HistoryItem { Id = Guid.NewGuid().ToString("N"), When = DateTime.Now.ToString("s"), Mode = mode.Name, Provider = r.Provider, Target = "Studio", In = input, Out = output.Text, Ms = r.Ms });
            }
            catch (OperationCanceledException) { if (!status.IsDisposed) status.Text = "Stopped."; }
            catch (Exception e) { if (!status.IsDisposed) { status.Text = "Failed. Check AI Providers."; new ToolTip().SetToolTip(status, e.Message); Settings.Log("studio: " + e.Message); } }
            if (!run.IsDisposed) { run.Enabled = true; stop.Enabled = false; }
        };
        run = Ui.Btn("Rewrite", () => { var t = go(); }, true);
        run.MinimumSize = new Size(Th.S(96), Th.S(32));
        draft.KeyDown += (o, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var t = go(); } };

        var project = app.CurrentProject();
        var chip2 = Ui.Lbl(project == null ? "No project" : "Project: " + project.Name, Th.F(8.5f), Th.Dim);
        chip2.Margin = new Padding(Th.S(4), Th.S(11), 0, 0);
        var tools = Ui.Row(chipRow, more, targetCombo, run, stop, chip2);
        tools.Dock = DockStyle.Fill;
        tools.MaximumSize = Size.Empty;
        tools.AutoSize = true;

        // Draft card
        var draftCard = new Pane();
        var draftTitle = new Label { Text = "Your draft", Dock = DockStyle.Top, Height = Th.S(28), Font = Th.F(10f, FontStyle.Bold), ForeColor = Th.Fg, BackColor = Color.Transparent };
        var draftFoot = new Panel { Dock = DockStyle.Bottom, Height = Th.S(70), BackColor = Color.Transparent, Padding = new Padding(0, Th.S(8), 0, 0) };
        draftFoot.Controls.Add(tips);
        draftFoot.Controls.Add(score);
        draftFoot.Controls.Add(meter);
        draftCard.Controls.Add(draft);
        draftCard.Controls.Add(draftFoot);
        draftCard.Controls.Add(draftTitle);

        // Result card
        var outCard = new Pane();
        var outHead = new Panel { Dock = DockStyle.Top, Height = Th.S(28), BackColor = Color.Transparent };
        outHead.Controls.Add(new Label { Text = "Result", Dock = DockStyle.Fill, Font = Th.F(10f, FontStyle.Bold), ForeColor = Th.Fg, BackColor = Color.Transparent });
        outHead.Controls.Add(status);
        var actions = Ui.Row(
            Ui.Btn("Insert into chat", () => { if (output.Text != "") app.InsertIntoChat(output.Text); }, false),
            Ui.Btn("Copy", () => { if (output.Text != "") { TextBridge.Put(output.Text); status.Text = "Copied."; } }),
            Ui.Btn("Use as draft", () => { if (output.Text != "") draft.Text = output.Text; }),
            Ui.Btn("Save as template", () =>
            {
                if (output.Text == "") return;
                S.Templates.Insert(0, new Template { Name = (draft.Text.Length > 30 ? draft.Text.Substring(0, 30) : draft.Text).Trim(), Body = output.Text, Category = "Saved" });
                S.Save();
                status.Text = "Saved to Templates.";
            }));
        actions.Dock = DockStyle.Bottom;
        actions.MaximumSize = Size.Empty;
        outCard.Controls.Add(output);
        outCard.Controls.Add(actions);
        outCard.Controls.Add(outHead);

        var root = new TableLayoutPanel { BackColor = Th.Bg, ColumnCount = 2, RowCount = 3, Padding = new Padding(Th.S(24), 0, Th.S(20), Th.S(16)) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(tools, 0, 0);
        root.SetColumnSpan(tools, 2);

        // Side by side when there's room, one above the other when narrow.
        bool? narrow = null;
        Action arrange = () =>
        {
            bool now = root.ClientSize.Width < Th.S(760);
            if (narrow == now) return;
            narrow = now;
            root.SuspendLayout();
            root.ColumnStyles.Clear();
            while (root.RowStyles.Count > 1) root.RowStyles.RemoveAt(1);
            root.Controls.Remove(draftCard);
            root.Controls.Remove(outCard);
            draftCard.Dock = DockStyle.Fill;
            outCard.Dock = DockStyle.Fill;
            if (now)
            {
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                draftCard.Margin = new Padding(0, Th.S(8), 0, Th.S(5));
                outCard.Margin = new Padding(0, Th.S(5), 0, 0);
                root.Controls.Add(draftCard, 0, 1);
                root.Controls.Add(outCard, 0, 2);
                root.SetColumnSpan(draftCard, 2);
                root.SetColumnSpan(outCard, 2);
            }
            else
            {
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
                draftCard.Margin = new Padding(0, Th.S(8), Th.S(6), 0);
                outCard.Margin = new Padding(Th.S(6), Th.S(8), 0, 0);
                root.Controls.Add(draftCard, 0, 1);
                root.Controls.Add(outCard, 1, 1);
                root.SetColumnSpan(draftCard, 1);
                root.SetColumnSpan(outCard, 1);
            }
            root.ResumeLayout();
        };
        root.Resize += (o, e) => arrange();
        arrange();
        rescore();
        return Header("Studio", "Write and refine a prompt here, then send it to any chat.", root);
    }

    // ---------- Context ----------

    Control ContextPage()
    {
        var page = new Stack();
        page.Title("About me");
        page.Hint("Who you are, your work, and how you like answers. Used by Improve, Engineer and other modes marked to use context.");
        var about = page.Add(Ui.Box(S.AboutMe, 4));
        about.TextChanged += (o, e) => S.AboutMe = about.Text;
        about.Leave += (o, e) => S.Save();

        page.Title("Live sources");
        var chat = page.Add(new Toggle("Read the current chat when I click (browser only)", S.ReadChat));
        chat.Changed += () => { S.ReadChat = chat.Checked; S.Save(); };
        page.Hint("Sends the last part of the visible conversation with your draft, so the prompt fits the chat. It goes to your AI provider, so leave it off for private chats.");
        var ask = page.Add(new Toggle("Engineer asks me up to 3 questions when something important is missing", S.AskFirst));
        ask.Changed += () => { S.AskFirst = ask.Checked; S.Save(); };
        var auto = page.Add(new Toggle("Pick the project automatically from its keywords in the window title or address", S.AutoProject));
        auto.Changed += () => { S.AutoProject = auto.Checked; S.Save(); app.W.Refresh2(); };

        page.Title("Projects");
        page.Hint("One per area of your work. The Engineer uses the active project's notes and files. Choose it here or from the bar's menu.");
        var active = Ui.Combo(new object[] { "None" }.Concat(S.Projects.Select(p => (object)p.Name)), string.IsNullOrEmpty(S.ActiveProject) ? "None" : S.ActiveProject, v =>
        {
            S.ActiveProject = (string)v == "None" ? "" : (string)v;
            S.Save();
            app.W.Refresh2();
        }, 280);
        page.Add(Ui.Row(Ui.Lbl("Active project"), active));

        var list = new OwnerList(false) { BackColor = Th.Field, Empty = "No projects yet. Click New to add one." };
        foreach (var p in S.Projects) list.Items.Add(p);
        var name = Ui.Box();
        var notes = Ui.Box("", 6);
        var keys = Ui.Box();
        var files = new OwnerList(false) { Height = Th.S(96), BackColor = Th.Field, Empty = "No files attached." };
        var editor = new Stack { Padding = new Padding(Th.S(14), 0, 0, 0), AutoScroll = false, BackColor = Th.Card };

        Action<Project> show = p =>
        {
            editor.Enabled = p != null;
            name.Text = p == null ? "" : p.Name;
            notes.Text = p == null ? "" : p.Notes;
            keys.Text = p == null ? "" : p.Keywords;
            files.Items.Clear();
            if (p != null) foreach (var f in p.Files) files.Items.Add(f);
        };
        list.SelectedIndexChanged += (o, e) => show(list.SelectedItem as Project);
        Func<Project> sel = () => list.SelectedItem as Project;
        name.TextChanged += (o, e) => { var p = sel(); if (p != null && name.Focused) { if (S.ActiveProject == p.Name) S.ActiveProject = name.Text; p.Name = name.Text; list.Items[list.SelectedIndex] = p; } };
        notes.TextChanged += (o, e) => { var p = sel(); if (p != null) p.Notes = notes.Text; };
        keys.TextChanged += (o, e) => { var p = sel(); if (p != null) p.Keywords = keys.Text; };
        foreach (var box in new[] { name, notes, keys }) box.Leave += (o, e) => S.Save();

        editor.Field("Name");
        editor.Add(name);
        editor.Field("Notes: goals, audience, product, tone, facts the AI should know");
        editor.Add(notes);
        editor.Field("Auto-pick keywords, comma separated (for example: acme, billing)");
        editor.Add(keys);
        editor.Field("Files: text, Markdown, CSV or code. The first 12,000 characters are used.");
        editor.Add(files);
        editor.Add(Ui.Row(
            Ui.Btn("Add files", () =>
            {
                var p = sel();
                if (p == null) return;
                using (var d = new OpenFileDialog { Multiselect = true, Filter = "Text files|*.txt;*.md;*.csv;*.json;*.xml;*.html;*.js;*.ts;*.py;*.cs;*.java;*.sql;*.yml;*.yaml;*.log|All files|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        foreach (var f in d.FileNames) if (!p.Files.Contains(f)) { p.Files.Add(f); files.Items.Add(f); }
                        S.Save();
                    }
            }),
            Ui.Btn("Remove file", () =>
            {
                var p = sel();
                if (p == null || files.SelectedItem == null) return;
                p.Files.Remove((string)files.SelectedItem);
                files.Items.Remove(files.SelectedItem);
                S.Save();
            })));

        var left = new Panel { BackColor = Th.Card };
        var buttons = Ui.Row(
            Ui.Btn("New", () =>
            {
                var p = new Project { Name = "Project " + (S.Projects.Count + 1) };
                S.Projects.Add(p);
                S.Save();
                list.SelectedIndex = list.Items.Add(p);
            }),
            Ui.Btn("Delete", () =>
            {
                var p = sel();
                if (p == null) return;
                if (MessageBox.Show(this, "Delete project \"" + p.Name + "\"? Your files stay where they are.", "PromptPilot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                S.Projects.Remove(p);
                if (S.ActiveProject == p.Name) S.ActiveProject = "";
                list.Items.Remove(p);
                show(null);
                S.Save();
                app.W.Refresh2();
            }));
        buttons.Dock = DockStyle.Bottom;
        list.Dock = DockStyle.Fill;
        left.Controls.Add(list);
        left.Controls.Add(buttons);
        var split = Ui.Split(left, editor, 200);
        split.Dock = DockStyle.None;
        split.BackColor = Th.Card;
        split.Height = Th.S(600);
        page.Add(split);
        show(null);
        if (list.Items.Count > 0) list.SelectedIndex = 0;

        page.Title("Check what gets sent");
        page.Hint("Shows the exact text Engineer sends to your AI provider, with private details masked.");
        page.Add(Ui.Row(Ui.Btn("Show exactly what Engineer would send", () =>
        {
            var ctx = app.BuildContext(app.LastTarget);
            if (S.ReadChat) ctx.Chat = "(the end of the visible chat is added here when you click)";
            var mask = Guard.Mask(ctx.About);
            if (S.Guard != "Off")
            {
                ctx.About = mask.Text;
                Guard.Mask(ctx.ProjectText, mask); ctx.ProjectText = mask.Text;
            }
            var req = Ai.Build(S.FindMode("engineer"), "(your draft goes here)", ctx);
            new TextViewForm("What gets sent", "SYSTEM\r\n" + req.System.Replace("\n", "\r\n") + "\r\n\r\nUSER\r\n" + req.User.Replace("\n", "\r\n")).ShowDialog(this);
        })));
        return Header("Context", "Tell the Engineer about you and your work so its prompts are specific.", page);
    }

    // ---------- Modes ----------

    Control ModesPage()
    {
        var list = new OwnerList(false);
        foreach (var m in S.Modes) list.Items.Add(m);
        var editor = new Stack();
        editor.Card();
        var name = Ui.Box();
        var text = Ui.Box("", 10);
        var hotkey = HotkeyBox("");
        var pinned = new Toggle("Show on the bar", false);
        var ctx = new Toggle("Use my context (About me, project, chat)", false);
        var prev = new Toggle("Preview before replacing", false);
        var temp = Ui.Combo(new object[] { "Exact", "Balanced", "Creative" }, "Balanced", null, 130);
        var warn = Ui.Lbl("", Th.F(9f), Th.Bad);
        bool loading = false;
        Func<Mode> sel = () => list.SelectedItem as Mode;
        Action saveHot = () =>
        {
            var failed = app.RegisterHotkeys();
            warn.Text = failed.Count == 0 ? "" : "Already used by another program: " + string.Join(", ", failed);
        };
        Action load = () =>
        {
            var m = sel();
            if (m == null) return;
            loading = true;
            name.Text = m.Name;
            text.Text = m.Instructions;
            hotkey.Text = m.Hotkey;
            pinned.Checked = m.Pinned;
            ctx.Checked = m.UseContext;
            prev.Checked = m.Preview;
            temp.SelectedItem = m.Temperature <= 0.2 ? "Exact" : m.Temperature >= 0.5 ? "Creative" : "Balanced";
            loading = false;
        };
        list.SelectedIndexChanged += (o, e) => load();
        name.TextChanged += (o, e) => { var m = sel(); if (m != null && !loading && name.Text.Trim() != "") { m.Name = name.Text; list.Items[list.SelectedIndex] = m; app.W.Refresh2(); } };
        text.TextChanged += (o, e) => { var m = sel(); if (m != null && !loading) m.Instructions = text.Text; };
        hotkey.TextChanged += (o, e) => { var m = sel(); if (m != null && !loading) { m.Hotkey = hotkey.Text; saveHot(); S.Save(); } };
        pinned.Changed += () => { var m = sel(); if (m != null) { m.Pinned = pinned.Checked; S.Save(); app.W.Refresh2(); } };
        ctx.Changed += () => { var m = sel(); if (m != null) { m.UseContext = ctx.Checked; S.Save(); } };
        prev.Changed += () => { var m = sel(); if (m != null) { m.Preview = prev.Checked; S.Save(); } };
        temp.SelectedIndexChanged += (o, e) => { var m = sel(); if (m != null && !loading) { var v = (string)temp.SelectedItem; m.Temperature = v == "Exact" ? 0.1 : v == "Creative" ? 0.7 : 0.3; S.Save(); } };
        foreach (var b in new[] { name, text }) b.Leave += (o, e) => S.Save();

        editor.Field("Name");
        editor.Add(name);
        editor.Field("Instructions to the AI");
        editor.Hint("This is what makes the mode behave as it does. Nothing is trained: change the words and the behaviour changes on the next click.");
        editor.Add(text);
        editor.Field("Hotkey: click, then press the keys. Backspace clears.");
        hotkey.Width = Th.S(220);
        hotkey.Tag = "natural";
        editor.Add(hotkey);
        editor.Add(warn);
        editor.Field("Options");
        editor.Add(pinned);
        editor.Add(ctx);
        editor.Add(prev);
        editor.Add(Ui.Row(Ui.Lbl("Creativity"), temp));
        editor.Add(Ui.Row(Ui.Btn("Reset to original", () =>
        {
            var m = sel();
            if (m == null) return;
            var d = Defaults.Modes().FirstOrDefault(x => x.Id == m.Id);
            if (d == null) { MessageBox.Show(this, "This is your own mode, so it has no original.", "PromptPilot"); return; }
            m.Instructions = d.Instructions; m.Temperature = d.Temperature; m.UseContext = d.UseContext; m.Name = d.Name;
            list.Items[list.SelectedIndex] = m;
            load();
            S.Save();
        })));

        var left = new Panel { BackColor = Th.Bg, Padding = new Padding(Th.S(24), Th.S(4), 0, Th.S(16)) };
        var buttons = Ui.Row(
            Ui.Btn("New", () =>
            {
                var m = new Mode { Id = "m" + DateTime.Now.Ticks, Name = "My mode", Instructions = "Describe how the draft should be rewritten.", UseContext = true };
                S.Modes.Add(m);
                S.Save();
                list.SelectedIndex = list.Items.Add(m);
            }),
            Ui.Btn("Delete", () =>
            {
                var m = sel();
                if (m == null) return;
                if (m.Builtin) { MessageBox.Show(this, "Built-in modes can be edited or reset, but not deleted. Turn off Show on the bar to hide one.", "PromptPilot"); return; }
                if (MessageBox.Show(this, "Delete mode \"" + m.Name + "\"?", "PromptPilot", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                S.Modes.Remove(m);
                list.Items.Remove(m);
                S.Save();
                app.RegisterHotkeys();
                app.W.Refresh2();
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }));
        buttons.Dock = DockStyle.Bottom;
        list.Dock = DockStyle.Fill;
        left.Controls.Add(list);
        left.Controls.Add(buttons);
        list.SelectedIndex = 0;
        return Header("Modes", "What each button does. Edit them, or add your own.", Ui.Split(left, editor, 230));
    }

    static TextBox HotkeyBox(string value)
    {
        var box = Ui.Box(value);
        box.ReadOnly = true;
        box.KeyDown += (o, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) { box.Text = ""; return; }
            var t = Native.HotkeyText(e.KeyData);
            if (t != null) box.Text = t;
        };
        return box;
    }

    // ---------- Templates ----------

    Control TemplatesPage()
    {
        var search = Ui.Box();
        Ui.Cue(search, "Search templates");
        var list = new OwnerList(true) { Empty = "No templates match." };
        list.Sub = o => ((Template)o).Category + "  \u00B7  used " + ((Template)o).Uses + " times";
        var editor = new Stack();
        editor.Card();
        var name = Ui.Box();
        var category = Ui.Box();
        var body = Ui.Box("", 12);
        bool loading = false;
        Func<Template> sel = () => list.SelectedItem as Template;
        Action reload = () =>
        {
            list.Items.Clear();
            var q = search.Text.Trim();
            foreach (var t in S.Templates.Where(t => q == "" || (t.Name + " " + t.Category + " " + t.Body).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                list.Items.Add(t);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        };
        search.TextChanged += (o, e) => reload();
        list.SelectedIndexChanged += (o, e) =>
        {
            var t = sel();
            loading = true;
            name.Text = t == null ? "" : t.Name;
            category.Text = t == null ? "" : t.Category;
            body.Text = t == null ? "" : t.Body;
            loading = false;
        };
        name.TextChanged += (o, e) => { var t = sel(); if (t != null && !loading) { t.Name = name.Text; list.Items[list.SelectedIndex] = t; } };
        category.TextChanged += (o, e) => { var t = sel(); if (t != null && !loading) t.Category = category.Text; };
        body.TextChanged += (o, e) => { var t = sel(); if (t != null && !loading) t.Body = body.Text; };
        foreach (var b in new[] { name, category, body }) b.Leave += (o, e) => S.Save();

        Action<Action<string>> use = done =>
        {
            var t = sel();
            if (t == null) return;
            t.Uses++;
            S.Save();
            var vars = VarForm.Vars(t.Body);
            if (vars.Count == 0) { done(t.Body); return; }
            new VarForm(t.Name, vars, v => done(VarForm.Fill(t.Body, v))).Show(this);
        };

        editor.Field("Name");
        editor.Add(name);
        editor.Field("Category");
        editor.Add(category);
        editor.Field("Text. Write {{name}} for parts to fill in each time.");
        editor.Add(body);
        editor.Add(Ui.Row(
            Ui.Btn("Insert into chat", () => use(app.InsertIntoChat), true),
            Ui.Btn("Open in Studio", () => use(text => { studioDraft = text; Go("Studio"); })),
            Ui.Btn("Copy", () => use(TextBridge.Put))));
        editor.Hint("Quick access from any app: " + S.HkPalette);

        var left = new Panel { BackColor = Th.Bg, Padding = new Padding(Th.S(24), Th.S(4), 0, Th.S(16)) };
        var buttons = Ui.Row(
            Ui.Btn("New", () =>
            {
                var t = new Template();
                S.Templates.Insert(0, t);
                S.Save();
                search.Text = "";
                reload();
                list.SelectedIndex = 0;
            }),
            Ui.Btn("Delete", () =>
            {
                var t = sel();
                if (t == null) return;
                if (MessageBox.Show(this, "Delete template \"" + t.Name + "\"?", "PromptPilot", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                S.Templates.Remove(t);
                S.Save();
                reload();
            }));
        buttons.Dock = DockStyle.Bottom;
        search.Dock = DockStyle.Top;
        list.Dock = DockStyle.Fill;
        var gap = new Panel { Dock = DockStyle.Top, Height = Th.S(8), BackColor = Th.Bg };
        left.Controls.Add(list);
        left.Controls.Add(gap);
        left.Controls.Add(search);
        left.Controls.Add(buttons);
        reload();
        return Header("Templates", "Reusable prompts with blanks to fill in.", Ui.Split(left, editor, 280));
    }

    // ---------- History ----------

    Control HistoryPage()
    {
        var all = History.Load(S.HistoryDays);
        var search = Ui.Box();
        Ui.Cue(search, "Search history");
        var favOnly = new Toggle("Favourites only", false);
        var list = new OwnerList(true) { Empty = "Nothing here yet. Your rewrites will appear here." };
        list.Sub = o => { var h = ((HistoryItemView)o).H; return h.Mode + "  \u00B7  " + h.When.Replace("T", " ") + "  \u00B7  " + h.Provider + (h.Fav ? "  \u00B7  \u2605" : ""); };
        var before = Ui.Box("", 6);
        var after = Ui.Box("", 8);
        before.ReadOnly = true;
        after.ReadOnly = true;
        Func<HistoryItem> sel = () => { var v = list.SelectedItem as HistoryItemView; return v == null ? null : v.H; };
        Action reload = () =>
        {
            list.BeginUpdate();
            list.Items.Clear();
            var q = search.Text.Trim();
            foreach (var h in all.Where(h => (!favOnly.Checked || h.Fav) && (q == "" || ((h.In ?? "") + " " + (h.Out ?? "") + " " + h.Mode).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).Take(500))
                list.Items.Add(new HistoryItemView { H = h });
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = 0; else { before.Text = ""; after.Text = ""; }
        };
        search.TextChanged += (o, e) => reload();
        favOnly.Changed += reload;
        list.SelectedIndexChanged += (o, e) => { var h = sel(); before.Text = h == null ? "" : h.In; after.Text = h == null ? "" : h.Out; };

        var detail = new Stack();
        detail.Card();
        detail.Field("Your draft");
        detail.Add(before);
        detail.Field("Result");
        detail.Add(after);
        detail.Add(Ui.Row(
            Ui.Btn("Insert into chat", () => { var h = sel(); if (h != null) app.InsertIntoChat(h.Out); }, true),
            Ui.Btn("Copy result", () => { var h = sel(); if (h != null) TextBridge.Put(h.Out); }),
            Ui.Btn("Reuse draft", () => { var h = sel(); if (h != null) { studioDraft = h.In; Go("Studio"); } }),
            Ui.Btn("Favourite", () => { var h = sel(); if (h != null) { h.Fav = !h.Fav; History.Write(Enumerable.Reverse(all)); list.Invalidate(); } }),
            Ui.Btn("Delete", () => { var h = sel(); if (h != null) { all.Remove(h); History.Write(Enumerable.Reverse(all)); reload(); } })));
        detail.Hint(S.HistoryOn ? "Stored only on this PC, in plain text. Turn it off or clear it under Privacy & System." : "History is turned off under Privacy & System.");

        var left = new Panel { BackColor = Th.Bg, Padding = new Padding(Th.S(24), Th.S(4), 0, Th.S(16)) };
        search.Dock = DockStyle.Top;
        favOnly.Dock = DockStyle.Top;
        favOnly.Tag = null;
        list.Dock = DockStyle.Fill;
        left.Controls.Add(list);
        left.Controls.Add(favOnly);
        left.Controls.Add(search);
        reload();
        return Header("History", all.Count + " rewrites saved on this PC.", Ui.Split(left, detail, 340));
    }

    class HistoryItemView
    {
        public HistoryItem H;
        public override string ToString() { var s = (H.In ?? "").Replace("\r", " ").Replace("\n", " "); return s.Length > 70 ? s.Substring(0, 70) : s; }
    }

    // ---------- Providers ----------

    Control ProvidersPage()
    {
        var page = new Stack();
        page.Card();
        page.Hint("Add free keys for two or three of these. They are tried from top to bottom, and if one fails or hits its limit the next takes over. Keys are encrypted for your Windows account.");
        var speed = Ui.Combo(new object[] { "1.5 seconds", "2.5 seconds", "4 seconds", "Never" }, S.HedgeMs == 1500 ? "1.5 seconds" : S.HedgeMs == 4000 ? "4 seconds" : S.HedgeMs >= 30000 ? "Never" : "2.5 seconds", v =>
        {
            var t = (string)v;
            S.HedgeMs = t.StartsWith("1.5") ? 1500 : t.StartsWith("4") ? 4000 : t == "Never" ? 60000 : 2500;
            S.Save();
        }, 130);
        page.Add(Ui.Row(Ui.Lbl("If a provider is slow, also start the next one after"), speed));

        for (int i = 0; i < S.Providers.Count; i++)
        {
            var p = S.Providers[i];
            int index = i;
            var title = Ui.Lbl(p.Name, Th.F(11f, FontStyle.Bold));
            var state = Ui.Lbl(p.Usable() ? "Ready" : "No key", Th.F(8.5f, FontStyle.Bold), p.Usable() ? Th.Good : Th.Dim);
            state.Margin = new Padding(0, Th.S(7), Th.S(10), 0);
            title.Margin = new Padding(0, Th.S(4), Th.S(10), 0);
            var enabled = new Toggle("On", p.Enabled);
            enabled.Changed += () => { p.Enabled = enabled.Checked; S.Save(); };
            var link = new LinkLabel { Text = "Get a free key", AutoSize = true, LinkColor = Th.Accent, ActiveLinkColor = Th.Accent, VisitedLinkColor = Th.Accent, Margin = new Padding(Th.S(10), Th.S(6), 0, 0), Tag = "natural" };
            link.LinkClicked += (o, e) => { try { Process.Start(p.KeyPage); } catch { } };
            var up = Ui.Btn("\u2191", () => MoveProvider(index, -1));
            var down = Ui.Btn("\u2193", () => MoveProvider(index, 1));
            up.MinimumSize = down.MinimumSize = new Size(Th.S(34), Th.S(28));
            var head = Ui.Row(title, state, enabled, link, up, down);
            page.Card();
            page.Add(head);
            if (!string.IsNullOrEmpty(p.Note)) page.Hint(p.Note);

            var key = Ui.Box("", 1, true);
            if (!string.IsNullOrEmpty(p.KeyEnc)) { try { key.Text = p.Key(); } catch { key.Text = ""; } }
            key.Width = Th.S(330);
            key.Tag = "natural";
            key.TextChanged += (o, e) => { var k = key.Text.Trim(); p.KeyEnc = k == "" ? null : Settings.Encrypt(k); };
            key.Leave += (o, e) => S.Save();
            var keyLabel = Ui.Lbl("Key", Th.F(9f), Th.Dim);
            keyLabel.Margin = new Padding(0, Th.S(5), Th.S(6), 0);
            var modelLabel = Ui.Lbl("Model", Th.F(9f), Th.Dim);
            modelLabel.Margin = new Padding(Th.S(10), Th.S(5), Th.S(6), 0);
            var model = new DarkCombo(true) { Width = Th.S(230), Text = p.Model, Tag = "natural" };
            model.TextChanged += (o, e) => { if (model.Text.Trim() != "") p.Model = model.Text.Trim(); };
            model.SelectedIndexChanged += (o, e) => S.Save();
            model.Leave += (o, e) => S.Save();
            var result = Ui.Lbl("", Th.F(9f), Th.Dim);
            var fetch = Ui.Btn("Load models", null);
            fetch.Click += async (o, e) =>
            {
                result.ForeColor = Th.Dim;
                result.Text = "Loading models\u2026";
                try
                {
                    var ids = await Task.Run(() => Ai.FetchModels(p));
                    if (result.IsDisposed) return;
                    model.Items.Clear();
                    model.Items.AddRange(ids.Cast<object>().ToArray());
                    model.Text = p.Model;
                    result.Text = ids.Count + " models found. Open the Model list to choose.";
                }
                catch (Exception ex) { if (!result.IsDisposed) { result.ForeColor = Th.Bad; result.Text = ex.Message; } }
            };
            var test = Ui.Btn("Test", null);
            test.Click += async (o, e) =>
            {
                S.Save();
                result.ForeColor = Th.Dim;
                result.Text = "Testing\u2026";
                try
                {
                    var r = await Task.Run(() => Ai.TestProvider(p, CancellationToken.None));
                    if (result.IsDisposed) return;
                    result.ForeColor = Th.Good;
                    result.Text = "Working in " + r.Ms + " ms: " + r.Text;
                }
                catch (Exception ex) { if (!result.IsDisposed) { result.ForeColor = Th.Bad; result.Text = ex.Message; } }
            };
            page.Add(Ui.Row(keyLabel, key, modelLabel, model));
            page.Add(Ui.Row(fetch, test));
            page.Add(result);
        }
        return Header("AI Providers", "All of these have free options. No payment details are needed.", page);
    }

    void MoveProvider(int i, int delta)
    {
        int j = i + delta;
        if (j < 0 || j >= S.Providers.Count) return;
        var t = S.Providers[i];
        S.Providers[i] = S.Providers[j];
        S.Providers[j] = t;
        S.Save();
        Go("Providers");
    }

    // ---------- Appearance ----------

    Control AppearancePage()
    {
        var page = new Stack();
        page.Title("Widget style");
        var grid = new FlowLayoutPanel { AutoSize = true, Tag = "wrap", WrapContents = true, BackColor = Color.Transparent };
        foreach (var style in Widget.Styles)
        {
            var st = style;
            var card = new StyleCard(S, st, S.Style == st, () => { S.Style = st; S.Save(); app.ApplyLook(); Go("Appearance"); });
            grid.Controls.Add(card);
        }
        page.Add(grid);

        page.Title("Placement");
        var place = Ui.Combo(new object[] { "Stay where I put it", "Follow the active window" }, S.Placement == "FollowWindow" ? "Follow the active window" : "Stay where I put it", v =>
        {
            S.Placement = (string)v == "Follow the active window" ? "FollowWindow" : "Free";
            S.Save();
            app.ApplyLook();
        }, 230);
        page.Add(place);
        page.Hint("Drag the widget by its dots. Following the window keeps it near the chat box, and uses a little more CPU only while that window is moving.");
        var size = Ui.Combo(new object[] { "Small", "Medium", "Large" }, new[] { "Small", "Medium", "Large" }[Math.Max(0, Math.Min(2, S.Size))], v =>
        {
            S.Size = Array.IndexOf(new[] { "Small", "Medium", "Large" }, (string)v);
            S.Save();
            Restyle();
        }, 120);
        var opacity = new TrackBar { Minimum = 30, Maximum = 100, Value = Math.Max(30, Math.Min(100, S.Opacity)), TickStyle = TickStyle.None, Width = Th.S(220), Height = Th.S(30), AutoSize = false, BackColor = Th.Card, Tag = "natural" };
        opacity.ValueChanged += (o, e) => { S.Opacity = opacity.Value; app.W.Refresh2(); };
        opacity.MouseUp += (o, e) => S.Save();
        var sizeLabel = Ui.Lbl("Size");
        sizeLabel.Margin = new Padding(0, Th.S(7), Th.S(8), 0);
        var opLabel = Ui.Lbl("Opacity");
        opLabel.Margin = new Padding(Th.S(16), Th.S(7), Th.S(8), 0);
        page.Add(Ui.Row(sizeLabel, size, opLabel, opacity));

        page.Title("Colours");
        var theme = Ui.Combo(new object[] { "Dark", "Light", "System" }, S.Theme, v => { S.Theme = (string)v; S.Save(); Restyle(); }, 120);
        page.Add(theme);
        var swatches = new List<Control>();
        foreach (var hex in new[] { "#4C9DFF", "#C8FF3D", "#8B7CF6", "#E5689A", "#F08A3C", "#3DB88B", "#E2B93B", "#9AA3B2" })
        {
            var h = hex;
            var chip = new Panel { Width = Th.S(30), Height = Th.S(30), BackColor = ColorTranslator.FromHtml(h), Cursor = Cursors.Hand, Margin = new Padding(0, Th.S(4), Th.S(6), 0), Tag = "natural" };
            if (S.Accent == h) chip.Paint += (o, e) => { using (var pen = new Pen(Th.Fg, Th.S(2))) e.Graphics.DrawRectangle(pen, 1, 1, chip.Width - 3, chip.Height - 3); };
            chip.Click += (o, e) => { S.Accent = h; S.Save(); Restyle(); };
            swatches.Add(chip);
        }
        page.Add(Ui.Row(swatches.ToArray()));

        page.Title("Where it appears");
        var everywhere = page.Add(new Toggle("Show on every app, not only AI chats", S.ShowEverywhere));
        everywhere.Changed += () => { S.ShowEverywhere = everywhere.Checked; S.Save(); };
        page.Field("AI websites, one per line");
        var sites = page.Add(Ui.Box(string.Join("\r\n", S.Sites), 7));
        sites.TextChanged += (o, e) => S.Sites = sites.Lines.Select(l => l.Trim()).Where(l => l != "").ToList();
        sites.Leave += (o, e) => S.Save();
        return Header("Appearance", "How the widget looks and where it sits.", page);
    }

    class StyleCard : Control
    {
        readonly Bitmap sample;
        readonly bool selected;
        readonly string name, tip;
        bool hover;

        public StyleCard(Settings s, string style, bool isSelected, Action click)
        {
            sample = Widget.Sample(s, style);
            selected = isSelected;
            name = Widget.StyleNames[style];
            tip = Widget.StyleTips[style];
            Size = new Size(Th.S(240), Th.S(160));
            Margin = new Padding(0, 0, Th.S(10), Th.S(10));
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            Click += (o, e) => click();
            MouseEnter += (o, e) => { hover = true; Invalidate(); };
            MouseLeave += (o, e) => { hover = false; Invalidate(); };
        }

        protected override void Dispose(bool d) { if (d) sample.Dispose(); base.Dispose(d); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Back(Parent));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Widget.Rounded(r, Th.S(8)))
            using (var b = new SolidBrush(hover ? Th.Hover : Th.Side))
            using (var p = new Pen(selected ? Th.Accent : Th.Border, selected ? 2 : 1))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            int area = Height - Th.S(62);
            float scale = Math.Min(1f, Math.Min((Width - Th.S(16)) / (float)sample.Width, (area - Th.S(8)) / (float)sample.Height));
            int w = (int)(sample.Width * scale), h = (int)(sample.Height * scale);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(sample, (Width - w) / 2, (area - h) / 2 + Th.S(2), w, h);
            TextRenderer.DrawText(g, name, Th.F(9.5f, FontStyle.Bold), new Rectangle(Th.S(10), area, Width - Th.S(20), Th.S(22)), Th.Fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, tip, Th.F(8.5f), new Rectangle(Th.S(10), area + Th.S(22), Width - Th.S(20), Th.S(36)), Th.Dim, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    // ---------- Privacy & System ----------

    Control SystemPage()
    {
        var page = new Stack();
        page.Title("Privacy shield");
        var labels = new Dictionary<string, string> { { "Mask", "Hide private details automatically" }, { "Warn", "Ask me first, then hide them" }, { "Off", "Off" } };
        var guard = Ui.Combo(labels.Values.Cast<object>(), labels[S.Guard == "Warn" || S.Guard == "Off" ? S.Guard : "Mask"], v =>
        {
            S.Guard = labels.First(k => k.Value == (string)v).Key;
            S.Save();
        }, 300);
        page.Add(guard);
        page.Hint("Before anything leaves your PC, emails, phone numbers, API keys, passwords, card numbers, PAN and Aadhaar numbers are swapped for placeholders such as [EMAIL_1]. The real values are put back in the result, so the AI never sees them.");

        page.Title("History");
        var hist = page.Add(new Toggle("Save my rewrites on this PC", S.HistoryOn));
        hist.Changed += () => { S.HistoryOn = hist.Checked; S.Save(); };
        var days = Ui.Combo(new object[] { "30 days", "90 days", "1 year" }, S.HistoryDays == 30 ? "30 days" : S.HistoryDays == 365 ? "1 year" : "90 days", v =>
        {
            var t = (string)v;
            S.HistoryDays = t == "30 days" ? 30 : t == "1 year" ? 365 : 90;
            S.Save();
        }, 110);
        var keep = Ui.Lbl("Keep for");
        keep.Margin = new Padding(0, Th.S(7), Th.S(8), 0);
        page.Add(Ui.Row(keep, days, Ui.Btn("Clear all history", () =>
        {
            if (MessageBox.Show(this, "Delete all saved history? This can't be undone.", "PromptPilot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) History.Clear();
        })));

        page.Title("Behaviour");
        var startup = page.Add(new Toggle("Start with Windows", App.StartupEnabled()));
        startup.Changed += () => App.SetStartup(startup.Checked);
        var selection = page.Add(new Toggle("If text is selected, rewrite only the selection", S.SelectionFirst));
        selection.Changed += () => { S.SelectionFirst = selection.Checked; S.Save(); };
        var warm = page.Add(new Toggle("Prepare the connection when an AI chat opens (faster first click)", S.WarmUp));
        warm.Changed += () => { S.WarmUp = warm.Checked; S.Save(); };
        var cache = page.Add(new Toggle("Reuse the result when the same text is rewritten again", S.Cache));
        cache.Changed += () => { S.Cache = cache.Checked; S.Save(); };

        page.Title("Other hotkeys");
        var warn = Ui.Lbl("", Th.F(9f), Th.Bad);
        Action<string, string, Action<string>> hk = (label, value, set) =>
        {
            var box = HotkeyBox(value);
            box.Width = Th.S(200);
            box.Tag = "natural";
            box.TextChanged += (o, e) =>
            {
                set(box.Text);
                S.Save();
                var failed = app.RegisterHotkeys();
                warn.Text = failed.Count == 0 ? "" : "Already used by another program: " + string.Join(", ", failed);
            };
            var l = Ui.Lbl(label);
            l.AutoSize = false;
            l.Size = new Size(Th.S(170), Th.S(24));
            l.Tag = "natural";
            page.Add(Ui.Row(l, box));
        };
        hk("Undo", S.HkUndo, v => S.HkUndo = v);
        hk("Templates palette", S.HkPalette, v => S.HkPalette = v);
        hk("Open Studio", S.HkStudio, v => S.HkStudio = v);
        hk("Turn the bar on or off", S.HkToggle, v => S.HkToggle = v);
        page.Add(warn);

        page.Title("Backup");
        page.Add(Ui.Row(
            Ui.Btn("Export settings", () =>
            {
                using (var d = new SaveFileDialog { FileName = "PromptPilot-backup.json", Filter = "JSON|*.json" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        var copy = Settings.FromJson<Settings>(Settings.ToJson(S));
                        foreach (var p in copy.Providers) p.KeyEnc = null; // keys only decrypt on this Windows account anyway
                        File.WriteAllText(d.FileName, Settings.ToJson(copy));
                    }
            }),
            Ui.Btn("Import templates and modes", () =>
            {
                using (var d = new OpenFileDialog { Filter = "JSON|*.json" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        try
                        {
                            var other = Settings.FromJson<Settings>(File.ReadAllText(d.FileName));
                            if (other.Templates != null) foreach (var t in other.Templates) if (!S.Templates.Any(x => x.Name == t.Name)) S.Templates.Add(t);
                            if (other.Modes != null) foreach (var m in other.Modes) if (!S.Modes.Any(x => x.Id == m.Id)) S.Modes.Add(m);
                            if (other.Projects != null) foreach (var pr in other.Projects) if (!S.Projects.Any(x => x.Name == pr.Name)) S.Projects.Add(pr);
                            S.Save();
                            app.RegisterHotkeys();
                        }
                        catch (Exception ex) { MessageBox.Show(this, "That file couldn't be read: " + ex.Message, "PromptPilot"); }
                    }
            }),
            Ui.Btn("Open data folder", () => { try { Process.Start(Settings.Dir); } catch { } })));
        page.Hint("API keys are never exported.");
        page.Title("About");
        page.Hint("PromptPilot 2.0 by Navas Shereef. Free and open source under the MIT licence.");
        Action<string> open = url => { try { Process.Start(url); } catch { } };
        page.Add(Ui.Row(
            Ui.Btn("Buy me a coffee", () => open("https://buymeacoffee.com/navas1"), true),
            Ui.Btn("GitHub", () => open("https://github.com/navasshereef1/PromptPilot")),
            Ui.Btn("Report a problem", () => open("https://github.com/navasshereef1/PromptPilot/issues"))));
        return Header("Privacy & System", "", page);
    }

    // ---------- Insights ----------

    Control InsightsPage()
    {
        var all = History.Load(S.HistoryDays);
        var page = new Stack();
        var cards = new FlowLayoutPanel { AutoSize = true, Tag = "wrap", WrapContents = true, BackColor = Color.Transparent };
        Func<string, string, Control> card = (value, label) =>
        {
            var p = new Pane { Size = new Size(Th.S(190), Th.S(92)), Margin = new Padding(0, 0, Th.S(10), Th.S(10)), Padding = new Padding(Th.S(6)) };
            p.Controls.Add(new Label { Text = label, Dock = DockStyle.Bottom, Height = Th.S(28), ForeColor = Th.Dim, Font = Th.F(9f), Padding = new Padding(Th.S(8), 0, 0, 0), UseMnemonic = false, BackColor = Color.Transparent });
            p.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, ForeColor = Th.Fg, Font = Th.F(20f, FontStyle.Bold), Padding = new Padding(Th.S(4), Th.S(6), 0, 0), UseMnemonic = false, BackColor = Color.Transparent });
            return p;
        };
        var timed = all.Where(h => h.Ms > 0).ToList();
        var sorted = timed.Select(h => h.Ms).OrderBy(x => x).ToList();
        var week = DateTime.Now.AddDays(-7);
        DateTime when;
        cards.Controls.Add(card(all.Count.ToString(), "Rewrites saved"));
        cards.Controls.Add(card(all.Count(h => DateTime.TryParse(h.When, out when) && when > week).ToString(), "In the last 7 days"));
        cards.Controls.Add(card(sorted.Count == 0 ? "-" : (sorted[sorted.Count / 2] / 1000.0).ToString("0.0") + "s", "Typical speed"));
        cards.Controls.Add(card(sorted.Count == 0 ? "-" : (sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.9))] / 1000.0).ToString("0.0") + "s", "Slowest 1 in 10"));
        page.Add(cards);

        Action<string, IEnumerable<IGrouping<string, HistoryItem>>> table = (title, groups) =>
        {
            page.Title(title);
            var list = groups.OrderByDescending(g => g.Count()).ToList();
            if (list.Count == 0) { page.Hint("Nothing yet."); return; }
            foreach (var g in list)
            {
                var t = g.Where(h => h.Ms > 0).Select(h => h.Ms).ToList();
                var name = Ui.Lbl(string.IsNullOrEmpty(g.Key) ? "Other" : g.Key);
                name.AutoSize = false;
                name.Size = new Size(Th.S(170), Th.S(24));
                name.Tag = "natural";
                var count = Ui.Lbl(g.Count() + " rewrites" + (t.Count == 0 ? "" : ", average " + (t.Average() / 1000.0).ToString("0.0") + "s"), Th.F(9.5f), Th.Dim);
                page.Add(Ui.Row(name, count));
            }
        };
        table("By mode", all.GroupBy(h => h.Mode));
        table("By provider", all.GroupBy(h => h.Provider));
        table("By destination", all.GroupBy(h => h.Target));

        page.Title("This app right now");
        using (var me = Process.GetCurrentProcess())
            page.Hint("Memory " + (me.WorkingSet64 / 1048576) + " MB with this window open. It is freed when you close this window. Total CPU time used since start: " + me.TotalProcessorTime.TotalSeconds.ToString("0.0") + "s.");
        return Header("Insights", "Counted from your local history. Nothing is sent anywhere.", page);
    }

    // ---------- screenshots for testing ----------

    public static void ShotAll(App app, string dir, int width, int height, string prefix)
    {
        Directory.CreateDirectory(dir);
        Silent = true;
        var w = new MainWindow(app) { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), Size = new Size(Th.S(width), Th.S(height)) };
        w.Show();
        foreach (var p in Pages)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            w.Go(p[0]);
            Application.DoEvents();
            File.AppendAllText(Path.Combine(dir, "timing.txt"), prefix + p[0] + ": " + sw.ElapsedMilliseconds + " ms\r\n");
            using (var b = new Bitmap(w.Width, w.Height))
            {
                w.DrawToBitmap(b, new Rectangle(0, 0, w.Width, w.Height));
                b.Save(Path.Combine(dir, prefix + p[0] + ".png"));
            }
        }
        w.Close();
        foreach (var st in Widget.Styles)
            using (var b = Widget.Sample(app.S, st)) b.Save(Path.Combine(dir, prefix + "widget-" + st + ".png"));
    }
}
