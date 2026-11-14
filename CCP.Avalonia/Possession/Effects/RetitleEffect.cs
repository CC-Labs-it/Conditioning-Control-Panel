// WPF: Services/Possession/Effects/RetitleEffect.cs:23-94.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class RetitleEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TitleRole = { PossessionRole.Title };
    private static readonly string[] LineKeys =
    {
        "possession_retitle_here", "possession_retitle_exit", "possession_retitle_reading",
        "possession_retitle_timer", "possession_retitle_ends"
    };
    private TextBlock? _text;
    private string? _original, _written;

    public override string Id => "retitle";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.Zero;
    public override IReadOnlyList<PossessionRole> Roles => TitleRole;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        IsRewritable(FindText(target?.Visual as Visual));

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var text = FindText(target?.Visual as Visual);
        if (!IsRewritable(text)) return Task.CompletedTask;
        _text = text!;
        _original = text!.Text;
        int index = ctx.Rng.Next(LineKeys.Length);
        var line = Loc.Get(LineKeys[index]);
        if (string.Equals(line, _original, StringComparison.OrdinalIgnoreCase))
            line = Loc.Get(LineKeys[(index + 1) % LineKeys.Length]);
        _written = line;
        text.Text = line;
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        if (_text != null && _original != null && _written != null
            && string.Equals(_text.Text, _written, StringComparison.Ordinal))
            _text.Text = _original;
        _text = null;
        _original = _written = null;
        return Task.CompletedTask;
    }

    // WPF PossessionVisual.FindTextBlock and IsRewritable, PossessionEffectBase.cs:271-310.
    private static TextBlock? FindText(Visual? root, int depth = 0)
    {
        if (root == null || depth > 12) return null;
        if (root is TextBlock text) return text;
        foreach (var child in root.GetVisualChildren())
            if (FindText(child, depth + 1) is { } found) return found;
        return null;
    }

    private static bool IsRewritable(TextBlock? text)
    {
        if (text == null || BindingOperations.GetBindingExpressionBase(text, TextBlock.TextProperty) != null)
            return false;
        if (string.IsNullOrWhiteSpace(text.Text) || text.Text.Length < 2) return false;
        var inlines = text.Inlines;
        return inlines == null || inlines.Count == 0 || (inlines.Count == 1 && inlines.First() is Run);
    }
}
