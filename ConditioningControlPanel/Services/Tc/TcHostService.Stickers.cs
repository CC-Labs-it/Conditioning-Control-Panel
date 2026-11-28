using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

// ============================ Sticker Sparkles ============================
//
// page -> host `achievement {id}` (CONTRACT.md section 1): a sticker went in this save's book for the first time.
// The host claims it (SPARKLES-WIRE.md, mode `sticker`), signed in only, one claim at a time through
// TcStickerQueue. The server prices it once per account per id; a credited receipt moves the wallet through the
// claim service's own adoption (the same raise-only rule and wallet credit the ending uses) and goes back to the
// page as `sparkles-receipt`. already / unknown / retired / sticker_cap / shadow / refusals post nothing and are
// logged once a window, never per sticker.
internal static partial class TcHostService
{
    private static TcStickerQueue? _stickers;
    private static bool _stickerSignedOutSaid;

    private static TcStickerQueue Stickers => _stickers ??= new TcStickerQueue(
        async claim => App.Sparkles is { } svc ? await svc.ClaimAsync(claim).ConfigureAwait(false) : null,
        wait => Task.Delay(wait),
        OnStickerSettled);

    /// <summary>Launch: a new window may offer every id again (the server keeps the once-ever rule).</summary>
    private static void ResetStickers()
    {
        _stickerSignedOutSaid = false;
        _stickers?.ResetWindow();
    }

    private static void OnAchievement(TcPageFrame f)
    {
        if (_host == null || string.IsNullOrEmpty(f.Id)) return;
        if (App.Sparkles == null || string.IsNullOrEmpty(App.UnifiedUserId))
        {
            if (!_stickerSignedOutSaid)
            {
                _stickerSignedOutSaid = true;
                App.Logger?.Information("Tc: signed out, stickers earn no Sparkles this window");
            }
            return;
        }
        Stickers.Offer(f.Id, _host);
    }

    /// <summary>Worker thread: one sticker claim settled.</summary>
    private static void OnStickerSettled(TcStickerQueue.Settled s)
    {
        if (!s.Post)
        {
            if (s.FirstQuiet)
                App.Logger?.Information("Tc: a sticker claim paid nothing ({Reason}); more like it stay unlogged this window",
                    TcStickerRules.QuietKey(s.Outcome));
            return;
        }
        App.Logger?.Information("Tc: sticker {Id} credited {Credited}", s.Id, s.Outcome?.Receipt?.Credited ?? 0);
        var owner = s.Owner as ChaosWebViewHost;
        // The same slip the ending posts (localised stamp, gentle switch, the higher of receipt and local wallet);
        // a sticker receipt names no result, so its lines are empty. Posted only to the window that earned it.
        _ = Sparkles.SparklesSlip.Deliver(Task.FromResult(s.Outcome), json =>
        {
            if (owner != null && ReferenceEquals(owner, _host)) owner.Post(new JRaw(json));
        });
    }
}
