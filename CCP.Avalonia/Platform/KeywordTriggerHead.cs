using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Keyboard slice of WPF KeywordTriggerService: audio, visual effect, haptic, XP, Chaster and avatar
/// comment actions, plus the OCR consumer and scan lifecycle (restack b220, #4299). The OCR engine itself
/// (<see cref="OcrSource"/>) and the Brain Drain visual remain a follow-up.</summary>
internal static class KeywordTriggerHead
{
    internal static KeywordAudioRuntime Engine { get; } = new();
    // OS input seams: tests can drive Win32PanicKey.OnDown without installing a native hook.
    internal static bool? KeyboardSupportedForTests;
    internal static Func<int, char?>? TranslateForTests;
    internal static bool HasKeyboardInput => KeyboardSupportedForTests ?? (OperatingSystem.IsWindows() &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
    private static bool _subscribed;
    private static int _generation;
    internal static void Sync()
    {
        if (CoreSettings.Current.KeywordTriggersEnabled && KeywordAudioRuntime.HasAccess)
        {
            Engine.Start();
            AttachKeyboard();
            SyncOcr();
        }
        else Stop();
    }

    // WPF MainWindow.Awareness.cs:480-499: the keyboard toggle does not start the service.
    internal static void AttachKeyboard()
    {
        if (!CoreSettings.Current.KeywordTriggersEnabled || _subscribed || !HasKeyboardInput) return;
        Win32PanicKey.KeyDown += OnKey;
        _subscribed = true;
        if (KeyboardSupportedForTests != true) Win32PanicKey.EnsureListening(); // shared panic hook
    }

    // WPF KeywordTriggerService.cs:176-185: start is idempotent and requires awareness access.
    // It does not change the saved master switch or install a keyboard hook.
    internal static void Start()
    {
        if (Engine.IsActive || !KeywordAudioRuntime.HasAccess) return;
        Engine.Start();
    }
    /// <summary>Confirm the shared hook before program rollover or enrollment can rely on it.
    /// Sync subscribes only on Windows outside the isolated user-data profile.</summary>
    internal static bool KeyboardSourceAvailable() => KeyboardSourceAvailable(OperatingSystem.IsWindows(),
        Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
        () => Win32PanicKey.EnsureListening() && Win32PanicKey.IsListening);

    internal static bool KeyboardSourceAvailable(bool windows, string? sandboxDir, Func<bool> ensureListening) =>
        windows && string.IsNullOrEmpty(sandboxDir) && ensureListening();

    // The hook itself stays: on this head the panic key (Win32Input.Start at boot) owns it, the case WPF :426 keeps it for.
    internal static void Stop() { DetachKeyboard(); _ocr?.Stop(); Engine.Stop(); KeywordAvatarComment.CancelPending(); }

    /// <summary>Screen capture plus recognition. Null on this head (docs/avalonia-decisions.html 2026-11-04):
    /// plain net10.0 (no Windows TFM) cannot reach WinRT's OcrEngine, Linux ships no OCR engine, and Wayland
    /// capture is interactive only. While null the scanner never starts; everything after the source is live.</summary>
    internal static Func<CancellationToken, Task<IReadOnlyList<ScreenOcrService.Word>>>? OcrSource;
    private static ScreenOcrService? _ocr;

    /// <summary>WPF MainWindow.Awareness.cs:414-428,461-468 and KeywordTriggers.cs:119: master, OCR switch and interval.</summary>
    internal static void SyncOcr()
    {
        var s = CoreSettings.Current;
        if (OcrSource == null) { _ocr?.Stop(); return; }
        _ocr ??= new ScreenOcrService(t => OcrSource?.Invoke(t) ?? Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([]),
            () => KeywordAudioRuntime.HasAccess, DeliverOcr);
        _ocr.Sync(s.KeywordTriggersEnabled && Engine.IsActive, s.ScreenOcrEnabled, s.ScreenOcrIntervalMs);
    }

    internal static Task TickOcrForTest() => _ocr?.TickAsync() ?? Task.CompletedTask;

    // WPF ScreenOcrService.cs:143-198: own-UI filter, then the consumer on the UI thread. Stop runs on the
    // UI thread too, so a scan that finished after it is dropped here.
    private static Task<bool> DeliverOcr(IReadOnlyList<ScreenOcrService.Word> words, CancellationToken token) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return false;
            var s = CoreSettings.Current;
            if (s.AwarenessIgnoreOwnUi) words = WithoutOwnWindows(words, OwnWindowRects());
            var foreground = (Process: (string?)null, Own: false);
            if (OperatingSystem.IsWindows() && KeywordMatchRules.NeedsForeground(s))
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var id);
                foreground = Foreground(id);
            }
            return Engine.CheckOcrWords(words, foreground.Process, foreground.Own);
        }).GetTask();

    internal static IReadOnlyList<ScreenOcrService.Word> WithoutOwnWindows(
        IReadOnlyList<ScreenOcrService.Word> words, IReadOnlyList<System.Drawing.Rectangle> own) =>
        own.Count == 0 ? words : words.Where(w => !own.Any(r => r.IntersectsWith(w.Bounds))).ToArray();

    // WPF App.xaml.cs:1600-1720: visible, unminimised windows; one that covers a whole monitor is an
    // overlay container and is skipped (#273).
    // shortcut: bouncing/subliminal text rects (WPF :1722-1740) are not added (#4717 in b221 brings them).
    internal static IReadOnlyList<System.Drawing.Rectangle> OwnWindowRects()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) return [];
        var rects = new List<System.Drawing.Rectangle>();
        foreach (var w in desktop.Windows)
        {
            if (!w.IsVisible || w.WindowState == global::Avalonia.Controls.WindowState.Minimized) continue;
            var size = global::Avalonia.PixelSize.FromSize(w.FrameSize ?? w.ClientSize, w.RenderScaling);
            if (size.Width <= 0 || size.Height <= 0 || w.Screens.All.Any(m =>
                size.Width >= m.Bounds.Width - 4 && size.Height >= m.Bounds.Height - 4)) continue;
            rects.Add(new System.Drawing.Rectangle(w.Position.X, w.Position.Y, size.Width, size.Height));
        }
        return rects;
    }

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
            var ch = special == null ? (TranslateForTests is { } translate ? translate(vk) : Translate(vk)) : null;
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

    internal static (string? Process, bool Own) Foreground(uint id)
    {
        // WPF KeywordTriggerService.cs:647-655: no foreground window (lock screen, mid-switch) names no app;
        // process id 0 would otherwise resolve to the System Idle Process and offer it as a chip.
        if (id == 0) return (null, false);
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
    private KeywordCooldowns _cooldowns = new();
    internal void ResetCooldownsForTest() { _cooldowns = new(); ClearOcr(); }   // Stop keeps the OCR guards
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
    internal void Stop() { IsActive = false; _input.Clear(); StopAudio(); }   // WPF :193-200 keeps the OCR guards
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
        Fire(fired, "Keyboard", now);
    }

    // One fire path for both sources (WPF KeywordTriggerService.cs:126-140 keyboard, :933-943 OCR).
    private void Fire(List<KeywordTrigger> fired, string source, DateTime now)
    {
        var s = CoreSettings.Current;
        _cooldowns.LastGlobal = now;
        foreach (var t in fired)
        {
            t.LastTriggeredAt = now;
            _cooldowns.Record(t, s, now.ToUniversalTime());
            Fires.Record(t, source, now);   // WPF KeywordTriggerService.cs:135,1191-1200 (pulse feed)
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

    // WPF KeywordTriggerService.cs:689-944. Kept on the same thread as keyboard input.
    private Dictionary<string, int> _ocrSeen = new();
    private readonly HashSet<string> _ocrFired = new();
    internal bool NeedsOcrConfirmation { get; private set; }
    private void ClearOcr() { _ocrSeen.Clear(); _ocrFired.Clear(); NeedsOcrConfirmation = false; }

    /// <summary>One OCR scan: a match fires only after <c>OcrConfirmationScans</c> scans at the same place and
    /// stays guarded while it stays on screen. True asks the scanner for a quick confirmation rescan.</summary>
    internal bool CheckOcrWords(IReadOnlyList<ScreenOcrService.Word> words, string? process = null, bool own = false)
    {
        NeedsOcrConfirmation = false;
        var s = CoreSettings.Current;
        // WPF :716-743: a lapse or stop keeps the streak; an empty scan or a blocked app clears it.
        if (!IsActive || !HasAccess) return false;
        if (words.Count == 0 || !KeywordMatchRules.AppAllowed(s, process, own)) { ClearOcr(); return false; }
        if (!s.KeywordTriggersEnabled || !s.ScreenOcrEnabled) return false;
        if (s.KeywordTriggers is not { Count: > 0 }) return false; // WPF :741-742 keeps the guards
        var now = Now();
        if ((now - _cooldowns.LastGlobal).TotalSeconds < s.KeywordGlobalCooldownSeconds) return false;
        var matched = new List<ScreenOcrService.Word>();
        var fired = new List<KeywordTrigger>();
        foreach (var trigger in KeywordMatchRules.Ordered(s.KeywordTriggers))
        {
            if (trigger == null || !trigger.Enabled || string.IsNullOrEmpty(trigger.Keyword) ||
                (now - trigger.LastTriggeredAt).TotalSeconds < trigger.CooldownSeconds ||
                _cooldowns.IsMuted(trigger.Keyword, now.ToUniversalTime())) continue;
            var hits = FindOcrMatches(trigger, words);
            if (hits.Count == 0) continue;
            fired.Add(trigger);
            matched.AddRange(hits);
        }
        if (matched.Count == 0) { ClearOcr(); return false; }

        var positions = new HashSet<string>();
        foreach (var word in matched)
            positions.Add($"{word.Text.ToLowerInvariant()}_{word.Bounds.X / 120}_{word.Bounds.Y / 120}");
        _ocrFired.IntersectWith(positions);
        var seen = new Dictionary<string, int>();
        var confirmed = new List<string>();
        foreach (var key in positions)
        {
            var count = (_ocrSeen.TryGetValue(key, out var previous) ? previous : 0) + 1;
            seen[key] = count;
            if (_ocrFired.Contains(key)) continue;
            if (count < Math.Max(1, s.OcrConfirmationScans)) NeedsOcrConfirmation = true;
            else confirmed.Add(key);
        }
        _ocrSeen = seen;
        if (confirmed.Count == 0) return NeedsOcrConfirmation;
        if (!s.OcrHighlightAll)
        {
            for (var i = confirmed.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (confirmed[i], confirmed[j]) = (confirmed[j], confirmed[i]);
            }
            var count = Random.Shared.Next(1, confirmed.Count + 1);
            confirmed.RemoveRange(count, confirmed.Count - count);
        }
        _ocrFired.UnionWith(confirmed);
        // Fire holds the response during a panic's quiet, the same as a keyboard match (WPF DispatchMergedAsync).
        Fire(fired, "OCR", now);
        return NeedsOcrConfirmation;
    }

    private static List<ScreenOcrService.Word> FindOcrMatches(KeywordTrigger trigger, IReadOnlyList<ScreenOcrService.Word> words)
    {
        var results = new List<ScreenOcrService.Word>();
        if (trigger.MatchType != KeywordMatchType.Regex)
        {
            var parts = trigger.Keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return results;
            for (var i = 0; i <= words.Count - parts.Length; i++)
            {
                var matches = true;
                for (var j = 0; j < parts.Length; j++)
                {
                    var text = words[i + j].Text;
                    var start = 0; var end = text.Length;
                    while (start < end && !char.IsLetterOrDigit(text[start])) start++;
                    while (end > start && !char.IsLetterOrDigit(text[end - 1])) end--;
                    if (!text.Substring(start, end - start).Equals(parts[j], StringComparison.OrdinalIgnoreCase))
                    { matches = false; break; }
                }
                if (!matches) continue;
                var bounds = words[i].Bounds;
                for (var j = 1; j < parts.Length; j++) bounds = System.Drawing.Rectangle.Union(bounds, words[i + j].Bounds);
                results.Add(parts.Length == 1 ? words[i] : new ScreenOcrService.Word(trigger.Keyword, bounds));
            }
            return results;
        }
        var textBuilder = new StringBuilder();
        var starts = new int[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            if (i > 0) textBuilder.Append(' ');
            starts[i] = textBuilder.Length;
            textBuilder.Append(words[i].Text);
        }
        try
        {
            foreach (Match match in Regex.Matches(textBuilder.ToString(), trigger.Keyword, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            {
                if (match.Length == 0) continue;
                System.Drawing.Rectangle? bounds = null;
                for (var i = 0; i < words.Count; i++)
                {
                    if (starts[i] + words[i].Text.Length <= match.Index || starts[i] >= match.Index + match.Length) continue;
                    bounds = bounds is { } r ? System.Drawing.Rectangle.Union(r, words[i].Bounds) : words[i].Bounds;
                }
                if (bounds is { } rect) results.Add(new ScreenOcrService.Word(match.Value, rect));
            }
        }
        catch (ArgumentException) { return new(); }
        catch (RegexMatchTimeoutException) { return new(); }
        return results;
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
        var legacy = fired.Count == 1 && (first.Actions == null || first.Actions.Count == 0);
        if (legacy)
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
        // WPF KeywordTriggerService DispatchActionsAsync: actions duck once when any audio action asks
        // and audio is not held; DispatchLegacyFlatFieldsAsync ducks on DuckAudio even while audio is held.
        // Either way a match whose clip never plays (held, missing) still ducks for 500 ms.
        var running = CoreSession.IsEngineRunning || CoreSession.IsSessionRunning;
        var duck = legacy ? first.DuckAudio
            : running && actions.OfType<PlayAudioAction>().Any(a => a.DuckSystemAudio);
        var played = false;
        foreach (var action in actions)
        {
            if (!IsActive || ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
            try
            {
                switch (action)
                {
                    // WPF DispatchActionAsync :1638: every audio action plays in order, each clip replacing the
                    // last (PlayTriggerAudio stops the previous one); a missing clip is skipped (restack b220, #4235).
                    // Legacy flat fields check the stored path as is (WPF File.Exists(trigger.AudioFilePath)).
                    case PlayAudioAction audio:
                        var result = await PlayAsync(audio, resolveRelative: !legacy, duck);
                        if (result == ClipResult.Stopped) return;
                        played |= result == ClipResult.Played;
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
        if (!played && duck) DuckBriefly();
    }

    // WPF Task.Delay(500) then Unduck(duckGen). The release is its own generation and survives a clip
    // stop. Unlike WPF it never unducks when it did not duck: Unduck(-1) would release another owner's duck.
    private void DuckBriefly()
    {
        if (!CoreSettings.Current.AudioDuckingEnabled) return;
        CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
        var generation = CoreAudio.DuckGeneration;
        _ = Release();
        async Task Release()
        {
            try { await Delay(500, CancellationToken.None); }
            catch (Exception ex) { Log.Warning(ex, "Keyword duck delay failed"); }
            finally { CoreAudio.Unduck(generation); }
        }
    }

    private enum ClipResult { Skipped, Played, Stopped }

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

    private async Task<ClipResult> PlayAsync(PlayAudioAction audio, bool resolveRelative, bool duck)
    {
        // WPF KeywordTriggerService.cs:1398,1841-1855: unsolicited audio requires an engine or session;
        // the other actions still run.
        if ((!CoreSession.IsEngineRunning && !CoreSession.IsSessionRunning) || string.IsNullOrEmpty(audio.FilePath)) return ClipResult.Skipped;
        var path = audio.FilePath;
        if (resolveRelative && !Path.IsPathRooted(path))
        {
            var resource = ContentLocator.Resolve(Path.Combine("Resources", path));
            var legacy = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
            path = File.Exists(resource) ? resource : File.Exists(legacy) ? legacy : path;
        }
        if (!File.Exists(path)) return ClipResult.Skipped;
        // WPF DispatchActionsAsync :1594-1628 holds one duck over the whole response. The next clip's duck is
        // taken before the replaced clip lets go of its own, so the shared duck count never touches zero
        // between two clips of one response; the last clip's duck ends 500 ms after it finishes.
        long? duckGeneration = null;
        if (duck && CoreSettings.Current.AudioDuckingEnabled)
        {
            CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            duckGeneration = CoreAudio.DuckGeneration;
        }
        StopClip();
        _duckGeneration = duckGeneration;
        var cancellation = _audio ??= new CancellationTokenSource();
        var token = cancellation.Token;
        {
            try
            {
                var count = Math.Max(1, audio.PlayCount);
                for (var i = 0; i < count; i++)
                {
                    if (i > 0 && audio.DelayBetweenMs > 0) await Delay(audio.DelayBetweenMs, token);
                    token.ThrowIfCancellationRequested();
                    // Old lane's stricter hold: also check after every repeat gap.
                    if (!CoreSession.IsEngineRunning && !CoreSession.IsSessionRunning) { StopAudio(); return ClipResult.Stopped; }
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
            catch (OperationCanceledException) { return ClipResult.Stopped; }
            catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopAudio(); return ClipResult.Stopped; }
            return ClipResult.Played;
        }
    }
}
