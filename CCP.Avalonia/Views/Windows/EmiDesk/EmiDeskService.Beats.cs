using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed partial class EmiDeskService
{
    // WPF EmiDeskService.cs:373-415, 800-1000 and EmiCodex.cs:636-733.
    internal Func<DateTime> Now = () => DateTime.Now;
    private DateTime _outSinceUtc = DateTime.MinValue, _lastDismissUtc = DateTime.MinValue;
    private int _bedtimeSkips;
    private string _summonVia = "rail";
    private bool _summonTouring;
    private string? _pendingMuteMoment;
    private DispatcherTimer? _emptyLibraryTimer, _bookOfferTimer;

    private int MinutesOut() => _outSinceUtc == DateTime.MinValue ? 0
        : Math.Max(0, (int)(Now().ToUniversalTime() - _outSinceUtc).TotalMinutes);

    private void SummonBeats(int summons)
    {
        var now = Now();
        if (_lastDismissUtc != DateTime.MinValue)
        {
            var gone = now.ToUniversalTime() - _lastDismissUtc;
            if (gone.TotalMinutes <= 5) Fire("backSoon", new { minutes = Math.Max(0, (int)gone.TotalMinutes) });
        }
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) Fire("weekend");
        if (EmiLineEngine.BedtimeSet) Fire("bedtimeBroken", new { n = ++_bedtimeSkips });
        else _bedtimeSkips = 0;

        if (summons < 2 || EmiState.Current.CodexOpens > 0 || LineEngine.EverSpent("bookOffer")
            || EmiBookCards.All.Count == 0) return;
        _bookOfferTimer?.Stop();
        _bookOfferTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _bookOfferTimer.Tick += (_, _) => BookOfferDue();
        _bookOfferTimer.Start();
    }

    internal void BookOfferDue()
    {
        if (_bookOfferTimer == null) return;
        _bookOfferTimer.Stop();
        _bookOfferTimer = null;
        if (IsOut && EmiState.Current.CodexOpens == 0 && EmiOffers.EffectFeasible("book:open"))
            Fire("bookOffer", new { why = "newuser" });
    }

    private void ScheduleEmptyLibrary()
    {
        _emptyLibraryTimer?.Stop();
        _emptyLibraryTimer = null;
        if (!EmiOffers.LibraryIsEmpty()) return;
        _emptyLibraryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _emptyLibraryTimer.Tick += (_, _) => EmptyLibraryDue();
        _emptyLibraryTimer.Start();
    }

    internal void EmptyLibraryDue()
    {
        if (_emptyLibraryTimer == null) return;
        _emptyLibraryTimer.Stop();
        _emptyLibraryTimer = null;
        // An installed pack can fill the library between the greeting and this tick.
        if (IsOut && EmiOffers.LibraryIsEmpty()) Fire("noMediaYet");
    }

    private void CancelFollowups()
    {
        _emptyLibraryTimer?.Stop(); _emptyLibraryTimer = null;
        _bookOfferTimer?.Stop(); _bookOfferTimer = null;
        _pendingMuteMoment = null;
    }
}
