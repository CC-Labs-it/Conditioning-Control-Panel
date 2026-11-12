using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Companion.Rain;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF RainLookSync's app half (RainLookSync.cs "the app's one") on this head. The engine is Core's
/// copy. Sandbox rule as <see cref="V2PurchaseHead"/>: with CCP_USERDATA_DIR set only a loopback
/// CCP_BACKROOM_BASE_URL is called; with none there is no client and nothing syncs.
/// This head has no dressing room (CompanionHeroCard.axaml.cs:414), so WPF's three wardrobe hooks have no
/// caller here: NoteLocalChange (RainWardrobeHostService.cs:404), LookOwingABake (:228) and the
/// "dressing room open" deferral in AdoptOnThisPc. The re-bake toast goes with them: nothing here can bake.</summary>
internal static class RainLookSyncHead
{
    private static string? _pulledFor;
    private static RainLookSync? _client;
    private static bool _built;

    /// <summary>The pack folder root, as the tube reads it (AvatarTubeWindow.RainPlayback.cs:34).</summary>
    internal static string PackRoot = Path.Combine(CorePaths.UserData, "rain-packs");

    /// <summary>WPF App_. Null in a sandbox without a loopback proxy. Tests set it (and restore it).</summary>
    internal static RainLookSync? Client
    {
        get
        {
            if (!_built)
            {
                _built = true;
                var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
                    Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar));
                _client = url == null ? null : new RainLookSync(null, Hooks(url));
            }
            return _client;
        }
        set { _built = true; _client = value; }
    }

    /// <summary>The held grants the clamp reads. Tests point it at their own ownership service (and restore it).</summary>
    internal static Func<IEnumerable<string>> Grants = () => PrizeOwnership.Grants;

    /// <summary>Test cleanup: the next pull is a first pull again.</summary>
    internal static void ForgetPulled() => _pulledFor = null;

    /// <summary>WPF RainLookSync.App_ hooks, against CoreSettings.</summary>
    internal static RainLookSync.Hooks Hooks(string baseUrl) => new()
    {
        Identity = BackRoomApi.AppIdentity,
        Local = () => (CoreSettings.Current.RainLook, CoreSettings.Current.RainLookPushOwed, CoreSettings.Current.RainLookAgreedUtc),
        Agreed = (look, at) =>
        {
            var s = CoreSettings.Current;
            if (string.Equals(RainLookCodec.Normalize(s.RainLook), RainLookCodec.Normalize(look), StringComparison.Ordinal))
                s.RainLookPushOwed = false;
            s.RainLookAgreedUtc = at;
            CoreSettings.Save();
        },
        Adopt = Adopt,
        Log = line => Serilog.Log.Information("RainLookSync: {Line}", line),
        BaseUrl = baseUrl,
    };

    /// <summary>WPF RainLookSync.OnAccountReady: a validate prizes block landed for this account (queued
    /// behind its snapshot by <see cref="PrizeOwnership.Seed"/>). Pull once per account per run. UI thread.</summary>
    internal static void OnAccountReady(string? account)
    {
        if (string.IsNullOrEmpty(account) || string.Equals(account, _pulledFor, StringComparison.Ordinal)) return;
        if (!string.Equals(account, BackRoomApi.AppIdentity()?.UnifiedId, StringComparison.Ordinal)) return;
        if (Client is not { } client) return;
        _pulledFor = account;
        _ = client.SyncAsync();
    }

    /// <summary>WPF RainLookSync.AdoptOnThisPc: clamp to what this account may wear, play its pack when this
    /// PC has one (else the embedded clips), settle. The tube follows RainPackHash itself.</summary>
    internal static void Adopt(string serverLook, DateTime? at)
    {
        var s = CoreSettings.Current;
        var look = RainLookClamp.Clamp(serverLook,
            new RainItemAccess(RainItemManifest.Shipped, new RainGrantSet(Grants()), new Progress()));
        s.RainLook = look;
        s.RainPackHash = RainLookSync.PackFor(look, hash => RainPackReader.TryRead(PackRoot, hash) != null);
        s.RainLookPushOwed = false;
        s.RainLookAgreedUtc = at;
        CoreSettings.Save();
    }

    /// <summary>WPF AppRainProgressSource: the higher of the current and highest-ever level, and the
    /// achievements engine.</summary>
    private sealed class Progress : IRainProgressSource
    {
        public int Level => Math.Max(CoreSettings.Current.PlayerLevel, CoreSettings.Current.HighestLevelEver);
        public IReadOnlyCollection<string> UnlockedAchievements
            => (IReadOnlyCollection<string>?)App.Achievements?.Progress?.UnlockedAchievements?.ToArray() ?? Array.Empty<string>();
    }
}
