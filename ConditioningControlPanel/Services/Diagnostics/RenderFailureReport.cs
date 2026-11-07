using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.DXGI;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>
    /// The extra block a render-thread failure writes into crash.log (ccp-bugs #1565, #1570, #1585).
    /// UCEERR_RENDERTHREADFAILURE is process-wide: every window's HwndTarget fails the same way, so
    /// the stack never names the window or the device that broke it. #1504/#1510/#1513 were cured by
    /// a software target on the browser hosts, and the same crash still arrives from a machine with
    /// that fix, so the next report has to say which GPU, which monitors and which windows (size,
    /// DPI, transparency, render mode) were up when it died. Nothing here touches the render thread:
    /// the adapter list is read once from DXGI (no device is made) and the window walk only reads
    /// properties. Every part is best effort; a part that throws is left out.
    /// </summary>
    internal static class RenderFailureReport
    {
        /// <summary>Windows listed at most; a crash storm of overlay windows must not flood the report.</summary>
        internal const int MaxWindows = 24;

        private static string? _adapters;
        private static readonly object Gate = new();

        /// <summary>Reads the adapter list off the UI thread at startup, so the crash path only prints it.</summary>
        internal static void WarmAdapters()
        {
            try { System.Threading.Tasks.Task.Run(() => { try { Adapters(); } catch { } }); } catch { }
        }

        /// <summary>The whole block, or an empty string when nothing could be read.</summary>
        internal static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Render diagnostics:");
            Safe(sb, () => "- GPU: " + Adapters());
            // RenderCapability builds a Dispatcher + MediaContext on a thread that has none (a TASK
            // report can come from the finalizer): read it only where WPF already lives.
            Safe(sb, () => OnUiThread() ? "- WPF render tier: " + (RenderCapability.Tier >> 16).ToString(CultureInfo.InvariantCulture) : null);
            Safe(sb, () => "- Private memory: " + (System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB");
            Safe(sb, Monitors);
            Safe(sb, Windows);
            Safe(sb, UiStack);
            return sb.ToString();
        }

        private static bool OnUiThread() => Application.Current?.Dispatcher.CheckAccess() == true;

        /// <summary>The crash stack stops at the WndProc's ExceptionWrapper, so it never names the call that
        /// moved the window. The UI thread's own stack still holds those frames, down to Main. Built
        /// without file info, so no source path ever reaches the log.</summary>
        private static string? UiStack()
        {
            if (!OnUiThread()) return null;
            return "- UI stack:" + Environment.NewLine + new System.Diagnostics.StackTrace(1, false).ToString().TrimEnd();
        }

        private static void Safe(StringBuilder sb, Func<string?> part)
        {
            try
            {
                var text = part();
                if (!string.IsNullOrEmpty(text)) sb.AppendLine(text);
            }
            catch { /* a diagnostic part never breaks the crash report */ }
        }

        private static string Adapters()
        {
            lock (Gate)
            {
                if (_adapters != null) return _adapters;
                var rows = new List<string>();
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                for (uint i = 0; i < 8; i++)
                {
                    if (factory.EnumAdapters1(i, out var adapter).Failure || adapter == null) break;
                    using (adapter)
                    {
                        var d = adapter.Description1;
                        if ((d.Flags & AdapterFlags.Software) != 0) continue;
                        rows.Add(FormatAdapter(d.Description, d.VendorId, (ulong)d.DedicatedVideoMemory));
                    }
                }
                _adapters = rows.Count == 0 ? "(none listed)" : string.Join("; ", rows);
                return _adapters;
            }
        }

        internal static string FormatAdapter(string? name, uint vendorId, ulong dedicatedBytes) =>
            string.Format(CultureInfo.InvariantCulture, "{0} (vendor 0x{1:X4}, {2} MB dedicated)",
                (name ?? "?").Trim(), vendorId, dedicatedBytes / (1024 * 1024));

        private static string Monitors()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            return "- Monitors: " + string.Join("; ", screens.Select(s => string.Format(CultureInfo.InvariantCulture,
                "{0}x{1} at ({2},{3}){4}", s.Bounds.Width, s.Bounds.Height, s.Bounds.X, s.Bounds.Y, s.Primary ? " primary" : "")));
        }

        /// <summary>The app's visible windows. Only on the UI thread (WPF objects refuse other threads).</summary>
        private static string? Windows()
        {
            if (!OnUiThread()) return null;
            var app = Application.Current!;
            var rows = new List<WindowRow>();
            foreach (Window w in app.Windows)
            {
                try
                {
                    if (!w.IsVisible) continue;
                    var hwnd = new WindowInteropHelper(w).Handle;
                    var source = hwnd == IntPtr.Zero ? null : HwndSource.FromHwnd(hwnd);
                    var dpi = VisualTreeHelper.GetDpi(w).DpiScaleX;
                    rows.Add(new WindowRow(w.GetType().Name, w.ActualWidth, w.ActualHeight, dpi, w.WindowState.ToString(),
                        w.AllowsTransparency, w.Topmost, source?.CompositionTarget?.RenderMode == RenderMode.SoftwareOnly));
                }
                catch { /* one odd window never hides the rest */ }
            }
            return FormatWindows(rows);
        }

        internal readonly record struct WindowRow(string Type, double Width, double Height, double Dpi, string State,
            bool Transparent, bool Topmost, bool Software);

        /// <summary>Largest first (the monitor-sized surfaces are the suspects), capped at <see cref="MaxWindows"/>.</summary>
        internal static string FormatWindows(IReadOnlyCollection<WindowRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("- Visible windows: ").Append(rows.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var r in rows.OrderByDescending(r => r.Width * r.Height * r.Dpi * r.Dpi).Take(MaxWindows))
            {
                sb.AppendLine();
                sb.Append(string.Format(CultureInfo.InvariantCulture, "  - {0} {1:0}x{2:0} px, dpi {3:0.00}, {4}{5}{6}{7}",
                    r.Type, r.Width * r.Dpi, r.Height * r.Dpi, r.Dpi, r.State,
                    r.Transparent ? ", transparent" : "", r.Topmost ? ", topmost" : "", r.Software ? ", software" : ", hardware"));
            }
            if (rows.Count > MaxWindows)
                sb.AppendLine().Append("  - (").Append((rows.Count - MaxWindows).ToString(CultureInfo.InvariantCulture)).Append(" more)");
            return sb.ToString();
        }
    }
}
