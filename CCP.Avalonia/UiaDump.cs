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
            foreach (var w in opened.ToList()) { Write(outDir, "startup-" + w.GetType().Name, w); Close(w); }
            opened.Clear();

            foreach (var door in Doors)
            {
                if (!Click(shell, door)) { fails++; continue; }
                Write(outDir, "door-" + door, shell);
            }
            foreach (var btn in DialogButtons)
            {
                if (!Click(shell, btn)) continue;   // a missing door is a parity finding, not a crash
                var fresh = opened.Where(w => w != shell).ToList();
                opened.Clear();
                if (fresh.Count == 0) Write(outDir, "dialog-" + btn, shell);
                foreach (var w in fresh) { Write(outDir, "dialog-" + btn, w); Close(w); }
            }

            var doorPeers = File.ReadAllText(Path.Combine(outDir, "start.json")).Split("\"DoorHome\"").Length - 1;
            if (doorPeers == 0) { Console.Error.WriteLine("uia-dump: no DoorHome peer in the start tree"); fails++; }
            Console.WriteLine($"uia-dump: {Directory.GetFiles(outDir, "*.json").Length} views, {fails} failure(s) -> {outDir}");
            return fails == 0 ? 0 : 1;
        }

        private static bool Click(Window w, string name)
        {
            var c = w.FindControl<Control>(name);
            if (c is null || !c.IsEffectivelyVisible || c.Bounds.Width <= 0) { Console.Error.WriteLine($"uia-dump: {name} not clickable"); return false; }
            if (c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) is not { } p) return false;
            w.MouseMove(p);
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Pump();
            return true;
        }

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
            var doc = new { head = "avalonia", view = key, window = w.GetType().Name, title = w.Title ?? "", nodes };
            File.WriteAllText(Path.Combine(dir, Safe(key) + ".json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }

        internal static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
    }
}
