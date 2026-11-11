using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Keyboard responses from WPF KeywordTriggerService. OCR and other actions remain a follow-up.</summary>
internal static class KeywordTriggerHead
{
    internal static KeywordAudioRuntime Engine { get; } = new();
    private static bool _subscribed;
    private static int _generation;
    internal static void Sync()
    {
        if (CoreSettings.Current.KeywordTriggersEnabled)
        {
            Engine.Start();
            if (!_subscribed && OperatingSystem.IsWindows() &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")))
            {
                Win32PanicKey.KeyDown += OnKey;
                _subscribed = true;
                Win32PanicKey.EnsureListening(); // shares the existing hook and its panic binding
            }
        }
        else Stop();
    }

    // The hook itself stays: on this head the panic key (Win32Input.Start at boot) owns it, the case WPF :426 keeps it for.
    internal static void Stop() { DetachKeyboard(); Engine.Stop(); }

    /// <summary>WPF MainWindow.Awareness.cs:493: Keyboard off drops only the key source; the engine and a playing clip stay.</summary>
    internal static void DetachKeyboard()
    {
        Interlocked.Increment(ref _generation);
        if (_subscribed) Win32PanicKey.KeyDown -= OnKey;
        _subscribed = false;
    }

    /// <summary>Panic cuts the clip; WPF leaves the trigger service itself running.</summary>
    internal static void StopAudio() => Engine.StopAudio();

    private static void OnKey(int vk, string? name)
    {
        // Hook thread: translate now (modifier state belongs to this press), then post. Never break panic.
        try
        {
            var special = vk switch
            {
                0x0D or 0x1B or 0x09 => (KeywordInputKey?)KeywordInputKey.Clear,
                0x08 => KeywordInputKey.Backspace,
                0x20 => KeywordInputKey.Space,
                _ => null,
            };
            var generation = Volatile.Read(ref _generation);
            var ch = special == null ? Translate(vk) : null;
            uint processId = 0;
            var needsForeground = KeywordMatchRules.NeedsForeground(CoreSettings.Current);
            if (needsForeground) GetWindowThreadProcessId(GetForegroundWindow(), out processId);
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                var foreground = needsForeground ? Foreground(processId) : (null, false);
                if (special is { } key) Engine.Key(key, foreground.Process, foreground.Own);
                else if (ch is { } c) Engine.Character(c, foreground.Process, foreground.Own);
                else Engine.Key(KeywordInputKey.None, foreground.Process, foreground.Own);
            });
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword input failed"); }
    }

    private static (string? Process, bool Own) Foreground(uint id)
    {
        try
        {
            using var p = Process.GetProcessById((int)id);
            return (p.ProcessName, id == (uint)Environment.ProcessId);
        }
        catch { return (null, false); }
    }

    private static char? Translate(int vk)
    {
        var state = new byte[256];
        foreach (var modifier in new[] { 0x10, 0x11, 0x12 })
            if (GetAsyncKeyState(modifier) < 0) state[modifier] = 0x80;
        state[0x14] = (byte)(GetKeyState(0x14) & 1);
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        var text = new StringBuilder(4);
        // Read without changing the foreground application's dead-key state.
        if (ToUnicodeEx((uint)vk, MapVirtualKey((uint)vk, 0), state, text, 4, 4, layout) != 1) return null;
        return char.IsControl(text[0]) ? null : text[0];
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint key, uint mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(
        uint key, uint scan, byte[] state, StringBuilder text, int capacity, uint flags, IntPtr layout);
}

internal sealed class KeywordAudioRuntime
{
    private readonly KeywordTextInput _input = new();
    private readonly KeywordCooldowns _cooldowns = new();
    private readonly SemaphoreSlim _durationGate = new(1, 1);
    private CancellationTokenSource _audio = new();
    private Action? _stop;
    internal Func<DateTime> Now = () => DateTime.Now;
    internal Func<int, CancellationToken, Task> Delay = Task.Delay;
    internal Func<long> Clock = () => Environment.TickCount64;
    internal Func<string, CancellationToken, Task<double?>>? DurationProbe;
    internal Action BubbleEffect = () =>
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } host)
            Views.Overlays.BubbleOverlay.SpawnOnce(host);
    };
    internal bool IsActive { get; private set; }
    internal void Start() { IsActive = true; _input.Clear(); }
    internal void Stop() { IsActive = false; _input.Clear(); StopAudio(); }

    private bool Accept(string? process, bool own)
    {
        var s = CoreSettings.Current;
        if (!IsActive || !s.KeywordTriggersEnabled ||
            !(CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness"))) return false;
        if (KeywordMatchRules.AppAllowed(s, process, own)) return true;
        _input.Clear(); // never join text typed on either side of a blocked app
        return false;
    }

    internal void Character(char ch, string? process = null, bool own = false)
    {
        if (!Accept(process, own)) return;
        _input.Append(ch, Now(), CoreSettings.Current.KeywordBufferTimeoutMs);
        Check();
    }

    internal void Key(KeywordInputKey key, string? process = null, bool own = false)
    {
        if (Accept(process, own) && _input.Key(key, Now(), CoreSettings.Current.KeywordBufferTimeoutMs)) Check();
    }

    private void Check()
    {
        var s = CoreSettings.Current;
        var now = Now();
        if ((now - _cooldowns.LastGlobal).TotalSeconds < s.KeywordGlobalCooldownSeconds) return;
        var fired = KeywordMatchRules.Collect(s.KeywordTriggers, _input.Text, now,
            word => _cooldowns.IsMuted(word, now.ToUniversalTime()));
        if (fired.Count == 0) return;
        _input.Clear();
        _cooldowns.LastGlobal = now;
        foreach (var t in fired)
        {
            t.LastTriggeredAt = now;
            _cooldowns.Record(t, s, now.ToUniversalTime());
        }
        // WPF KeywordTriggerService.cs:1263-1269 holds dispatch during panic quiet.
        if (ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
        _ = Dispatch(fired, _audio.Token);
    }

    private async Task Dispatch(List<KeywordTrigger> fired, CancellationToken cancellation)
    {
        var first = fired[0];
        var legacy = fired.Count == 1 && (first.Actions == null || first.Actions.Count == 0);
        // WPF KeywordTriggerService.cs:1681-1689: legacy HighlightOnly returns before all responses.
        if (legacy && first.VisualEffect == KeywordVisualEffect.HighlightOnly) return;

        var actions = new List<KeywordAction>();
        if (legacy)
        {
            actions.Add(new PlayAudioAction { FilePath = first.AudioFilePath, Volume = first.AudioVolume,
                PlayCount = first.AudioPlayCount, DelayBetweenMs = first.AudioDelayBetweenMs, DuckSystemAudio = first.DuckAudio });
            actions.Add(new VisualEffectAction { Effect = first.VisualEffect });
            if (first.HapticEnabled) actions.Add(new HapticAction { Intensity = first.HapticIntensity });
            if (first.XPAward > 0) actions.Add(new AddXpAction { Amount = first.XPAward });
        }
        else
        {
            // WPF :1271-1294 keeps every action for one trigger, but deduplicates merged fires
            // by type (visual effects by variant), preserving the first action's position.
            var seen = new HashSet<string>();
            foreach (var trigger in fired)
                foreach (var action in trigger.Actions ?? new())
                {
                    if (action == null || !action.Enabled) continue;
                    var key = action is VisualEffectAction visual ? $"VisualEffect:{visual.Effect}" : action.GetType().Name;
                    if (fired.Count == 1 || seen.Add(key)) actions.Add(action);
                }
        }

        // WPF :1351-1358 ducks once for the list, even when a requested clip is missing.
        // WPF :1692-1695 also ducks a legacy response while idle.
        var running = CoreSession.IsEngineRunning || CoreSession.IsSessionRunning;
        var ducked = CoreSettings.Current.AudioDuckingEnabled && (legacy ? first.DuckAudio :
            running && actions.OfType<PlayAudioAction>().Any(a => a.DuckSystemAudio));
        long generation = 0;
        if (ducked)
        {
            CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            generation = CoreAudio.DuckGeneration;
        }
        var released = 0;
        void ReleaseDuck()
        {
            if (ducked && Interlocked.Exchange(ref released, 1) == 0) CoreAudio.Unduck(generation);
        }
        using var registration = ducked ? cancellation.Register(ReleaseDuck) : default;
        var durations = new List<Task<TimeSpan>>();

        // WPF :1363-1367,1491-1518 awaits the repeat gaps before the next action.
        try
        {
            foreach (var action in actions)
            {
                cancellation.ThrowIfCancellationRequested();
                switch (action)
                {
                    case PlayAudioAction audio:
                        if (CoreSession.IsEngineRunning || CoreSession.IsSessionRunning)
                        {
                            var duration = await Play(audio, cancellation, ducked);
                            if (duration != null) durations.Add(duration);
                        }
                        break;
                    case VisualEffectAction visual:
                        FireVisual(visual.Effect, first.Keyword);
                        break;
                    case HapticAction haptic:
                        // WPF :1419-1424 uses the keyword pattern and its configured intensity.
                        _ = CoreHaptics.Service?.TriggerKeywordPatternAsync(first.Keyword, haptic.Intensity);
                        break;
                    case AddXpAction xp when xp.Amount > 0:
                        // WPF :1426-1436 applies the multiplier only during a session.
                        CoreProgression.AddXP(xp.Amount * (CoreSession.IsSessionRunning ? CoreSettings.Current.KeywordSessionMultiplier : 1), "KeywordTrigger");
                        break;
                }
            }
            if (ducked)
            {
                // WPF :1383-1387 waits the longest length plus 500ms from dispatch end; parse time counts.
                var dispatched = Clock();
                var lengths = await Task.WhenAll(durations).WaitAsync(cancellation);
                var longest = lengths.Length == 0 ? 0 : lengths.Max(d => d.TotalMilliseconds);
                var wait = Math.Ceiling(longest + 500 - (Clock() - dispatched));
                if (wait > 0) await Delay((int)Math.Min(int.MaxValue, wait), cancellation);
                cancellation.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warning(ex, "Keyword dispatch failed"); }
        finally { ReleaseDuck(); }
    }

    private void FireVisual(KeywordVisualEffect effect, string keyword)
    {
        try
        {
            // WPF KeywordTriggerService.cs:1768-1787. Reuse the installed head renderers.
            switch (effect)
            {
                case KeywordVisualEffect.SubliminalFlash:
                    if (CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                    break;
                case KeywordVisualEffect.ExactSubliminal:
                    // WPF Subliminal/SubliminalService.cs:314-323: custom text bypasses whisper lookup.
                    var text = keyword.ToUpperInvariant().Trim();
                    if (text.Length > 200) text = text.Substring(0, 200);
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        Views.Overlays.SubliminalWhisperShow.HapticThenDraw(text);
                        CoreProgression.AddXP(10, "Subliminal");
                    }
                    // Always mute, even with loop protection off. Never shorten an existing mute.
                    _cooldowns.Mute(keyword, 3000, Now().ToUniversalTime());
                    break;
                case KeywordVisualEffect.ImageFlash:
                    CoreFlash.ShowProvider?.Invoke();
                    break;
                case KeywordVisualEffect.Bubbles:
                    BubbleEffect();
                    break;
                case KeywordVisualEffect.MindWipe:
                    // WPF :1795-1798: it is audio, so hold it while idle and skip an empty pool.
                    if (CoreMindWipe.ClipCount > 0 && (CoreSession.IsEngineRunning || CoreSession.IsSessionRunning))
                        CoreMindWipe.TriggerOnce();
                    break;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword visual failed"); }
    }

    // ponytail: WPF FireDemoTrigger (KeywordTriggerService :394-424) checks HasAccess (#887) and fires a synthetic
    // Highlight+AvatarComment trigger with no audio and no settings; both actions land with wire-shell-keyword-triggers-b.
    internal void FireDemo(string keyword, string source) { }

    internal void StopAudio()
    {
        var cancellation = _audio; _audio = new CancellationTokenSource();
        cancellation.Cancel(); cancellation.Dispose();
        StopClip();
    }

    private void StopClip()
    {
        var stop = _stop; _stop = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Keyword clip stop failed"); }
    }

    internal async Task<double?> ReadDuration(string path, CancellationToken cancellation)
    {
        try
        {
            if (DurationProbe is { } probe) return await probe(path, cancellation).WaitAsync(cancellation);
            var lengths = Views.Overlays.MandatoryVideoOverlay.Lengths;
            if (lengths == null) return null;
            await _durationGate.WaitAsync(cancellation);
            try { return await lengths.GetOrComputeDurationAsync(path).WaitAsync(cancellation); }
            finally { _durationGate.Release(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex) { Log.Debug(ex, "Keyword clip duration unavailable"); return null; }
    }

    private async Task<Task<TimeSpan>?> Play(PlayAudioAction audio, CancellationToken cancellation, bool ducked)
    {
        if (string.IsNullOrEmpty(audio.FilePath)) return null;
        var path = audio.FilePath;
        if (!Path.IsPathRooted(path))
        {
            var resource = ContentLocator.Resolve(Path.Combine("Resources", path));
            var legacy = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
            path = File.Exists(resource) ? resource : File.Exists(legacy) ? legacy : path;
        }
        if (!File.Exists(path)) return null;
        // WPF :1873-1890 plays at once and takes TotalTime from the header; the parse runs beside playback.
        var probe = ducked ? ReadDuration(path, cancellation) : Task.FromResult<double?>(null);
        TaskCompletionSource<TimeSpan>? timing = null;
        var count = Math.Max(1, audio.PlayCount);
        for (var i = 0; i < count; i++)
        {
            if (i > 0 && audio.DelayBetweenMs > 0) await Delay(audio.DelayBetweenMs, cancellation);
            cancellation.ThrowIfCancellationRequested();
            // WPF :1863-1865 replaces only the clip, not another dispatch's pending actions.
            StopClip();
            var clip = timing = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Settle(clip, probe, finished.Task);
            _stop = CoreAudio.PlayStoppable(path,
                KeywordMatchRules.AudioVolume(audio.Volume, CoreSettings.Current.MasterVolume), "keyword-trigger",
                onStarted: duration => { if (duration > TimeSpan.Zero) clip.TrySetResult(duration); },
                onFinished: () => finished.TrySetResult());
        }
        return timing?.Task;
    }

    /// <summary>A replaced clip can stop before its Playing event reports a length, so the parsed length
    /// still counts; zero only once the parse has failed and the clip has ended.</summary>
    private static async Task Settle(TaskCompletionSource<TimeSpan> clip, Task<double?> probe, Task finished)
    {
        double? seconds = null;
        try { seconds = await probe; } catch (OperationCanceledException) { }
        if (seconds is > 0 && seconds < TimeSpan.MaxValue.TotalSeconds) clip.TrySetResult(TimeSpan.FromSeconds(seconds.Value));
        await finished;
        clip.TrySetResult(TimeSpan.Zero);
    }
}
