// WPF AvatarTubeWindow.GenerationPill.cs:29-125; CirceEmotes.cs:312-343, 885-911, 983-1000,
// 1060-1140, 1220-1233, 1260-1340. Shared maps remain the source of clips, layout and timing.
using System;
using System.IO;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private string? _emoteFolder;
    private JObject? _emoteMap;
    private EmoteClipPlayer?[] _emotePlayers = new EmoteClipPlayer?[2];
    private int _emoteLayer, _emoteVersion, _emoteSpeechVersion;
    private bool? _pillShowing;
    private long _emoteStarted;
    private IDisposable? _emoteHold;
    internal string? CurrentEmoteClip { get; private set; }
    internal Action<int, Action> EmoteSchedule = (ms, fire) => DispatcherTimer.RunOnce(fire, TimeSpan.FromMilliseconds(ms));
    // Portrait playback is absent in this head. Kept explicit at the rule boundary.
    internal bool EmotePortraitMode { get; set; }
    internal string? CurrentEmoteFolder => _emoteFolder;
    private static string? GenerationFolder()
    {
        using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
        using var reader = new StreamReader(stream);
        return AvatarGenerationRule.ParseRegistry(reader.ReadToEnd()).generations
            .FirstOrDefault(e => string.Equals(e.modId, CoreMods.ActiveModId, StringComparison.OrdinalIgnoreCase)
                && AvatarGenerationRule.IsV2(e.generation)).folder;
    }
    internal void UpdateGenerationPill()
    {
        var pill = this.FindControl<Border>("BtnAvatarGeneration")!;
        bool has = GenerationFolder() != null;
        bool show = AvatarGenerationRule.PillVisible(has, EmotePortraitMode, _possessionGlitchLayer.IsVisible);
        bool v2 = AvatarGenerationRule.IsV2(CoreSettings.Current.AvatarGeneration);
        foreach (int n in new[] { 1, 2 })
        {
            bool on = (n == 2) == v2;
            this.FindControl<Border>($"PillSegV{n}")!.Background =
                this.TryFindResource(on ? "DepthPressedShade" : "DepthRaisedSheen", out var brush) ? brush as IBrush : Brushes.Transparent;
            this.FindControl<TextBlock>($"PillTxtV{n}")!.Foreground = on
                ? (this.TryFindResource("PinkBrush", out var pink) ? pink as IBrush : Brushes.HotPink)
                : new SolidColorBrush(Color.Parse("#B8A9C6"));
        }
        ToolTip.SetTip(pill, Loc.Get(v2 ? "tube_pill_v2_tip" : "tube_pill_v1_tip"));
        if (_pillShowing == show) return;
        _pillShowing = show;
        int ms = show ? GentleVisuals.RiseMs(GentleVisuals.FadeMs(180)) : GentleVisuals.FadeMs(140);
        pill.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) } };
        pill.IsHitTestVisible = show;
        if (show) pill.IsVisible = true;
        pill.Opacity = show ? 1 : 0;
        if (!show) DispatcherTimer.RunOnce(() =>
        {
            if (_pillShowing != false) return;
            // Glitch keeps the plate slot, matching WPF Hidden.
            pill.IsVisible = has && !EmotePortraitMode && _possessionGlitchLayer.IsVisible;
        }, TimeSpan.FromMilliseconds(ms));
    }
    private void GenerationPillPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) FlipEmoteGeneration();
    }
    internal void FlipEmoteGeneration()
    {
        if (!AvatarGenerationRule.PillVisible(GenerationFolder() != null, EmotePortraitMode, _possessionGlitchLayer.IsVisible)) return;
        CoreSettings.Current.AvatarGeneration = AvatarGenerationRule.Toggle(CoreSettings.Current.AvatarGeneration);
        CoreSettings.Save();
        RefreshEmoteGeneration();
        UpdateQuickMenuState();
        PlayEmoteReaction("greeting", null);
    }
    internal void StopEmoteGeneration()
    {
        ++_emoteVersion;
        ++_emoteSpeechVersion; _emoteTalking = false;
        _emoteHold?.Dispose();
        foreach (var player in _emotePlayers) player?.Dispose();
        _emotePlayers = new EmoteClipPlayer?[2];
        foreach (var name in new[] { "ImgAvatarAnimated", "ImgAvatarAnimatedB" }) this.FindControl<Image>(name)!.IsVisible = false;
        this.FindControl<Image>("ImgAvatar")!.IsVisible = true;
        ApplyAvatarTransform(_currentAvatarSet);
        _emoteFolder = null;
        _emoteMap = null; CurrentEmoteClip = null;
    }
    private void RefreshEmoteGeneration()
    {
        StopEmoteGeneration();
        UpdateGenerationPill();
        if (EmotePortraitMode) return;
        // V1 retains the static fallback when no emote set exists. Generation rows never enter arrows.
        _emoteFolder = AvatarGenerationRule.ResolveFolder(CoreSettings.Current.AvatarGeneration, GenerationFolder(), () =>
        {
            var local = Path.Combine(CoreMods.ActiveModPackage?.InstalledPath ?? "", "resources", "emotes", $"set{_currentAvatarSet}");
            if (File.Exists(Path.Combine(local, "emotes.json"))) return local;
            using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
            using var reader = new StreamReader(stream);
            return AvatarGenerationRule.ParseRegistry(reader.ReadToEnd()).sets.FirstOrDefault(e =>
                e.modId == CoreMods.ActiveModId && e.set == _currentAvatarSet).folder;
        });
        if (_emoteFolder == null) return;
        try
        {
            using var stream = OpenEmote("emotes.json");
            using var reader = new StreamReader(stream);
            _emoteMap = JObject.Parse(reader.ReadToEnd());
            _poseTimer.Stop();
            var layout = _emoteMap["layout"];
            double scale = (double?)layout?["scale"] ?? 1;
            _avatarBorder.RenderTransform = new TransformGroup { Children = {
                new ScaleTransform(scale, scale), new TranslateTransform((double?)layout?["offsetX"] ?? 0, (double?)layout?["offsetY"] ?? 0) } };
            _ = PlayEmote("idle");
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Emote map unavailable"); StopEmoteGeneration(); }
    }

    private Uri EmoteUri(string file) => Path.IsPathRooted(_emoteFolder!)
        ? new Uri(Path.Combine(_emoteFolder!, file)) : new Uri($"avares://CCP.Avalonia/Resources/{_emoteFolder}/{file}");
    private Stream OpenEmote(string file) => EmoteUri(file) is var uri && uri.IsFile ? File.OpenRead(uri.LocalPath) : AssetLoader.Open(uri);

    internal void PlayEmoteReaction(string? stem, string? mood, bool skipHold = false)
    {
        if (_emoteMap == null) return;
        var prefixes = _emoteMap["stemPrefix"] as JObject;
        string? clip = prefixes?.Properties().OrderByDescending(p => p.Name.Length)
            .FirstOrDefault(p => stem?.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase) == true)?.Value.Value<string>();
        clip ??= (string?)(_emoteMap["mood"] as JObject)?.GetValue(mood?.Split(',')[0].Trim() ?? "", StringComparison.OrdinalIgnoreCase);
        StartEmoteReaction(clip ?? PickExpressive() ?? PickEmoteIdle(), skipHold || stem == "greeting");
    }
    private string? PickExpressive() => (_emoteMap?["expressive"]?["pool"] as JArray)?.Values<string>()
        .OrderBy(c => c == CurrentEmoteClip ? 1 : 0).ThenBy(_ => _random.Next()).FirstOrDefault();
    private void StartEmoteReaction(string clip, bool skipHold = false)
    {
        int remaining = Math.Max(0, ((int?)_emoteMap?["minClipMs"] ?? 2500) - (int)(Environment.TickCount64 - _emoteStarted));
        if (skipHold || remaining == 0) { _ = PlayEmote(clip); return; }
        _emoteHold?.Dispose();
        _emoteHold = DispatcherTimer.RunOnce(() => { _ = PlayEmote(clip); }, TimeSpan.FromMilliseconds(remaining));
    }

    internal string PickEmoteIdle()
    {
        var all = (_emoteMap?["idleRotation"] as JArray)?.ToArray() ?? Array.Empty<JToken>();
        var pool = all.Where(e => (string?)e["clip"] != CurrentEmoteClip).ToArray();
        if (pool.Length == 0) pool = all;
        int sum = pool.Sum(e => Math.Max(1, (int?)e["weight"] ?? 1));
        if (sum == 0) return "idle";
        int pick = _random.Next(sum);
        foreach (var entry in pool) if ((pick -= Math.Max(1, (int?)entry["weight"] ?? 1)) < 0) return (string?)entry["clip"] ?? "idle";
        return "idle";
    }

    internal async System.Threading.Tasks.Task PlayEmote(string clip)
    {
        if (_emoteMap == null || _avatarSetsClosed) return;
        CurrentEmoteClip = clip;
        int version = ++_emoteVersion, next = 1 - _emoteLayer;
        var image = this.FindControl<Image>(next == 0 ? "ImgAvatarAnimated" : "ImgAvatarAnimatedB")!;
        var outgoing = this.FindControl<Image>(_emoteLayer == 0 ? "ImgAvatarAnimated" : "ImgAvatarAnimatedB")!;
        _emotePlayers[next]?.Dispose();
        var player = _emotePlayers[next] = new EmoteClipPlayer(image);
        // WPF CirceEmotes.cs:1333-1340: a missing optional reaction must not stop rotation.
        void Recover() { if (version == _emoteVersion && clip != "idle") { _emoteTalking = false; _ = PlayEmote("idle"); } }
        player.Failed += (_, _) => Recover();
        player.Completed += sender => { if (version == _emoteVersion && !_emoteTalking) _ = PlayEmote(PickEmoteIdle()); };
        try
        {
            var uri = EmoteUri(clip + ".webp");
            if (!(uri.IsFile ? File.Exists(uri.LocalPath) : AssetLoader.Exists(uri))) uri = EmoteUri(clip + ".gif");
            await player.Load(uri);
            if (version != _emoteVersion || !player.IsReady) return;
            int fade = GentleVisuals.FadeMs((int?)_emoteMap?["fadeMs"] ?? 1000);
            image.Transitions = outgoing.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(fade) } };
            image.IsVisible = true;
            image.Opacity = 1;
            outgoing.Opacity = 0;
            this.FindControl<Image>("ImgAvatar")!.IsVisible = false;
            _emoteLayer = next;
            _emoteStarted = Environment.TickCount64;
            DispatcherTimer.RunOnce(() => { if (version == _emoteVersion) _emotePlayers[1 - next]?.Stop(); }, TimeSpan.FromMilliseconds(fade));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Emote clip unavailable: {Clip}", clip); Recover(); }
    }
    // WPF CirceEmotes.cs:623-726, 783-798, 845-860: voice first, authored mouth joins, early tail.
    private bool _emoteTalking;
    internal async System.Threading.Tasks.Task PlayEmoteSpeech(string text, string? audio, string? mood)
    {
        if (_emoteMap == null) return;
        _emoteTalking = false;
        _emoteHold?.Dispose();
        int version = ++_emoteSpeechVersion;
        long began = Environment.TickCount64;
        double duration = string.IsNullOrWhiteSpace(text) ? 2500 : Math.Clamp(0.45 * text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length + 0.8, 2, 7) * 1000;
        if (audio != null && ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.Shared is { } vlc)
            try { using var media = new LibVLCSharp.Shared.Media(vlc, audio, LibVLCSharp.Shared.FromType.FromPath);
                await media.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseLocal, 2000);
                if (media.Duration > 0) duration = media.Duration; } catch { }
        if (version != _emoteSpeechVersion || _emoteMap == null) return;
        var pool = _emoteMap["talking"]?["pool"] as JArray;
        bool nonverbal = System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+)(?:[\s,.~!\-]+(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+))*[\s,.~!\-]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (nonverbal) { StartEmoteReaction(PickExpressive() ?? PickEmoteIdle()); return; }
        if (pool == null || pool.Count == 0 || duration <= 1400) { PlayEmoteReaction(audio == null ? null : Path.GetFileNameWithoutExtension(audio), mood); return; }
        var timing = new JObject();
        try { using var stream = OpenEmote("clip_timing.json"); using var reader = new StreamReader(stream); timing = JObject.Parse(reader.ReadToEnd()); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Optional emote timing unavailable"); }
        int start = Math.Max((int?)_emoteMap["talkStartDelayMs"] ?? 1000, Math.Max(0, ((int?)_emoteMap["minClipMs"] ?? 2500) - (int)(began - _emoteStarted)));
        int end = (int)duration - ((int?)_emoteMap["talkLeadOutMs"] ?? 500);
        if (end - start < 500) { PlayEmoteReaction(audio == null ? null : Path.GetFileNameWithoutExtension(audio), mood); return; }
        _emoteTalking = true;
        int at = start, covered = start, count = Math.Min(pool.Count, (int?)_emoteMap["talking"]?["maxTalkClips"] ?? 3);
        string? last = null;
        var clips = pool.OrderBy(_ => _random.Next()).Take(count).Values<string>().ToArray();
        for (int i = 0; i < clips.Length; i++)
        {
            string clip = last = clips[i]!;
            Schedule(at, () => _ = PlayEmote(clip));
            covered = at + ((int?)timing[clip]?["speakEndMs"] ?? 3100);
            string next = clips[Math.Min(i + 1, clips.Length - 1)]!;
            at = Math.Max(at + ((int?)_emoteMap["minClipMs"] ?? 2500), covered - ((int?)timing[next]?["speakStartMs"] ?? 600));
            if (at >= end) break;
        }
        if (last != null && covered + 250 < end && at < end) Schedule(at, () => _ = PlayEmote(last));
        Schedule(end, () => { _emoteTalking = false; PlayEmoteReaction(audio == null ? null : Path.GetFileNameWithoutExtension(audio), mood, skipHold: true); });
        void Schedule(int ms, Action action) => EmoteSchedule((int)Math.Max(0, ms - (Environment.TickCount64 - began)), () =>
        { if (version == _emoteSpeechVersion && !_avatarSetsClosed) action(); });
    }
}
