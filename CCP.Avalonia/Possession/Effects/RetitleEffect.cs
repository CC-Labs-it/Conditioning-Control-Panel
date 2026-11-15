// WPF: Services/Possession/Effects/RetitleEffect.cs:23-94.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

// The shell title is a binding. Animation-priority overrides keep it alive beneath the haunt.
internal sealed class RetitleEffect : PossessionEffectBase
{
    internal static readonly string[] Lines =
    {
        "still here?", "there is no exit", "i can see you reading this",
        "the timer is not the way out", "you already know how this ends"
    };

    public override string Id => "retitle";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.Zero;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost { PossessionWindow: { } window }
        && window.FindControl<TextBlock>("TxtTitleBarVersion") != null;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is IAvaloniaPossessionHost { PossessionWindow: { } window }
            && window.FindControl<TextBlock>("TxtTitleBarVersion") is { } text)
        {
            var line = Lines[ctx.Rng.Next(Lines.Length)];
            var windowLine = window.Title?.StartsWith(SandboxMarker.Prefix, StringComparison.Ordinal) == true
                ? SandboxMarker.Prefix + line : line;
            Override(window, Window.TitleProperty, windowLine);
            Override(text, TextBlock.TextProperty, line);
        }
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration) => Task.CompletedTask;
}

// WPF RetitleEffect.cs:24-89: rewrite an unbound heading until reassembly.
internal sealed class RetitleHeadingEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Title };
    private TextBlock? _text;
    private string? _original, _written;

    public override string Id => "retitleheading";
    protected override string NameId => "retitle";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.Zero;
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        target?.Role == PossessionRole.Title && target.Visual is Visual visual &&
        TypoHeadEffect.IsRewritable(TypoHeadEffect.FindText(visual), 2);

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (target?.Role != PossessionRole.Title || target.Visual is not Visual visual) return Task.CompletedTask;
        var text = TypoHeadEffect.FindText(visual);
        if (!TypoHeadEffect.IsRewritable(text, 2)) return Task.CompletedTask;
        _text = text;
        _original = text!.Text;
        _written = RetitleEffect.Lines[ctx.Rng.Next(RetitleEffect.Lines.Length)];
        if (string.Equals(_written, _original, StringComparison.OrdinalIgnoreCase))
            _written = RetitleEffect.Lines[(Array.IndexOf(RetitleEffect.Lines, _written) + 1) % RetitleEffect.Lines.Length];
        text.Text = _written;
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        if (_text != null && _original != null && _written != null &&
            string.Equals(_text.Text, _written, StringComparison.Ordinal))
            _text.Text = _original;
        _text = null;
        _original = _written = null;
        return Task.CompletedTask;
    }
}
