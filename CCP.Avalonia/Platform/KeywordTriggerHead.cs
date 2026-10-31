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

/// <summary>Keyboard slice of WPF KeywordTriggerService: audio, haptic, XP, Chaster, quest credit and the pulse feed. OCR and visual actions remain a follow-up.</summary>
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
            RecordFire(t, "Keyboard", now);   // WPF KeywordTriggerService.cs:1191-1200
            try { TriggerFired?.Invoke(t); } catch (Exception ex) { Log.Warning(ex, "Keyword fired handler failed"); }
        }
        LastDispatch = DispatchAsync(fired);
    }

    /// <summary>WPF TriggerFired (KeywordTriggerService :55). Raised on the UI thread, once per fired trigger.</summary>
    internal event Action<KeywordTrigger>? TriggerFired;

    // Test seams for the response actions (WPF KeywordTriggerService.cs:1393-1455).
    internal Action QuestCredit = () => App.Quests?.TrackKeywordTrigger();
    internal Action<string, double> Haptic = (keyword, intensity) => _ = CoreHaptics.Service?.TriggerKeywordPatternAsync(keyword, intensity);
    internal Action<double> AddXp = amount => CoreProgression.AddXP(amount, "KeywordTrigger");
    internal Func<int, int> ChasterBook = seconds => ChasterHead.Service?.NoteSeconds("watcher", seconds).AppliedSeconds ?? 0;
    internal Func<string> ChasterCapPath = () => Path.Combine(CorePaths.UserData, "chaster_trigger_caps.json");

    private const int PulseBufferCapacity = 20;   // WPF :64
    private readonly LinkedList<TriggerFireRecord> _recentFires = new();
    private KeywordTriggerChasterCap? _chasterCap;

    /// <summary>WPF GetRecentFires :72: newest first, for the Awareness "Last Detected" feed.</summary>
    internal IReadOnlyList<TriggerFireRecord> GetRecentFires() { lock (_recentFires) return _recentFires.ToArray(); }

    private void RecordFire(KeywordTrigger t, string source, DateTime now)
    {
        var record = new TriggerFireRecord { Keyword = t.Keyword, TriggerId = t.Id, VisualEffect = t.VisualEffect,
            Source = source, FiredAt = now, ActionKeys = ActionKeys(t) };
        lock (_recentFires)
        {
            _recentFires.AddFirst(record);
            while (_recentFires.Count > PulseBufferCapacity) _recentFires.RemoveLast();
        }
    }

    /// <summary>WPF BuildActionKeySnapshot :110. AddXp is deliberately not shown.</summary>
    internal static List<string> ActionKeys(KeywordTrigger t) => (t.Actions ?? new()).Where(a => a is { Enabled: true })
        .Select(a => a switch
        {
            PlayAudioAction => "PlayAudio",
            HighlightAction => "Highlight",
            HapticAction => "Haptic",
            AvatarCommentAction => "AvatarComment",
            ExtendSessionAction ext => $"ExtendSession:{ext.Minutes}",
            ChasterAddTimeAction ch => $"ChasterAddTime:{ch.Minutes}",
            VisualEffectAction ve => $"VisualEffect:{ve.Effect}",
            _ => null,
        }).OfType<string>().ToList();

    /// <summary>WPF KeywordTriggerService.cs:1217,1257,1674.</summary>
    internal Task LastDispatch { get; private set; } = Task.CompletedTask;
    private async Task DispatchAsync(List<KeywordTrigger> fired)
    {
        // WPF KeywordTriggerService.cs:1261: keep response actions quiet after panic.
        if (ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
        foreach (var _ in fired)
            try { QuestCredit(); } catch (Exception ex) { Log.Warning(ex, "Keyword quest credit failed"); }
        var first = fired[0];
        List<KeywordAction> actions;
        if (fired.Count == 1 && (first.Actions == null || first.Actions.Count == 0))
        {
            // Legacy flat fields; HighlightOnly runs only the highlight, which a keystroke never has words for.
            actions = new();
            if (first.VisualEffect == KeywordVisualEffect.HighlightOnly) return;
            actions.Add(new PlayAudioAction { FilePath = first.AudioFilePath, Volume = first.AudioVolume,
                PlayCount = first.AudioPlayCount, DelayBetweenMs = first.AudioDelayBetweenMs, DuckSystemAudio = first.DuckAudio });
            if (first.HapticEnabled) actions.Add(new HapticAction { Intensity = first.HapticIntensity });
            if (first.XPAward > 0) actions.Add(new AddXpAction { Amount = first.XPAward });
        }
        else if (fired.Count == 1) actions = first.Actions!.Where(a => a is { Enabled: true }).ToList();
        else
        {
            var seen = new HashSet<string>();   // WPF KeywordTriggerService.cs:1322: one slot per type, VisualEffect per effect
            actions = fired.SelectMany(t => t.Actions ?? new()).Where(a => a is { Enabled: true } &&
                seen.Add(a is VisualEffectAction ve ? $"VisualEffect:{ve.Effect}" : a.GetType().Name)).ToList();
        }
        var played = false;
        foreach (var action in actions)
        {
            if (!IsActive || ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
            try
            {
                switch (action)
                {
                    // One clip at a time on this head: the first enabled clip wins (WPF's next play stops the last).
                    case PlayAudioAction audio when !played:
                        played = true;
                        if (!await PlayAsync(audio)) return;
                        break;
                    case HapticAction haptic: Haptic(first.Keyword, haptic.Intensity); break;
                    // WPF KeywordTriggerService.cs:1435-1444; MainWindow.xaml.cs:339 wires the session callback.
                    case AddXpAction xp when xp.Amount > 0:
                        AddXp(xp.Amount * (CoreSession.IsSessionRunning ? CoreSettings.Current.KeywordSessionMultiplier : 1));
                        break;
                    case ChasterAddTimeAction chaster: BookChaster(chaster, first); break;
                    // ponytail: VisualEffect, Highlight and AvatarComment need the overlay/avatar ports
                    // (wire-shell-keyword-triggers-b-next); WPF ExtendSession is itself a logged stub.
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Keyword action {Action} failed", action.GetType().Name); }
        }
    }

    /// <summary>WPF KeywordTriggerService.cs:1467: per-trigger day allowance, then Circe's tab.</summary>
    private void BookChaster(ChasterAddTimeAction chaster, KeywordTrigger trigger)
    {
        var id = string.IsNullOrEmpty(trigger.Id) ? "keyword:" + (trigger.Keyword ?? "") : trigger.Id;
        var now = Now();
        var path = ChasterCapPath();
        _chasterCap ??= KeywordTriggerChasterCap.Load(path);
        var allowed = _chasterCap.Allowance(id, Math.Clamp(chaster.Minutes, 0, 60) * 60, now);
        if (allowed <= 0) { Log.Information("Keyword Chaster time for '{Keyword}' is at its day cap", trigger.Keyword); return; }
        var applied = ChasterBook(allowed);
        if (applied <= 0) return;
        _chasterCap.Record(id, applied, now);
        _chasterCap.Save(path);
    }

    // ponytail: WPF FireDemoTrigger (KeywordTriggerService.cs:401-430) checks HasAccess (#887) and fires a synthetic
    // Highlight+AvatarComment trigger with no audio and no settings; both actions land with wire-shell-keyword-triggers-b-next.
    internal void FireDemo(string keyword, string source) { }

    internal void StopAudio()
    {
        _audio?.Cancel(); _audio?.Dispose(); _audio = null;
        var stop = _stop; _stop = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Keyword clip stop failed"); }
        if (_duckGeneration is { } generation) CoreAudio.Unduck(generation);
        _duckGeneration = null;
    }

    private async Task<bool> PlayAsync(PlayAudioAction audio)
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
        // WPF KeywordTriggerService.cs:1366,1513-1519: finish repeat scheduling before the next action.
        {
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
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopAudio(); return false; }
            return true;
        }
    }
}
