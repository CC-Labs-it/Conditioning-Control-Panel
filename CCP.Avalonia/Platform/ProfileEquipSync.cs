using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.ProfileEquip;
using static ConditioningControlPanel.Services.ProfileEquip.ProfileEquipRules;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Serilog;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Services/Profile/ProfileEquipSync.cs (app half) on this head: the profile frame and avatar through
/// /v2/equip family <c>profile</c>. updatedAt is shared by families, so slots are compared too; an owed push beats
/// adoption; an unowned id answers ok:true with the OLD value, so a PUT settles on the answer's slots. One GET per
/// account per run (after the sync answer), a PUT only on a real frame/avatar change; no poll, no retry loop.</summary>
public static class ProfileEquipSync
{
    public const string Family = "profile";
    public const string Path = "/v2/equip";

    internal static HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _pulledFor;
    private static CancellationTokenSource _pending = new();
    internal static Action<Action>? PostForTests;
    internal static Func<string, bool>? ReadyForTests;
    // The sync answer has filled an empty loadout for this account first (SyncPush.CosmeticsAccount): an adoption
    // before it would make the loadout non-empty and lose the account's banner, accent and pins (WPF order:
    // the sync precedes ProfileLoaded there).
    private static bool Ready(string account) => ReadyForTests?.Invoke(account) ?? (AccountSeed.Sync?.CosmeticsAccount == account);

    /// <summary>Tests only: drop in-flight requests and queued settlements, and forget the per-run pull.
    /// No panic hook on purpose: WPF ProfileEquipSync has none (a stop stricter than WPF is a policy call).</summary>
    internal static void ResetForTests()
    {
        Interlocked.Exchange(ref _pending, new CancellationTokenSource()).Cancel();
        _pulledFor = null; PostForTests = null; ReadyForTests = null;
    }

    /// <summary>Raised on the UI thread when an adoption changed a slot: repaint the card.</summary>
    public static event Action? Adopted;

    /// <summary>Send a real slot change after the settings save, on the UI thread.</summary>
    public static void Saved(ProfileSlots before, ProfileCosmetics chosen)
    {
        try
        {
            var now = ProfileSlots.Of(chosen);
            if (now == before) return;
            var s = CoreSettings.Current;
            if (s == null) return;
            if (!string.IsNullOrEmpty(s.UnifiedId)) s.ProfileEquipAccount = s.UnifiedId;
            var stamp = MarkOwed(s);
            CoreSettings.Save();
            if (s.ProfileEquipAccount is { } account && Ready(account)) _ = PutAsync(now, stamp);
        }
        catch (Exception ex) { Log.Debug("ProfileEquipSync save failed: {E}", ex.Message); }
    }

    /// <summary>Retry an owed push or pull once per account after the loadout arrives.</summary>
    public static async Task PullOnceAsync()
    {
        var id = BackRoomApi.AppIdentity();
        if (id == null) return;
        var account = id.Value.UnifiedId;
        var token = _pending.Token;
        if (!Ready(account) || _pulledFor == account) return;
        _pulledFor = account;
        try
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            if (s.ProfileEquipPushOwed && (s.ProfileEquipAccount == null || s.ProfileEquipAccount == account))
            {
                if (!await PutAsync(OwnSlots(s), s.ProfileEquipUpdatedMs).ConfigureAwait(false) && _pulledFor == account)
                    _pulledFor = null;
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BackRoomApi.BaseUrl}{Path}?unified_id={Uri.EscapeDataString(account)}");
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req, token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { _pulledFor = null; return; }
            var o = ParseObject(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!IsEquipReply(o)) { _pulledFor = null; return; }
            var server = ReadProfile(o);
            UiPost(() => { if (!token.IsCancellationRequested) Settle(account, server?.Slots, server?.UpdatedMs ?? 0); });
        }
        catch (Exception ex)
        {
            if (_pulledFor == account) _pulledFor = null;
            Log.Debug("ProfileEquipSync GET failed soft: {E}", ex.Message);
        }
    }

    private static void Settle(string account, ProfileSlots? server, long serverMs)
    {
        try
        {
            var s = CoreSettings.Current;
            if (s == null || !Ready(account) || BackRoomApi.AppIdentity()?.UnifiedId != account) return;
            var step = Decide(s.ProfileEquipAccount, account, s.ProfileEquipPushOwed, OwnSlots(s), server, serverMs, s.ProfileEquipUpdatedMs);
            if (step == ProfileEquipStep.Push && !s.ProfileEquipPushOwed)
            {
                // A refused first-run push leaves no pending clear.
                _ = PutAsync(OwnSlots(s), s.ProfileEquipUpdatedMs, firstRun: true);
                return;
            }
            if (s.ProfileEquipAccount != account)
            {
                if (s.ProfileEquipAccount != null) { s.ProfileEquipUpdatedMs = 0; s.ProfileEquipPushOwed = false; }
                s.ProfileEquipAccount = account;
            }
            if (step == ProfileEquipStep.Adopt) Adopt(s, server!.Value, serverMs);
            else if (step == ProfileEquipStep.Push)
            {
                var stamp = MarkOwed(s);
                _ = PutAsync(OwnSlots(s), stamp);
            }
            CoreSettings.Save();
        }
        catch (Exception ex) { Log.Debug("ProfileEquipSync settle failed: {E}", ex.Message); }
    }

    /// <summary>Through SanitizeOwn, on the two slots alone, so nothing else in the loadout can be dropped.</summary>
    private static void Adopt(AppSettings s, ProfileSlots server, long serverMs)
    {
        var clean = ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(new ProfileCosmetics { FrameId = server.FrameId, AvatarId = server.AvatarId }));
        var changed = clean != ProfileSlots.Of(s.ProfileCosmetics);
        if (changed) s.ProfileCosmetics = WithSlots(s.ProfileCosmetics, clean);
        // A slot this PC refused (grants lagging a buy elsewhere) keeps the old stamp, so the next launch reads it again.
        if (clean == server && serverMs > 0) s.ProfileEquipUpdatedMs = serverMs;
        if (!changed) return;
        Log.Information("Profile equip adopted: frame {Frame}, avatar {Avatar}", clean.FrameId ?? "none", clean.AvatarId ?? "none");
        Adopted?.Invoke();
    }

    private static ProfileSlots OwnSlots(AppSettings s) => ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(s.ProfileCosmetics));

    private static async Task<bool> PutAsync(ProfileSlots slots, long stamp, bool firstRun = false)
    {
        var id = BackRoomApi.AppIdentity();
        if (id == null) return false;
        var account = id.Value.UnifiedId;
        var token = _pending.Token;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, BackRoomApi.BaseUrl + Path)
            {
                Content = new StringContent(BuildPut(account, slots, keepEmpty: firstRun).ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req, token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Log.Debug("ProfileEquipSync PUT answered {Status}; keeping the local pick", (int)res.StatusCode);
                return false;
            }
            if (ReadProfile(ParseObject(await res.Content.ReadAsStringAsync().ConfigureAwait(false))) is not { } stored) return false;
            UiPost(() =>
            {
                try
                {
                    var s = CoreSettings.Current;
                    // Unless the pick changed again while this PUT was out: that one still owes its own.
                    if (token.IsCancellationRequested || s == null || !Ready(account) || s.UnifiedId != account || s.ProfileEquipUpdatedMs != stamp) return;
                    if (!firstRun && !s.ProfileEquipPushOwed) return;
                    s.ProfileEquipPushOwed = false;
                    s.ProfileEquipAccount = account;
                    // Settle on the server answer, including its clock.
                    Adopt(s, stored.Slots, stored.UpdatedMs);
                    CoreSettings.Save();
                }
                catch (Exception ex) { Log.Debug("ProfileEquipSync PUT settle failed: {E}", ex.Message); }
            });
            return true;
        }
        catch (Exception ex) { Log.Debug("ProfileEquipSync PUT failed soft: {E}", ex.Message); return false; }
    }

    private static void UiPost(Action a)
    {
        try { if (PostForTests is { } post) post(a); else Dispatcher.UIThread.Post(a); }
        catch { }
    }

}
