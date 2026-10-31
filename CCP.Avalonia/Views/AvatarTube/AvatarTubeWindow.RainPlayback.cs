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
    private string? _rainFolder, _rainClip;
    private int _rainLayer, _rainVersion;
    private IDisposable? _rainFade;
    internal string RainPackRoot { get; set; } = Path.Combine(CorePaths.UserData, "rain-packs");
    internal string? RainFolder => _rainFolder;
    internal RainClipPlayer? RainActivePlayer => _rainPlayers[_rainLayer];

    private void WatchRainSettings()
    {
        _rainSettings = CoreSettings.Current;
        _rainSettings.PropertyChanged += RainSettingsChanged;
    }
    private void RainSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.AvatarGeneration) or nameof(AppSettings.RainPackHash))
            RunOnAvatar(() => { if (!_avatarSetsClosed) _ = RefreshRainPlayback(); });
    }
    internal async Task RefreshRainPlayback()
    {
        StopRainPlayback();
        if (_avatarSetsClosed) return;
        try
        {
            using var registry = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
            using var reader = new StreamReader(registry);
            var v2 = AvatarGenerationRule.ParseRegistry(reader.ReadToEnd()).generations.FirstOrDefault(row =>
                string.Equals(row.modId, CoreMods.ActiveModId, StringComparison.OrdinalIgnoreCase) && AvatarGenerationRule.IsV2(row.generation)).folder;
            _rainFolder = RainPackPlayback.ResolveTubeFolder(CoreSettings.Current.AvatarGeneration, v2, CoreSettings.Current.RainPackHash,
                hash => (_rainPack = RainPackReader.TryRead(RainPackRoot, hash))?.Folder, () => null);
            if (_rainFolder == null) return;
            using var stream = OpenRainFile("emotes.json");
            using var map = new StreamReader(stream);
            _rainMap = JObject.Parse(map.ReadToEnd());
            await PlayRainClip(PickRainIdle());
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Rain emote playback could not start"); StopRainPlayback(); }
    }
    private Stream OpenRainFile(string name) => Path.IsPathRooted(_rainFolder!)
        ? File.OpenRead(Path.Combine(_rainFolder!, name))
        : AssetLoader.Open(new Uri($"avares://CCP.Avalonia/Resources/{_rainFolder}/{name}"));
    private double RainLayout(string key, double fallback = 0) => (double?)_rainMap?["layout"]?[key] ?? fallback;
    private string PickRainIdle()
    {
        var all = (_rainMap?["idleRotation"] as JArray)?.Where(v => (string?)v["clip"] != _rainClip).ToArray() ?? Array.Empty<JToken>();
        if (all.Length == 0) all = (_rainMap?["idleRotation"] as JArray)?.ToArray() ?? Array.Empty<JToken>();
        int weight = all.Sum(v => Math.Max(1, (int?)v["weight"] ?? 1));
        if (weight == 0) return "idle";
        int pick = _random.Next(weight);
        foreach (var item in all) if ((pick -= Math.Max(1, (int?)item["weight"] ?? 1)) < 0) return (string?)item["clip"] ?? "idle";
        return "idle";
    }
    internal async Task PlayRainClip(string clip)
    {
        if (_rainMap == null || _rainFolder == null || _avatarSetsClosed || !RainPackReader.IsValidClip(clip)) return;
        int version = ++_rainVersion, next = 1 - _rainLayer;
        var image = this.FindControl<Image>(next == 0 ? "ImgAvatarAnimated" : "ImgAvatarAnimatedB")!;
        var outgoing = this.FindControl<Image>(_rainLayer == 0 ? "ImgAvatarAnimated" : "ImgAvatarAnimatedB")!;
        _rainPlayers[next]?.Dispose();
        var player = _rainPlayers[next] = new RainClipPlayer(image);
        player.Completed += sender => { if (version == _rainVersion) _ = PlayRainClip(PickRainIdle()); };
        if (_rainPack != null && _rainPack.Clips.TryGetValue(clip, out var entry))
            await player.LoadStrip(Path.Combine(_rainFolder, entry.File), entry.Frames, entry.Fps);
        else
            await player.Load(Path.IsPathRooted(_rainFolder) ? new Uri(Path.Combine(_rainFolder, clip + ".webp"))
                : new Uri($"avares://CCP.Avalonia/Resources/{_rainFolder}/{clip}.webp"));
        if (version != _rainVersion || !player.IsReady) return;
        _rainClip = clip; _rainLayer = next; image.Tag = clip; ApplyTubeLayoutOffsets(); _poseTimer.Stop(); _imgAvatar.IsVisible = false;
        int fade = ConditioningControlPanel.Services.Safety.GentleVisuals.FadeMs(Math.Max(0, (int?)_rainMap["fadeMs"] ?? 1000));
        image.Transitions = outgoing.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(fade) } };
        image.IsVisible = true; image.Opacity = 1; outgoing.Opacity = 0;
        _rainFade?.Dispose();
        _rainFade = DispatcherTimer.RunOnce(() => { if (version == _rainVersion) _rainPlayers[1 - next]?.Stop(); }, TimeSpan.FromMilliseconds(fade));
    }
    private void StopRainPlayback()
    {
        ++_rainVersion; _rainFade?.Dispose(); _rainFade = null;
        foreach (var player in _rainPlayers) player?.Dispose();
        Array.Clear(_rainPlayers);
        foreach (var name in new[] { "ImgAvatarAnimated", "ImgAvatarAnimatedB" }) this.FindControl<Image>(name)!.IsVisible = false;
        _imgAvatar.IsVisible = true; _rainPack = null; _rainMap = null; _rainFolder = _rainClip = null;
        ApplyTubeLayoutOffsets();
        if (!_avatarSetsClosed && _avatarPoses.Count(p => p != null) > 1) _poseTimer.Start();
    }
}
