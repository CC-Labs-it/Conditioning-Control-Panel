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
    /// with it down, detached and re-attached. Same reasoning as <see cref="X11OverlayProbe"/>:
    /// a dropped or mis-marshalled request returns success and changes nothing, so only the
    /// server's answer counts. Read-only towards everything but its own two windows, which it
    /// closes; no settings are written (no settings service is seeded). Exit 0 = PASS.
    ///
    /// <para><c>holdSeconds</c> keeps both windows open (attached, bubble up) after the checks so a
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
            void CompareShape(string label)
            {
                var xid = Xid(tube!);
                var server = Rects(display, xid);
                var ours = tube!.InputRects ?? Array.Empty<PixelRect>();
                int w = (int)Math.Ceiling(tube.ClientSize.Width * tube.RenderScaling);
                int h = (int)Math.Ceiling(tube.ClientSize.Height * tube.RenderScaling);
                int samples = 0, mismatches = 0, inside = 0;
                for (int y = 0; y < h; y += 8)
                    for (int x = 0; x < w; x += 8)
                    {
                        samples++;
                        bool s = Hit(server, x, y), o = TubeInputShape.Hits(ours, x, y);
                        if (s) inside++;
                        if (s != o) mismatches++;
                    }
                Console.WriteLine($"{label}: window {w}x{h}, server {server.Length} rects, ours {ours.Length}; " +
                                  $"{inside}/{samples} samples take input");
                Check(server.Length > 0, $"{label}: the server has a non-empty input shape");
                Check(mismatches == 0, $"{label}: server shape == computed shape ({mismatches} sample mismatches)");
                Check(inside < samples / 2, $"{label}: less than half the window takes input");
                Check(!Hit(server, 2, 2) && !Hit(server, w - 3, h - 3), $"{label}: the canvas corners are click-through");
            }

            bool Owned()
            {
                var owner = IntPtr.Zero;
                return XGetTransientForHint(display, Xid(tube!), out owner) != 0 && owner == Xid(shell);
            }

            shell.Opened += (_, _) =>
            {
                tube = new AvatarTubeWindow(shell);
                if (tube.IsDetached) tube.Attach();
                Step(() => tube.Show(), () =>
                {
                    Console.WriteLine($"shell {shell.Position} {shell.ClientSize}; tube {tube.Position} {tube.ClientSize} scaling {tube.RenderScaling}");
                    CompareShape("attached");
                    Check(Owned(), "attached: WM_TRANSIENT_FOR = the shell");
                    var bubble = tube.FindControl<Border>("SpeechBubble")!;
                    Step(() =>
                    {
                        tube.FindControl<TextBlock>("TxtSpeech")!.Text = "Could not load those pictures. Try again in a moment?";
                        bubble.IsVisible = true;
                    }, () =>
                    {
                        CompareShape("bubble up");
                        var p = bubble.TranslatePoint(new Point(4, 4), tube)!.Value * tube.RenderScaling;
                        var server = Rects(display, Xid(tube));
                        Check(Hit(server, (int)p.X, (int)p.Y), "bubble up: its corner takes input");
                        Step(() => bubble.IsVisible = false, () =>
                        {
                            CompareShape("bubble down");
                            Check(!Hit(Rects(display, Xid(tube)), (int)p.X, (int)p.Y), "bubble down: the same corner is click-through again");
                            Step(() => tube.Detach(), () =>
                            {
                                CompareShape("detached");
                                Check(!Owned(), "detached: WM_TRANSIENT_FOR cleared");
                                Step(() => tube.Attach(), () =>
                                {
                                    CompareShape("re-attached");
                                    Check(Owned(), "re-attached: WM_TRANSIENT_FOR = the shell");
                                    if (holdSeconds <= 0) { Finish(); return; }
                                    bubble.IsVisible = true;
                                    Console.WriteLine($"holding {holdSeconds}s: tube at {tube.Position}, size {tube.ClientSize}");
                                    Console.Out.Flush();
                                    DispatcherTimer.RunOnce(Finish, TimeSpan.FromSeconds(holdSeconds));
                                });
                            });
                        });
                    });
                }, 1500);
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
