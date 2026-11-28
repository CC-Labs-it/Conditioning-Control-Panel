using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF Services/EmiDesk/EmiOffers.cs:42,267,536. Unsupported effects stay infeasible.
internal static class EmiOffers
{
    internal static bool EffectFeasible(string? effect)
    {
        if (string.IsNullOrWhiteSpace(effect)) return false;
        var e = effect.Trim();
        var desk = EmiDeskService.Instance;
        if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
            return desk.IsTargetAvailable(e.Substring(5));
        if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
        {
            var id = e.Substring(7);
            return desk.IsTargetAvailable(id) && !EmiState.Current.Pins.Contains(id, StringComparer.OrdinalIgnoreCase);
        }
        return (e.Equals("spiral", StringComparison.OrdinalIgnoreCase) && desk.Window != null && SpiralOverlay.CanShowTimed) ||
            (e.Equals("rain", StringComparison.OrdinalIgnoreCase) && EmiGifRain.Images().Count > 0) ||
            e.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            (e.Equals("bedtime", StringComparison.OrdinalIgnoreCase) && !EmiLineEngine.BedtimeSet);
    }

    internal static void Run(string? effect, bool fromAsk)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        var e = effect.Trim();
        var desk = EmiDeskService.Instance;
        if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase)) desk.OpenTarget(e.Substring(5));
        else if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase)) desk.PinTop(e.Substring(7));
        else if (e.Equals("spiral", StringComparison.OrdinalIgnoreCase))
        {
            if (desk.Window is { } host && SpiralOverlay.CanShowTimed && !ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced())
            {
                EmiDeskWindow.GlassSpiralEffect(host);
                if (SpiralOverlay.HasTimedPayload) desk.Fire("effectFired", new { channel = "spiral", fromAsk });
            }
        }
        else if (e.Equals("rain", StringComparison.OrdinalIgnoreCase))
        {
            if (EmiGifRain.Start(desk.Window)) desk.Fire("effectFired", new { channel = "rain", fromAsk });
        }
        else if (e.Equals("bedtime", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTime.Now;
            var six = now.Date.AddHours(6);
            if (now >= six) six = six.AddDays(1);
            EmiState.Current.BedtimeUntil = six.ToUniversalTime();
            EmiState.SaveSoon();
            desk.Fire("bedtimeSet", new { fromAsk });
        }
    }
}
