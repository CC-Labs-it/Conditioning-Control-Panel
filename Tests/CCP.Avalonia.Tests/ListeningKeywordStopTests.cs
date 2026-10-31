using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ListeningKeywordStopTests
{
    // WPF AutonomyService.VoiceCommandHost.cs:96 -> KeywordTriggerService.cs:193-199:
    // voice stops input and current audio without editing the saved switch, even after access lapses.
    [Fact]
    public void InlineKeywordStopCutsAudioAndInputWithoutChangingTheSavedSwitch() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var file = Path.GetTempFileName();
        var shell = new MainShellWindow();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var s = CoreSettings.Current;
        // The shared engine retains its cooldown clock across tests; preserve that instance state too.
        var cooldowns = (ConditioningControlPanel.Services.KeywordCooldowns)typeof(KeywordAudioRuntime)
            .GetField("_cooldowns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(KeywordTriggerHead.Engine)!;
        var lastGlobal = cooldowns.LastGlobal;
        cooldowns.LastGlobal = DateTime.MinValue;
        var stopped = 0;
        var played = 0;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.KeywordTriggersEnabled = true;
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordGlobalCooldownSeconds = s.KeywordPerKeywordCooldownSeconds = 0;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = "portstop", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = file, DuckSystemAudio = false } } } };
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => stopped++; };
            var engine = KeywordTriggerHead.Engine;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            engine.Start();
            foreach (var c in "portstop") engine.Character(c);
            Assert.Equal(1, played);
            Assert.True(engine.IsActive);
            CoreEntitlement.HasPremiumProvider = () => false; // stopping is never entitlement-gated
            Assert.Contains("keyword_off", shell.VoiceCmds.Available.Select(i => i.Name));
            Assert.DoesNotContain("keyword_on", shell.VoiceCmds.Available.Select(i => i.Name));
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi keyword triggers off", new[] { "hey bambi" }));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(engine.IsActive);
            Assert.Equal(1, stopped);
            Assert.True(s.KeywordTriggersEnabled);
            CoreEntitlement.HasPremiumProvider = () => true;
            foreach (var c in "portstop") engine.Character(c);
            Assert.Equal(1, played);
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi keyword triggers off", new[] { "hey bambi" }));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, stopped);

            shell.RefreshSheListeningTab();
            var row = shell.SheListeningPage!.GetLogicalDescendants().OfType<IconLabel>()
                .Single(t => t.Tag as string == "voice:keyword_on|keyword_off");
            Assert.True(row.IsVisible);
            var panel = (global::Avalonia.Controls.Panel)row.Parent!;
            var text = (global::Avalonia.Controls.TextBlock)panel.Children[panel.Children.IndexOf(row) + 1];
            var phrases = string.Concat(text.Inlines!.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text));
            Assert.DoesNotContain("trigger words on", phrases);
            Assert.Contains("trigger words off", phrases);
        }
        finally { KeywordTriggerHead.Stop(); cooldowns.LastGlobal = lastGlobal; shell.Close(); File.Delete(file); }
    });
}
