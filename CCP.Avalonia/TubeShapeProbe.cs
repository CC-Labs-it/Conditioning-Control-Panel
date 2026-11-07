using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// <c>--tube-shape-probe [holdSeconds]</c>: opens a REAL avatar tube docked beside a stand-in
    /// shell on the live X server and reads back what the server holds - the tube's input shape
    /// (XShapeGetRectangles, ShapeInput) and its WM_TRANSIENT_FOR - attached, with the bubble up,
    /// with it down, detached and re-attached - and the same for the speech bubble's own window
    /// (owned by the tube, its shape only the bubble, off the shell while attached, the tube's shape
    /// untouched while it is up). Same reasoning as <see cref="X11OverlayProbe"/>:
    /// a dropped or mis-marshalled request returns success and changes nothing, so only the
    /// server's answer counts. Read-only towards everything but its own two windows, which it
    /// closes; no settings are written (no settings service is seeded). Exit 0 = PASS.
    ///
    /// <para><c>holdSeconds</c> keeps the windows open (attached, bubble up) after the checks so a
    /// host-side tool can look at them - e.g. WindowFromPoint on WSLg's RAIL windows.</para>
    /// </summary>
    internal static class TubeShapeProbe
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibXext = "libXext.so.6";
        private const int ShapeInput = 2;   // X11/extensions/shapeconst.h

        [StructLayout(LayoutKind.Sequential)]
        private struct XRectangle { public short X, Y; public ushort Width, Height; }

        [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] private static extern int XFree(IntPtr data);
        [DllImport(LibX11)] private static extern int XGetTransientForHint(IntPtr display, IntPtr window, out IntPtr owner);
        [DllImport(LibXext)] private static extern IntPtr XShapeGetRectangles(IntPtr display, IntPtr window, int kind, out int count, out int ordering);

        public static int Run(int holdSeconds)
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
            Platform.FramelessShadow.Install();   // idempotent; the app installs it in OnFrameworkInitializationCompleted
            var wm = Platform.X11Overlay.WindowManagerName;
            bool wantNoShadow = Platform.FramelessShadow.WmDrawsGhostShadow(wm, Environment.GetEnvironmentVariable("CCP_X11_FRAME_SHADOW"));
            Console.WriteLine($"window manager: {wm ?? "(none)"}; no-shadow hint expected: {wantNoShadow}");

            var shell = new Window
            {
                Title = "probe-shell", Width = 900, Height = 700, Background = Brushes.DarkSlateBlue,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(900, 150),
            };
            AvatarTubeWindow? tube = null;
            var exit = 1;
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { Console.Error.WriteLine("no X display"); return 1; }

            bool ok = true;
            void Check(bool pass, string what) { Console.WriteLine((pass ? "ok   " : "FAIL ") + what); ok &= pass; }

            void Step(Action action, Action after, int ms = 900)
            {
                action();
                DispatcherTimer.RunOnce(() =>
                {
                    try { after(); }
                    catch (Exception e) { Console.Error.WriteLine("probe threw: " + e); ok = false; Finish(); }
                }, TimeSpan.FromMilliseconds(ms));
            }

            void Finish()
            {
                exit = ok ? 0 : 1;
                Console.WriteLine(ok ? "\nPASS - the server holds the painted-pixel shape and the ownership"
                                     : "\nFAIL");
                try { tube?.Close(); shell.Close(); } catch { }
                lifetime.Shutdown();
            }

            // Server shape vs our computed rects: sampled on an 8 px grid over the whole window.
            // Returns the server's samples that take input, so two states can be compared.
            System.Collections.Generic.HashSet<int> CompareShape(string label, Window window, PixelRect[]? computed, double maxShare = 0.5)
            {
                var server = Rects(display, Xid(window));
                var ours = computed ?? Array.Empty<PixelRect>();
                int w = (int)Math.Ceiling(window.ClientSize.Width * window.RenderScaling);
                int h = (int)Math.Ceiling(window.ClientSize.Height * window.RenderScaling);
                int samples = 0, mismatches = 0;
                var inside = new System.Collections.Generic.HashSet<int>();
                for (int y = 0; y < h; y += 8)
                    for (int x = 0; x < w; x += 8)
                    {
                        samples++;
                        bool s = Hit(server, x, y), o = TubeInputShape.Hits(ours, x, y);
                        if (s) inside.Add(y * 65536 + x);
                        if (s != o) mismatches++;
                    }
                Console.WriteLine($"{label}: window {w}x{h}, server {server.Length} rects, ours {ours.Length}; " +
                                  $"{inside.Count}/{samples} samples take input");
                Check(server.Length > 0, $"{label}: the server has a non-empty input shape");
                Check(mismatches == 0, $"{label}: server shape == computed shape ({mismatches} sample mismatches)");
                Check(inside.Count < samples * maxShare, $"{label}: under {maxShare:P0} of the window takes input");
                Check(!Hit(server, 2, 2) && !Hit(server, w - 3, h - 3), $"{label}: the corners are click-through");
                return inside;
            }

            bool OwnedBy(Window window, Window? owner)
            {
                var got = IntPtr.Zero;
                bool has = XGetTransientForHint(display, Xid(window), out got) != 0 && got != IntPtr.Zero;
                return owner == null ? !has : has && got == Xid(owner);
            }

            // The bubble's own window (WPF Speech.cs:1060): mapped, owned by the tube, its own shape,
            // off the shell while attached.
            void CheckBubble(string label, bool attached)
            {
                var b = tube!.BubbleWindow;
                Check(b is { IsVisible: true }, $"{label}: the bubble's own window is up");
                if (b is not { IsVisible: true }) return;
                tube.ApplyBubbleShape();
                // The bubble fills most of its window; what must stay clear is the margin around it.
                CompareShape($"{label} (bubble window)", b, tube.BubbleInputRects, maxShare: 0.9);
                Check(OwnedBy(b, tube), $"{label}: bubble WM_TRANSIENT_FOR = the tube");
                Check(Platform.X11Overlay.HasNoFrameShadowHint(b) == wantNoShadow, $"{label}: bubble no-shadow window type = {wantNoShadow}");
                var bb = new PixelRect(b.Position, PixelSize.FromSize(b.ClientSize, b.RenderScaling));
                var sb = new PixelRect(shell.Position, PixelSize.FromSize(shell.ClientSize, shell.RenderScaling));
                if (attached) Check(!bb.Intersects(sb), $"{label}: the bubble window {bb} stays off the shell {sb}");
                Console.WriteLine($"{label}: bubble window at {b.Position}, size {b.ClientSize}, topmost {b.Topmost}");
            }

            void Say(string text) => tube!.GigglePriority(text, playSound: false, aiGenerated: false);

            void Begin()
            {
                Console.WriteLine($"shell {shell.Position} {shell.ClientSize}; tube {tube!.Position} {tube.ClientSize} scaling {tube.RenderScaling}");
                var attached = CompareShape("attached", tube, tube.InputRects);
                Check(OwnedBy(tube, shell), "attached: WM_TRANSIENT_FOR = the shell");
                Check(Platform.X11Overlay.HasNoFrameShadowHint(tube) == wantNoShadow, $"attached: tube no-shadow window type = {wantNoShadow}");
                Check(!Platform.X11Overlay.HasNoFrameShadowHint(shell), "the opaque shell keeps the WM's frame");
                Check(tube.BubbleWindow is { IsVisible: false }, "attached: the bubble window exists and is hidden while she is quiet");
                Step(() => Say("I'm right here, sweetie. Tell me what you want to see next?"), () =>
                {
                    var withBubble = CompareShape("bubble up (tube)", tube, tube.InputRects);
                    Check(withBubble.SetEquals(attached), "bubble up: the tube's own shape did not change");
                    CheckBubble("bubble up", attached: true);
                    Step(() => tube.EndSpeechBubble(), () =>
                    {
                        Check(tube.BubbleWindow is { IsVisible: false }, "bubble down: its window is hidden again");
                        Step(() => tube.Detach(), () =>
                        {
                            CompareShape("detached", tube, tube.InputRects);
                            Check(OwnedBy(tube, null), "detached: WM_TRANSIENT_FOR cleared");
                            Step(() => Say("Free at last. Where shall we go?"), () =>
                            {
                                CheckBubble("detached bubble", attached: false);
                                Step(() => { tube.EndSpeechBubble(); tube.Attach(); }, () =>
                                {
                                    CompareShape("re-attached", tube, tube.InputRects);
                                    Check(OwnedBy(tube, shell), "re-attached: WM_TRANSIENT_FOR = the shell");
                                    if (holdSeconds <= 0) { Finish(); return; }
                                    Step(() => Say("Holding still so you can look at me."), () =>
                                    {
                                        var b = tube.BubbleWindow!;
                                        Console.WriteLine($"holding {holdSeconds}s: tube at {tube.Position}, size {tube.ClientSize}; " +
                                                          $"bubble at {b.Position}, size {b.ClientSize}");
                                        Console.Out.Flush();
                                        DispatcherTimer.RunOnce(Finish, TimeSpan.FromSeconds(holdSeconds));
                                    });
                                });
                            });
                        });
                    });
                });
            }

            shell.Opened += (_, _) =>
            {
                tube = new AvatarTubeWindow(shell);
                if (tube.IsDetached) tube.Attach();
                // Under CPU rendering a cold start can take seconds before the tube really opens; the
                // first run sampled it at 0,0 with no shape computed yet. Wait for Opened and a shape.
                var started = DateTime.UtcNow;
                void WaitForShape()
                {
                    if (tube.BubbleWindow != null && tube.InputRects is { Length: > 0 }) { DispatcherTimer.RunOnce(Begin, TimeSpan.FromMilliseconds(600)); return; }
                    if (DateTime.UtcNow - started > TimeSpan.FromSeconds(20)) { Check(false, "the tube opened and computed a shape within 20 s"); Finish(); return; }
                    DispatcherTimer.RunOnce(WaitForShape, TimeSpan.FromMilliseconds(250));
                }
                tube.Show();
                WaitForShape();
            };

            shell.Show();
            lifetime.Start(Array.Empty<string>());
            XCloseDisplay(display);
            return exit;
        }

        private static bool Hit(XRectangle[] rects, int x, int y)
        {
            foreach (var r in rects)
                if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height) return true;
            return false;
        }

        private static XRectangle[] Rects(IntPtr display, IntPtr window)
        {
            var ptr = XShapeGetRectangles(display, window, ShapeInput, out var count, out _);
            if (ptr == IntPtr.Zero || count <= 0) return Array.Empty<XRectangle>();
            try
            {
                var rects = new XRectangle[count];
                for (var i = 0; i < count; i++)
                    rects[i] = Marshal.PtrToStructure<XRectangle>(ptr + i * Marshal.SizeOf<XRectangle>());
                return rects;
            }
            finally { XFree(ptr); }
        }

        private static IntPtr Xid(TopLevel window)
        {
            var handle = window.TryGetPlatformHandle();
            return handle is not null && handle.HandleDescriptor == "XID" ? handle.Handle : IntPtr.Zero;
        }
    }
}
