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

    internal static void Stop()
    {
        Interlocked.Increment(ref _generation);
        if (_subscribed) Win32PanicKey.KeyDown -= OnKey;
        _subscribed = false;
        Engine.Stop();
    }

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
            GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                var foreground = Foreground(processId);
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
        // WPF merge: preset first, and only the first enabled PlayAudio action wins.
        var audio = fired.SelectMany(t => t.Actions ?? new()).OfType<PlayAudioAction>().FirstOrDefault(a => a.Enabled);
        if (audio == null && fired.Count == 1 && (fired[0].Actions == null || fired[0].Actions.Count == 0))
        {
            var t = fired[0];
            audio = new PlayAudioAction { FilePath = t.AudioFilePath, Volume = t.AudioVolume,
                PlayCount = t.AudioPlayCount, DelayBetweenMs = t.AudioDelayBetweenMs, DuckSystemAudio = t.DuckAudio };
        }
        if (audio != null) Play(audio);
    }

    internal void FireDemo(string keyword, string source)
    {
        var t = CoreSettings.Current.KeywordTriggers.FirstOrDefault(t =>
            string.Equals(t.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (t?.Actions?.OfType<PlayAudioAction>().FirstOrDefault(a => a.Enabled) is { } audio) Play(audio);
    }

    private void StopAudio()
    {
        _audio?.Cancel(); _audio?.Dispose(); _audio = null;
        var stop = _stop; _stop = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Keyword clip stop failed"); }
        if (_duckGeneration is { } generation) CoreAudio.Unduck(generation);
        _duckGeneration = null;
    }

    private void Play(PlayAudioAction audio)
    {
        if (string.IsNullOrEmpty(audio.FilePath)) return;
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
                    if (i > 0 && audio.DelayBetweenMs > 0) await Task.Delay(audio.DelayBetweenMs, token);
                    token.ThrowIfCancellationRequested();
                    _stop?.Invoke();
                    var last = i == count - 1;
                    _stop = CoreAudio.PlayStoppable(path,
                        KeywordMatchRules.AudioVolume(audio.Volume, CoreSettings.Current.MasterVolume), "keyword-trigger",
                        onFinished: () => { if (last) Dispatcher.UIThread.Post(async () =>
                        {
                            try { await Task.Delay(500, token); if (!token.IsCancellationRequested) StopAudio(); }
                            catch (OperationCanceledException) { }
                        }); });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopAudio(); }
        }
    }
}
