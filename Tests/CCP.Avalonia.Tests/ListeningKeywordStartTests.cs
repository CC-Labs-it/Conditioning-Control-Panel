using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ListeningKeywordStartTests
{
    [Fact]
    public void RawControlStateFollowsKeyEventsInsteadOfTheLaterServerSnapshot()
    {
        var keys = new HashSet<int> { 37, 105 };
        var held = new HashSet<int>();
        X11PanicKey.TrackControl(keys, held, 37, true);
        Assert.Equal(4u, X11PanicKey.ControlAtEvent(0, 0, keys, held));
        X11PanicKey.TrackControl(keys, held, 105, true);
        X11PanicKey.TrackControl(keys, held, 37, false);
        Assert.Equal(4u, X11PanicKey.ControlAtEvent(0, 0, keys, held));
        X11PanicKey.TrackControl(keys, held, 105, false);
        Assert.Equal(0u, X11PanicKey.ControlAtEvent(4, 0, keys, held));
        Assert.Equal(4u, X11PanicKey.ControlAtEvent(0, 4, keys, held)); // sticky Control
        Assert.Null(X11PanicKey.ControlAtEvent(4, 0, null, held)); // fail closed if the map is unavailable
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void PlatformAttachmentUsesAndDetachesOnlyItsOwnSource(bool windows, bool linux)
    {
        var saved = X11PanicKey.KeywordKey;
        X11PanicKey.KeywordKey = null;
        var winEvents = 0;
        var x11Events = 0;
        var ensured = 0;
        Action<int, string?> win = (_, _) => winEvents++;
        Action<ulong, uint> x11 = (_, _) => x11Events++;
        Action? detach = null;
        try
        {
            detach = KeywordTriggerHead.AttachPlatformKeyboard(windows, linux, win, x11, () => ensured++);
            Assert.Equal(windows || linux, detach != null);
            Win32PanicKey.OnDown(0x41, DateTime.UtcNow);
            X11PanicKey.KeywordKey?.Invoke('c', 4);
            Assert.Equal(windows ? 1 : 0, winEvents);
            Assert.Equal(linux ? 1 : 0, x11Events);
            Assert.Equal(windows ? 1 : 0, ensured);
            detach?.Invoke();
            detach = null;
            Win32PanicKey.OnDown(0x41, DateTime.UtcNow);
            X11PanicKey.KeywordKey?.Invoke('c', 4);
            Assert.Equal(windows ? 1 : 0, winEvents);
            Assert.Equal(linux ? 1 : 0, x11Events);
        }
        finally { detach?.Invoke(); X11PanicKey.KeywordKey = saved; }
    }

    // WPF VoiceCommandHost.cs:95 -> KeywordTriggerService.cs:176-188, :214-235.
    [Fact]
    public void VoiceRearmsInjectedSourceWithoutChangingSettingsAndRespectsAccessAndScope() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        Dispatcher.UIThread.RunJobs();
        var file = Path.GetTempFileName();
        var s = CoreSettings.Current;
        var savedAvailability = KeywordTriggerHead.KeyboardAvailable;
        var savedAttach = KeywordTriggerHead.CanAttachKeyboard;
        var savedSource = KeywordTriggerHead.AttachKeyboard;
        var savedUnicode = KeywordTriggerHead.X11ToUnicode;
        Action<ulong, uint>? keyboard = null;
        var cooldowns = (KeywordCooldowns)typeof(KeywordAudioRuntime)
            .GetField("_cooldowns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(KeywordTriggerHead.Engine)!;
        var lastGlobal = cooldowns.LastGlobal;
        var played = 0;
        var savedNow = KeywordTriggerHead.Engine.Now;
        var now = DateTime.Now;
        try
        {
            KeywordTriggerHead.DetachKeyboard();
            KeywordTriggerHead.Stop();
            Assert.False(KeywordTriggerHead.CanAttachKeyboard()); // sandbox profiles never attach by default
            KeywordTriggerHead.CanAttachKeyboard = () => true; // no real listener is started by this test
            KeywordTriggerHead.KeyboardAvailable = () => true;
            KeywordTriggerHead.AttachKeyboard = handler => { keyboard = handler; return () => keyboard = null; };
            KeywordTriggerHead.X11ToUnicode = sym => sym < 128 ? sym : 0;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.KeywordTriggersEnabled = true;
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordGlobalCooldownSeconds = s.KeywordPerKeywordCooldownSeconds = 0;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = "portword", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = file, DuckSystemAudio = false } } } };
            cooldowns.LastGlobal = DateTime.MinValue;
            KeywordTriggerHead.Engine.Now = () => now;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => { }; };

            KeywordTriggerHead.Sync(); // without access, even a saved-on source cannot start
            Assert.False(KeywordTriggerHead.Engine.IsActive);
            Assert.False(KeywordTriggerHead.KeyboardInputAllowed);
            Assert.Contains("keyword_on", shell.VoiceCmds.Available.Select(i => i.Name));
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi trigger words on", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            Assert.False(KeywordTriggerHead.Engine.IsActive);
            Assert.True(s.KeywordTriggersEnabled);

            CoreEntitlement.IsFreeTodayProvider = name => name == "awareness";
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi trigger words on", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            Assert.True(KeywordTriggerHead.Engine.IsActive);
            Assert.True(KeywordTriggerHead.KeyboardInputAllowed);
            Assert.NotNull(keyboard);
            foreach (var c in "port") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi trigger words on", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs(); // a repeated Start must not clear the partly typed word
            foreach (var c in "word") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            now = now.AddSeconds(2); // WPF clamps both cooldowns to at least one second

            // Scope uses the captured foreground identity; own-focus exclusion must suppress input.
            s.KeywordTriggerIgnoreOwnFocus = true;
            foreach (var c in "portword") KeywordTriggerHead.PostX11Key(c, 0, "ccp", true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            s.KeywordTriggerIgnoreOwnFocus = false;
            var oldListener = keyboard!;
            foreach (var c in "portword") oldListener((ulong)c, 0);
            KeywordTriggerHead.Stop(); // queued presses from the old subscription must not leak into the new one
            KeywordTriggerHead.Start();
            foreach (var c in "portword") oldListener((ulong)c, 0); // in-flight old callback after rearm
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            foreach (var c in "portword") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, played);

            now = now.AddSeconds(2);
            foreach (var c in "portwx") keyboard!((ulong)c, 0);
            keyboard!(0xff08, 0); // backspace
            foreach (var c in "ord") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, played);
            now = now.AddSeconds(2);
            foreach (var c in "port") keyboard!((ulong)c, 0);
            keyboard!(0xff8d, 0); // keypad return clears the buffer
            foreach (var c in "word") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, played);

            // WPF ToUnicode buffers Ctrl+C as a control character, never the letter C.
            now = now.AddSeconds(2);
            s.KeywordTriggers[0].Keyword = "c";
            keyboard!(0xff0d, 0); // isolate this keyword from the previous buffer
            keyboard!('c', 4); // ControlMask
            keyboard!('C', 5); // Control + Shift
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, played);
            keyboard!('c', 0x80); // AltGr alone does not mean Control
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, played);

            // Dropping a shortcut entirely would also be wrong: it could join a word across it.
            now = now.AddSeconds(2);
            s.KeywordTriggers[0].Keyword = "portword";
            foreach (var c in "portw") keyboard!((ulong)c, 0);
            keyboard!('c', 4);
            foreach (var c in "ord") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, played);
            keyboard!(0xff0d, 0);
            foreach (var c in "portword") keyboard!((ulong)c, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, played);

            KeywordTriggerHead.DetachKeyboard();
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.Start();
            Assert.Null(keyboard); // a user-disabled keyboard stays disabled
            Assert.True(s.KeywordTriggersEnabled);
            s.KeywordTriggersEnabled = false;
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.Start();
            Assert.True(KeywordTriggerHead.Engine.IsActive); // WPF starts the service without saving the switch
            Assert.False(KeywordTriggerHead.KeyboardInputAllowed);
            foreach (var c in "portword") KeywordTriggerHead.Engine.Character(c);
            Assert.Equal(5, played); // the saved switch still gates matching
        }
        finally
        {
            KeywordTriggerHead.DetachKeyboard();
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.KeyboardAvailable = savedAvailability;
            KeywordTriggerHead.CanAttachKeyboard = savedAttach;
            KeywordTriggerHead.AttachKeyboard = savedSource;
            KeywordTriggerHead.X11ToUnicode = savedUnicode;
            KeywordTriggerHead.Engine.Now = savedNow;
            cooldowns.LastGlobal = lastGlobal;
            shell.Close();
            File.Delete(file);
        }
    });
}
