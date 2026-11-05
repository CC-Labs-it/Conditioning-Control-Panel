using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// Renders the real MainWindow, or any ported view, offscreen and writes a PNG.
    ///
    /// This exists because "it compiles" and "it boots" are both weaker claims than "it draws".
    /// A desktop screenshot cannot run on a CI runner with no display server; headless Skia
    /// rendering can, so this is the only form of visual proof that survives in the pipeline.
    /// It draws the same window the user sees - not a stripped-down stand-in.
    /// </summary>
    internal static class RenderProof
    {
        private static bool _setUp;
        private static readonly BindingErrorSink _bindingErrors = new();

        /// <summary>
        /// A failed binding is SILENT in Avalonia: the control keeps its default and the render
        /// exits 0. That is how a view with every binding broken (EngineRoomDrawer's provider
        /// bindings, on main) draws a header over 700px of nothing and passes. This sink counts
        /// Binding-area warnings and errors per view and prints them, so the log names the
        /// property and the path rather than leaving it to someone squinting at a PNG.
        /// </summary>
        private sealed class BindingErrorSink : ILogSink
        {
            public readonly List<string> Messages = new();
            public bool IsEnabled(LogEventLevel level, string area) =>
                area == LogArea.Binding && level >= LogEventLevel.Warning;
            public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
                => Messages.Add($"{source?.GetType().Name}: {messageTemplate}");
            public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
            {
                try { Messages.Add($"{source?.GetType().Name}: {string.Format(messageTemplate.Replace("{Property}", "{0}").Replace("{Path}", "{1}").Replace("{Error}", "{2}"), propertyValues)}"); }
                catch { Messages.Add($"{source?.GetType().Name}: {messageTemplate}"); }
            }
        }

        /// <summary>
        /// One headless platform per process. SetupWithoutStarting() throws if called twice,
        /// which is what --render-all would otherwise do for every view.
        /// </summary>
        internal static void EnsureSetUp()
        {
            if (_setUp) return;
            // Start from the app's own builder so the render uses the same Inter font the user
            // sees; a bare Configure<App>() drew every PNG in the platform fallback face, which
            // hides exactly the text-metric overruns these renders exist to show. UseHeadless
            // replaces the windowing subsystem UsePlatformDetect chose, so no display is needed.
            Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            Logger.Sink = _bindingErrors;
            _setUp = true;
        }

        public static int Run(string outPath) => Run(outPath, null);

        /// <param name="viewFactory">Optional factory for a view to host instead of the default
        /// window content, so a ported view can be rendered on its own for a side-by-side fidelity
        /// comparison against its WPF original.
        ///
        /// A FACTORY, not an instance, and that distinction is load-bearing: an instance passed as
        /// an argument is constructed before this method runs, so its XAML loads before
        /// SetupWithoutStarting() has called App.Initialize(). The view then binds against an
        /// uninitialised app - which showed up as every localized string rendering as its raw key
        /// while the same lookup succeeded in --smoke. Construct after setup, not before.</param>
        /// <summary>True once a render path (--render-view / --render-all) runs: views must then
        /// draw from in-memory state and never load-with-cleanup or save a real profile.</summary>
        internal static bool Rendering;

        public static int Run(string outPath, Func<Control>? viewFactory)
        {
            Window? window = null;
            try
            {
                Rendering = true;
                EnsureSetUp();
                _bindingErrors.Messages.Clear();

                if (viewFactory is not null)
                {
                    var view = viewFactory();
                    if (view is Window w)
                    {
                        // A ported dialog is a Window: show IT, not its content re-parented into a
                        // host. Re-parenting breaks every `$parent[Window]` binding in the dialog
                        // (they resolve to the host, whose DataContext is null) and rendered blank
                        // labels with exit code 0 - the exact class of bug this flag exists to catch.
                        window = w;
                        if (double.IsNaN(w.Width) || w.Width < 200) w.Width = 1100;
                        // The mock haptics toast keeps its WPF 240x92; every other frame is unchanged.
                        if (double.IsNaN(w.Height) || (w.Height < 200 && w is not global::ConditioningControlPanel.Avalonia.Views.Overlays.HapticMockToastWindow)) w.Height = 780;
                    }
                    else
                    {
                        // Wide enough that the shell's 196px rail plus content is not clipped.
                        window = new MainWindow { Width = 1100, Height = 780, Content = view };
                    }
                }
                else
                {
                    window = new MainWindow { Width = 880, Height = 620 };
                }
                window.Show();

                // Two passes: layout settles on the first, content is painted on the second.
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                using var frame = Scale == 1 ? window.CaptureRenderedFrame() : RenderScaled(window, Scale);
                if (frame is null)
                {
                    Console.Error.WriteLine("render produced no frame");
                    return 1;
                }

                var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                frame.Save(outPath);

                var len = new FileInfo(outPath).Length;
                var errs = _bindingErrors.Messages.Count;
                var (iconTexts, unnamed) = IconCensus(window);
                CensusTexts += iconTexts;
                CensusUnnamed += unnamed;
                Console.WriteLine($"rendered -> {outPath} ({frame.PixelSize.Width}x{frame.PixelSize.Height}, {len} bytes){(errs > 0 ? $"  [{errs} binding error(s)]" : "")}{(iconTexts + unnamed > 0 ? $"  [icons {iconTexts}/{unnamed}]" : "")}");
                foreach (var m in _bindingErrors.Messages.Distinct().Take(20))
                    Console.WriteLine("    binding: " + m);
                return len > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("render failed: " + ex);
                return 1;
            }
            finally
            {
                // --render-all runs many views in one process; a window left open by a failed
                // view would otherwise stay in the headless platform's window list.
                try { window?.Close(); } catch { /* already closed or never shown */ }
            }
        }

        internal static int CensusTexts, CensusUnnamed;

        /// <summary>Fluent icons census (oracle fluent-icons.md (e).6): visible texts that still lead with an emoji/symbol
        /// (Loc, code-set and data-driven strings the static guard cannot see; an IconGlyph that fell back to its emoji
        /// counts too), and buttons whose only content is an icon but that carry no AutomationProperties.Name.</summary>
        internal static (int IconTexts, int Unnamed) IconCensus(Visual root)
        {
            int texts = 0, unnamed = 0;
            void Walk(Visual v, ref bool icon, ref bool text)
            {
                if (!v.IsVisible) return;
                if (v is ConditioningControlPanel.Avalonia.Controls.IconGlyph g)
                {
                    icon = true;
                    if (g.IsFallback) texts++;
                    return;
                }
                if (v is TextBlock tb)
                {
                    var s = tb.Inlines is { Count: > 0 } inl
                        ? string.Concat(inl.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text))
                        : tb.Text;
                    var split = ConditioningControlPanel.Avalonia.Controls.IconText.Split(s);
                    if (split.Icons.Length > 0) { texts++; icon = true; }
                    if (split.Rest.Length > 0) text = true;
                }
                if (v is Button b)
                {
                    bool bi = false, bt = false;
                    foreach (var c in global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(b)) Walk(c, ref bi, ref bt);
                    if (bi && !bt && string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(b))) unnamed++;
                    icon |= bi; text |= bt;
                    return;
                }
                foreach (var c in global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(v)) Walk(c, ref icon, ref text);
            }
            bool i0 = false, t0 = false;
            Walk(root, ref i0, ref t0);
            return (texts, unnamed);
        }

        /// <summary>--scale N: render at N x DPI (HiDPI proof of icons and text). 1 = the live frame.</summary>
        public static double Scale { get; set; } = 1;

        private static global::Avalonia.Media.Imaging.Bitmap RenderScaled(Window window, double scale)
        {
            var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale), (int)Math.Ceiling(window.Bounds.Height * scale));
            var rtb = new global::Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            rtb.Render(window);
            return rtb;
        }

        /// <summary>Render one view, found by simple or full type name.</summary>
        public static int RunView(string typeName, string outPath)
        {
            var type = Views().FirstOrDefault(t =>
                string.Equals(t.Name, typeName, StringComparison.Ordinal) ||
                string.Equals(t.FullName, typeName, StringComparison.Ordinal));
            if (type is null)
            {
                Console.Error.WriteLine($"no view named '{typeName}'. Known views:");
                foreach (var t in Views()) Console.Error.WriteLine("  " + t.Name);
                return 2;
            }
            return Run(outPath, () => Construct(type));
        }

        /// <summary>Render every view to &lt;dir&gt;/&lt;TypeName&gt;.png. Non-zero if any fails.</summary>
        public static int RunAll(string dir)
        {
            Directory.CreateDirectory(dir);
            var failed = new List<string>();
            var views = Views().ToList();
            foreach (var t in views)
            {
                var rc = Run(Path.Combine(dir, t.Name + ".png"), () => Construct(t));
                if (rc != 0) failed.Add(t.Name);
            }
            // P122: the WPF first-light entry is capture-rig-only after ceremony retirement.
            // Render its otherwise unreachable canvas explicitly, without a new production door.
            const string firstLight = "SpiralFirstLightVisual";
            if (Run(Path.Combine(dir, firstLight + ".png"), () =>
            {
                var visual = new Controls.SpiralFirstLightVisual(global::Avalonia.Media.Color.Parse("#E879B9"));
                visual.Begin(false);
                visual.SetFrame(2.5, 2.5);
                return visual;
            }) != 0) failed.Add(firstLight);
            // Before the summary: the gate reads the LAST line as "N/N views rendered".
            Console.WriteLine($"icon-census: {CensusTexts} icon-led texts, {CensusUnnamed} unnamed icon-only buttons");
            Console.WriteLine($"{views.Count + 1 - failed.Count}/{views.Count + 1} views rendered to {dir}");
            foreach (var f in failed) Console.Error.WriteLine("  FAILED " + f);
            return failed.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Every concrete Control under the Views namespace - including ones with NO parameterless
        /// constructor, which then fail in RunAll rather than silently shrinking the proof. A dialog
        /// that needs arguments gets an `internal` render constructor with sample data (see
        /// TextEditorDialog); internal, so no production caller can ship the sample.
        /// Sorted so the --render-all output and the "known views" listing are stable.
        /// </summary>
        private static IEnumerable<Type> Views() =>
            typeof(RenderProof).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsNested
                            && typeof(Control).IsAssignableFrom(t)
                            && (t.Namespace ?? "").StartsWith("ConditioningControlPanel.Avalonia.Views", StringComparison.Ordinal))
                .OrderBy(t => t.Name, StringComparer.Ordinal);

        private static Control Construct(Type t)
        {
            var ctor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                ?? throw new InvalidOperationException($"{t.Name} has no parameterless constructor; add an internal render constructor with sample data.");
            return (Control)ctor.Invoke(null);
        }
    }
}
