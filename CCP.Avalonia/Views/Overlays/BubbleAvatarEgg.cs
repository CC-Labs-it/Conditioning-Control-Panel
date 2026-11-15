using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF BubbleService.cs:705-831. The head owns the window and media dependencies.
internal sealed class BubbleAvatarEgg
{
    internal sealed record Avatar(Func<AmbientBubble, CancellationToken, Task> Glide,
        Func<Task> Return, Action<string, string?> Speak);
    private readonly Func<bool> _enabled;
    private readonly Func<Avatar?> _avatar;
    private readonly List<AmbientBubble> _bubbles;
    private readonly Random _random;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, (string Text, string? Audio)?> _pick;
    private readonly Func<string, Task<double?>> _duration;
    private readonly Action<AmbientBubble, float> _pop;
    internal Func<int, CancellationToken, Task> Delay = (ms, ct) => Task.Delay(ms, ct);
    private CancellationTokenSource? _cancel;
    private AmbientBubble? _claimed;
    private DateTime _cooldown;
    internal Task? Running { get; private set; }

    internal BubbleAvatarEgg(Func<bool> enabled, Func<Avatar?> avatar, List<AmbientBubble> bubbles,
        Random random, Func<DateTime> clock, Func<string, (string Text, string? Audio)?> pick,
        Func<string, Task<double?>> duration, Action<AmbientBubble, float> pop)
    {
        _enabled = enabled; _avatar = avatar; _bubbles = bubbles; _random = random;
        _clock = clock; _pick = pick; _duration = duration; _pop = pop;
    }

    internal static bool Gates(bool enabled, bool triggers, bool chaos, bool video) =>
        enabled && triggers && !chaos && !video;

    internal void Scan()
    {
        if (Running is { IsCompleted: false } || !_enabled() || _clock() < _cooldown) return;
        var avatar = _avatar();
        if (avatar == null) return;
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            if (!b.TryClaimForAvatar(_random, _clock())) continue;
            _claimed = b;
            _cancel = new CancellationTokenSource();
            Running = Run(b, avatar, _cancel.Token);
            break;
        }
    }

    internal void Cancel()
    {
        _cancel?.Cancel();
        _claimed?.ReleaseAvatarClaim();
    }

    private async Task Run(AmbientBubble b, Avatar avatar, CancellationToken ct)
    {
        try
        {
            await avatar.Glide(b, ct);
            ct.ThrowIfCancellationRequested();
            if (!_bubbles.Contains(b) || b.Popping) return;
            string id = b.Spec?.VariantId ?? "";
            string rule = id switch
            {
                "flash" or "subliminal" or "pink" or "spiral" or "braindrain" or "glitch" => "egg_avatar_pop_" + id,
                _ => "egg_avatar_pop_generic"
            };
            var line = _pick(rule) ?? (rule == "egg_avatar_pop_generic" ? null : _pick("egg_avatar_pop_generic"))
                ?? (Loc.Get("bubble_avatar_pop_fallback"), (string?)null);
            double? seconds = line.Item2 != null ? await _duration(line.Item2).WaitAsync(ct) : null;
            ct.ThrowIfCancellationRequested();
            if (!_bubbles.Contains(b) || b.Popping) return;
            avatar.Speak(line.Item1, line.Item2);
            int ms = seconds is > 0 ? (int)Math.Clamp(seconds.Value * 1000 + 900, 1500, 6000)
                : Math.Clamp(line.Item1.Length * 55 + 600, 1500, 4500);
            await Delay(ms, ct);
            ct.ThrowIfCancellationRequested();
            if (!_bubbles.Contains(b) || b.Popping) return;
            b.ReleaseAvatarClaim();
            _pop(b, 1.5f);
            await Delay(2500, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("Avatar bubble egg: {E}", ex.Message); }
        finally
        {
            b.ReleaseAvatarClaim();
            _claimed = null;
            try { await avatar.Return(); }
            catch (Exception ex) { Log.Debug("Avatar bubble return: {E}", ex.Message); }
            _cooldown = _clock().AddSeconds(60);
            _cancel?.Dispose();
            _cancel = null;
        }
    }

    private static VideoMetadataCache? _lengths;
    internal static async Task<double?> ClipDuration(string path)
    {
        if (!File.Exists(path) || LibVlcAudio.Shared is not { } vlc) return null;
        _lengths ??= new VideoMetadataCache(vlc);
        return await _lengths.GetOrComputeDurationAsync(path);
    }
}
