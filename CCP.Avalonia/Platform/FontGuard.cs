using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Services/UI/FontGuard: a present-but-unreadable face throws before a fallback
/// chain can advance. Probe once before windows exist, then stop naming every broken link.</summary>
internal sealed class FontGuard
{
    internal const string MonoChain = "Cascadia Mono, Consolas, Courier New";
    internal const string MonoResourceKey = "Font.Mono";
    internal static FontGuard Startup { get; } = new();

    private static readonly string[] ProbedFamilies =
        { "Cascadia Mono", "Consolas", "Courier New", "Segoe MDL2 Assets", "Segoe UI Emoji" };
    private readonly HashSet<string> _broken = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _probe;
    private bool _verified;

    // The small OS seam: tests inject file-open failures without corrupting an installed font.
    internal FontGuard(Action<string>? probe = null) => _probe = probe ?? Probe;
    internal IReadOnlyCollection<string> BrokenFamilies => _broken;
    internal bool IsBroken(string? family) => !string.IsNullOrWhiteSpace(family) && _broken.Contains(family.Trim());

    internal string Sanitize(string? chain) => string.IsNullOrWhiteSpace(chain) ? "Segoe UI"
        : _broken.Count == 0 ? chain : FilterChain(chain, IsBroken);

    internal static string FilterChain(string? chain, Func<string, bool> isBroken)
    {
        if (string.IsNullOrWhiteSpace(chain)) return "Segoe UI";
        var kept = chain.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && !isBroken(s)).ToArray();
        return kept.Length == 0 ? "Segoe UI" : string.Join(", ", kept);
    }

    internal void Verify(IResourceDictionary resources, bool windows)
    {
        if (_verified) return;
        _verified = true;
        // Off Windows AppFonts maps every mono link to bundled Noto Sans Mono: no installed
        // mono file is opened, so probing the Windows names there would test the wrong font.
        if (windows)
            foreach (var family in ProbedFamilies)
            {
                try { _probe(family); }
                catch (Exception ex)
                {
                    _broken.Add(family);
                    Log.Warning("[FONTGUARD] Font family {Family} is installed but unreadable ({Error}) - dropping it from every guarded font chain for this session. The app will use its fallback face. Reinstalling that font restores it.",
                        family, ex.GetType().Name);
                }
            }

        try
        {
            var mono = Sanitize(MonoChain);
            resources[MonoResourceKey] = new FontFamily(mono);
            if (_broken.Count > 0) Log.Information("[FONTGUARD] {Key} resolved to {Chain}", MonoResourceKey, mono);
        }
        catch (Exception ex) { Log.Warning("[FONTGUARD] could not publish font resources: {Error}", ex.GetType().Name); }
    }

    private static void Probe(string family)
    {
        // Typeface construction alone never opens the file. Resolving glyphs and line metrics
        // reaches the same font data a measure pass uses. An absent family is harmless, as in WPF.
        if (FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(family)), out var glyphs))
            _ = glyphs.Metrics.LineSpacing;
    }
}
