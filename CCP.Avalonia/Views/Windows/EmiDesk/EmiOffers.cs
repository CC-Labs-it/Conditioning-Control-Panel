using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Avalonia.Tours;
using Serilog;

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
        if (e.StartsWith("tour:", StringComparison.OrdinalIgnoreCase))
            return TourFeasible(e.Substring(5));
        return (e.Equals("rain", StringComparison.OrdinalIgnoreCase) && EmiGifRain.Images().Count > 0) ||
            e.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            (e.Equals("bedtime", StringComparison.OrdinalIgnoreCase) && !EmiLineEngine.BedtimeSet);
    }

    // WPF EmiOffers.cs:114-183: reject a dead chip, then spend the knock before starting.
    private static string? TourName(string verb) => verb.Trim().ToLowerInvariant() switch
    {
        "shortwalk" => EmiKnockMachine.ShortWalkTour,
        "upgrade" => EmiKnockMachine.UpgradeTour,
        _ => null
    };

    private static bool TourFeasible(string verb)
    {
        try
        {
            var tour = TourName(verb);
            return tour != null && TutorialHeadHooks.Shell != null && !CoreSession.IsSessionRunning
                && App.Sessions?.IsRunning != true && !TutorialHead.Service.IsActive
                && TutorialHead.ShellStartup.IsCompleted && !EmiState.HasTourDone(tour);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour offer probe failed"); return false; }
    }

    internal static void Run(string? effect, bool fromAsk)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        var e = effect.Trim();
        var desk = EmiDeskService.Instance;
        if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase)) desk.OpenTarget(e.Substring(5));
        else if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase)) desk.PinTop(e.Substring(7));
        else if (e.StartsWith("tour:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var verb = e.Substring(5);
                if (!TourFeasible(verb)) return;
                var tour = TourName(verb)!;
                EmiState.NoteKnockAnswered();
                TutorialHead.StartOnShell(Enum.Parse<TutorialType>(tour));
                EmiTargets.Moment("effectFired", new { channel = "tour", fromAsk });
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] tour offer failed"); }
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
