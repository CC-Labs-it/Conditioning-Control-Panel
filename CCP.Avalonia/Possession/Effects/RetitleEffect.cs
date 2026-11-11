// WPF: Services/Possession/Effects/RetitleEffect.cs:23-94.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

// The shell title is a binding. Animation-priority overrides keep it alive beneath the haunt.
internal sealed class RetitleEffect : PossessionEffectBase
{
    private static readonly string[] Lines =
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
