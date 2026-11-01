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
    // WPF VoiceCommandHost.cs:95 -> KeywordTriggerService.cs:176-188, :214-235.
    [Fact]
    public void VoiceRearmsX11SourceWithoutChangingSettingsAndRespectsAccessAndScope() => AvaloniaTestDispatcher.Run(() =>
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
            Assert.NotNull(X11PanicKey.KeywordKey);
            foreach (var c in "port") X11PanicKey.KeywordKey!((ulong)c);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi trigger words on", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs(); // a repeated Start must not clear the partly typed word
            foreach (var c in "word") X11PanicKey.KeywordKey!((ulong)c);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            now = now.AddSeconds(2); // WPF clamps both cooldowns to at least one second

            // Scope uses the captured foreground identity; own-focus exclusion must suppress input.
            s.KeywordTriggerIgnoreOwnFocus = true;
            foreach (var c in "portword") KeywordTriggerHead.PostX11Key(c, "ccp", true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            s.KeywordTriggerIgnoreOwnFocus = false;
            var oldListener = X11PanicKey.KeywordKey!;
            foreach (var c in "portword") oldListener((ulong)c);
            KeywordTriggerHead.Stop(); // queued presses from the old subscription must not leak into the new one
            KeywordTriggerHead.Start();
            foreach (var c in "portword") oldListener((ulong)c); // in-flight old callback after rearm
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, played);
            foreach (var c in "portword") X11PanicKey.KeywordKey!((ulong)c);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, played);

            now = now.AddSeconds(2);
            foreach (var c in "portwx") X11PanicKey.KeywordKey!((ulong)c);
            X11PanicKey.KeywordKey!(0xff08); // backspace
            foreach (var c in "ord") X11PanicKey.KeywordKey!((ulong)c);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, played);
            now = now.AddSeconds(2);
            foreach (var c in "port") X11PanicKey.KeywordKey!((ulong)c);
            X11PanicKey.KeywordKey!(0xff8d); // keypad return clears the buffer
            foreach (var c in "word") X11PanicKey.KeywordKey!((ulong)c);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, played);

            KeywordTriggerHead.DetachKeyboard();
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.Start();
            Assert.Null(X11PanicKey.KeywordKey); // a user-disabled keyboard stays disabled
            Assert.True(s.KeywordTriggersEnabled);
            s.KeywordTriggersEnabled = false;
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.Start();
            Assert.True(KeywordTriggerHead.Engine.IsActive); // WPF starts the service without saving the switch
            Assert.False(KeywordTriggerHead.KeyboardInputAllowed);
            foreach (var c in "portword") KeywordTriggerHead.Engine.Character(c);
            Assert.Equal(3, played); // the saved switch still gates matching
        }
        finally
        {
            KeywordTriggerHead.DetachKeyboard();
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.KeyboardAvailable = savedAvailability;
            KeywordTriggerHead.CanAttachKeyboard = savedAttach;
            KeywordTriggerHead.Engine.Now = savedNow;
            cooldowns.LastGlobal = lastGlobal;
            shell.Close();
            File.Delete(file);
        }
    });
}
