// Drives the real WPF app with FlaUI (UIA3) and writes, per view, <key>.json (the UI Automation
// control view: type, AutomationId, Name, class, IsEnabled, IsOffscreen, window-relative bounds) and
// PNGs of the default / hover / pressed / opened states. View keys and the JSON schema match
// CCP.Avalonia/UiaDump.cs so .github/scripts/uia-parity.py can diff the two heads.
//
// usage: WpfUia <ConditioningControlPanel.exe> <out-dir>
// Safety is the workflow's job (firewall block on the exe, CCP_USERDATA_DIR profile, dead proxy);
// this driver never clicks Start, never signs in and never types into a field.
using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.UIA3;

var exe = args[0];
var outDir = Directory.CreateDirectory(args[1]).FullName;
// Same lists as CCP.Avalonia/UiaDump.cs.
string[] doors = { "DoorHome", "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSocial", "DoorYou", "DoorLibrary", "DoorSettings" };
string[] dialogButtons = { "BtnNavSearch", "BtnMainHelp", "BtnInbox", "BtnTitleBarBugReport", "BtnAccountChip" };
var failures = new List<string>();

using var automation = new UIA3Automation();
var app = Application.Launch(new ProcessStartInfo(exe, "--panel") { WorkingDirectory = Path.GetDirectoryName(exe)! });
try
{
    // The splash is the first top-level window; the panel is the one that carries the nav rail.
    Window? main = null;
    var sw = Stopwatch.StartNew();
    while (main is null && sw.Elapsed < TimeSpan.FromMinutes(4))
    {
        Thread.Sleep(2000);
        if (app.HasExited) { Console.Error.WriteLine($"WPF app exited early ({app.ExitCode})"); return 1; }
        try { main = app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.FindFirstDescendant(c => c.ByAutomationId("DoorHome")) is not null); }
        catch (Exception ex) { Console.Error.WriteLine("waiting: " + ex.Message); }
    }
    if (main is null) { Console.Error.WriteLine("no panel window with DoorHome after 4 min"); Capture.Screen().ToFile(Path.Combine(outDir, "_timeout.png")); return 1; }
    Thread.Sleep(5000);   // let the startup ladder put up whatever it shows
    try { main.Patterns.Window.PatternOrDefault?.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Maximized); } catch { }
    Thread.Sleep(1000);

    Dump("start", main);
    Shot(main, "start");
    var known = new HashSet<IntPtr> { main.Properties.NativeWindowHandle.ValueOrDefault };
    foreach (var w in Others()) { Dump("startup-" + w.ClassName, w); Shot(w, "startup-" + w.ClassName); CloseWin(w); }

    foreach (var door in doors) Step("door-" + door, door, expectWindow: false);
    foreach (var btn in dialogButtons) Step("dialog-" + btn, btn, expectWindow: true);

    void Step(string key, string id, bool expectWindow)
    {
        try
        {
            main.SetForeground();
            var el = main.FindFirstDescendant(c => c.ByAutomationId(id));
            if (el is null) { failures.Add($"{key}: no element {id}"); return; }
            Shot(el, key + "-default");
            var centre = el.GetClickablePoint();
            Mouse.MoveTo(centre); Thread.Sleep(400); Shot(el, key + "-hover");
            // Pressed, then released OFF the control: a WPF Button only clicks when released over it.
            Mouse.Down(MouseButton.Left); Thread.Sleep(200); Shot(el, key + "-pressed");
            var r = main.BoundingRectangle;
            Mouse.MoveTo(new System.Drawing.Point(r.Right - 5, r.Bottom - 5)); Mouse.Up(MouseButton.Left);
            Thread.Sleep(300);
            el.Click(); Thread.Sleep(1500);
            var fresh = Others().ToList();
            if (!expectWindow || fresh.Count == 0) { Dump(key, main); Shot(main, key + "-opened"); }
            foreach (var w in fresh) { Dump(key, w); Shot(w, key + "-opened"); CloseWin(w); }
            if (expectWindow) Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);   // in-window popups
            Thread.Sleep(500);
        }
        catch (Exception ex) { failures.Add($"{key}: {ex.GetType().Name}: {ex.Message}"); }
    }

    IEnumerable<Window> Others()
    {
        foreach (var w in app.GetAllTopLevelWindows(automation))
            if (!known.Contains(w.Properties.NativeWindowHandle.ValueOrDefault)) yield return w;
    }

    void CloseWin(Window w)
    {
        known.Add(w.Properties.NativeWindowHandle.ValueOrDefault);   // one that refuses to close is not "new" next time
        try { w.Close(); } catch (Exception ex) { failures.Add($"close {w.ClassName}: {ex.Message}"); }
        Thread.Sleep(800);
    }
}
finally
{
    File.WriteAllLines(Path.Combine(outDir, "_failures.txt"), failures);
    try { app.Close(); Thread.Sleep(3000); } catch { }
    try { if (!app.HasExited) app.Kill(); } catch { }
}
Console.WriteLine($"wpf-uia: {Directory.GetFiles(outDir, "*.json").Length} views, {failures.Count} step failure(s)");
return 0;

void Shot(AutomationElement el, string name)
{
    try { Capture.Element(el).ToFile(Path.Combine(outDir, Safe(name) + ".png")); }
    catch (Exception ex) { failures.Add($"shot {name}: {ex.Message}"); }
}

void Dump(string key, Window w)
{
    var origin = w.BoundingRectangle;
    var nodes = new List<object>();
    var walker = automation.TreeWalkerFactory.GetControlViewWalker();
    void Walk(AutomationElement e, int depth)
    {
        var p = e.Properties;
        var b = p.BoundingRectangle.ValueOrDefault;
        string ct;
        try { ct = p.ControlType.ValueOrDefault.ToString(); } catch { ct = "?"; }
        nodes.Add(new
        {
            depth,
            type = ct,
            id = p.AutomationId.ValueOrDefault ?? "",
            name = p.Name.ValueOrDefault ?? "",
            cls = p.ClassName.ValueOrDefault ?? "",
            enabled = p.IsEnabled.ValueOrDefault,
            offscreen = p.IsOffscreen.ValueOrDefault,
            bounds = new[] { b.X - origin.X, b.Y - origin.Y, b.Width, b.Height },
        });
        for (var c = walker.GetFirstChild(e); c is not null; c = walker.GetNextSibling(c)) Walk(c, depth + 1);
    }
    try { Walk(w, 0); }
    catch (Exception ex) { failures.Add($"dump {key}: {ex.Message} (after {nodes.Count} nodes)"); }
    var doc = new { head = "wpf", view = key, window = w.ClassName ?? "", title = w.Title ?? "", nodes };
    File.WriteAllText(Path.Combine(outDir, Safe(key) + ".json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
}

static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
