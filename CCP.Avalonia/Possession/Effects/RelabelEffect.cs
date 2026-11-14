// WPF: Services/Possession/Effects/RelabelEffect.cs:33-205.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

/// <summary>Temporarily changes a Start or Stop button caption.</summary>
internal sealed class RelabelEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] ButtonRole = { PossessionRole.Button };
    public override string Id => "relabel";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(2500);
    public override IReadOnlyList<PossessionRole> Roles => ButtonRole;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => Eligible(target?.Visual as Button, out _);

    private static bool Eligible(Button? button, out TextBlock? label)
    {
        label = null;
        if (button is not { IsEffectivelyVisible: true } || button.Bounds.Width <= 0 || button.Bounds.Height <= 0
            || PossessionTree.IsOffLimits(button) || PossessionLease.IsLeased(button)) return false;

        if (button.Content is string text)
            return IsStartOrStop(text) && !string.Equals(text, Stay(), StringComparison.OrdinalIgnoreCase);

        label = FindLabel(button, 0);
        return label != null && !string.Equals(label.Text, Stay(), StringComparison.OrdinalIgnoreCase);
    }

    // WPF searches the visual tree for the label, leaving the icon and content tree alone.
    private static TextBlock? FindLabel(Visual node, int depth)
    {
        if (depth > 12) return null;
        if (node is TextBlock label && IsStartOrStop(label.Text)) return label;
        foreach (var child in node.GetVisualChildren())
        {
            var found = FindLabel(child, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        // The charge yields to the UI thread. Recheck in case the target changed while it ran.
        if (target?.Visual is Button button && Eligible(button, out var label))
        {
            if (label != null) Override(label, TextBlock.TextProperty, Stay());
            else Override(button, ContentControl.ContentProperty, Stay());
        }
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration) => Task.CompletedTask;

    private static string Stay() => Localized("lockdown_poss_stay", "Stay");
    private static string Localized(string key, string fallback)
    {
        try { var value = Loc.Get(key); return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value; }
        catch { return fallback; }
    }

    internal static bool IsStartOrStop(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();
        return StartsWithWord(value, "start") || StartsWithWord(value, "stop")
            || StartsWithWord(value, Localized("btn_start", "start").Trim())
            || StartsWithWord(value, Localized("btn_stop", "stop").Trim());
    }

    private static bool StartsWithWord(string text, string word) => word.Length > 0
        && text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
        && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]));
}
