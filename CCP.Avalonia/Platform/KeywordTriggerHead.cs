using System;
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

/// <summary>Keyboard/audio and exact subliminal slice of WPF KeywordTriggerService. OCR and other actions remain a follow-up.</summary>
internal static class KeywordTriggerHead
{
    internal static KeywordAudioRuntime Engine { get; } = new();
    private static bool _subscribed;
    private static int _generation;
    internal static void Sync()
    {
        if (CoreSettings.Current.KeywordTriggersEnabled && KeywordAudioRuntime.HasAccess)
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
    // WPF KeywordTriggerService.cs:176-188: access gate, and Start must preserve a live buffer.
    internal static bool HasAccess => CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness");
    internal void Start() { if (IsActive || !HasAccess) return; IsActive = true; _input.Clear(); }
    internal void Stop() { IsActive = false; _input.Clear(); StopAudio(); }

    private bool Accept(string? process, bool own)
    {
        var s = CoreSettings.Current;
        if (!IsActive || !s.KeywordTriggersEnabled ||
            !HasAccess) return false;
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
        // WPF merge: preset first, and only the first enabled PlayAudio action wins.
        var audio = fired.SelectMany(t => t.Actions ?? new()).OfType<PlayAudioAction>().FirstOrDefault(a => a.Enabled);
        if (audio == null && fired.Count == 1 && (fired[0].Actions == null || fired[0].Actions.Count == 0))
        {
            var t = fired[0];
            audio = new PlayAudioAction { FilePath = t.AudioFilePath, Volume = t.AudioVolume,
                PlayCount = t.AudioPlayCount, DelayBetweenMs = t.AudioDelayBetweenMs, DuckSystemAudio = t.DuckAudio };
        }
        if (audio != null) Play(audio);
        FireExactSubliminal(fired);
    }

    private void FireExactSubliminal(System.Collections.Generic.List<KeywordTrigger> fired)
    {
        // WPF KeywordTriggerService.cs:1257-1313,1713,1774-1782.
        // A merged response borrows the first keyword and deduplicates each visual effect.
        var exact = fired.SelectMany(t => t.Actions ?? new()).OfType<VisualEffectAction>()
            .Any(a => a.Enabled && a.Effect == KeywordVisualEffect.ExactSubliminal);
        if (fired.Count == 1 && (fired[0].Actions == null || fired[0].Actions.Count == 0))
            exact = fired[0].VisualEffect == KeywordVisualEffect.ExactSubliminal;
        if (!exact || ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
        try
        {
            // WPF SubliminalService.cs:311-324: custom one-shot has no linked whisper.
            var text = fired[0].Keyword.ToUpperInvariant().Trim();
            if (text.Length > 200) text = text.Substring(0, 200);
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            Views.Overlays.SubliminalWhisperShow.HapticThenDraw(text);
            CoreProgression.AddXP(10, "Subliminal");
            // Mandatory echo protection, independent of the optional loop-protection toggle.
            _cooldowns.Mute(fired[0].Keyword, 3000, Now().ToUniversalTime());
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword exact subliminal failed"); }
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

    private void Play(PlayAudioAction audio)
    {
        // WPF KeywordTriggerService.cs:1398,1841-1855: unsolicited audio requires an engine or session.
        if ((!CoreSession.IsEngineRunning && !CoreSession.IsSessionRunning) || string.IsNullOrEmpty(audio.FilePath)) return;
        var path = audio.FilePath;
        if (!Path.IsPathRooted(path))
        {
            var resource = ContentLocator.Resolve(Path.Combine("Resources", path));
            var legacy = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
            path = File.Exists(resource) ? resource : File.Exists(legacy) ? legacy : path;
        }
        if (!File.Exists(path)) return;
        StopAudio();
        var cancellation = _audio = new CancellationTokenSource();
        var token = cancellation.Token;
        if (audio.DuckSystemAudio && CoreSettings.Current.AudioDuckingEnabled)
        {
            CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            _duckGeneration = CoreAudio.DuckGeneration;
        }
        _ = Repeat();
        async Task Repeat()
        {
            try
            {
                var count = Math.Max(1, audio.PlayCount);
                for (var i = 0; i < count; i++)
                {
                    if (i > 0 && audio.DelayBetweenMs > 0) await Delay(audio.DelayBetweenMs, token);
                    token.ThrowIfCancellationRequested();
                    // Old lane's stricter hold: also check after every repeat gap.
                    if (!CoreSession.IsEngineRunning && !CoreSession.IsSessionRunning) { StopAudio(); return; }
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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopAudio(); }
        }
    }
}
