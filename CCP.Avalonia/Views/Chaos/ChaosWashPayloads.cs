using System;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

/// <summary>The wash arm of WPF Services/Chaos/EffectPayloadFiring.cs:50-59.
/// Other payload kinds remain explicitly unported on this head.</summary>
internal static class ChaosWashPayloads
{
    internal static void Seed(Func<Visual?> host)
    {
        EffectPayload.Firer = payload =>
        {
            if (payload is not OverlayPayload { OverlayKind: "braindrain" } wash)
            {
                Log.Information("Chaos payload {Payload} not fired: no effect runner on this head yet", payload.DisplayName);
                return;
            }
            if (host() is not { } visual) return;
            // WPF :54-59: the ordinary chaos wash ignores strength and duration multipliers.
            // Only an explicit opacity (the dashboard glitch) uses the scaled duration.
            if (wash.BraindrainOpacity is double opacity)
            {
                var duration = (int)(wash.Scale(1500, 4500) * EffectPayload.GlobalDurationMult * wash.DurationMult);
                ChaosFlashOverlay.Show(visual, duration, opacity);
            }
            else ChaosFlashOverlay.Show(visual);
        };
    }
}
