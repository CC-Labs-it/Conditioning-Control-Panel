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
    internal static Func<bool> CanAttachKeyboard = () => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"));
    internal static Func<bool> KeyboardAvailable = () => CanAttachKeyboard() && (OperatingSystem.IsWindows() || X11PanicKey.KeywordSourceReady);
    internal static bool KeyboardInputAllowed => Engine.IsActive && CoreSettings.Current.KeywordTriggersEnabled
        && (CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness"));
    internal static Func<Action<ulong, uint>, Action?> AttachKeyboard = x11 => AttachPlatformKeyboard(
        OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OnKey, x11, () => Win32PanicKey.EnsureListening());
    internal static Func<uint, uint> X11ToUnicode = xkb_keysym_to_utf32;
    private static Action? _detachKeyboard;
    private static int _generation;
    private static bool _keyboardEnabled;
    internal static void Sync()
    {
        if (!CoreSettings.Current.KeywordTriggersEnabled) { _keyboardEnabled = false; Stop(); return; }
        _keyboardEnabled = true;
        Start();
    }

    // WPF KeywordTriggerService.cs:176-188: Start is idempotent, checks access, and
    // does not edit KeywordTriggersEnabled. The hook is shared with panic.
    internal static void Start()
    {
        if (!Engine.IsActive && !(CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("awareness"))) return;
        Engine.Start();
        if (!_keyboardEnabled || _detachKeyboard != null || !CanAttachKeyboard()) return;
        var generation = Volatile.Read(ref _generation);
        _detachKeyboard = AttachKeyboard((sym, mods) => OnX11Key(sym, mods, generation));
    }

    // The same hook serves panic. Return only the keyword subscription's detach action.
    internal static Action? AttachPlatformKeyboard(bool windows, bool linux, Action<int, string?> win,
        Action<ulong, uint> x11, Action ensureWindows)
    {
        if (windows)
        {
            Win32PanicKey.KeyDown += win;
            try { ensureWindows(); }
            catch { Win32PanicKey.KeyDown -= win; throw; }
            return () => Win32PanicKey.KeyDown -= win;
        }
        if (!linux) return null;
        X11PanicKey.KeywordKey += x11;
        return () => X11PanicKey.KeywordKey -= x11;
    }

    // Voice Stop leaves the user's source choice intact for a later Start.
    internal static void Stop() { SuspendKeyboard(); Engine.Stop(); }

    /// <summary>WPF MainWindow.Awareness.cs:493: Keyboard off drops only the key source.</summary>
    internal static void DetachKeyboard() { _keyboardEnabled = false; SuspendKeyboard(); }

    private static void SuspendKeyboard()
    {
        Interlocked.Increment(ref _generation);
        var detach = _detachKeyboard;
        _detachKeyboard = null;
        detach?.Invoke();
    }

    // X11's listener captures the layout and modifier state before it posts. Native Wayland
    // windows do not expose global keys. Never log captured text.
    private static void OnX11Key(ulong sym, uint modifiers, int generation)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        var foreground = KeywordMatchRules.NeedsForeground(CoreSettings.Current)
            ? X11Windows.Foreground() : (null, false);
        PostX11Key(sym, modifiers, foreground.Item1, foreground.Item2, generation);
    }

    [DllImport("libxkbcommon.so.0")] private static extern uint xkb_keysym_to_utf32(uint keysym);
    internal static void PostX11Key(ulong sym, uint modifiers, string? process, bool own)
        => PostX11Key(sym, modifiers, process, own, Volatile.Read(ref _generation));

    private static void PostX11Key(ulong sym, uint modifiers, string? process, bool own, int generation)
    {
        var special = sym switch
        {
            0xff0d or 0xff8d or 0xff1b or 0xff09 or 0xfe20 => (KeywordInputKey?)KeywordInputKey.Clear,
            0xff08 => KeywordInputKey.Backspace,
            0x20 => KeywordInputKey.Space,
            _ => null,
        };
        var value = special == null ? X11ToUnicode((uint)sym) : 0;
        char? ch = value is > 0 and <= char.MaxValue && !char.IsControl((char)value) ? (char)value : null;
        // XLookupString's ControlMask translation, matching WPF ToUnicode's control characters.
        // Keep the control character in the buffer so text on either side cannot join.
        if ((modifiers & 4) != 0 && ch is { } control)
        {
            if (control >= '@' && control < 127) ch = (char)(control & 31);
            else if (control == '2') ch = '\0';
            else if (control is >= '3' and <= '7') ch = (char)(control - '3' + 27);
            else if (control == '8') ch = (char)127;
            else if (control == '/') ch = (char)31;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != Volatile.Read(ref _generation)) return;
            if (special is { } key) Engine.Key(key, process, own);
            else if (ch is { } c) Engine.Character(c, process, own);
            else Engine.Key(KeywordInputKey.None, process, own);
        });
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
        var count = ToUnicodeEx((uint)vk, MapVirtualKey((uint)vk, 0), state, text, 4, 4, layout);
        return WindowsTranslatedChar(count, text);
    }

    // WPF KeywordTriggerService.cs:1933-1938 also buffers translated control characters.
    internal static char? WindowsTranslatedChar(int count, StringBuilder text)
        => count == 1 && text.Length > 0 ? text[0] : null;

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
    internal void Start() { if (IsActive) return; IsActive = true; _input.Clear(); }
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
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Keyword audio failed"); StopAudio(); }
        }
    }
}
