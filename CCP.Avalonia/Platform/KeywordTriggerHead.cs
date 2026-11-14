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

/// <summary>Keyboard slice of WPF KeywordTriggerService: audio, visual effect, haptic, XP, Chaster and avatar
/// comment actions. OCR and the Brain Drain visual remain a follow-up.</summary>
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
    internal static void Stop() { DetachKeyboard(); Engine.Stop(); KeywordAvatarComment.CancelPending(); }

    /// <summary>WPF MainWindow.Awareness.cs:493: Keyboard off drops only the key source; the engine and a playing clip stay.</summary>
    internal static void DetachKeyboard()
    {
        Interlocked.Increment(ref _generation);
        Engine.ClearInput();
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
            // WPF KeywordTriggerService.cs:618-622: sample even in Everywhere mode, at most every 2 s.
            var needsForeground = Engine.NeedsForeground(DateTime.UtcNow);
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
    // WPF KeywordTriggerService.cs:489-521: session-only, newest first, eight distinct apps.
    private readonly LinkedList<string> _seenApps = new();
    private DateTime _lastSampleAt = DateTime.MinValue;
    internal bool NeedsForeground(DateTime now)
    {
        if (KeywordMatchRules.NeedsForeground(CoreSettings.Current)) return true;
        if ((now - _lastSampleAt).TotalSeconds < 2) return false;
        _lastSampleAt = now;
        return true;
    }
    internal string[] GetRecentForegroundApps() => _seenApps.ToArray();
    private void RememberSeenApp(string? process, bool own)
    {
        if (own || string.IsNullOrWhiteSpace(process)) return;
        var existing = _seenApps.FirstOrDefault(a => string.Equals(a, process, StringComparison.OrdinalIgnoreCase));
        if (existing != null) _seenApps.Remove(existing);
        _seenApps.AddFirst(existing ?? process);
        while (_seenApps.Count > 8) _seenApps.RemoveLast();
    }

    private readonly KeywordTextInput _input = new();
    private readonly KeywordCooldowns _cooldowns = new();
    private CancellationTokenSource? _audio;
    private Action? _stop;
    private int _playGeneration;
    private long? _duckGeneration;
    internal Func<DateTime> Now = () => DateTime.Now;
    internal Func<int, CancellationToken, Task> Delay = Task.Delay;
    // WPF KeywordTriggerService.cs:72,135,1196: record each accepted fire before notifying.
    internal KeywordFireLog Fires { get; } = new();
    internal event Action<KeywordTrigger>? TriggerFired;
    internal bool IsActive { get; private set; }
    // WPF KeywordTriggerService.cs:176-188: access gate, and Start must preserve a live buffer.
    internal static bool HasAccess => CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness");
    internal void Start() { if (IsActive || !HasAccess) return; IsActive = true; _input.Clear(); }
    internal void Stop() { IsActive = false; _input.Clear(); StopAudio(); }
    internal void ClearInput() => _input.Clear();

    private bool Accept(string? process, bool own)
    {
        var s = CoreSettings.Current;
        if (!IsActive || !s.KeywordTriggersEnabled ||
            !HasAccess) return false;
        RememberSeenApp(process, own);
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
            Fires.Record(t, "Keyboard", now);   // WPF KeywordTriggerService.cs:135,1191-1200 (pulse feed)
            try { TriggerFired?.Invoke(t); } catch (Exception ex) { Log.Warning(ex, "Keyword fired handler failed"); }
            TrackAchievement();   // WPF GamificationBridge.OnKeywordTriggerFired: on TriggerFired, before the panic hold
        }
        // WPF KeywordTriggerService DispatchMergedAsync (b9368d963): matched and cooled down, but held during a panic's quiet.
        if (ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced())
        {
            Log.Information("KeywordTriggers: held - a panic is still quiet");
            return;
        }
        LastDispatch = DispatchAsync(fired);
        // WPF KeywordTriggerService.cs:1272,1345,1363: all enabled actions for one trigger;
        // :1283-1293: only the first enabled comment when multiple triggers merge.
        var comments = fired.SelectMany(t => t.Actions ?? new()).OfType<AvatarCommentAction>().Where(a => a.Enabled);
        if (fired.Count > 1) comments = comments.Take(1);
        foreach (var comment in comments) _ = KeywordAvatarComment.DispatchAsync(comment, fired[0]);
    }
    internal Action QuestCredit = () => App.Quests?.TrackKeywordTrigger();
    internal Action AchievementCredit = () => App.Achievements?.TrackKeywordTrigger();
    private void TrackAchievement()
    {
        try { AchievementCredit(); } catch (Exception ex) { Log.Warning(ex, "Keyword achievement credit failed"); }
    }
    internal Action<string, double> Haptic = (keyword, intensity) => _ = CoreHaptics.Service?.TriggerKeywordPatternAsync(keyword, intensity);
    internal Action<double> AddXp = amount => CoreProgression.AddXP(amount, "KeywordTrigger");
    /// <summary>Test seam; null runs <see cref="FireVisual"/>.</summary>
    internal Action<KeywordVisualEffect, string>? Visual;
    /// <summary>WPF BubbleService.SpawnOnce through the shell window; test seam.</summary>
    internal Action BubbleEffect = () =>
    {
        if (Views.Windows.MainShellWindow.Current is { } host) Views.Overlays.BubbleOverlay.SpawnOnce(host);
    };
    internal Func<int, int> ChasterBook = seconds => ChasterHead.Service?.NoteSeconds("watcher", seconds).AppliedSeconds ?? 0;
    internal Func<string> ChasterCapPath = () => Path.Combine(CorePaths.UserData, "chaster_trigger_caps.json");
    private KeywordTriggerChasterCap? _chasterCap;
    // WPF GetRecentFires: the Awareness pulse feed reads the shared Core ring (KeywordFireLog).
    internal IReadOnlyList<TriggerFireRecord> GetRecentFires() => Fires.Recent();
    internal Task LastDispatch { get; private set; } = Task.CompletedTask;
    private async Task DispatchAsync(List<KeywordTrigger> fired)
    {
        if (ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
        foreach (var _ in fired)
            try { QuestCredit(); } catch (Exception ex) { Log.Warning(ex, "Keyword quest credit failed"); }
        var first = fired[0];
        List<KeywordAction> actions;
        if (fired.Count == 1 && (first.Actions == null || first.Actions.Count == 0))
        {
            actions = new();
            if (first.VisualEffect == KeywordVisualEffect.HighlightOnly) return;
            actions.Add(new PlayAudioAction { FilePath = first.AudioFilePath, Volume = first.AudioVolume,
                PlayCount = first.AudioPlayCount, DelayBetweenMs = first.AudioDelayBetweenMs, DuckSystemAudio = first.DuckAudio });
            // WPF DispatchLegacyFlatFieldsAsync: the flat visual fires after the audio, before haptic and XP.
            actions.Add(new VisualEffectAction { Effect = first.VisualEffect });
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
                    case PlayAudioAction audio when !played:
                        played = true;
                        if (!await PlayAsync(audio)) return;
                        break;
                    case VisualEffectAction visual: (Visual ?? FireVisual)(visual.Effect, first.Keyword); break;
                    case HapticAction haptic: Haptic(first.Keyword, haptic.Intensity); break;
                    case AddXpAction xp when xp.Amount > 0:
                        AddXp(xp.Amount * (CoreSession.IsSessionRunning ? CoreSettings.Current.KeywordSessionMultiplier : 1));
                        break;
                    case ChasterAddTimeAction chaster: BookChaster(chaster, first); break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Keyword action {Action} failed", action.GetType().Name); }
        }
    }

    // WPF KeywordTriggerService FireVisualEffect: reuse the installed head renderers.
    private void FireVisual(KeywordVisualEffect effect, string keyword)
    {
        try
        {
            switch (effect)
            {
                case KeywordVisualEffect.SubliminalFlash:
                    if (CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                    break;
                case KeywordVisualEffect.ExactSubliminal:
                    // WPF FlashSubliminalCustom: trimmed, capped and tag-free, haptic lead-in, 10 XP.
                    if (!string.IsNullOrWhiteSpace(keyword)) CoreSubliminal.CustomShowProvider?.Invoke(keyword.ToUpperInvariant());
                    // The echoed word must never re-fire itself: always mute 3 s, even with loop protection off.
                    _cooldowns.Mute(keyword, 3000, Now().ToUniversalTime());
                    break;
                case KeywordVisualEffect.ImageFlash:
                    CoreFlash.ShowProvider?.Invoke();
                    break;
                case KeywordVisualEffect.OverlayPulse:
                    Views.Overlays.OverlayPulse.PulseOverlays();
                    break;
                case KeywordVisualEffect.MindWipe:
                    // It is audio: held while idle, and an empty pool stays silent.
                    if (CoreMindWipe.ClipCount > 0 && (CoreSession.IsEngineRunning || CoreSession.IsSessionRunning))
                        CoreMindWipe.TriggerOnce();
                    break;
                case KeywordVisualEffect.Bubbles:
                    BubbleEffect();
                    break;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword visual {Effect} failed", effect); }
    }

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
    // WPF KeywordTriggerService.cs:393-432: gated synthetic fire, no saved trigger/audio lookup; the
    // avatar comment dispatches (WPF DispatchResponseAsync). Null OCR words draw no highlight.
    internal void FireDemo(string keyword, string source)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        if (!(CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness"))) return;
        var demo = new KeywordTrigger
        {
            Id = "tutorial:demo", Keyword = keyword, MatchType = KeywordMatchType.PlainText, Enabled = true,
            VisualEffect = KeywordVisualEffect.HighlightOnly,
            Actions = new() { new HighlightAction { Enabled = true },
                new AvatarCommentAction { Enabled = true, FallbackPhraseCategory = "PuppyPraise", RequireAiAvailable = false } },
        };
        try
        {
            var now = Now();
            // WPF KeywordTriggerService.cs:139,426: demos bypass checks but still mute the word.
            _cooldowns.Record(demo, CoreSettings.Current, now.ToUniversalTime());
            Fires.Record(demo, source, now);
            TriggerFired?.Invoke(demo);
            TrackAchievement();
            // WPF DispatchResponseAsync :1253 credits the quest first; its catch skips the response on a failure.
            try { QuestCredit(); }
            catch (Exception ex) { Log.Warning(ex, "Keyword quest credit failed"); return; }
            _ = KeywordAvatarComment.DispatchAsync((AvatarCommentAction)demo.Actions[1], demo);
        }
        catch (Exception ex) { Log.Debug(ex, "FireDemo failed"); }
    }

    internal void StopAudio()
    {
        _audio?.Cancel(); _audio?.Dispose(); _audio = null;
        StopClip();
    }
    private void StopClip()
    {
        _playGeneration++;
        var stop = _stop; _stop = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Keyword clip stop failed"); }
        if (_duckGeneration is { } generation) CoreAudio.Unduck(generation);
        _duckGeneration = null;
    }

    private async Task<bool> PlayAsync(PlayAudioAction audio)
    {
        // WPF KeywordTriggerService.cs:1398,1841-1855: unsolicited audio requires an engine or session;
        // the other actions still run.
        if ((!CoreSession.IsEngineRunning && !CoreSession.IsSessionRunning) || string.IsNullOrEmpty(audio.FilePath)) return true;
        var path = audio.FilePath;
        if (!Path.IsPathRooted(path))
        {
            var resource = ContentLocator.Resolve(Path.Combine("Resources", path));
            var legacy = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
            path = File.Exists(resource) ? resource : File.Exists(legacy) ? legacy : path;
        }
        if (!File.Exists(path)) return true;
        StopClip();
        var cancellation = _audio ??= new CancellationTokenSource();
        var token = cancellation.Token;
        if (audio.DuckSystemAudio && CoreSettings.Current.AudioDuckingEnabled)
        {
            CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            _duckGeneration = CoreAudio.DuckGeneration;
        }
        {
            try
            {
                var count = Math.Max(1, audio.PlayCount);
                for (var i = 0; i < count; i++)
                {
                    if (i > 0 && audio.DelayBetweenMs > 0) await Delay(audio.DelayBetweenMs, token);
                    token.ThrowIfCancellationRequested();
                    // Old lane's stricter hold: also check after every repeat gap.
                    if (!CoreSession.IsEngineRunning && !CoreSession.IsSessionRunning) { StopAudio(); return false; }
                    _stop?.Invoke();
                    var last = i == count - 1;
                    var generation = ++_playGeneration;
                    _stop = CoreAudio.PlayStoppable(path,
                        KeywordMatchRules.AudioVolume(audio.Volume, CoreSettings.Current.MasterVolume), "keyword-trigger",
                        onFinished: () => { if (last) Dispatcher.UIThread.Post(async () =>
                        {
                            try { await Delay(500, token); if (!token.IsCancellationRequested && generation == _playGeneration) StopClip(); }
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
