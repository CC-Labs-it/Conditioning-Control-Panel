using System;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// PORTED from ConditioningControlPanel/Services/EmiDesk: the ask gates (EmiDeskService.AskSituationOk
// :199), the effect probe and runner for the book (EmiOffers.EffectFeasible :42 / Run :267, book
// verb :195-245), and her book offer (EmiCodex.MaybeOfferSoon :656 / MaybeOffer :694, summon
// route EmiDeskService.MaybeOfferBookOnSummon :862).
// ponytail: open:/pinTop:/tour:/spiral/video/burst/rain/shrink/bedtime/packs/assets effects stay
// refused by the probe (no dead chip) until their runners exist on this head (wire-win-emi-codex-b-next).
internal sealed partial class EmiDeskService
{
    /// <summary>WPF EmiCodex.OfferDelayMs: bookOffer is a ceremony, so it must not land on the line before it.</summary>
    internal const int BookOfferDelayMs = 20_000;
    private ITimer? _offerTimer;

    /// <summary>WPF App.xaml.cs:375-376: the two ask probes the Core engine reads off the head.</summary>
    internal static void SeedEngineProbes()
    {
        EmiLineEngine.AskSituationProbe = () => Instance.AskSituationOk();
        EmiLineEngine.EffectFeasibleProbe = Instance.EffectFeasible;
    }

    /// <summary>WPF AskSituationOk: the situational half of the offer gates.</summary>
    internal bool AskSituationOk()
    {
        try
        {
            var win = _window;
            if (!IsOut || win == null || !win.IsVisible) return false;
            if (win.PresentationActive || win.InputLocked || win.Transiting || win.AskLive) return false;
            if (CoreEngine.Video?.IsPlaying == true || CoreSession.IsSessionRunning) return false;
            if (AvatarTube.AvatarTubeWindow.Live?.HasBubbleUp == true) return false;
            var main = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            return main?.WindowState != WindowState.Minimized;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ask situation probe failed");
            return false;
        }
    }

    /// <summary>WPF EmiOffers.EffectFeasible, for the effects this head can run. Unknown is refused.</summary>
    internal bool EffectFeasible(string? effect)
    {
        if (string.IsNullOrWhiteSpace(effect)) return false;
        var e = effect.Trim();
        if (e.StartsWith("book:", StringComparison.OrdinalIgnoreCase))
            // WPF BookFeasible: the one verb, the book not already open, and a book in this build.
            return string.Equals(e.Substring(5).Trim(), "open", StringComparison.OrdinalIgnoreCase)
                && _window?.Book == null && EmiBookCards.All.Count > 0;
        return string.Equals(e, "none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>WPF EmiOffers.Run, for the effects <see cref="EffectFeasible"/> admits.</summary>
    internal void RunEffect(string? effect, bool fromAsk)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        var e = effect.Trim();
        try
        {
            if (e.StartsWith("book:", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(e.Substring(5).Trim().ToLowerInvariant(), "open", StringComparison.Ordinal))
                {
                    Log.Debug("[EmiDesk] unknown book verb {Verb}, ignored", e);
                    return;
                }
                _window?.OpenBook();
                Fire("effectFired", new { channel = "book", fromAsk });
                return;
            }
            if (!string.Equals(e, "none", StringComparison.OrdinalIgnoreCase))
                Log.Debug("[EmiDesk] effect {Effect} has no runner on this head", e);
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] effect {Effect} failed", e); }
    }

    /// <summary>WPF EmiCodex.HasContent: <c>{exe}/Resources/web/codex</c> holds a bundle or chapters.</summary>
    internal static bool CodexHasContent =>
        EmiCodexRules.HasContent(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "codex"));

    /// <summary>WPF MaybeOfferBookOnSummon: from the second summon, while unread and unspent.</summary>
    private void MaybeOfferBookOnSummon(int summons)
    {
        try
        {
            if (summons < 2) return;
            if (EmiState.Current.CodexOpens > 0) return;
            if (LineEngine.EverSpent("bookOffer")) return;
            MaybeOfferSoon("newuser");
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book route failed"); }
    }

    /// <summary>WPF MaybeOfferSoon: offer one beat later; a second call re-arms rather than queues.</summary>
    internal void MaybeOfferSoon(string? why = null, int delayMs = BookOfferDelayMs)
    {
        try
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => MaybeOfferSoon(why, delayMs)); return; }
            if (!CodexHasContent) return;
            _offerTimer?.Dispose();
            ITimer? timer = null;
            timer = Clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_offerTimer, timer)) return;   // re-armed or cancelled after the tick queued
                _offerTimer?.Dispose();
                _offerTimer = null;
                MaybeOffer(why);
            }), null, TimeSpan.FromMilliseconds(Math.Max(0, delayMs)), Timeout.InfiniteTimeSpan);
            _offerTimer = timer;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiCodex] could not schedule the offer"); }
    }

    /// <summary>WPF MaybeOffer: the three brakes, then the moment. The engine owns ever/1.</summary>
    internal void MaybeOffer(string? why = null)
    {
        try
        {
            // WPF EmiCodex.IsOpen is the hosted/plain codex window, which nothing opens (no caller of
            // EmiCodex.Open on either head), so it is false here too.
            var refusal = EmiCodexRules.OfferRefusal(CodexHasContent, false, () => EmiState.Current.CodexOpens);
            if (refusal != null) { Log.Debug("[EmiCodex] no offer: {Why}", refusal); return; }
            Fire("bookOffer", new { why = why ?? "idle" });
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiCodex] offer failed"); }
    }

    /// <summary>Drop a pending offer: the widget it was meant for is going away.</summary>
    private void CancelBookOffer()
    {
        _offerTimer?.Dispose();
        _offerTimer = null;
    }
}
