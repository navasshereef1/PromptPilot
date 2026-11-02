using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Shows the rewrite as it streams in, with a word diff once complete.
class PreviewForm : ThemedForm
{
    readonly TaskCompletionSource<AiResult> done = new TaskCompletionSource<AiResult>();
    readonly Settings settings;
    readonly AiRequest request;
    readonly Func<string, string> finish;
    readonly string original;
    readonly RichTextBox view = new RichTextBox();
    readonly Label info = Ui.Lbl("", Th.F(9f), Th.Dim);
    readonly Button apply, regen;
    Toggle showDiff;
    CancellationTokenSource cts;
    AiResult result;
    bool closing;

    public PreviewForm(Settings s, string modeName, string draft, AiRequest req, Func<string, string> unmask)
    {
        settings = s; request = req; finish = unmask; original = draft;
        Text = "Preview: " + modeName;
        Size = new Size(Th.S(760), Th.S(560));
        MinimumSize = new Size(Th.S(520), Th.S(360));
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;

        view.Dock = DockStyle.Fill;
        view.BorderStyle = BorderStyle.None;
        view.BackColor = Th.Field;
        view.ForeColor = Th.Fg;
        view.Font = Th.F(10.5f);
        view.DetectUrls = false;
        view.HandleCreated += (o, e) => Ui.DarkScroll(view);

        apply = Ui.Btn("Replace in chat  (Ctrl+Enter)", () => Close2(true), true);
        regen = Ui.Btn("Try again", () => Generate(request.Temperature + 0.3));
        apply.Enabled = false;
        showDiff = new Toggle("Show changes", true);
        showDiff.Changed += Render;
        var bar = Ui.Row(apply, regen, Ui.Btn("Copy", () => { if (result != null) TextBridge.Put(CurrentText()); }), Ui.Btn("Cancel", () => Close2(false)), showDiff);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = Th.S(70), Padding = new Padding(Th.S(12), Th.S(4), Th.S(12), Th.S(4)), BackColor = Th.Bg };
        info.Dock = DockStyle.Top;
        bar.Dock = DockStyle.Bottom;
        bottom.Controls.Add(bar);
        bottom.Controls.Add(info);
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Th.S(12), Th.S(12), Th.S(12), Th.S(4)), BackColor = Th.Bg };
        holder.Controls.Add(view);
        Controls.Add(holder);
        Controls.Add(bottom);

        KeyDown += (o, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close2(false);
            if (e.KeyCode == Keys.Enter && e.Control && result != null) { e.SuppressKeyPress = true; Close2(true); }
        };
        FormClosing += (o, e) => Close2(false);
    }

    string CurrentText() { return view.ReadOnly ? finish(result.Text) : view.Text; }

    public Task<AiResult> Result()
    {
        Show();
        Activate();
        Generate(request.Temperature);
        return done.Task;
    }

    async void Generate(double temperature)
    {
        if (cts != null) cts.Cancel();
        var mine = cts = new CancellationTokenSource();
        result = null;
        apply.Enabled = false;
        view.ReadOnly = true;
        view.Clear();
        info.Text = "Writing\u2026";
        var req = new AiRequest { System = request.System, User = request.User, Temperature = Math.Min(1.0, temperature) };
        try
        {
            var r = await Ai.RunStream(settings, req, piece =>
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { if (mine == cts) view.AppendText(piece); }));
            }, mine.Token);
            if (mine != cts) return;
            result = r;
            info.Text = r.Provider + " in " + (r.Ms / 1000.0).ToString("0.0") + "s. Green is added, red is removed. Turn off Show changes to edit the text.";
            apply.Enabled = true;
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (mine == cts && !IsDisposed) info.Text = "Failed: " + e.Message;
        }
    }

    void Render()
    {
        if (result == null) return;
        var text = finish(result.Text);
        view.Clear();
        if (!showDiff.Checked)
        {
            view.ReadOnly = false;
            view.Text = text;
            return;
        }
        view.ReadOnly = true;
        foreach (var part in Diff.Words(original, text))
        {
            view.SelectionStart = view.TextLength;
            view.SelectionColor = part.Kind == 0 ? Th.Fg : part.Kind > 0 ? Th.Good : Th.Bad;
            view.SelectionFont = part.Kind < 0 ? new Font(view.Font, FontStyle.Strikeout) : view.Font;
            view.AppendText(part.Text);
        }
    }

    void Close2(bool accept)
    {
        if (closing) return;
        closing = true;
        if (cts != null) cts.Cancel();
        if (accept && result != null)
            done.TrySetResult(new AiResult { Text = CurrentText(), Provider = result.Provider, Ms = result.Ms });
        else done.TrySetResult(null);
        BeginInvoke((Action)Dispose);
    }
}

// Asks the Engineer's clarifying questions.
class InterviewForm : ThemedForm
{
    readonly TaskCompletionSource<string> done = new TaskCompletionSource<string>();
    readonly List<KeyValuePair<string, TextBox>> fields = new List<KeyValuePair<string, TextBox>>();
    bool finished;

    public InterviewForm(List<string> questions)
    {
        Text = "A few quick questions";
        Size = new Size(Th.S(560), Th.S(150 + questions.Count * 104));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        KeyPreview = true;
        var stack = new Stack();
        stack.Hint("Answer what you can. Blank answers are skipped.");
        foreach (var q in questions)
        {
            stack.Field(q);
            var box = Ui.Box("", 2);
            stack.Add(box);
            fields.Add(new KeyValuePair<string, TextBox>(q, box));
        }
        stack.Add(Ui.Row(Ui.Btn("Build prompt  (Ctrl+Enter)", () => Finish(true), true), Ui.Btn("Skip questions", () => { foreach (var f in fields) f.Value.Text = ""; Finish(true); }), Ui.Btn("Cancel", () => Finish(false))));
        Controls.Add(stack);
        KeyDown += (o, e) =>
        {
            if (e.KeyCode == Keys.Escape) Finish(false);
            if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; Finish(true); }
        };
        FormClosing += (o, e) => Finish(false);
    }

    public Task<string> Ask()
    {
        Show();
        Activate();
        if (fields.Count > 0) fields[0].Value.Focus();
        return done.Task;
    }

    void Finish(bool ok)
    {
        if (finished) return;
        finished = true;
        done.TrySetResult(ok ? string.Join("\n", fields.Where(f => f.Value.Text.Trim() != "").Select(f => "Q: " + f.Key + "\nA: " + f.Value.Text.Trim())) : null);
        BeginInvoke((Action)Dispose);
    }
}

// Fills in {{variables}} before a template is used.
class VarForm : ThemedForm
{
    public static List<string> Vars(string body)
    {
        return Regex.Matches(body ?? "", @"\{\{\s*([^{}]+?)\s*\}\}").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    public static string Fill(string body, Dictionary<string, string> values)
    {
        return Regex.Replace(body ?? "", @"\{\{\s*([^{}]+?)\s*\}\}", m => { string v; return values.TryGetValue(m.Groups[1].Value, out v) && v != "" ? v : m.Value; });
    }

    readonly Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();

    public VarForm(string name, List<string> vars, Action<Dictionary<string, string>> ok)
    {
        Text = name;
        Size = new Size(Th.S(520), Math.Min(Th.S(680), Th.S(130 + vars.Count * 92)));
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        var stack = new Stack();
        foreach (var v in vars)
        {
            stack.Field(v);
            var b = Ui.Box("", 2);
            stack.Add(b);
            boxes[v] = b;
        }
        stack.Add(Ui.Row(Ui.Btn("Use template", () =>
        {
            var values = boxes.ToDictionary(k => k.Key, k => k.Value.Text.Trim());
            Close();
            ok(values);
        }, true), Ui.Btn("Cancel", Close)));
        Controls.Add(stack);
    }
}

// Quick launcher for templates.
class PaletteForm : ThemedForm
{
    readonly App app;
    readonly TextBox search = Ui.Box();
    readonly OwnerList list = new OwnerList(true);
    readonly IntPtr returnTo;

    public PaletteForm(App a)
    {
        app = a;
        returnTo = app.LastAi;
        Text = "Templates";
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(Th.S(560), Th.S(420));
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;
        ShowInTaskbar = false;
        Padding = new Padding(Th.S(1));
        BackColor = Th.Border;

        var inner = new Panel { Dock = DockStyle.Fill, BackColor = Th.Card, Padding = new Padding(Th.S(12)) };
        search.Dock = DockStyle.Top;
        search.Font = Th.F(12f);
        list.Dock = DockStyle.Fill;
        list.Sub = o => { var t = (Template)o; return t.Category + "  \u00B7  " + Regex.Replace(t.Body, @"\s+", " "); };
        var gap = new Panel { Dock = DockStyle.Top, Height = Th.S(8), BackColor = Th.Card };
        inner.Controls.Add(list);
        inner.Controls.Add(gap);
        inner.Controls.Add(search);
        Controls.Add(inner);

        search.TextChanged += (o, e) => Reload();
        KeyDown += (o, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.Down && list.SelectedIndex < list.Items.Count - 1) { list.SelectedIndex++; e.Handled = true; }
            if (e.KeyCode == Keys.Up && list.SelectedIndex > 0) { list.SelectedIndex--; e.Handled = true; }
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Use(); }
        };
        list.DoubleClick += (o, e) => Use();
        Deactivate += (o, e) => { if (!picking) Close(); };
        Shown += (o, e) => { Activate(); search.Focus(); };
        Reload();
    }

    bool picking;

    void Reload()
    {
        var q = search.Text.Trim();
        list.Items.Clear();
        var match = app.S.Templates.OrderByDescending(t => t.Uses)
            .Where(t => q == "" || (t.Name + " " + t.Category + " " + t.Body).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        foreach (var t in match) list.Items.Add(t);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    void Use()
    {
        var t = list.SelectedItem as Template;
        if (t == null) return;
        t.Uses++;
        app.S.Save();
        var vars = VarForm.Vars(t.Body);
        picking = true;
        Hide();
        if (vars.Count == 0) { app.InsertIntoChat(t.Body); Close(); return; }
        var form = new VarForm(t.Name, vars, values => app.InsertIntoChat(VarForm.Fill(t.Body, values)));
        form.FormClosed += (o, e) => Close();
        form.Show();
        form.Activate();
    }
}

// Plain text viewer, used to show exactly what gets sent.
class TextViewForm : ThemedForm
{
    public TextViewForm(string title, string text)
    {
        Text = title;
        Size = new Size(Th.S(720), Th.S(560));
        StartPosition = FormStartPosition.CenterParent;
        var box = Ui.Box(text, 10);
        box.ReadOnly = true;
        box.Dock = DockStyle.Fill;
        box.Font = new Font("Consolas", 10f);
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Th.S(12)), BackColor = Th.Bg };
        holder.Controls.Add(box);
        Controls.Add(holder);
    }
}
