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
    private CancellationTokenSource? _audio;
    private Action? _stop;
    private long? _duckGeneration;
    internal Func<DateTime> Now = () => DateTime.Now;
    internal Func<int, CancellationToken, Task> Delay = Task.Delay;
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
        _ = Dispatch(fired);
    }

    private async Task Dispatch(List<KeywordTrigger> fired)
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

        // WPF :1692-1695,1742-1745 ducks even an idle legacy response for 500ms.
        if (legacy && !(CoreSession.IsEngineRunning || CoreSession.IsSessionRunning) &&
            first.DuckAudio && CoreSettings.Current.AudioDuckingEnabled)
        {
            CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            _ = ReleaseLegacyDuck(CoreAudio.DuckGeneration);
        }

        // WPF :1363-1367,1491-1518 awaits the repeat gaps before the next action.
        foreach (var action in actions)
        {
            switch (action)
            {
                case PlayAudioAction audio:
                    if ((CoreSession.IsEngineRunning || CoreSession.IsSessionRunning) && !await Play(audio)) return;
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
    }

    private async Task ReleaseLegacyDuck(long generation)
    {
        try { await Delay(500, CancellationToken.None); CoreAudio.Unduck(generation); }
        catch (Exception ex) { Log.Debug(ex, "Keyword legacy unduck failed"); }
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
        _audio?.Cancel(); _audio?.Dispose(); _audio = null;
        var stop = _stop; _stop = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Keyword clip stop failed"); }
        if (_duckGeneration is { } generation) CoreAudio.Unduck(generation);
        _duckGeneration = null;
    }

    private async Task<bool> Play(PlayAudioAction audio)
    {
        if (string.IsNullOrEmpty(audio.FilePath)) return true;
        var path = audio.FilePath;
        if (!Path.IsPathRooted(path))
        {
            var resource = ContentLocator.Resolve(Path.Combine("Resources", path));
            var legacy = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
            path = File.Exists(resource) ? resource : File.Exists(legacy) ? legacy : path;
        }
        if (!File.Exists(path)) return true;
        StopAudio();
        var cancellation = _audio = new CancellationTokenSource();
        var token = cancellation.Token;
        if (audio.DuckSystemAudio && CoreSettings.Current.AudioDuckingEnabled)
        {
            CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            _duckGeneration = CoreAudio.DuckGeneration;
        }
        try
        {
            var count = Math.Max(1, audio.PlayCount);
            for (var i = 0; i < count; i++)
            {
                if (i > 0 && audio.DelayBetweenMs > 0) await Delay(audio.DelayBetweenMs, token);
                token.ThrowIfCancellationRequested();
                _stop?.Invoke();
                var last = i == count - 1;
                _stop = CoreAudio.PlayStoppable(path,
                    KeywordMatchRules.AudioVolume(audio.Volume, CoreSettings.Current.MasterVolume), "keyword-trigger",
                    onFinished: () => { if (last) Dispatcher.UIThread.Post(async () =>
                    {
                        try { await Delay(500, token); if (!token.IsCancellationRequested) StopAudio(); }
                        catch (OperationCanceledException) { }
                    }); });
            }
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopAudio(); return false; }
    }
}
