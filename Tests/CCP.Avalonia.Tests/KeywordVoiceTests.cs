using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class KeywordVoiceTests
{
    // WPF VoiceCommandHost.cs:95-96 and KeywordTriggerService.cs:176-199, 214-218.
    [Fact]
    public Task VoiceOffStopsTheClipAndOnRestartsMatchingWithoutChangingTheSwitch() => WithShell(shell =>
    {
        var s = CoreSettings.Current;
        var file = Path.GetTempFileName();
        var played = 0;
        var stopped = 0;
        try
        {
            s.KeywordTriggersEnabled = true;
            s.KeywordGlobalCooldownSeconds = 0;
            s.KeywordPerKeywordCooldownSeconds = 0;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = "cobalt", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = file, DuckSystemAudio = false } } } };
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => stopped++; };
            KeywordTriggerHead.Sync();
            Type("cobalt");
            Assert.Equal(1, played);
            Speak(shell, "trigger words off");
            Assert.False(KeywordTriggerHead.Engine.IsActive);
            Assert.Equal(1, stopped);
            Assert.True(s.KeywordTriggersEnabled);
            var afterCooldown = KeywordTriggerHead.Engine.Now().AddSeconds(2);
            KeywordTriggerHead.Engine.Now = () => afterCooldown;
            Type("cobalt");
            Assert.Equal(1, played);
            Speak(shell, "trigger words on");
            Assert.True(KeywordTriggerHead.Engine.IsActive);
            Type("cobalt");
            Assert.Equal(2, played);
            Assert.True(s.KeywordTriggersEnabled);
        }
        finally { KeywordTriggerHead.Stop(); File.Delete(file); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VoiceOnChecksAccessAndLeavesTheSavedSwitchAlone() => WithShell(shell =>
    {
        var s = CoreSettings.Current;
        KeywordTriggerHead.Stop();
        s.KeywordTriggersEnabled = false;
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        Speak(shell, "trigger words on");
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        CoreEntitlement.IsFreeTodayProvider = feature => feature == "awareness";
        Speak(shell, "trigger words on");
        Assert.True(KeywordTriggerHead.Engine.IsActive);
        Assert.False(s.KeywordTriggersEnabled);
        Speak(shell, "trigger words off");
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        Assert.False(s.KeywordTriggersEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task RepeatedVoiceOnDoesNotClearTheCurrentTypedWord() => WithShell(shell =>
    {
        var s = CoreSettings.Current;
        var file = Path.GetTempFileName();
        var played = 0;
        try
        {
            s.KeywordTriggersEnabled = true;
            s.KeywordGlobalCooldownSeconds = 0;
            s.KeywordPerKeywordCooldownSeconds = 0;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = "planet", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = file, DuckSystemAudio = false } } } };
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => { }; };
            KeywordTriggerHead.Stop();
            Speak(shell, "trigger words on");
            Type("pla");
            Speak(shell, "trigger words on");
            Type("net");
            Assert.Equal(1, played);
        }
        finally { KeywordTriggerHead.Stop(); File.Delete(file); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task KeywordCommandsAppearInGrammarAndTab() => WithShell(shell =>
    {
        Assert.Contains("trigger words on", shell.VoiceCmds.Grammar());
        Assert.Contains("trigger words off", shell.VoiceCmds.Grammar());
        shell.ShowTab("shelistening");
        Dispatcher.UIThread.RunJobs();
        var row = shell.SheListeningPage!.GetLogicalDescendants()
            .OfType<ConditioningControlPanel.Avalonia.Controls.IconLabel>()
            .Single(x => Equals(x.Tag, "voice:keyword_on|keyword_off"));
        Assert.True(row.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnsupportedKeyboardHidesKeywordCommandsAndTabRow() => WithShell(shell =>
    {
        KeywordTriggerHead.DetachKeyboard();
        KeywordTriggerHead.KeyboardSupportedForTests = null;
        Assert.Equal(OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")),
            KeywordTriggerHead.HasKeyboardInput);
        KeywordTriggerHead.KeyboardSupportedForTests = false;
        Assert.DoesNotContain("trigger words on", shell.VoiceCmds.Grammar());
        Assert.DoesNotContain("trigger words off", shell.VoiceCmds.Grammar());
        shell.ShowTab("shelistening");
        Dispatcher.UIThread.RunJobs();
        var row = shell.SheListeningPage!.GetLogicalDescendants()
            .OfType<ConditioningControlPanel.Avalonia.Controls.IconLabel>()
            .Single(x => Equals(x.Tag, "voice:keyword_on|keyword_off"));
        Assert.False(row.IsVisible);
        return Task.CompletedTask;
    });

    // WPF MainWindow.Awareness.cs:480-499: the keyboard sub-toggle does not start the service.
    [Fact]
    public Task KeyboardToggleDoesNotRestartVoiceStoppedRuntime() => WithShell(shell =>
    {
        CoreSettings.Current.KeywordTriggersEnabled = true;
        KeywordTriggerHead.Sync();
        var awareness = new AwarenessTabView();
        var keyboard = awareness.FindControl<CheckBox>("ChkAwarenessKeyboard")!;
        Assert.True(keyboard.IsChecked);
        Speak(shell, "trigger words off");
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        keyboard.IsChecked = false;
        keyboard.IsChecked = true;
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        Assert.True(CoreSettings.Current.KeywordTriggersEnabled);
        return Task.CompletedTask;
    });

    // WPF MainWindow.xaml.cs:338 and KeywordTriggerService.cs:193-199, 214-218.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task VoiceOffAndOnPreserveTheKeyboardEventPath(bool toggleKeyboard) => WithShell(shell =>
    {
        var s = CoreSettings.Current;
        var file = Path.GetTempFileName();
        var played = 0;
        var word = toggleKeyboard ? "maple" : "raven";
        try
        {
            s.KeywordTriggersEnabled = true;
            s.KeywordGlobalCooldownSeconds = 1;
            s.KeywordPerKeywordCooldownSeconds = 1;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = word, CooldownSeconds = 1,
                Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = file, DuckSystemAudio = false } } } };
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => { }; };
            KeywordTriggerHead.Sync();
            TypeOnKeyboard(word);
            Assert.Equal(1, played);
            Speak(shell, "trigger words off");
            if (toggleKeyboard)
            {
                var keyboard = new AwarenessTabView().FindControl<CheckBox>("ChkAwarenessKeyboard")!;
                keyboard.IsChecked = false;
                keyboard.IsChecked = true;
                Assert.False(KeywordTriggerHead.Engine.IsActive);
            }
            var afterCooldown = KeywordTriggerHead.Engine.Now().AddSeconds(2);
            KeywordTriggerHead.Engine.Now = () => afterCooldown;
            TypeOnKeyboard(word);
            Assert.Equal(1, played);
            Speak(shell, "trigger words on");
            TypeOnKeyboard(word);
            Assert.Equal(2, played);
        }
        finally { KeywordTriggerHead.Stop(); File.Delete(file); }
        return Task.CompletedTask;
    });

    private static void TypeOnKeyboard(string text)
    {
        foreach (var ch in text)
        {
            Win32PanicKey.OnDown(char.ToUpperInvariant(ch), DateTime.UtcNow);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Type(string text) { foreach (var ch in text) KeywordTriggerHead.Engine.Character(ch); }
    private static void Speak(MainShellWindow shell, string phrase)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + phrase, new[] { "hey bambi" }));
        Dispatcher.UIThread.RunJobs();
    }

    private static Task WithShell(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        KeywordTriggerHead.KeyboardSupportedForTests = true;
        KeywordTriggerHead.TranslateForTests = vk => vk is >= 'A' and <= 'Z' ? char.ToLowerInvariant((char)vk) : null;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        // The head's singleton runtime survives the suite's normal process-state snapshot.
        var cooldowns = (KeywordCooldowns)typeof(KeywordAudioRuntime)
            .GetField("_cooldowns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(KeywordTriggerHead.Engine)!;
        var lastGlobal = cooldowns.LastGlobal;
        var now = KeywordTriggerHead.Engine.Now;
        cooldowns.LastGlobal = DateTime.MinValue;
        KeywordTriggerHead.Engine.Now = () => new DateTime(2026, 10, 10, 12, 0, 0);
        var shell = new MainShellWindow();
        try { shell.Show(); await body(shell); }
        finally { KeywordTriggerHead.Stop(); KeywordTriggerHead.KeyboardSupportedForTests = null; KeywordTriggerHead.TranslateForTests = null; KeywordTriggerHead.Engine.Now = now; cooldowns.LastGlobal = lastGlobal; shell.RequestExit(); }
    });
}
