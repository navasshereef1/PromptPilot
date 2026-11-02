using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// Makes the app visible to Windows Search and Settings > Apps, for this user only (no admin needed).
static class Install
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PromptPilot";
    static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PromptPilot.lnk"); } }

    // Cheap when nothing changed, and repairs itself if the exe was moved.
    public static void Ensure()
    {
        try
        {
            var exe = Application.ExecutablePath;
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                if (k != null && exe.Equals(k.GetValue("DisplayIcon") as string, StringComparison.OrdinalIgnoreCase) && File.Exists(Shortcut)) return;

            MakeShortcut(exe);
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "PromptPilot");
                k.SetValue("DisplayVersion", "2.0.1");
                k.SetValue("Publisher", "Navas Shereef");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", Path.GetDirectoryName(exe));
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
            if (App.StartupEnabled()) App.SetStartup(true); // keeps the entry pointing at this exe
        }
        catch (Exception e) { Settings.Log("registration failed: " + e.Message); }
    }

    static void MakeShortcut(string exe)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        var shell = Activator.CreateInstance(shellType);
        var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Shortcut });
        var t = link.GetType();
        Action<string, object> set = (name, value) => t.InvokeMember(name, BindingFlags.SetProperty, null, link, new[] { value });
        set("TargetPath", exe);
        set("WorkingDirectory", Path.GetDirectoryName(exe));
        set("IconLocation", exe + ",0");
        set("Description", "Rewrite prompts in any AI chat");
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
    }

    public static void Remove()
    {
        var answer = MessageBox.Show("Remove PromptPilot from Start and Settings, and stop it running?\n\nYour settings and history stay in:\n" + Settings.Dir + "\n\nThe program stays in:\n" + Path.GetDirectoryName(Application.ExecutablePath) + "\nDelete those two folders yourself to remove everything.",
            "Uninstall PromptPilot", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        var me = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcessesByName("PromptPilot"))
            using (p) if (p.Id != me) try { p.Kill(); } catch { }
        App.SetStartup(false);
        try { File.Delete(Shortcut); } catch (Exception e) { Settings.Log("uninstall: " + e.Message); }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
    }
}
