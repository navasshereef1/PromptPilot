using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

class App
{
    public readonly Settings S;
    public Widget W;
    readonly NotifyIcon tray = new NotifyIcon();
    WindowWatcher watcher;
    MainWindow main;
    Icon iconOn, iconOff;

    public IntPtr Foreground = IntPtr.Zero;
    public IntPtr LastAi = IntPtr.Zero;
    public string LastTarget = "";
    string lastTitle, lastProc = "", lastUrl = "";
    int detectGen;
    readonly Dictionary<string, string> urlCache = new Dictionary<string, string>();
    static readonly int MyPid = Process.GetCurrentProcess().Id;

    bool busy;
    CancellationTokenSource cts;
    string undoText;
    bool undoNative;
    IntPtr undoTarget;
    string clipOnce;
    DateTime lastWarm = DateTime.MinValue;
    readonly Dictionary<string, AiResult> cache = new Dictionary<string, AiResult>();

    public bool CanUndo { get { return undoText != null; } }
    public string ProjectChip { get { var p = CurrentProject(); return p == null ? null : p.Name; } }

    public App(Settings s)
    {
        S = s;
        Th.Apply(S);
        W = new Widget(this);
        var small = SystemInformation.SmallIconSize.Width;
        iconOn = Th.AppIcon("app.ico", small);
        iconOff = Th.AppIcon("app-off.ico", small);
        BuildTray();
        var taken = RegisterHotkeys();
        if (taken.Count > 0) Notify("Another program already uses " + string.Join(", ", taken) + ". Change it under Modes or Privacy & System.");
        watcher = new WindowWatcher(OnWindow, OnMoved);
        watcher.TrackMoves(S.Placement == "FollowWindow");
        watcher.Start();
        if (!Ai.HasProvider(S)) OpenMain("Providers");
        TrimSoon(4000);
    }

    int trimGen;

    // Gives memory back once things go quiet. It pages back in quickly when next needed.
    public async void TrimSoon(int ms = 20000)
    {
        int gen = ++trimGen;
        await Task.Delay(ms);
        if (gen == trimGen && !busy && main == null) Native.TrimMemory();
    }

    public void Shutdown()
    {
        tray.Visible = false;
        watcher.Dispose();
        Application.Exit();
    }

    // ---------- window detection ----------

    void OnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        uint pid;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == (uint)MyPid) { OwnWindowFront(); return; }
        var title = Native.Title(hwnd);
        if (hwnd == Foreground && title == lastTitle) return;
        bool sameWindow = hwnd == Foreground;
        Foreground = hwnd;
        lastTitle = title;
        int gen = ++detectGen;
        if (!sameWindow) lastProc = Native.ProcessName(hwnd);
        var proc = lastProc;

        if (!S.Enabled) { SetActive(false, ""); return; }
        if (Detect.IsApp(proc)) { SetActive(true, Detect.TargetName(proc, "", title)); return; }
        if (!Detect.IsBrowser(proc))
        {
            if (S.ShowEverywhere) SetActive(true, ""); else SetActive(false, "");
            return;
        }

        string known;
        if (urlCache.TryGetValue(hwnd + "|" + title, out known))
        {
            lastUrl = known;
            bool hit = Detect.IsAiUrl(known, S.Sites);
            SetActive(hit || S.ShowEverywhere, hit ? Detect.TargetName(proc, known, title) : "");
            return;
        }

        // Reading the address bar can take a moment, so it stays off the UI thread.
        Task.Run(() =>
        {
            string url = null;
            bool match;
            try
            {
                url = Detect.BrowserUrl(hwnd);
                match = url != null ? Detect.IsAiUrl(url, S.Sites) : Detect.IsAiTitle(title);
            }
            catch (Exception) { match = Detect.IsAiTitle(title); }
            if (!W.IsHandleCreated) return;
            W.BeginInvoke((Action)(() =>
            {
                if (url != null)
                {
                    if (urlCache.Count >= 100) urlCache.Clear();
                    urlCache[hwnd + "|" + title] = url;
                }
                if (gen != detectGen) return;
                lastUrl = url ?? "";
                SetActive(match || S.ShowEverywhere, match ? Detect.TargetName(proc, url, title) : "");
            }));
        });
    }

    // The widget stays out of the way of our own window, except where it is being restyled.
    public async void OwnWindowFront(bool certain = false)
    {
        if (busy) return;
        if (!certain)
        {
            // Windows can report a closing window of ours after the next app is already in
            // front, and the front window can lag the report slightly, so look again shortly.
            await Task.Delay(120);
            if (busy) return;
            var front = Native.GetForegroundWindow();
            uint pid;
            Native.GetWindowThreadProcessId(front, out pid);
            if (front != IntPtr.Zero && pid != (uint)MyPid) { OnWindow(front); return; }
        }
        detectGen++; // any address bar lookup still running is out of date
        Foreground = IntPtr.Zero;
        W.SetActive(main != null && !main.IsDisposed && main.Page == "Appearance");
    }

    void OnMoved(IntPtr hwnd)
    {
        if (W.Visible && S.Placement == "FollowWindow") W.Place();
    }

    void SetActive(bool on, string target)
    {
        if (on && !string.IsNullOrEmpty(target)) LastTarget = target;
        if (on && (!string.IsNullOrEmpty(target) || Detect.IsBrowser(lastProc) || Detect.IsApp(lastProc))) LastAi = Foreground;
        if (busy) return;
        W.SetActive(on);
        if (on && S.WarmUp && Ai.HasProvider(S) && (DateTime.UtcNow - lastWarm).TotalSeconds > 90)
        {
            lastWarm = DateTime.UtcNow;
            Task.Run(() => Ai.Warm(S));
        }
    }

    public Project CurrentProject()
    {
        if (S.AutoProject)
        {
            var hay = (lastTitle ?? "") + " " + (lastUrl ?? "");
            foreach (var p in S.Projects)
                foreach (var k in (p.Keywords ?? "").Split(',').Select(x => x.Trim()).Where(x => x != ""))
                    if (hay.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return p;
        }
        return S.Active();
    }

    // ---------- context ----------

    const int MaxFiles = 12000, MaxChat = 6000, MaxClip = 6000;

    public static string ProjectText(Project p)
    {
        if (p == null) return "";
        var b = new StringBuilder(p.Notes ?? "");
        int left = MaxFiles;
        foreach (var f in p.Files)
        {
            if (left <= 0) break;
            try
            {
                var t = File.ReadAllText(f);
                if (t.Length > left) t = t.Substring(0, left);
                left -= t.Length;
                b.Append("\n\n--- " + Path.GetFileName(f) + " ---\n" + t);
            }
            catch (Exception e) { Settings.Log("context file skipped: " + e.Message); }
        }
        return b.ToString();
    }

    public ContextBundle BuildContext(string target)
    {
        var p = CurrentProject();
        return new ContextBundle { About = S.AboutMe ?? "", ProjectName = p == null ? "" : p.Name, ProjectText = ProjectText(p), Target = target ?? "" };
    }

    async Task<string> ReadChat()
    {
        var read = Task.Run(() => Detect.ReadChatTail(MaxChat));
        // A slow page must not hold up the rewrite.
        return await Task.WhenAny(read, Task.Delay(1500)) == read ? read.Result : "";
    }

    static MaskResult MaskAll(ContextBundle c, string draft)
    {
        var m = Guard.Mask(c.About); c.About = m.Text;
        Guard.Mask(c.ProjectText, m); c.ProjectText = m.Text;
        Guard.Mask(c.Chat, m); c.Chat = m.Text;
        Guard.Mask(c.Clipboard, m); c.Clipboard = m.Text;
        Guard.Mask(c.Answers, m); c.Answers = m.Text;
        Guard.Mask(draft, m);
        return m;
    }

    // ---------- the rewrite ----------

    public async void Run(Mode mode, bool forcePreview)
    {
        if (busy) return;
        if (!Ai.HasProvider(S)) { OpenMain("Providers"); return; }
        busy = true;
        W.ClearFlash();
        cts = new CancellationTokenSource();
        var target = Native.GetForegroundWindow();
        var targetName = LastTarget;
        var proc = Native.ProcessName(target);
        try
        {
            await TextBridge.WaitForModifiers();
            if (Detect.IsBrowser(proc) && !await Task.Run(() => Detect.FocusIsTextBox()))
            {
                W.Flash("Click in the chat box first");
                return;
            }

            // The clipboard goes back right after reading, so it stays yours during the wait.
            var saved = TextBridge.Save();
            var read = await TextBridge.ReadBox(S.SelectionFirst);
            TextBridge.Restore(saved);
            if (read.Text.Trim() == "") { W.Flash("Nothing typed yet"); return; }

            W.ShowStatus("Working\u2026", true);

            var ctx = new ContextBundle();
            if (mode.UseContext)
            {
                ctx = await Task.Run(() => BuildContext(targetName));
                if (clipOnce != null) { ctx.Clipboard = clipOnce; clipOnce = null; }
                if (S.ReadChat && Detect.IsBrowser(proc)) ctx.Chat = await ReadChat();
            }

            var mask = S.Guard == "Off" ? new MaskResult { Text = read.Text } : MaskAll(ctx, read.Text);
            var draft = mask.Text;
            if (S.Guard == "Warn" && mask.Map.Count > 0)
            {
                var answer = MessageBox.Show("This text contains " + string.Join(", ", mask.Kinds) + ".\n\nSend it to the AI with those parts hidden?\nYes hides them, No cancels.",
                    "PromptPilot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                Native.Focus(target);
                if (answer != DialogResult.Yes) { W.Flash("Cancelled"); return; }
            }

            if (S.AskFirst && mode.Id == "engineer")
            {
                var q = await Ai.Run(S, Ai.Questions(draft, ctx), cts.Token);
                var questions = Ai.ParseQuestions(q.Text);
                if (questions.Count > 0)
                {
                    var answers = await new InterviewForm(questions).Ask();
                    if (answers == null) { Native.Focus(target); W.Flash("Cancelled"); return; }
                    ctx.Answers = S.Guard == "Off" ? answers : Guard.Mask(answers, mask).Text;
                    Native.Focus(target);
                    await Task.Delay(180);
                }
            }

            var req = Ai.Build(mode, draft, ctx);
            string finalText;
            string providerName = "";
            int ms = 0;
            if (forcePreview || mode.Preview)
            {
                var form = new PreviewForm(S, mode.Name, read.Text, req, t => Guard.Unmask(t, mask));
                var res = await form.Result();
                if (res == null) { Native.Focus(target); W.Flash("Cancelled"); return; }
                finalText = res.Text; providerName = res.Provider; ms = res.Ms;
                Native.Focus(target);
                await Task.Delay(180);
            }
            else
            {
                var key = mode.Id + "\n" + req.System + "\n" + req.User;
                AiResult res;
                if (!S.Cache || !cache.TryGetValue(key, out res))
                {
                    res = await Ai.Run(S, req, cts.Token);
                    if (cache.Count >= 32) cache.Clear();
                    cache[key] = res;
                }
                finalText = Guard.Unmask(res.Text, mask);
                providerName = res.Provider; ms = res.Ms;
            }

            // Never overwrite text the user changed or moved away from while we waited.
            var saved2 = TextBridge.Save();
            bool same = Native.GetForegroundWindow() == target && (await TextBridge.Reread(read.Selection)) == read.Text;
            if (!same)
            {
                TextBridge.Put(finalText);
                W.Flash("Text changed. Result is on the clipboard: Ctrl+V");
            }
            else
            {
                await TextBridge.Paste(finalText);
                TextBridge.Restore(saved2);
                undoText = read.Text;
                undoNative = read.Selection;
                undoTarget = target;
                var done = mask.Map.Count > 0 ? "Done. " + mask.Map.Count + " private item(s) masked" : "Done in " + (ms / 1000.0).ToString("0.0") + "s";
                W.Flash(done);
            }
            if (S.HistoryOn)
                History.Add(new HistoryItem { Id = Guid.NewGuid().ToString("N"), When = DateTime.Now.ToString("s"), Mode = mode.Name, Provider = providerName, Target = targetName, In = read.Text, Out = finalText, Ms = ms });
        }
        catch (OperationCanceledException) { W.Flash("Cancelled"); }
        catch (Exception e)
        {
            Settings.Log("run failed: " + e.Message);
            W.Flash("AI failed. See the tray message");
            tray.ShowBalloonTip(8000, "PromptPilot", e.Message.Substring(0, Math.Min(240, e.Message.Length)), ToolTipIcon.Warning);
        }
        finally
        {
            busy = false;
            if (W.Status != null && W.Busy) W.ShowStatus(null, false);
            OnWindow(Native.GetForegroundWindow());
            W.CollapseIfOutside();
            TrimSoon();
        }
    }

    public async void DoUndo()
    {
        if (busy || undoText == null) return;
        busy = true;
        var saved = TextBridge.Save();
        try
        {
            await TextBridge.WaitForModifiers();
            if (Native.GetForegroundWindow() != undoTarget) { W.Flash("Go back to that chat box first"); return; }
            if (undoNative) Native.Chord(0x5A); // Z: a partial selection can only be undone by the editor
            else
            {
                Native.Chord(0x41);
                await TextBridge.Paste(undoText);
            }
            undoText = null;
            W.Flash("Restored");
        }
        catch (Exception e) { Settings.Log("undo failed: " + e.Message); }
        finally
        {
            TextBridge.Restore(saved);
            busy = false;
        }
    }

    // Puts text into the most recent AI chat, at the cursor.
    public async void InsertIntoChat(string text)
    {
        if (LastAi == IntPtr.Zero || !Native.IsWindow(LastAi))
        {
            TextBridge.Put(text);
            Notify("No AI chat found yet. The text is on your clipboard.");
            return;
        }
        Native.Focus(LastAi);
        await Task.Delay(220);
        await TextBridge.WaitForModifiers();
        var proc = Native.ProcessName(LastAi);
        if (Native.GetForegroundWindow() != LastAi || (Detect.IsBrowser(proc) && !await Task.Run(() => Detect.FocusIsTextBox())))
        {
            TextBridge.Put(text);
            W.Flash("Click in the chat box, then Ctrl+V");
            return;
        }
        var saved = TextBridge.Save();
        await TextBridge.Paste(text);
        TextBridge.Restore(saved);
    }

    public void Notify(string msg) { tray.ShowBalloonTip(5000, "PromptPilot", msg, ToolTipIcon.Info); }

    // ---------- widget and menus ----------

    public void WidgetClick(string key, Control owner, Point at, bool shift)
    {
        if (key == "status") { if (cts != null) cts.Cancel(); return; }
        if (key == "undo") { DoUndo(); return; }
        if (key == "more") { ShowMenu(owner, at); return; }
        if (key.StartsWith("mode:"))
        {
            var m = S.FindMode(key.Substring(5));
            if (m != null) Run(m, shift);
        }
    }

    public void ShowMenu(Control owner, Point at)
    {
        var menu = BuildWidgetMenu();
        W.MenuOpen = true;
        menu.Closed += (o, e) => { W.MenuOpen = false; W.CollapseIfOutside(); };
        menu.Show(owner, at);
    }

    ContextMenuStrip BuildWidgetMenu()
    {
        var menu = Ui.Menu();
        foreach (var m in S.Modes.Where(x => !x.Pinned && !x.Id.StartsWith("image")))
        {
            var mode = m;
            Ui.Item(menu, m.Name, () => Run(mode, false));
        }
        var images = S.Modes.Where(x => !x.Pinned && x.Id.StartsWith("image")).ToList();
        if (images.Count > 0)
        {
            var image = Ui.Sub("Image prompts");
            foreach (var m in images)
            {
                var mode = m;
                Ui.Item(image.DropDown, m.Name.Replace("Image: ", ""), () => Run(mode, false));
            }
            menu.Items.Add(image);
        }
        menu.Items.Add(new ToolStripSeparator());

        var project = Ui.Sub("Project");
        var current = CurrentProject();
        Ui.Item(project.DropDown, "None", () => { S.ActiveProject = ""; S.Save(); W.Refresh2(); }, current == null);
        foreach (var p in S.Projects)
        {
            var name = p.Name;
            Ui.Item(project.DropDown, name, () => { S.ActiveProject = name; S.Save(); W.Refresh2(); }, current == p);
        }
        Ui.Item(project.DropDown, "Manage context\u2026", () => OpenMain("Context"));
        menu.Items.Add(project);

        Ui.Item(menu, "Read current chat for context", () => { S.ReadChat = !S.ReadChat; S.Save(); }, S.ReadChat);
        Ui.Item(menu, "Ask me questions before Engineer", () => { S.AskFirst = !S.AskFirst; S.Save(); }, S.AskFirst);
        Ui.Item(menu, clipOnce == null ? "Attach clipboard to next rewrite" : "Clipboard attached", () =>
        {
            try { clipOnce = clipOnce == null && Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { clipOnce = null; }
            if (clipOnce != null && clipOnce.Length > MaxClip) clipOnce = clipOnce.Substring(0, MaxClip);
        }, clipOnce != null);
        menu.Items.Add(new ToolStripSeparator());
        Ui.Item(menu, "Templates\u2026", () => ShowPalette());
        Ui.Item(menu, "Open PromptPilot", () => OpenMain("Studio"));
        Ui.Item(menu, "Hide bar", () => SetEnabled(false));
        return menu;
    }

    void BuildTray()
    {
        var menu = Ui.Menu();
        menu.Opening += (o, e) => FillTray(menu);
        FillTray(menu);
        tray.ContextMenuStrip = menu;
        tray.Icon = S.Enabled ? iconOn : iconOff;
        tray.Text = "PromptPilot";
        tray.DoubleClick += (o, e) => OpenMain("Studio");
        tray.Visible = true;
    }

    void FillTray(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        Ui.Item(menu, "Open PromptPilot", () => OpenMain("Studio"));
        menu.Items.Add(new ToolStripSeparator());
        Ui.Item(menu, "Always on", () => SetEnabled(!S.Enabled), S.Enabled);
        Ui.Item(menu, "Show bar on every app", () => { S.ShowEverywhere = !S.ShowEverywhere; S.Save(); Foreground = IntPtr.Zero; OnWindow(Native.GetForegroundWindow()); }, S.ShowEverywhere);
        Ui.Item(menu, "Start with Windows", () => SetStartup(!StartupEnabled()), StartupEnabled());
        var style = Ui.Sub("Widget style");
        foreach (var name in Widget.Styles)
        {
            var st = name;
            Ui.Item(style.DropDown, Widget.StyleNames[st], () => { S.Style = st; S.Save(); ApplyLook(); }, S.Style == st);
        }
        menu.Items.Add(style);
        menu.Items.Add(new ToolStripSeparator());
        Ui.Item(menu, "Exit", Shutdown);
    }

    public void SetEnabled(bool on)
    {
        S.Enabled = on;
        S.Save();
        tray.Icon = on ? iconOn : iconOff;
        Foreground = IntPtr.Zero;
        OnWindow(Native.GetForegroundWindow());
        if (!on) W.SetActive(false);
    }

    public void ApplyLook()
    {
        Th.Apply(S);
        watcher.TrackMoves(S.Placement == "FollowWindow");
        W.Refresh2();
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("PromptPilot") != null;
    }

    public static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue("PromptPilot", "\"" + Application.ExecutablePath + "\" --background");
            else k.DeleteValue("PromptPilot", false);
        }
    }

    public void OpenMain(string page)
    {
        if (MainWindow.Silent) return; // rendering documentation images, so never take focus
        if (main == null || main.IsDisposed)
        {
            main = new MainWindow(this);
            main.Activated += (o, e) => OwnWindowFront(true);
            // The window is freed when closed, so the app stays small in the background.
            main.FormClosed += async (o, e) =>
            {
                main.Dispose();
                main = null;
                await Task.Delay(300);
                Foreground = IntPtr.Zero;
                OnWindow(Native.GetForegroundWindow());
                TrimSoon(1500);
            };
        }
        main.Go(page);
        main.TopMost = true;
        main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
        main.TopMost = false;
    }

    public void ShowPalette()
    {
        new PaletteForm(this).Show();
    }

    // ---------- hotkeys ----------

    const int HkUndo = 1, HkPalette = 2, HkStudio = 3, HkToggle = 4, HkModeBase = 100;
    readonly List<int> registered = new List<int>();

    public List<string> RegisterHotkeys()
    {
        foreach (var id in registered) Native.UnregisterHotKey(W.Handle, id);
        registered.Clear();
        var failed = new List<string>();
        Action<int, string> add = (id, text) =>
        {
            uint mods, vk;
            if (!Native.ParseHotkey(text, out mods, out vk)) return;
            if (Native.RegisterHotKey(W.Handle, id, mods | 0x4000, vk)) registered.Add(id);
            else { failed.Add(text); Settings.Log("hotkey taken: " + text); }
        };
        add(HkUndo, S.HkUndo);
        add(HkPalette, S.HkPalette);
        add(HkStudio, S.HkStudio);
        add(HkToggle, S.HkToggle);
        for (int i = 0; i < S.Modes.Count; i++) add(HkModeBase + i, S.Modes[i].Hotkey);
        return failed;
    }

    public void OnHotkey(int id)
    {
        if (id == HkStudio) { OpenMain("Studio"); return; }
        if (id == HkToggle) { SetEnabled(!S.Enabled); return; }
        if (id == HkPalette) { ShowPalette(); return; }
        if (!W.IsActive) return;
        if (id == HkUndo) { DoUndo(); return; }
        int i = id - HkModeBase;
        if (i >= 0 && i < S.Modes.Count) Run(S.Modes[i], false);
    }
}

static class Program
{
    static Settings Migrated(string json)
    {
        var s = Settings.FromJson<Settings>(json);
        typeof(Settings).GetMethod("Fill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(s, null);
        return s;
    }

    static string UpgradedGroq()
    {
        return Migrated("{\"Providers\":[{\"Name\":\"Groq\",\"Url\":\"https://g/chat/completions\",\"Model\":\"llama-3.3-70b-versatile\"}]}").Providers.First(p => p.Name == "Groq").Model;
    }

    static bool KeepsEdits()
    {
        var s = Migrated("{\"Version\":2,\"Providers\":[{\"Name\":\"Groq\",\"Url\":\"https://g/chat/completions\",\"Model\":\"mine\"}],\"Modes\":[{\"Id\":\"fix\",\"Name\":\"Fix\",\"Builtin\":true,\"Instructions\":\"my words\"}]}");
        return s.FindMode("fix").Instructions == "my words" && s.Providers.First(p => p.Name == "Groq").Model == "mine";
    }

    static int SelfTest(string outFile)
    {
        List<ScoreCheck> checks;
        int weak = Scorer.Score("write email", out checks);
        int strong = Scorer.Score("You are a career coach. I am a marketer working on my resume because I want a new job. Write 3 bullet points for my role, under 40 words each, and avoid buzzwords.", out checks);
        var sites = Defaults.Sites();
        var masked = Guard.Mask("mail me at a.b@x.com, key sk-abcdefghijklmnopqrstuv, card 4111 1111 1111 1111, not a card 1234 5678 9012 3456, phone +91 98765 43210, PAN ABCDE1234F, password: hunter2, version 1.2.3 and year 2026");
        var diff = Diff.Words("hello big world", "hello small world");
        var v1 = Settings.FromJson<Settings>("{\"Enabled\":true,\"ShowEverywhere\":false,\"X\":1803,\"Y\":386,\"Providers\":[{\"Name\":\"Gemini\",\"Url\":\"https://g/chat/completions\",\"Model\":\"my-model\",\"KeyEnc\":\"abc\"}]}");
        var fill = typeof(Settings).GetMethod("Fill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        fill.Invoke(v1, null);
        uint hm, hv;
        var ctx = new ContextBundle { About = "marketer", ProjectName = "Acme", ProjectText = "notes", Target = "Claude" };
        var req = Ai.Build(new Mode { Id = "x", Instructions = "do", UseContext = true }, "draft", ctx);
        var noCtx = Ai.Build(new Mode { Id = "y", Instructions = "do", UseContext = false }, "draft", ctx);

        var results = new Dictionary<string, bool> {
            { "fence", Ai.Clean("```text\nhello\n```") == "hello" },
            { "think", Ai.Clean("<think>x\ny</think> hello") == "hello" },
            { "tags", Ai.Clean("<user_prompt>\nhello\n</user_prompt>") == "hello" },
            { "inner code kept", Ai.Clean("use ```x``` here") == "use ```x``` here" },
            { "chatgpt", Detect.IsAiUrl("https://chatgpt.com/c/123", sites) },
            { "claude bare", Detect.IsAiUrl("claude.ai/new", sites) },
            { "gemini", Detect.IsAiUrl("gemini.google.com/app", sites) },
            { "perplexity www", Detect.IsAiUrl("https://www.perplexity.ai/", sites) },
            { "search not matched", !Detect.IsAiUrl("google.com/search?q=claude.ai", sites) },
            { "lookalike not matched", !Detect.IsAiUrl("https://claude.ai.evil.com/", sites) },
            { "plain google not matched", !Detect.IsAiUrl("https://www.google.com/", sites) },
            { "title", Detect.IsAiTitle("Plan - Claude - Google Chrome") && !Detect.IsAiTitle("Inbox - Google Chrome") },
            { "target", Detect.TargetName("chrome", "https://claude.ai/new", "") == "Claude" && Detect.TargetName("chrome", "chatgpt.com", "") == "ChatGPT" },
            { "key roundtrip", Settings.Decrypt(Settings.Encrypt("abc")) == "abc" },
            { "response parse", Ai.ParseFull("{\"choices\":[{\"message\":{\"content\":\"hi\"}}]}") == "hi" },
            { "stream parse", Ai.ParseDelta(new System.Web.Script.Serialization.JavaScriptSerializer(), "{\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}") == "hi" },
            { "questions none", Ai.ParseQuestions("NONE").Count == 0 },
            { "questions list", Ai.ParseQuestions("1. Who?\n- What?\nWhy now?\nFourth?").Count == 3 },
            { "score orders", strong > weak && strong >= 70 && Scorer.Score("", out checks) == 0 },
            { "mask email", masked.Text.Contains("[EMAIL_") && !masked.Text.Contains("a.b@x.com") },
            { "mask key", !masked.Text.Contains("sk-abcdefghij") },
            { "mask card", !masked.Text.Contains("4111 1111") },
            { "skip non card", masked.Map.Values.Count(v => v.Contains("4111")) == 1 },
            { "mask phone", !masked.Text.Contains("98765") },
            { "mask pan", !masked.Text.Contains("ABCDE1234F") },
            { "mask password", !masked.Text.Contains("hunter2") },
            { "keep version", masked.Text.Contains("1.2.3") && masked.Text.Contains("2026") },
            { "unmask restores", Guard.Unmask(masked.Text, masked).Contains("a.b@x.com") && Guard.Unmask(masked.Text, masked).Contains("hunter2") },
            { "diff", diff.Any(p => p.Kind == 1 && p.Text == "small") && diff.Any(p => p.Kind == -1 && p.Text == "big") && diff.Any(p => p.Kind == 0 && p.Text.Contains("hello")) },
            { "migrate keeps key", v1.Providers.First(p => p.Name == "Gemini").KeyEnc == "abc" && v1.Providers.First(p => p.Name == "Gemini").Model == "my-model" },
            { "migrate adds providers", v1.Providers.Any(p => p.Name == "Groq") && v1.Providers.Count == 6 },
            { "migrate position", v1.Positions["Bar"][0] == 1803 },
            { "migrate modes", v1.Modes.Count >= 8 && v1.FindMode("engineer").Pinned },
            { "icons embedded", Th.AppIcon("app.ico", 16).Width == 16 && Th.AppIcon("app-off.ico", 32).Width == 32 },
            { "upgrade fixes groq model", UpgradedGroq() == "qwen/qwen3.8-27b" },
            { "upgrade keeps later edits", KeepsEdits() },
            { "image modes exist", v1.Modes.Count(m => m.Id.StartsWith("image")) == 5 },
            { "no pressure tricks", v1.FindMode("engineer").Instructions.Contains("Never add threats") },
            { "templates have blanks", v1.Templates.Count(t => t.Category == "Technique") >= 10 && VarForm.Vars(v1.Templates.First(t => t.Name == "Pre-mortem").Body).Count == 1 },
            { "ollama off",!v1.Providers.First(p => p.Name.StartsWith("Ollama")).Usable() },
            { "hotkey parse", Native.ParseHotkey("Ctrl+Alt+1", out hm, out hv) && hm == 3 && hv == 0x31 },
            { "hotkey space", Native.ParseHotkey("Ctrl+Alt+Space", out hm, out hv) && hv == 0x20 },
            { "hotkey needs modifier", !Native.ParseHotkey("A", out hm, out hv) },
            { "context sent", req.User.Contains("<about_me>") && req.User.Contains("Project: Acme") && req.System.Contains("XML tags") },
            { "fix skips context", !noCtx.User.Contains("about_me") && !noCtx.System.Contains("XML tags") },
        };
        var failed = results.Where(c => !c.Value).Select(c => c.Key).ToList();
        File.WriteAllText(outFile, failed.Count == 0 ? "ok " + results.Count : "FAILED: " + string.Join(", ", failed));
        return failed.Count;
    }

    // Measures real speed and shows real output with saved keys. Sends only the samples below.
    static int Bench(string outFile, string provider, string models, string modes)
    {
        var s = Settings.Load();
        var p = s.Providers.First(x => x.Name == provider);
        s.Providers = new List<Provider> { p };
        var log = new StringBuilder();
        var project = s.Projects.FirstOrDefault(x => x.Name == s.ActiveProject) ?? s.Projects.FirstOrDefault();
        var ctx = new ContextBundle { About = s.AboutMe ?? "", ProjectName = project == null ? "" : project.Name, ProjectText = App.ProjectText(project), Target = "Claude" };
        foreach (var model in models.Split(','))
        {
            p.Model = model;
            foreach (var id in modes.Split(','))
            {
                var mode = s.FindMode(id);
                var draft = id.StartsWith("image") ? "poster for a training course, young woman in office, looks professional, no clutter" : id == "fix" ? "helo, i wnat make plan for my teem this week" : "write ad copy for our new batch of the course so more people join";
                try
                {
                    var one = Stopwatch.StartNew();
                    var r = Ai.Run(s, Ai.Build(mode, draft, ctx), CancellationToken.None).Result;
                    log.AppendLine("=== " + model + " / " + mode.Name + ": " + one.ElapsedMilliseconds + " ms\r\n" + r.Text + "\r\n");
                }
                catch (Exception e) { log.AppendLine("=== " + model + " / " + mode.Name + " FAILED: " + (e.InnerException ?? e).Message + "\r\n"); }
            }
        }
        File.WriteAllText(outFile, log.ToString());
        return 0;
    }

    static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    [STAThread]
    static int Main(string[] args)
    {
        var started = Stopwatch.StartNew();
        var data = Arg(args, "--data");
        if (data != null) Settings.Dir = data;
        Directory.CreateDirectory(Settings.Dir);

        // Remembers which code to prepare in advance, so startup gets faster after the first run.
        System.Runtime.ProfileOptimization.SetProfileRoot(Settings.Dir);
        System.Runtime.ProfileOptimization.StartProfile("startup.jit");

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.Expect100Continue = false;
        ServicePointManager.UseNagleAlgorithm = false;
        ServicePointManager.DefaultConnectionLimit = 8;

        if (Array.IndexOf(args, "--uninstall") >= 0)
        {
            Application.EnableVisualStyles();
            Install.Remove();
            return 0;
        }

        var selftest = Arg(args, "--selftest");
        if (selftest != null) return SelfTest(selftest);
        var bench = Arg(args, "--bench");
        if (bench != null) return Bench(bench, Arg(args, "--provider"), Arg(args, "--models"), Arg(args, "--modes"));

        bool first;
        var mutexName = "PromptPilot_" + (data == null ? "Main" : Math.Abs(data.GetHashCode()).ToString());
        using (new Mutex(true, mutexName, out first))
        {
            if (!first)
            {
                // Already running in the background, so opening it again shows its window.
                var running = Native.FindWindow(null, "PromptPilot Bar");
                if (running != IntPtr.Zero)
                {
                    Native.AllowSetForegroundWindow(-1);
                    Native.PostMessage(running, Widget.WM_OPEN, IntPtr.Zero, IntPtr.Zero);
                }
                return 0;
            }
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var shot = Arg(args, "--shot");
            MainWindow.Silent = shot != null;
            var app = new App(Settings.Load());
            if (shot != null)
            {
                MainWindow.ShotAll(app, shot, 1120, 740, "");
                MainWindow.ShotAll(app, shot, 760, 600, "narrow-");
                app.Shutdown();
                return 0;
            }
            var timing = Arg(args, "--timing");
            if (timing != null) File.WriteAllText(timing, started.ElapsedMilliseconds + " ms to ready");
            if (data == null)
            {
                Install.Ensure();
                if (Array.IndexOf(args, "--background") < 0) app.OpenMain("Studio");
            }
            Application.Run();
            GC.KeepAlive(app);
        }
        return 0;
    }
}
