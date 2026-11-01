using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.BackRoom;
using static ConditioningControlPanel.Services.Companion.RainLookSync;

namespace ConditioningControlPanel.Services.Companion;

// WPF lifetime, wardrobe, tube and notification hooks over the shared Core engine.
internal static class RainLookSyncHead
{
    // ------------------------------------------------------------------ the app's one

    private static RainLookSync? _app;
    private static string? _pulledFor;

    private static RainLookSync App_ => _app ??= new RainLookSync(null, new Hooks
    {
        Identity = BackRoomApi.AppIdentity,
        Local = () => App.Settings?.Current is { } s ? (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc) : null,
        Agreed = (look, at) =>
        {
            if (App.Settings?.Current is not { } s) return;
            if (string.Equals(RainLookCodec.Normalize(s.RainLook), RainLookCodec.Normalize(look), StringComparison.Ordinal))
                s.RainLookPushOwed = false;
            s.RainLookAgreedUtc = at;
            App.Settings.Save();
        },
        Adopt = AdoptOnThisPc,
        Log = line => App.Logger?.Information("RainLookSync: {Line}", line),
    });

    private static bool HasPack(string hash)
    {
        try { return RainPackStore.CreateDefault().TryGetFolder(hash) != null; }
        catch (Exception) { return false; }
    }

    /// <summary>The account is known (a validate prizes block landed, grants applied): pull once per
    /// account per run. UI thread.</summary>
    public static void OnAccountReady(string? account)
    {
        if (string.IsNullOrEmpty(account) || string.Equals(account, _pulledFor, StringComparison.Ordinal)) return;
        if (!string.Equals(account, BackRoomApi.AppIdentity()?.UnifiedId, StringComparison.Ordinal)) return;
        _pulledFor = account;
        _ = App_.SyncAsync();
    }

    /// <summary>The wardrobe's Apply committed a look on this PC: owe a push and send it. UI thread.</summary>
    public static void NoteLocalChange()
    {
        if (App.Settings?.Current is not { } s) return;
        s.RainLookPushOwed = true;
        App.Settings.Save();
        _ = App_.SyncAsync();
    }

    /// <summary>The live re-bake rule for the dressing room's ready frame: the clamped look to bake, or null.</summary>
    internal static string? LookOwingABake()
    {
        var s = App.Settings?.Current;
        if (s == null) return null;
        var look = RainWardrobeCatalog.Clamp(s.RainLook, App.Ownership?.Grants);
        return RebakeOwed(look, s.RainPackHash, HasPack) ? look : null;
    }

    private static void AdoptOnThisPc(string serverLook, DateTime? at)
    {
        if (App.Settings?.Current is not { } s) return;
        // A dressing room on screen owns the look until it closes.
        if (RainWardrobeHostService.IsActive)
        {
            App.Logger?.Information("RainLookSync: adopt deferred, the dressing room is open");
            _pulledFor = null;   // the next validate prizes block pulls again
            return;
        }
        var look = RainWardrobeCatalog.Clamp(serverLook, App.Ownership?.Grants);
        s.RainLook = look;
        s.RainPackHash = PackFor(look, HasPack);
        s.RainLookPushOwed = false;
        s.RainLookAgreedUtc = at;
        App.Settings.Save();
        App.AvatarWindow?.RefreshRainLook();
        // RainV2Gate off: the look is adopted silently, never a toast about a wardrobe nobody can open.
        if (RainV2Gate.On && RebakeOwed(look, s.RainPackHash, HasPack))
            App.Notifications?.Show(Loc.Get("rain_look_synced_rebake"), NotificationType.Info, TimeSpan.FromSeconds(8));
    }
}
