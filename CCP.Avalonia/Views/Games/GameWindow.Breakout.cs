using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{

    // WPF BackRoomMedia.cs:151-170 and BackRoomRemotePool.cs:202-223.
    private GoonOnlineMedia? _breakoutRemote;
    private IReadOnlyList<GameMediaManifest.Entry> _breakoutClips = Array.Empty<GameMediaManifest.Entry>();
    private string _breakoutChannels = "";
    private TaskCompletionSource<bool>? _breakoutWarm;
    private Action<GoonOnlineMedia.Snapshot>? _breakoutSnapshot;
    private bool _breakoutWaveFinished = true;
    private long? _breakoutLastWarmMs;
    private const int BreakoutWarmWaitMs = 2000, BreakoutWarmGapMs = 8000; // WPF BackRoomRemotePool.cs:122,129
    internal Func<long>? BreakoutClockForTests;
    internal Action<Action<GoonOnlineMedia.Snapshot>>? BreakoutStartForTests;
    private readonly HashSet<string> _breakoutMediaRequests = new(StringComparer.Ordinal);
    // A per-window test seam; production always uses the pool below.
    internal Func<IReadOnlyList<GameMediaManifest.Entry>>? BreakoutClipsForTests;

    private static string BreakoutSource(JToken? requested)
    {
        var s = CoreSettings.Current;
        var source = BackRoomBridge.MediaSourceRequest(requested);
        if (source == null || source == "auto") return BackRoomWire.EffectiveMediaSource(s);
        return source is "online" or "mixed" && !s.HasRemoteMediaConsent ? "local" : source;
    }

    internal static List<string> BreakoutChannels(AppSettings s)
    {
        var off = new HashSet<string>(s.BackRoomMediaSubsOff, StringComparer.OrdinalIgnoreCase);
        var channels = s.BackRoomMediaSubs.Select(FypOnlineCoordinator.SanitizeSub)
            .Where(n => n != null && !off.Contains(n)).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(AppSettings.BackRoomMediaSubCap).ToList();
        if (channels.Count == 0) channels = FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs);
        if (channels.Count == 0) channels = FypOnlineCoordinator.Catalog[0].Subs.ToList();
        return channels;
    }

    private IReadOnlyList<GameMediaManifest.Entry> BreakoutClips(string source, bool start = true)
    {
        if (source is not ("online" or "mixed") || !CoreSettings.Current.HasRemoteMediaConsent)
            return Array.Empty<GameMediaManifest.Entry>();
        if (BreakoutClipsForTests != null) return BreakoutClipsForTests();
        var channels = BreakoutChannels(CoreSettings.Current);
        string key = string.Join(",", channels.Select(n => n.ToLowerInvariant()));
        if (_breakoutRemote == null || key != _breakoutChannels)
        {
            if (!start) return Array.Empty<GameMediaManifest.Entry>();
            DisposeBreakoutMedia();
            _breakoutClips = Array.Empty<GameMediaManifest.Entry>();
            _breakoutChannels = key;
            GoonOnlineMedia.Materialize = DtrhRemoteMedia.AssetsTempCopier.Materialize;
            GoonOnlineMedia.ReleaseTempFile = DtrhRemoteMedia.AssetsTempCopier.Release;
            GoonOnlineMedia? pool = null;
            void Snapshot(GoonOnlineMedia.Snapshot snapshot) => Dispatcher.UIThread.Post(() =>
            {
                if (_closing || !ReferenceEquals(pool, _breakoutRemote) || !CoreSettings.Current.HasRemoteMediaConsent) return;
                var oldCount = _breakoutClips.Count;
                _breakoutClips = snapshot.Videos.Select(item => new GameMediaManifest.Entry(item.Name,
                    _server!.AssetUrl(Path.GetRelativePath(CorePaths.EffectiveAssets, item.Path)))).ToArray();
                _breakoutWaveFinished = snapshot.State != "loading";
                if (_breakoutClips.Count > 0 || _breakoutWaveFinished) _breakoutWarm?.TrySetResult(true);
                if (oldCount == 0 && _breakoutClips.Count > 0) Post(new { type = "media-warm", station = "breakout" });
            });
            pool = GoonOnlineMedia.ForGameClips("backroom-breakout", Snapshot);
            _breakoutRemote = pool;
            _breakoutSnapshot = Snapshot;
        }
        // WPF BackRoomRemotePool.cs:271-280: refill a completed pool below 16 clips.
        var now = BreakoutClockForTests?.Invoke() ?? Environment.TickCount64;
        if (start && _breakoutClips.Count < 16 && _breakoutWaveFinished
            && (_breakoutLastWarmMs == null || now - _breakoutLastWarmMs >= BreakoutWarmGapMs))
        {
            _breakoutLastWarmMs = now;
            _breakoutWaveFinished = false;
            _breakoutWarm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (BreakoutStartForTests is { } fake) fake(_breakoutSnapshot!);
            else if (_breakoutClips.Count == 0) _breakoutRemote!.Start(channels);
            else _breakoutRemote!.More();
        }
        return _breakoutClips;
    }

    private void DisposeBreakoutMedia()
    {
        _breakoutWarm?.TrySetResult(false);
        _breakoutWarm = null;
        _breakoutWaveFinished = true;
        _breakoutLastWarmMs = null;
        _breakoutSnapshot = null;
        _breakoutRemote?.Dispose();
        _breakoutRemote = null;
        _breakoutChannels = "";
        _breakoutClips = Array.Empty<GameMediaManifest.Entry>();
    }

    private void HandleBreakoutMedia(JObject o)
    {
        var reqId = (string?)o["reqId"];
        var station = (string?)o["station"];
        if (string.IsNullOrEmpty(reqId) || station == null || !System.Text.RegularExpressions.Regex.IsMatch(station, "^[a-z0-9_-]{1,24}$")
            || !_breakoutMediaRequests.Add(reqId)) return;
        var requested = o["source"];
        int count = BackRoomBridge.MediaCount(o["count"]);
        int seed = Random.Shared.Next();
        var source = BreakoutSource(requested);
        var clips = BreakoutClips(source); // start the pool before the disk walk
        var warm = source is "online" or "mixed" && clips.Count == 0 ? _breakoutWarm?.Task : null;
        var wait = warm == null ? Task.CompletedTask : Task.WhenAny(warm, Task.Delay(BreakoutWarmWaitMs));
        var server = _server;
        var library = _library ??= new(() => server == null ? new() : GameMediaManifest.BuildLocal(server.AssetUrl));
        _ = Task.Run(async () =>
        {
            GameMediaManifest.Manifest local;
            try { local = library.Value; } catch { local = new(); }
            await wait.ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing) return;
                var currentSource = BreakoutSource(requested); // consent may have changed during the wait
                var currentClips = BreakoutClips(currentSource, start: false);
                Post(GameMediaManifest.BreakoutDeal(local, reqId, seed, count, currentSource, CoreSettings.Current.RemoteMediaRatio, currentClips));
            });
        });
    }

    private bool IsBreakout => Spec.Id is "breakout" or "breakoutdemo";
    private bool _breakoutSwitchPending;

    // WPF BackRoomHostService.cs:295-325: focus, ask with No as default, then replace.
    internal static Func<GameWindow, Task<bool>> ConfirmBreakoutSwitch = owner =>
        MessageDialog.ConfirmAsync(owner, Loc.Get("launcher_switch_title"), Loc.Get("launcher_switch_body"),
            defaultToCancel: true, okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"));

    private async Task SwitchBreakoutAsync(string id)
    {
        if (_breakoutSwitchPending) return;
        _breakoutSwitchPending = true;
        try
        {
            if (!await ConfirmBreakoutSwitch(this)) return;
            // The dialog yields: panic or the title-bar X may have ended this run meanwhile.
            if (_closing) return;
            if (id == "breakout" && !BreakoutGate()) return;
            Close();
            Launch(id);
        }
        catch (Exception ex) { Log.Warning(ex, "[Breakout] switch refused"); }
        finally { _breakoutSwitchPending = false; }
    }

    private SettingsService? _breakoutSettingsService;
    private AppSettings? _breakoutSettings;

    // WPF BackRoomHostService.cs:689-720: rebind after cloud restore and push live motion changes.
    private void HookBreakoutSettings()
    {
        if (_breakoutSettings != null) _breakoutSettings.PropertyChanged -= OnBreakoutSettingChanged;
        _breakoutSettings = CoreSettings.Current;
        _breakoutSettings.PropertyChanged += OnBreakoutSettingChanged;
    }

    private void OnBreakoutSettingsReplaced() => Dispatcher.UIThread.Post(() =>
    {
        if (_closing) return;
        DisposeBreakoutMedia();
        HookBreakoutSettings();
        PushBreakoutSettings();
    });

    private void OnBreakoutSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.BackRoomMediaSource) or nameof(AppSettings.MediaSource)
            or nameof(AppSettings.BackRoomMediaSubs) or nameof(AppSettings.BackRoomMediaSubsOff)
            or nameof(AppSettings.RemoteMediaConsented) or nameof(AppSettings.FypOnlineConsented))
            Dispatcher.UIThread.Post(DisposeBreakoutMedia);
        if (e.PropertyName is nameof(AppSettings.MotionLevel) or nameof(AppSettings.BackRoomFxIntensity)
            or nameof(AppSettings.BackRoomTunnel) or nameof(AppSettings.BackRoomMelt)
            or nameof(AppSettings.BackRoomInvertLook) or nameof(AppSettings.BackRoomMediaSource)
            or nameof(AppSettings.BackRoomMediaSubs) or nameof(AppSettings.BackRoomMediaSubsOff)
            or nameof(AppSettings.MediaSource) or nameof(AppSettings.RemoteMediaRatio)
            or nameof(AppSettings.RemoteMediaConsented) or nameof(AppSettings.FypOnlineConsented)) Dispatcher.UIThread.Post(PushBreakoutSettings);
    }

    private void PushBreakoutSettings()
    {
        if (_closing) return;
        // WPF BackRoomHostService.cs:620-632; play.html merges partial settings frames.
        var s = CoreSettings.Current;
        var motion = s.MotionLevel;
        Post(new { type = "settings", motion = motion.ToString().ToLowerInvariant(),
            reduced = motion != MotionLevel.Full, breakout = BreakoutEntitlementFor(Spec.Id), breakoutStandalone = true,
            intensity = BackRoomWire.IntensityWire(s, motion), intensityChoice = BackRoomWire.IntensityChoiceWire(s),
            gates = BackRoomWire.GatesWire(s), media = BackRoomWire.MediaWire(s), invertLook = s.BackRoomInvertLook });
    }

    // WPF BreakoutHostService.cs:14-17 and BreakoutAccess.cs:9-16.
    internal static bool BreakoutGate() => TierGate.DemandLab(Loc.Get("launcher_game_breakout_title"));
    private static bool FullBreakoutAllowed => TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
    private static object FullBreakoutEntitlement() => FullBreakoutAllowed
        ? new { storyLimit = 8, endless = true, demo = false }
        : new { storyLimit = 3, endless = false, demo = true };

    private DispatcherTimer? _breakoutAccessWatch;
    private ProviderSubscription? _breakoutPatreon, _breakoutSubscribeStar;
    private bool _lastBreakoutAccess;

    // WPF BackRoomHostService.cs:736-763: events plus a 2 s watch for quiet grace expiry.
    private void StartBreakoutAccessWatch()
    {
        if (!IsBreakout) return;
        _breakoutSettingsService = CoreSettings.Service;
        if (_breakoutSettingsService != null) _breakoutSettingsService.CurrentReplaced += OnBreakoutSettingsReplaced;
        HookBreakoutSettings();
        if (_server != null) _ = BreakoutClips(BreakoutSource(null));
        if (Spec.Id != "breakout") return;
        _lastBreakoutAccess = FullBreakoutAllowed;
        _breakoutPatreon = AccountSeed.Patreon;
        _breakoutSubscribeStar = AccountSeed.SubscribeStar;
        if (_breakoutPatreon != null) _breakoutPatreon.TierChanged += OnBreakoutTierChanged;
        if (_breakoutSubscribeStar != null) _breakoutSubscribeStar.TierChanged += OnBreakoutTierChanged;
        _breakoutAccessWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _breakoutAccessWatch.Tick += OnBreakoutAccessTick;
        _breakoutAccessWatch.Start();
    }

    private void OnBreakoutTierChanged(object? sender, PatreonTier tier) => Dispatcher.UIThread.Post(PushBreakoutAccess);
    private void OnBreakoutAccessTick(object? sender, EventArgs e) => PushBreakoutAccess();

    internal void PushBreakoutAccess()
    {
        if (Spec.Id != "breakout" || _closing) return;
        var allowed = FullBreakoutAllowed;
        if (allowed == _lastBreakoutAccess) return;
        _lastBreakoutAccess = allowed;
        // WPF BackRoomHostService.cs:620-632, 756-763. The page accepts partial settings frames.
        PushBreakoutSettings();
    }

    private void StopBreakoutAccessWatch()
    {
        if (_breakoutSettings != null) _breakoutSettings.PropertyChanged -= OnBreakoutSettingChanged;
        if (_breakoutSettingsService != null) _breakoutSettingsService.CurrentReplaced -= OnBreakoutSettingsReplaced;
        _breakoutSettings = null;
        _breakoutSettingsService = null;
        _breakoutAccessWatch?.Stop();
        if (_breakoutAccessWatch != null) _breakoutAccessWatch.Tick -= OnBreakoutAccessTick;
        _breakoutAccessWatch = null;
        if (_breakoutPatreon != null) _breakoutPatreon.TierChanged -= OnBreakoutTierChanged;
        if (_breakoutSubscribeStar != null) _breakoutSubscribeStar.TierChanged -= OnBreakoutTierChanged;
        _breakoutPatreon = _breakoutSubscribeStar = null;
    }
}
