// WPF: Services/Possession/Effects/RelabelEffect.cs:33-205.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

/// <summary>Temporarily changes an unbound, plain Start or Stop button caption.</summary>
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

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => Eligible(target?.Visual as Button);

    private static bool Eligible(Button? button) => button is { IsEffectivelyVisible: true, Content: string text }
        && button.Bounds.Width > 0 && button.Bounds.Height > 0
        && !PossessionTree.IsOffLimits(button) && !PossessionLease.IsLeased(button)
        && BindingOperations.GetBindingExpressionBase(button, ContentControl.ContentProperty) == null
        && IsStartOrStop(text) && !string.Equals(text, Stay(), StringComparison.OrdinalIgnoreCase);

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        // The charge yields to the UI thread. Recheck in case the target changed while it ran.
        if (target?.Visual is Button button && Eligible(button))
            Override(button, ContentControl.ContentProperty, Stay());
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
