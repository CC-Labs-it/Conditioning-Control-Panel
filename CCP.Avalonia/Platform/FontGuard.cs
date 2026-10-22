using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;


namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF <c>Services/UI/FontGuard.cs</c>: once at startup, resolve each system family the app names
    /// itself the way a layout pass would; a family whose probe THROWS (installed but unreadable, the
    /// v6.8.6 blank-app incident) is logged once and struck out of every chain handed out from then on,
    /// including the application <c>Font.Mono</c> resource consumers bind with DynamicResource. An absent
    /// family is not broken: a chain already walks past it. Off Windows every mono link maps to the
    /// bundled Noto Sans Mono (<see cref="AppFonts"/>), so the probe opens no system file there.
    /// </summary>
    internal static class FontGuard
    {
        /// <summary>The monospace chain (WPF FontGuard.MonoChain / App.xaml Font.Mono).</summary>
        public const string MonoChain = "Cascadia Mono, Consolas, Courier New";

        public const string MonoResourceKey = "Font.Mono";

        // WPF FontGuard.ProbedFamilies minus its icon and emoji fonts: this head names neither in any
        // chain (Fluent IconGlyph; emoji via SystemEmojiFontCollection; IconGuardTests forbids the
        // names), so there is nothing to strike them from. Segoe UI is the floor, not probed.
        private static readonly string[] ProbedFamilies = { "Cascadia Mono", "Consolas", "Courier New" };

        private static readonly HashSet<string> _broken = new(StringComparer.OrdinalIgnoreCase);
        private static bool _verified;

        public static IReadOnlyCollection<string> BrokenFamilies => _broken;

        internal static bool Verified => _verified;

        public static bool IsBroken(string? family) =>
            !string.IsNullOrWhiteSpace(family) && _broken.Contains(family!.Trim());

        /// <summary>The guarded monospace family: <see cref="MonoChain"/> minus broken links.</summary>
        public static FontFamily Mono => Family(MonoChain);

        /// <summary>A family for <paramref name="chain"/> with broken links struck out. Never throws.</summary>
        public static FontFamily Family(string chain)
        {
            try { return new FontFamily(Sanitize(chain)); }
            catch { return new FontFamily("Segoe UI"); }
        }

        public static string Sanitize(string? chain)
        {
            if (string.IsNullOrWhiteSpace(chain)) return "Segoe UI";
            if (_broken.Count == 0) return chain!;
            return FilterChain(chain!, IsBroken);
        }

        /// <summary>Pure half of <see cref="Sanitize"/> (WPF FontGuard.FilterChain). A bundled
        /// <c>avares://…#Face</c> link never matches a system family name, so it passes untouched.</summary>
        public static string FilterChain(string chain, Func<string, bool>? isBroken)
        {
            if (string.IsNullOrWhiteSpace(chain)) return "Segoe UI";
            if (isBroken == null) return chain;
            var kept = chain.Split(',').Select(l => l.Trim()).Where(l => l.Length > 0 && !isBroken(l)).ToList();
            return kept.Count > 0 ? string.Join(", ", kept) : "Segoe UI";
        }

        /// <summary>Probe once and publish <c>Font.Mono</c>. Second call is a no-op (WPF Verify).</summary>
        public static void Verify() => Verify(CanResolve);

        internal static void Verify(Func<string, string?> probe)
        {
            if (_verified) return;
            _verified = true;
            foreach (var family in ProbedFamilies)
            {
                if (probe(family) is not { } error) continue;
                _broken.Add(family);
                Serilog.Log.Warning("[FONTGUARD] Font family {Family} is installed but unreadable ({Error}) - dropping it from every font chain for this session. The app will use its fallback face. Reinstalling that font restores it.",
                    family, error);
            }
            PublishResources();
        }

        /// <summary>Test seam: forget the verdict so a probe can run again.</summary>
        internal static void Reset()
        {
            _verified = false;
            _broken.Clear();
        }

        private static void PublishResources()
        {
            try
            {
                if (Application.Current is not { } app) return;
                var mono = Sanitize(MonoChain);
                app.Resources[MonoResourceKey] = new FontFamily(mono);
                if (_broken.Count > 0) Serilog.Log.Information("[FONTGUARD] {Key} resolved to \"{Chain}\"", MonoResourceKey, mono);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("[FONTGUARD] could not publish font resources: {Msg}", ex.Message);
            }
        }

        // WPF CanResolve built a Typeface and asked for its glyph typeface (GetFirstMatchingFont opens
        // the file). Avalonia's equivalent open is FontManager.TryGetGlyphTypeface. Null = readable.
        private static string? CanResolve(string family)
        {
            try
            {
                FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(family)), out _);
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
