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
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Keyboard/audio slice of WPF KeywordTriggerService. OCR and the other actions remain a follow-up.</summary>
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
    private CancellationTokenSource _pendingAudio = new();
    private long _voiceGeneration;
    private Action? _stop;
    private long? _duckGeneration;
    private readonly System.Collections.Generic.HashSet<object> _ducks = new();
    private Action? _releaseVoice;
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
        // WPF KeywordTriggerService.cs:1257-1267: record the match, but do not dispatch
        // effects or audio during the five-minute post-panic quiet.
        if (PanicAftermath.TriggersSilenced()) return;

        _ = Dispatch(fired);
    }

    // WPF KeywordTriggerService.cs:1250-1255,1283-1294,1361-1367,1490-1518,
    // 1674-1713: preserve action order and wait for repeat gaps, including legacy fields.
    private async Task Dispatch(System.Collections.Generic.List<KeywordTrigger> fired)
    {
        try
        {
            var legacy = fired.Count == 1 && (fired[0].Actions == null || fired[0].Actions.Count == 0);
            if (legacy)
            {
                var t = fired[0];
                var audio = new PlayAudioAction { FilePath = t.AudioFilePath, Volume = t.AudioVolume,
                    PlayCount = t.AudioPlayCount, DelayBetweenMs = t.AudioDelayBetweenMs, DuckSystemAudio = t.DuckAudio };
                if (!await Play(audio) || !IsActive || PanicAftermath.TriggersSilenced()) return;
                if (t.VisualEffect == KeywordVisualEffect.BrainDrain) new BrainDrainMeltPayload().Fire();
                return;
            }

            bool audioSeen = false, drainSeen = false;
            var actions = fired.SelectMany(t => t.Actions ?? new()).Where(a => a != null && a.Enabled).ToList();
            foreach (var action in actions)
            {
                if (!IsActive || PanicAftermath.TriggersSilenced()) return;
                if (action is PlayAudioAction audio && (fired.Count == 1 || !audioSeen))
                {
                    audioSeen = true;
                    if (!await Play(audio)) return;
                }
                else if (action is VisualEffectAction { Effect: KeywordVisualEffect.BrainDrain }
                    && (fired.Count == 1 || !drainSeen))
                {
                    drainSeen = true;
                    new BrainDrainMeltPayload().Fire();
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Keyword action dispatch failed"); }
    }

    // ponytail: WPF FireDemoTrigger (KeywordTriggerService :394-424) checks HasAccess (#887) and fires a synthetic
    // Highlight+AvatarComment trigger with no audio and no settings; both actions land with wire-shell-keyword-triggers-b.
    internal void FireDemo(string keyword, string source) { }

    // WPF KeywordTriggerService.cs:1512-1517,1859-1865: replacing the playing clip
    // must not cancel another match waiting between repeats. Panic/stop cancels all of them.
    internal void StopAudio()
    {
        var pending = _pendingAudio;
        _pendingAudio = new CancellationTokenSource();
        pending.Cancel(); pending.Dispose();
        StopCurrentAudio();
        _ducks.Clear();
        if (_duckGeneration is { } generation) CoreAudio.Unduck(generation);
        _duckGeneration = null;
    }

    private void StopCurrentAudio()
    {
        ++_voiceGeneration;
        var stop = _stop; _stop = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Keyword clip stop failed"); }
        var release = _releaseVoice; _releaseVoice = null; release?.Invoke();
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
        StopCurrentAudio();
        var token = _pendingAudio.Token;
        var owner = new object();
        if (audio.DuckSystemAudio && CoreSettings.Current.AudioDuckingEnabled)
        {
            if (_ducks.Count == 0) { CoreAudio.Duck(CoreSettings.Current.DuckingLevel); _duckGeneration = CoreAudio.DuckGeneration; }
            _ducks.Add(owner);
        }
        void Release() { if (_ducks.Remove(owner) && _ducks.Count == 0 && _duckGeneration is { } g) { CoreAudio.Unduck(g); _duckGeneration = null; } }
        try
        {
            var count = Math.Max(1, audio.PlayCount);
            for (var i = 0; i < count; i++)
            {
                if (i > 0 && audio.DelayBetweenMs > 0) await Delay(audio.DelayBetweenMs, token);
                token.ThrowIfCancellationRequested();
                StopCurrentAudio();
                var generation = ++_voiceGeneration;
                var last = i == count - 1;
                _releaseVoice = last ? Release : null;
                _stop = CoreAudio.PlayStoppable(path,
                    KeywordMatchRules.AudioVolume(audio.Volume, CoreSettings.Current.MasterVolume), "keyword-trigger",
                    onFinished: () => { if (last) Dispatcher.UIThread.Post(async () =>
                    {
                        try { await Delay(500, token); if (!token.IsCancellationRequested && generation == _voiceGeneration) StopCurrentAudio(); else Release(); }
                        catch (OperationCanceledException) { }
                    }); });
            }
            return true;
        }
        catch (OperationCanceledException) { Release(); return false; }
        catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopCurrentAudio(); Release(); return false; }
    }
}
