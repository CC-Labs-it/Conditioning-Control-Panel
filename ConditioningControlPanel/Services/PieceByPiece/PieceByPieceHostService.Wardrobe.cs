using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// THE WARDROBE (7.2 shop, lane B3-C1): what the player's chess men wear. Frames and rules live in
/// <see cref="PbpLookRules"/>. This half does the plumbing:
/// <list type="bullet">
/// <item>the page gets the chess grants (<c>owned</c>) and the saved look (<c>pbp:look</c>) at boot, on
///   every ownership change, and again when the Prize Counter it opened closes (contract 1)</item>
/// <item>a pick from the page is clamped, saved locally first, then PUT to <c>/v2/equip</c> in the
///   background; launch pulls <c>GET /v2/equip</c> and adopts the server copy when it is newer
///   (contract 3). Every failure keeps the local copy: the route deploys after this ships.</item>
/// <item><c>open-prize-counter</c> opens the counter WINDOW over the board on the chess tab (owner
///   call, no relay). The button is on the chess menu, so no game is running to pause.</item>
/// </list>
/// </summary>
internal static partial class PieceByPieceHostService
{
    private const string EquipPath = "/v2/equip";
    private static bool _counterHooked;
    private static bool _ownershipHooked;

    private static void HookOwnership(bool on)
    {
        try
        {
            if (App.Ownership == null) return;
            App.Ownership.OwnershipChanged -= OnOwnershipChanged;
            _ownershipHooked = false;
            if (on) { App.Ownership.OwnershipChanged += OnOwnershipChanged; _ownershipHooked = true; }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private static void OnOwnershipChanged(object? sender, Prizes.OwnershipChangedEventArgs e)
    {
        if (_ownershipHooked) PostWardrobe();
    }

    private static ChessLook SavedLook() => PbpLookRules.Read(App.Settings?.Current?.ChessLook).Look;

    /// <summary>The grants frame, then the saved look clamped to them. Safe from any thread.</summary>
    private static void PostWardrobe() => RunOnUi(() =>
    {
        try
        {
            var grants = App.Ownership?.Grants;
            _host?.Post(PbpLookRules.OwnedFrame(grants));
            _host?.Post(PbpLookRules.LookFrame(PbpLookRules.Clamp(SavedLook(), grants)));
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: wardrobe post failed: {E}", ex.Message); }
    });

    /// <summary>A pick from the page: clamp, save, PUT in the background.</summary>
    private static void OnLookPicked(JObject o)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;
        var look = PbpLookRules.Clamp(PbpLookRules.Normal(o["look"]), App.Ownership?.Grants);
        var (was, _) = PbpLookRules.Read(settings.ChessLook);
        if (was.Equals(look) && !string.IsNullOrEmpty(settings.ChessLook)) return;
        SaveAndPush(settings, look);
    }

    /// <summary>Local first with the push owed, then the PUT in the background clears the flag.</summary>
    private static void SaveAndPush(Models.AppSettings settings, ChessLook look)
    {
        settings.ChessLook = PbpLookRules.Write(look, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), owed: true);
        try { App.Settings!.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _ = PutEquipAsync(look);
    }

    /// <summary>
    /// The Prize Counter's Wear on an owned chess row (set, banner, insignia): the same write a pick on
    /// the board's wardrobe makes (clamped, saved, PUT), and an open board redraws. UI thread. False
    /// when the board does not draw that id or the account does not own it.
    /// </summary>
    internal static bool WearFromCounter(string itemId)
    {
        var settings = App.Settings?.Current;
        if (settings == null || PbpLookRules.SlotOf(itemId) == null) return false;
        var grants = App.Ownership?.Grants;
        var (was, _) = PbpLookRules.Read(settings.ChessLook);
        var look = PbpLookRules.Clamp(PbpLookRules.With(was, itemId), grants);
        if (PbpLookRules.SlotOf(itemId) is { } slot && !Wears(look, slot, itemId)) return false;
        if (!was.Equals(look) || string.IsNullOrEmpty(settings.ChessLook)) SaveAndPush(settings, look);
        if (_host != null) PostWardrobe();
        App.Logger?.Information("PieceByPiece: wearing {Id} from the counter", itemId);
        return true;
    }

    private static bool Wears(ChessLook look, string slot, string id) => slot switch
    {
        "set" => look.Set == id,
        "banner" => look.Banner == id,
        _ => look.Insignia == id,
    };

    /// <summary>Contract 1: the counter window over the board, on the chess tab.</summary>
    private static void OnOpenCounter(JObject o)
    {
        var tab = PbpLookRules.CounterTabOf(o);
        RunOnUi(() =>
        {
            try
            {
                // One close hook per counter: a second press while it is up only brings it forward.
                Action<string>? hook = _counterHooked ? null : OnCounterClosed;
                _counterHooked = true;
                PrizeCounterHostService.Launch(PbpLookRules.CounterFrom, tab, hook);
            }
            catch (Exception ex) { _counterHooked = false; App.Logger?.Warning("PieceByPiece: counter open failed: {E}", ex.Message); }
        });
    }

    /// <summary>The counter went (contract 1): the page re-reads what it owns and wears. Panic and app
    /// exit never hand focus back; a board closed under the counter has nothing to hear it.</summary>
    private static void OnCounterClosed(string reason)
    {
        _counterHooked = false;
        if (_host == null) return;
        PostWardrobe();
        if (CounterHandsFocusBack(reason)) _host.FocusWeb();
    }

    /// <summary>Only a plain back (or a Wear that stays on the board) gives the board the keyboard again.</summary>
    internal static bool CounterHandsFocusBack(string reason)
        => reason != PrizeCounterHostService.ClosePanic && reason != PrizeCounterHostService.CloseAppExit
           && reason != PrizeCounterHostService.CloseFailed;

    private static void StopCounterWatch() => _counterHooked = false;

    // ============================ /v2/equip (contract 3) ============================

    private static HttpRequestMessage EquipRequest(HttpMethod method, string token, string uid, JObject? body)
    {
        var url = ProxyBaseUrl + EquipPath + (method == HttpMethod.Get ? "?unified_id=" + Uri.EscapeDataString(uid) : "");
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add(PbpNetRelay.TokenHeader, token);
        req.Headers.Add(PbpNetRelay.VersionHeader, UpdateService.AppVersion);
        if (body != null) req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return req;
    }

    /// <summary>Launch: adopt the server's chess look when it is newer, else push ours if it differs.
    /// A missing route (404), an error or no account keeps the local copy and says nothing.</summary>
    private static async Task PullEquipAsync()
    {
        var uid = App.UnifiedUserId ?? "";
        var token = SafeAuthToken();
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token)) return;
        try
        {
            using var req = EquipRequest(HttpMethod.Get, token, uid, null);
            using var res = await Http.SendAsync(req, CancellationToken.None).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { App.Logger?.Debug("PieceByPiece: equip GET {Status}, local look kept", (int)res.StatusCode); return; }
            var body = JObject.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            var (server, serverAt) = PbpLookRules.FromEquip(body);
            var (local, localAt) = PbpLookRules.Read(App.Settings?.Current?.ChessLook);
            var owed = PbpLookRules.IsOwed(App.Settings?.Current?.ChessLook);
            var (look, adopt, push) = PbpLookRules.Reconcile(local, localAt, server, serverAt, owed);
            if (adopt)
            {
                RunOnUi(() =>
                {
                    var s = App.Settings?.Current;
                    if (s == null) return;
                    s.ChessLook = PbpLookRules.Write(look, serverAt);
                    try { App.Settings!.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
                });
                PostWardrobe();
            }
            else if (push) await PutEquipAsync(look).ConfigureAwait(false);
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: equip pull failed, local look kept: {E}", ex.Message); }
    }

    private static async Task PutEquipAsync(ChessLook look)
    {
        var uid = App.UnifiedUserId ?? "";
        var token = SafeAuthToken();
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token)) return;
        try
        {
            using var req = EquipRequest(HttpMethod.Put, token, uid, PbpLookRules.EquipBody(uid, look));
            using var res = await Http.SendAsync(req, CancellationToken.None).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { App.Logger?.Debug("PieceByPiece: equip PUT {Status}, local look kept, push owed", (int)res.StatusCode); return; }
            // Landed: clear the owed flag, unless the player picked again while it was in flight.
            RunOnUi(() =>
            {
                var s = App.Settings?.Current;
                if (s == null || !PbpLookRules.IsOwed(s.ChessLook)) return;
                var (now, at) = PbpLookRules.Read(s.ChessLook);
                if (!now.Equals(look)) return;
                s.ChessLook = PbpLookRules.Write(now, at);
                try { App.Settings!.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: equip PUT failed, local look kept: {E}", ex.Message); }
    }
}
