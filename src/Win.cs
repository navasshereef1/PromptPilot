using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string list);

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static void Chord(byte key)
    {
        const byte Ctrl = 0x11; const uint Up = 2;
        keybd_event(Ctrl, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, Up, UIntPtr.Zero);
        keybd_event(Ctrl, 0, Up, UIntPtr.Zero);
    }

    public static bool ModifiersDown()
    {
        return new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    }

    public static string ProcessName(IntPtr h)
    {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        try { using (var p = Process.GetProcessById((int)pid)) return p.ProcessName.ToLowerInvariant(); }
        catch { return ""; }
    }

    public static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static void Focus(IntPtr target)
    {
        if (target == IntPtr.Zero || !IsWindow(target)) return;
        uint pid, me = GetCurrentThreadId(), fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid), other = GetWindowThreadProcessId(target, out pid);
        AttachThreadInput(me, fg, true);
        AttachThreadInput(me, other, true);
        SetForegroundWindow(target);
        AttachThreadInput(me, other, false);
        AttachThreadInput(me, fg, false);
    }

    public static void TrimMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        EmptyWorkingSet(GetCurrentProcess());
    }

    public static bool ParseHotkey(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+').Select(x => x.Trim()).ToArray();
        foreach (var p in parts.Take(parts.Length - 1))
        {
            if (p == "Ctrl") mods |= 2;
            else if (p == "Alt") mods |= 1;
            else if (p == "Shift") mods |= 4;
            else if (p == "Win") mods |= 8;
            else return false;
        }
        var key = parts[parts.Length - 1];
        if (key.Length == 1 && char.IsLetterOrDigit(key[0])) { vk = char.ToUpperInvariant(key[0]); return mods != 0; }
        Keys k;
        if (!Enum.TryParse(key, true, out k)) return false;
        vk = (uint)k;
        return mods != 0;
    }

    public static string HotkeyText(Keys data)
    {
        var key = data & Keys.KeyCode;
        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin || key == Keys.None) return null;
        var parts = new List<string>();
        if ((data & Keys.Control) != 0) parts.Add("Ctrl");
        if ((data & Keys.Alt) != 0) parts.Add("Alt");
        if ((data & Keys.Shift) != 0) parts.Add("Shift");
        if (parts.Count == 0) return null;
        var name = key >= Keys.D0 && key <= Keys.D9 ? ((char)key).ToString() : key.ToString();
        parts.Add(name);
        return string.Join("+", parts);
    }
}

static class Detect
{
    static readonly HashSet<string> Browsers = new HashSet<string> { "chrome", "msedge", "brave", "firefox", "opera", "vivaldi", "comet", "arc", "chromium" };
    static readonly HashSet<string> Apps = new HashSet<string> { "chatgpt", "claude" };

    public static bool IsBrowser(string proc) { return Browsers.Contains(proc); }
    public static bool IsApp(string proc) { return Apps.Contains(proc); }

    public static bool IsAiUrl(string url, IEnumerable<string> sites)
    {
        var u = Regex.Replace((url ?? "").Trim(), @"^(https?://)?(www\.)?", "", RegexOptions.IgnoreCase);
        foreach (var raw in sites)
        {
            var site = raw.Trim();
            if (site == "") continue;
            if (u.StartsWith(site, StringComparison.OrdinalIgnoreCase) && (u.Length == site.Length || "/:?#".IndexOf(u[site.Length]) >= 0)) return true;
        }
        return false;
    }

    public static bool IsAiTitle(string title) { return Regex.IsMatch(title ?? "", @"\b(ChatGPT|Claude|Gemini|Perplexity|Copilot|DeepSeek|Grok)\b"); }

    public static string TargetName(string proc, string url, string title)
    {
        var s = (url ?? "") + " " + (title ?? "") + " " + (proc ?? "");
        if (Regex.IsMatch(s, @"claude", RegexOptions.IgnoreCase)) return "Claude";
        if (Regex.IsMatch(s, @"chatgpt|chat\.openai", RegexOptions.IgnoreCase)) return "ChatGPT";
        if (Regex.IsMatch(s, @"gemini|aistudio", RegexOptions.IgnoreCase)) return "Gemini";
        return "";
    }

    // The address bar element is cached per window, since finding it is the slow part.
    static IntPtr cachedWindow;
    static AutomationElement cachedBox;

    public static string BrowserUrl(IntPtr hwnd)
    {
        if (hwnd != cachedWindow || cachedBox == null)
        {
            cachedBox = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            cachedWindow = hwnd;
        }
        if (cachedBox == null) return null;
        try
        {
            object pattern;
            return cachedBox.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) ? ((ValuePattern)pattern).Current.Value : null;
        }
        catch (ElementNotAvailableException) { cachedBox = null; return null; }
    }

    // In a browser, Ctrl+A outside a text box grabs the whole page or the address bar,
    // so only known text box types pass. Chrome reports role=textbox as Edit and a bare
    // contenteditable as Group. An error counts as "not a text box": nothing gets sent.
    public static bool FocusIsTextBox()
    {
        try
        {
            var c = AutomationElement.FocusedElement.Current;
            // Chrome builds its accessibility tree on the first request and answers Pane
            // until it is ready, so that answer means "ask again", not "no text box".
            for (int i = 0; i < 6 && c.ControlType == ControlType.Pane; i++)
            {
                System.Threading.Thread.Sleep(120);
                c = AutomationElement.FocusedElement.Current;
            }
            bool ok = (c.ControlType == ControlType.Edit || c.ControlType == ControlType.Group)
                && (c.ClassName ?? "").IndexOf("Omnibox", StringComparison.OrdinalIgnoreCase) < 0
                && c.Name != "Address and search bar";
            if (!ok) Settings.Log("not a text box: " + c.ControlType.ProgrammaticName + " class=" + c.ClassName);
            return ok;
        }
        catch (Exception e)
        {
            Settings.Log("focus check failed: " + e.Message);
            return false;
        }
    }

    // Reads the conversation on the page. Only runs on click, and only when the user turned it on.
    public static string ReadChatTail(int max)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var e = AutomationElement.FocusedElement;
            AutomationElement doc = null;
            for (int i = 0; e != null && i < 60; i++)
            {
                if (e.Current.ControlType == ControlType.Document) { doc = e; break; }
                e = walker.GetParent(e);
            }
            if (doc == null) return "";
            object pattern;
            string text = "";
            if (doc.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) text = ((TextPattern)pattern).DocumentRange.GetText(-1);
            if (string.IsNullOrWhiteSpace(text))
            {
                var parts = doc.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                var sb = new StringBuilder();
                foreach (AutomationElement t in parts) sb.AppendLine(t.Current.Name);
                text = sb.ToString();
            }
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"(\r?\n\s*){3,}", "\n\n").Trim();
            return text.Length > max ? text.Substring(text.Length - max) : text;
        }
        catch (Exception e)
        {
            Settings.Log("chat read failed: " + e.Message);
            return "";
        }
    }
}

// Event driven: Windows calls us when something changes, so idle costs nothing.
class WindowWatcher : IDisposable
{
    const uint ForegroundEv = 3, MinimizeEndEv = 0x17, LocationEv = 0x800B, NameEv = 0x800C;
    readonly Native.WinEventProc proc;
    readonly IntPtr fgHook;
    readonly Action<IntPtr> onChange, onMove;
    IntPtr nameHook, moveHook;
    uint hookedPid;
    bool wantMoves;
    public IntPtr Current { get; private set; }

    public WindowWatcher(Action<IntPtr> changed, Action<IntPtr> moved)
    {
        onChange = changed;
        onMove = moved;
        proc = OnEvent;
        fgHook = Native.SetWinEventHook(ForegroundEv, ForegroundEv, IntPtr.Zero, proc, 0, 0, 0);
    }

    public void TrackMoves(bool on)
    {
        wantMoves = on;
        hookedPid = 0;
        Rehook(Native.GetForegroundWindow());
    }

    public void Start()
    {
        var h = Native.GetForegroundWindow();
        Rehook(h);
        Current = h;
        onChange(h);
    }

    void Rehook(IntPtr hwnd)
    {
        uint pid;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == hookedPid) return;
        Unhook(ref nameHook);
        Unhook(ref moveHook);
        hookedPid = pid;
        if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return;
        // Only the active app is watched, so other programs cost nothing.
        nameHook = Native.SetWinEventHook(NameEv, NameEv, IntPtr.Zero, proc, pid, 0, 0);
        if (wantMoves) moveHook = Native.SetWinEventHook(LocationEv, LocationEv, IntPtr.Zero, proc, pid, 0, 0);
    }

    static void Unhook(ref IntPtr h) { if (h != IntPtr.Zero) { Native.UnhookWinEvent(h); h = IntPtr.Zero; } }

    void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (ev == ForegroundEv)
        {
            Rehook(hwnd);
            Current = hwnd;
            onChange(hwnd);
        }
        else if (obj == 0 && child == 0 && hwnd == Current)
        {
            if (ev == NameEv) onChange(hwnd);
            else if (ev == LocationEv) onMove(hwnd);
        }
    }

    public void Dispose()
    {
        Unhook(ref nameHook);
        Unhook(ref moveHook);
        Native.UnhookWinEvent(fgHook);
    }
}

// Clipboard-based reading and pasting, which works in every editor.
static class TextBridge
{
    public class Read { public string Text = ""; public bool Selection; }

    public static async Task WaitForModifiers()
    {
        // A held Ctrl+Alt from a hotkey would corrupt our Ctrl+A / C / V.
        for (int i = 0; i < 40 && Native.ModifiersDown(); i++) await Task.Delay(25);
    }

    static string TryText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (ExternalException) { return null; }
    }

    static void Clear()
    {
        for (int i = 0; i < 4; i++)
        {
            try { Clipboard.Clear(); return; }
            catch (ExternalException) { System.Threading.Thread.Sleep(30); }
        }
    }

    static async Task<string> Poll(int tries)
    {
        for (int i = 0; i < tries; i++)
        {
            await Task.Delay(40);
            var t = TryText();
            if (t != null) return t;
        }
        return null;
    }

    public static async Task<Read> ReadBox(bool trySelection)
    {
        Clear();
        if (trySelection)
        {
            Native.Chord(0x43); // C
            var sel = await Poll(3);
            if (!string.IsNullOrWhiteSpace(sel)) return new Read { Text = sel, Selection = true };
            Clear();
        }
        Native.Chord(0x41); // A
        Native.Chord(0x43); // C
        return new Read { Text = await Poll(16) ?? "" };
    }

    public static async Task<string> Reread(bool selection)
    {
        Clear();
        if (!selection) Native.Chord(0x41);
        Native.Chord(0x43);
        return await Poll(16) ?? "";
    }

    public static DataObject Save()
    {
        var copy = new DataObject();
        try
        {
            var src = Clipboard.GetDataObject();
            if (src != null)
                foreach (var f in src.GetFormats(false))
                    try { var v = src.GetData(f, false); if (v != null) copy.SetData(f, false, v); }
                    catch { }
        }
        catch { }
        return copy;
    }

    public static void Restore(DataObject saved)
    {
        try
        {
            if (saved.GetFormats().Length == 0) Clear();
            else Clipboard.SetDataObject(saved, true, 5, 100);
        }
        catch (Exception e) { Settings.Log("clipboard restore failed: " + e.Message); }
    }

    public static void Put(string text) { Clipboard.SetDataObject(text, true, 5, 100); }

    // The target app reads the clipboard asynchronously, so hold it briefly after Ctrl+V.
    public static async Task Paste(string text)
    {
        Put(text);
        Native.Chord(0x56); // V
        await Task.Delay(300);
    }
}
