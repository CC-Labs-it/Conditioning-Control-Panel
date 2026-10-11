using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// --uia-dump &lt;dir&gt;: the Avalonia half of the WPF behaviour lane (.github/workflows/reference-render.yml).
    /// Walks the shell's automation peers headlessly for the same view keys the FlaUI driver
    /// (.github/wpf-uia) captures on Windows - start, every nav door, the dialogs reachable without an
    /// account - and writes one &lt;key&gt;.json per view in the shared schema that
    /// .github/scripts/uia-parity.py compares. Fails if the shell exposes no door peers, so a broken
    /// walk cannot pass as "Avalonia has nothing".
    /// </summary>
    internal static class UiaDump
    {
        // Same lists as .github/wpf-uia/Program.cs; a key only one head writes shows up as unpaired.
        internal static readonly string[] Doors =
            { "DoorHome", "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSocial", "DoorYou", "DoorLibrary", "DoorSettings" };
        internal static readonly string[] DialogButtons =
            { "BtnNavSearch", "BtnMainHelp", "BtnInbox", "BtnTitleBarBugReport", "BtnAccountChip" };

        public static int Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            RenderProof.EnsureSetUp();
            var opened = new List<Window>();
            Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));

            var shell = new Views.Windows.MainShellWindow { Width = 1400, Height = 900 };
            shell.Show();
            Pump();
            opened.Remove(shell);
            var fails = 0;

            Write(outDir, "start", shell);
            // Keyed by window type; uia-parity.py pairs the WPF dump (UIA sees only the title) through the shared title.
            foreach (var w in opened.ToList()) { Write(outDir, "startup-" + Label(w), w); Close(w); }
            opened.Clear();

            foreach (var door in Doors)
            {
                if (!Click(shell, door)) { fails++; continue; }
                // Same shape as the WPF driver: a window the door opened (an intro card) is its own view.
                foreach (var w in TakeOpened(opened, shell)) { Write(outDir, $"door-{door}-{Label(w)}", w); Close(w); }
                Write(outDir, "door-" + door, shell);
            }
            opened.Clear();   // nothing a door left behind may land under the first dialog key
            foreach (var btn in DialogButtons)
            {
                if (!Click(shell, btn)) continue;   // a missing door is a parity finding, not a crash
                var fresh = TakeOpened(opened, shell);
                if (fresh.Count == 0) Write(outDir, "dialog-" + btn, shell);
                foreach (var w in fresh) { Write(outDir, fresh.Count == 1 ? "dialog-" + btn : $"dialog-{btn}-{Label(w)}", w); Close(w); }
                shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");   // in-window popups, as the WPF driver
                Pump();
                // The help overlay spans the title bar (as WPF's); dismiss it the way a user does, on its backdrop.
                if (shell.FindControl<Control>("MainTutorialOverlay") is { IsVisible: true } help
                    && help.TranslatePoint(new Point(20, help.Bounds.Height / 2), shell) is { } beside) Press(shell, beside);
            }

            fails += covered;
            var doorPeers = File.ReadAllText(Path.Combine(outDir, "start.json")).Split("\"DoorHome\"").Length - 1;
            if (doorPeers == 0) { Console.Error.WriteLine("uia-dump: no DoorHome peer in the start tree"); fails++; }
            Console.WriteLine($"uia-dump: {Directory.GetFiles(outDir, "*.json").Length} views, {fails} failure(s) -> {outDir}");
            return fails == 0 ? 0 : 1;
        }

        private static int covered;

        private static bool Click(Window w, string name)
        {
            var c = w.FindControl<Control>(name);
            if (c is null || !c.IsEffectivelyVisible || c.Bounds.Width <= 0) { Console.Error.WriteLine($"uia-dump: {name} not clickable"); return false; }
            if (c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) is not { } p) return false;
            // A click that lands on something else is not a click on this control: say so instead of
            // dumping an unchanged tree as if the control did nothing. Hit testing reads the last frame.
            w.CaptureRenderedFrame();
            var hit = w.InputHitTest(p) as Visual;
            if (hit is null || (hit != c && !c.IsVisualAncestorOf(hit)))
            {
                var by = hit?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(v => !string.IsNullOrEmpty(v.Name));
                covered++;   // the harness failed to reach a control that exists: never a silent pass
                Console.Error.WriteLine($"uia-dump: {name} is covered at {p} by {hit?.GetType().Name ?? "nothing"} in {by?.Name}");
                return false;
            }
            Press(w, p);
            return true;
        }

        private static void Press(Window w, Point p)
        {
            w.CaptureRenderedFrame();   // headless input hit-tests the last rendered frame
            w.MouseMove(p);
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Pump();
        }

        private static List<Window> TakeOpened(List<Window> opened, Window shell)
        {
            var fresh = opened.Where(w => w != shell).Distinct().ToList();
            opened.Clear();
            return fresh;
        }

        private static string Label(Window w) => w.GetType().Name;

        // The tab views on screen, so the parity script can tag a door view with its own rows.
        private static List<string> VisibleTabs(Window w) =>
            w.GetVisualDescendants().OfType<Control>()
                .Where(c => c.GetType().Namespace?.EndsWith(".Views.Tabs") == true && c.IsEffectivelyVisible && c.Bounds.Width > 0)
                .Select(c => c.GetType().Name).Distinct().ToList();

        private static void Close(Window w)
        {
            try { w.Close(); } catch (Exception ex) { Console.Error.WriteLine($"uia-dump: close {w.GetType().Name}: {ex.Message}"); }
            Pump();
        }

        private static void Pump()
        {
            for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
        }

        private static void Write(string dir, string key, Window w)
        {
            var nodes = new List<object>();
            // Only control elements get a node, like the UIA control view FlaUI walks on WPF; a
            // content-only peer's children are lifted to its depth.
            void Walk(AutomationPeer p, int depth)
            {
                if (!p.IsControlElement() && depth > 0)
                {
                    foreach (var c in p.GetChildren()) Walk(c, depth);
                    return;
                }
                var b = p.GetBoundingRectangle();
                nodes.Add(new
                {
                    depth,
                    type = p.GetAutomationControlType().ToString(),
                    id = p.GetAutomationId() ?? "",
                    name = p.GetName() ?? "",
                    cls = p.GetClassName() ?? "",
                    enabled = p.IsEnabled(),
                    offscreen = p.IsOffscreen(),
                    bounds = new[] { (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height },
                });
                foreach (var c in p.GetChildren()) Walk(c, depth + 1);
            }
            Walk(ControlAutomationPeer.CreatePeerForElement(w), 0);
            var doc = new { head = "avalonia", view = key, window = w.GetType().Name, title = w.Title ?? "", tabs = VisibleTabs(w), nodes };
            File.WriteAllText(Path.Combine(dir, Safe(key) + ".json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }

        internal static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
    }
}
