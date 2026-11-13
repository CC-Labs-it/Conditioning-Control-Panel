using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Rain;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private readonly RainClipPlayer?[] _rainPlayers = new RainClipPlayer?[2];
    private AppSettings? _rainSettings;
    private RainPackReader? _rainPack;
    private JObject? _rainMap;
    private string? _rainFolder, _rainClip, _rainPending;
    private int _rainLayer, _rainVersion, _rainFailures, _rainHoldVersion; private bool _rainLoading;
    private long _rainClipStarted;
    private long _rainClickTick = -3000; // not WPF's long.MinValue (CirceEmotes.cs:138): now minus it wraps negative and blocks every click
    private IDisposable? _rainFade, _rainHold;
    internal Func<long> RainClock = static () => Environment.TickCount64;
    internal Func<Action, TimeSpan, IDisposable> RainHoldScheduler = (action, delay) => DispatcherTimer.RunOnce(action, delay);
    internal TimeSpan RainRetryDelay = TimeSpan.FromMilliseconds(1200);
    internal int RainFailureCount => _rainFailures;
    internal int RainVersion => _rainVersion;
    internal string RainPackRoot { get; set; } = Path.Combine(CorePaths.UserData, "rain-packs");
    /// <summary>The release switch (Core RainV2Gate, off in this release); tests open it to drive the V2 path.</summary>
    internal Func<bool> RainGateOn { get; set; } = static () => RainV2Gate.On;
    internal string? RainFolder => _rainFolder;
    internal string? RainClip => _rainClip;
    internal RainClipPlayer? RainActivePlayer => _rainPlayers[_rainLayer];

    private void WatchRainSettings() { _rainSettings = CoreSettings.Current; _rainSettings.PropertyChanged += RainSettingsChanged; PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateRainVisibility(IsVisible); }; }
    private void RainSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(AppSettings.AvatarGeneration) or nameof(AppSettings.RainPackHash))
            RunOnAvatar(() => { if (!_avatarSetsClosed) _ = RefreshRainPlayback(); });
    }
    internal async Task RefreshRainPlayback() {
        StopRainPlayback();
        if (_avatarSetsClosed) return;
        try {
            using var registry = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
            using var reader = new StreamReader(registry);
            var v2 = AvatarGenerationRule.ParseRegistry(reader.ReadToEnd()).generations.FirstOrDefault(row =>
                string.Equals(row.modId, CoreMods.ActiveModId, StringComparison.OrdinalIgnoreCase) && AvatarGenerationRule.IsV2(row.generation)).folder;
            // WPF CirceEmotes.cs:169: RainV2Gate off, a saved "v2" plays V1 and stays saved.
            _rainFolder = RainPackPlayback.ResolveTubeFolder(AvatarGenerationRule.Effective(CoreSettings.Current.AvatarGeneration, RainGateOn()), v2, CoreSettings.Current.RainPackHash,
                hash => (_rainPack = RainPackReader.TryRead(RainPackRoot, hash))?.Folder, ResolveV1EmoteFolder);
            if (_rainFolder == null) return;
            using var stream = OpenRainFile("emotes.json");
            using var map = new StreamReader(stream);
            _rainMap = JObject.Parse(map.ReadToEnd());
            if (IsVisible) await PlayRainClip(PickRainIdle());
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Rain emote playback could not start"); StopRainPlayback(); }
    }
    // WPF ResolveV1EmoteFolder + ModLocalEmoteFolder (CirceEmotes.cs:178-206): the mod's own set{N}
    // folder first, then the embedded registry row for (mod, set). A row whose clips this head does
    // not embed answers null, so the tube keeps its static poses instead of failing every refresh.
    private string? ResolveV1EmoteFolder() {
        try {
            var installed = CoreMods.ActiveModPackage?.InstalledPath;
            if (!string.IsNullOrEmpty(installed)) {
                var local = Path.Combine(installed, "resources", "emotes", $"set{_currentAvatarSet}");
                if (File.Exists(Path.Combine(local, "emotes.json"))) return local;
            }
            using var registry = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
            using var reader = new StreamReader(registry);
            var folder = AvatarGenerationRule.ParseRegistry(reader.ReadToEnd()).sets.FirstOrDefault(row =>
                string.Equals(row.modId, CoreMods.ActiveModId, StringComparison.OrdinalIgnoreCase) && row.set == _currentAvatarSet).folder;
            return folder != null && AssetLoader.Exists(new Uri($"avares://CCP.Avalonia/Resources/{folder}/emotes.json")) ? folder : null;
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "V1 emote folder lookup failed"); return null; }
    }
    // WPF ResolveEmoteClipUri (CirceEmotes.cs:1238): <clip>.webp when the folder ships one, else <clip>.gif.
    private Uri RainClipUri(string clip) {
        if (Path.IsPathRooted(_rainFolder!)) {
            var webp = Path.Combine(_rainFolder!, clip + ".webp");
            return new Uri(File.Exists(webp) ? webp : Path.Combine(_rainFolder!, clip + ".gif"));
        }
        var embedded = new Uri($"avares://CCP.Avalonia/Resources/{_rainFolder}/{clip}.webp");
        return AssetLoader.Exists(embedded) ? embedded : new Uri($"avares://CCP.Avalonia/Resources/{_rainFolder}/{clip}.gif");
    }
    private Stream OpenRainFile(string name) => Path.IsPathRooted(_rainFolder!)
        ? File.OpenRead(Path.Combine(_rainFolder!, name))
        : AssetLoader.Open(new Uri($"avares://CCP.Avalonia/Resources/{_rainFolder}/{name}"));
    private double RainLayout(string key, double fallback = 0) => (double?)_rainMap?["layout"]?[key] ?? fallback;
    private string PickRainIdle() {
        var all = (_rainMap?["idleRotation"] as JArray)?.Where(v => (string?)v["clip"] != _rainClip).ToArray() ?? Array.Empty<JToken>();
        if (all.Length == 0) all = (_rainMap?["idleRotation"] as JArray)?.ToArray() ?? Array.Empty<JToken>();
        int weight = all.Sum(v => Math.Max(1, (int?)v["weight"] ?? 1));
        if (weight == 0) return "idle";
        int pick = _random.Next(weight);
        foreach (var item in all) if ((pick -= Math.Max(1, (int?)item["weight"] ?? 1)) < 0) return (string?)item["clip"] ?? "idle";
        return "idle";
    }
    internal async Task PlayRainClip(string clip) {
        if (_rainMap == null || _rainFolder == null || _avatarSetsClosed || !IsVisible || !RainPackReader.IsValidClip(clip)) return;
        int version = ++_rainVersion, next = 1 - _rainLayer; _rainLoading = true;
        var image = this.FindControl<Image>(next == 0 ? "ImgAvatarAnimated" : "ImgAvatarAnimatedB")!;
        var outgoing = this.FindControl<Image>(_rainLayer == 0 ? "ImgAvatarAnimated" : "ImgAvatarAnimatedB")!;
        _rainPlayers[next]?.Dispose();
        var player = _rainPlayers[next] = new RainClipPlayer(image);
        player.Completed += sender => { if (ReferenceEquals(sender, RainActivePlayer) && !_rainLoading && IsVisible && _rainPending == null) _ = PlayRainClip(PickRainIdle()); };
        if (_rainPack != null && _rainPack.Clips.TryGetValue(clip, out var entry))
            await player.LoadStrip(Path.Combine(_rainFolder, entry.File), entry.Frames, entry.Fps);
        else
            await player.Load(RainClipUri(clip));
        if (version != _rainVersion) return; _rainLoading = false;
        if (!player.IsReady) {   // WPF watchdog (CirceEmotes.cs:555) re-issues a failed load, then advances; the shown frame stays up.
            _rainFade?.Dispose();
            ++_rainFailures; _rainFade = DispatcherTimer.RunOnce(() => { if (version == _rainVersion && _rainPending == null) _ = PlayRainClip(PickRainIdle()); }, RainRetryDelay);
            return;
        }
        _rainFailures = 0; _rainClip = clip; _rainClipStarted = RainClock(); _rainLayer = next; image.Tag = clip; ApplyTubeLayoutOffsets(); _poseTimer.Stop(); _imgAvatar.IsVisible = false;
        int fade = ConditioningControlPanel.Services.Safety.GentleVisuals.FadeMs(Math.Max(0, (int?)_rainMap["fadeMs"] ?? 1000));
        image.Transitions = outgoing.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(fade) } };
        image.IsVisible = true; image.Opacity = 1; outgoing.Opacity = 0;
        _rainFade?.Dispose();
        _rainFade = DispatcherTimer.RunOnce(() => { if (version == _rainVersion) _rainPlayers[1 - next]?.Stop(); }, TimeSpan.FromMilliseconds(fade));
    }
    // WPF CirceClickEmote (CirceEmotes.cs:866): a click plays one click emote per 3 s, then idle resumes.
    internal bool RainClickEmote() {
        long now = RainClock();
        var pool = (_rainMap?["clickEmotes"] as JArray)?.Select(c => (string?)c).Where(c => c != null && (_rainPack?.Clips.ContainsKey(c) ?? true)).ToList();
        if (!IsVisible || pool == null || pool.Count == 0 || now - _rainClickTick < 3000) return false;
        if (pool.Count > 1) pool.Remove(_rainClip);
        _rainClickTick = now;
        RequestRainClip(pool[_random.Next(pool.Count)]!);
        return true;
    }
    // WPF CirceCrossfadeTo holds the current clip for 2 s and coalesces requests.
    internal void RequestRainClip(string clip) {
        if (!IsVisible || _rainMap == null || clip == _rainClip) return;
        _rainHold?.Dispose(); int holdVersion = ++_rainHoldVersion;
        long delay = _rainClip == null ? 0 : Math.Max(0, 2000 - (RainClock() - _rainClipStarted));
        if (delay == 0) { _rainHold = null; _rainPending = null; _ = PlayRainClip(clip); return; }
        _rainPending = clip;
        _rainHold = RainHoldScheduler(() => { if (holdVersion != _rainHoldVersion) return; _rainHold = null; _rainPending = null; if (clip != _rainClip) _ = PlayRainClip(clip); }, TimeSpan.FromMilliseconds(delay));
    }
    private void UpdateRainVisibility(bool visible) {
        if (!visible) {
            ++_rainVersion; ++_rainHoldVersion; _rainLoading = false; _rainFade?.Dispose(); _rainHold?.Dispose(); _rainFade = _rainHold = null; _rainPending = null;
            foreach (var player in _rainPlayers) player?.Pause();
            for (int i = 0; i < _rainPlayers.Length; i++) if (i != _rainLayer || _rainClip == null) { _rainPlayers[i]?.Dispose(); _rainPlayers[i] = null; }
        } else {
            var player = RainActivePlayer; player?.Resume();
            if (_rainMap != null && (player == null || !player.IsReady || player.IsComplete)) _ = PlayRainClip(PickRainIdle());
        }
    }
    private void StopRainPlayback() {
        ++_rainVersion; ++_rainHoldVersion; _rainLoading = false; _rainFade?.Dispose(); _rainHold?.Dispose(); _rainFade = _rainHold = null; _rainPending = null; _rainFailures = 0;
        foreach (var player in _rainPlayers) player?.Dispose();
        Array.Clear(_rainPlayers);
        foreach (var name in new[] { "ImgAvatarAnimated", "ImgAvatarAnimatedB" }) this.FindControl<Image>(name)!.IsVisible = false;
        _imgAvatar.IsVisible = true; _rainPack = null; _rainMap = null; _rainFolder = _rainClip = null;
        ApplyTubeLayoutOffsets();
        if (!_avatarSetsClosed && _avatarPoses.Count(p => p != null) > 1) _poseTimer.Start();
    }
}
