using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Frame and avatar sync (shared shop contract C5). A sync pushes the whole local loadout and the server
/// replaces the whole object, so a pick made on the website or the phone would be wiped by the next desktop
/// sync. These two slots therefore live in <c>/v2/equip</c> family <c>profile</c>, the way
/// <see cref="EmiDesk.DeskEquipSync"/> does the desk: write locally, PUT in the background, adopt a newer
/// server copy once per account per launch.
///
/// <para><b>Two slots only.</b> Adoption never touches banner, accent, title, pins, decoration or charms.</para>
///
/// <para><b>A pending pick wins.</b> <c>updatedAt</c> is shared by every family, so another family's PUT makes
/// the server's profile copy look newer than a pick that never got there. Before any PUT that copy only
/// mirrors <c>user.cosmetics</c>, so it cannot prove a pick was made elsewhere either. So an owed push goes up
/// instead of being judged (checked again at the moment of adoption), an existing pick goes up once before
/// the first adoption, and a newer stamp only counts when the slots really differ.</para>
///
/// <para><b>The answer is the truth.</b> The server answers an id the account does not own with <c>ok:true</c>
/// and the OLD value, so a PUT settles on the slots in its answer, never on what was sent.</para>
///
/// <para><b>Fail soft.</b> The family deploys separately: a refusal, an offline app or junk leaves the local
/// pick as it is, and the push stays owed.</para>
/// </summary>
public static class ProfileEquipSync
{
    public const string Family = "profile";
    public const string Path = "/v2/equip";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _pulledFor;

    /// <summary>Raised on the UI thread when an adoption changed a slot: repaint the card.</summary>
    public static event Action? Adopted;

    public static JObject BuildPut(string unifiedId, ProfileSlots slots, bool keepEmpty = false) => ProfileEquipRules.BuildPut(unifiedId, slots, keepEmpty);
    public static bool IsEquipReply(JObject? reply) => ProfileEquipRules.IsEquipReply(reply);
    public static (ProfileSlots Slots, long UpdatedMs)? ReadProfile(JObject? reply) => ProfileEquipRules.ReadProfile(reply);
    public static bool ShouldAdopt(long serverMs, long localMs) => ProfileEquipRules.ShouldAdopt(serverMs, localMs);
    public static ProfileEquipStep Decide(string? storedAccount, string account, bool pushOwed, ProfileSlots local, ProfileSlots? server, long serverMs, long localMs)
        => ProfileEquipRules.Decide(storedAccount, account, pushOwed, local, server, serverMs, localMs);
    public static ProfileCosmetics WithSlots(ProfileCosmetics? current, ProfileSlots slots) => ProfileEquipRules.WithSlots(current, slots);

    // ---------------------------------------------------------------- the app

    /// <summary>
    /// The Customize dialog's save (and the counter's Wear, the same path), after settings hold the new
    /// loadout. Sends only a real change of these two slots: a banner or pin save must not stamp the shared
    /// updatedAt. UI thread.
    /// </summary>
    public static void Saved(ProfileSlots before, ProfileCosmetics chosen)
    {
        try
        {
            var now = ProfileSlots.Of(chosen);
            if (now == before) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            if (!string.IsNullOrEmpty(s.UnifiedId)) s.ProfileEquipAccount = s.UnifiedId;
            var stamp = MarkOwed(s);
            App.Settings?.Save();
            _ = PutAsync(now, stamp);
        }
        catch (Exception ex) { App.Logger?.Debug("ProfileEquipSync save failed: {E}", ex.Message); }
    }

    /// <summary>
    /// Once per account per launch, from the UI thread after the profile has landed: an owed push goes up,
    /// otherwise read the server copy and settle it. A network failure leaves the next profile load free to try.
    /// </summary>
    public static async Task PullOnceAsync()
    {
        var id = BackRoomApi.AppIdentity();
        if (id == null) return;
        var account = id.Value.UnifiedId;
        if (_pulledFor == account) return;
        _pulledFor = account;
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            if (s.ProfileEquipPushOwed && (s.ProfileEquipAccount == null || s.ProfileEquipAccount == account))
            {
                await PutAsync(OwnSlots(s), s.ProfileEquipUpdatedMs).ConfigureAwait(false);
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BackRoomApi.BaseUrl}{Path}?unified_id={Uri.EscapeDataString(account)}");
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return;
            var o = ParseObject(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!IsEquipReply(o)) return;
            var server = ReadProfile(o);
            UiPost(() => Settle(account, server?.Slots, server?.UpdatedMs ?? 0));
        }
        catch (Exception ex)
        {
            if (_pulledFor == account) _pulledFor = null;
            App.Logger?.Debug("ProfileEquipSync GET failed soft: {E}", ex.Message);
        }
    }

    private static void Settle(string account, ProfileSlots? server, long serverMs)
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null || BackRoomApi.AppIdentity()?.UnifiedId != account) return;
            var step = Decide(s.ProfileEquipAccount, account, s.ProfileEquipPushOwed, OwnSlots(s), server, serverMs, s.ProfileEquipUpdatedMs);
            if (step == ProfileEquipStep.Push && !s.ProfileEquipPushOwed)
            {
                // The first-run push. Nothing is marked until it lands, so a refusal (the family not deployed
                // yet) is simply asked again next launch and never turns into a both-slots push.
                _ = PutAsync(OwnSlots(s), s.ProfileEquipUpdatedMs, firstRun: true);
                return;
            }
            if (s.ProfileEquipAccount != account)
            {
                // A new account starts clean. Null before this = this client's first look at the signed-in account.
                if (s.ProfileEquipAccount != null) { s.ProfileEquipUpdatedMs = 0; s.ProfileEquipPushOwed = false; }
                s.ProfileEquipAccount = account;
            }
            if (step == ProfileEquipStep.Adopt) Adopt(s, server!.Value, serverMs);
            else if (step == ProfileEquipStep.Push)
            {
                var stamp = MarkOwed(s);
                _ = PutAsync(OwnSlots(s), stamp);
            }
            App.Settings?.Save();
        }
        catch (Exception ex) { App.Logger?.Debug("ProfileEquipSync settle failed: {E}", ex.Message); }
    }

    /// <summary>Through SanitizeOwn, on the two slots alone, so nothing else in the loadout can be dropped.</summary>
    private static void Adopt(AppSettings s, ProfileSlots server, long serverMs)
    {
        var clean = ProfileSlots.Of(CosmeticsCatalog.SanitizeOwn(new ProfileCosmetics { FrameId = server.FrameId, AvatarId = server.AvatarId }));
        var changed = clean != ProfileSlots.Of(s.ProfileCosmetics);
        if (changed) s.ProfileCosmetics = WithSlots(s.ProfileCosmetics, clean);
        // A slot this PC refused (grants lagging a buy elsewhere) keeps the old stamp, so the next launch reads it again.
        if (clean == server && serverMs > 0) s.ProfileEquipUpdatedMs = serverMs;
        if (!changed) return;
        App.Logger?.Information("Profile equip adopted: frame {Frame}, avatar {Avatar}", clean.FrameId ?? "none", clean.AvatarId ?? "none");
        Adopted?.Invoke();
    }

    private static ProfileSlots OwnSlots(AppSettings s) => ProfileSlots.Of(CosmeticsCatalog.SanitizeOwn(s.ProfileCosmetics));

    private static long MarkOwed(AppSettings s)
    {
        var stamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), s.ProfileEquipUpdatedMs + 1);
        s.ProfileEquipUpdatedMs = stamp;
        s.ProfileEquipPushOwed = true;
        return stamp;
    }

    private static async Task PutAsync(ProfileSlots slots, long stamp, bool firstRun = false)
    {
        var id = BackRoomApi.AppIdentity();
        if (id == null) return;
        var account = id.Value.UnifiedId;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, BackRoomApi.BaseUrl + Path)
            {
                Content = new StringContent(BuildPut(account, slots, keepEmpty: firstRun).ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                App.Logger?.Debug("ProfileEquipSync PUT answered {Status}; keeping the local pick", (int)res.StatusCode);
                return;
            }
            if (ReadProfile(ParseObject(await res.Content.ReadAsStringAsync().ConfigureAwait(false))) is not { } stored) return;
            UiPost(() =>
            {
                try
                {
                    var s = App.Settings?.Current;
                    // Unless the pick changed again while this PUT was out: that one still owes its own.
                    if (s == null || s.UnifiedId != account || s.ProfileEquipUpdatedMs != stamp) return;
                    if (!firstRun && !s.ProfileEquipPushOwed) return;
                    s.ProfileEquipPushOwed = false;
                    s.ProfileEquipAccount = account;
                    // The answer's slots, not the ones sent: an unowned id comes back as the old value, and a
                    // first-run push comes back with the slot it left out. The server's clock from here on,
                    // so a skewed local clock cannot hide a newer pick.
                    Adopt(s, stored.Slots, stored.UpdatedMs);
                    App.Settings?.Save();
                }
                catch (Exception ex) { App.Logger?.Debug("ProfileEquipSync PUT settle failed: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ProfileEquipSync PUT failed soft: {E}", ex.Message); }
    }

    private static void UiPost(Action a)
    {
        try { System.Windows.Application.Current?.Dispatcher?.BeginInvoke(a); }
        catch { }
    }

    private static JObject? ParseObject(string body) => ProfileEquipRules.ParseObject(body);
}
