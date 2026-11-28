using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeCompanionLevelTests
{
    [Fact]
    public Task LevelEventRefreshesCaptionAndSpeaksMilestonesAndPanicStopsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var previousPackage = CoreMods.ActiveModPackageProvider;
        var previousId = CoreMods.ActiveModIdProvider;
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "tube-test" }, null, false);
        CoreMods.ActiveModIdProvider = () => "tube-test";
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = false;
        s.ActiveCompanionId = (int)CompanionId.OGBambiSprite;
        s.SelectedAvatarSet = 3;
        s.ModAvatarSet.Clear();
        var progress = new CompanionProgress { CompanionId = CompanionId.OGBambiSprite, Level = 8 };
        s.CompanionProgressData[(int)CompanionId.OGBambiSprite] = progress;
        PanicAftermath.EndQuiet();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.NotNull(tube);
            tube.AvatarSwitchScheduler = (_, fire) => { fire(); return () => { }; };
            var sounds = new System.Collections.Generic.List<bool>();
            tube.SpeechAudioPlayer = (playSound, _, _, spoken) => { sounds.Add(playSound); spoken?.Invoke(); };
            tube.SwitchToCompanionAvatar(CompanionId.OGBambiSprite);
            Assert.Equal(3, tube.CurrentAvatarSet);
            Assert.Equal(0, s.ActiveCompanionId);
            Assert.Same(progress, s.ActiveCompanionProgress);
            var caption = tube.FindControl<TextBlock>("TxtAvatarLevel")!;
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            CompanionCore.AddCompanionXP(CompanionProgress.GetXPForLevel(8), "test");
            Assert.Contains("9", caption.Text);
            Assert.False(tube.HasBubbleUp);
            CompanionCore.AddCompanionXP(CompanionProgress.GetXPForLevel(9), "test");
            Assert.Contains("10", caption.Text);
            while (tube.TypewriterFrame is { } typed) typed();   // priority lines type (WPF Speech.cs:373,515)
            Assert.Contains("level 10", text.Text);
            Assert.True(tube.HasBubbleUp);
            Assert.Equal(new[] { true }, sounds);   // WPF Speech.cs:332,543-546: the milestone giggles
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(shell);
            Assert.False(tube.HasBubbleUp);
            progress.Level = 99; progress.CurrentXP = 0;
            CompanionCore.AddCompanionXP(CompanionProgress.GetXPForLevel(99), "test");
            while (tube.TypewriterFrame is { } typed2) typed2();
            Assert.Contains("MAX LEVEL", text.Text);
            Assert.True(tube.HasBubbleUp);
            shell.OnEngineStopped();   // WPF Reactions.cs:631-635: a stop only giggles, the line stays
            Assert.True(tube.HasBubbleUp);
            Assert.Contains("MAX LEVEL", text.Text);
        }
        finally { shell.Close(); PanicAftermath.EndQuiet(); CoreMods.ActiveModPackageProvider = previousPackage; CoreMods.ActiveModIdProvider = previousId; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicRetiresLevelEventAlreadyPostedToUi() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var previousPackage = CoreMods.ActiveModPackageProvider;
        var previousId = CoreMods.ActiveModIdProvider;
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "tube-test" }, null, false);
        CoreMods.ActiveModIdProvider = () => "tube-test";
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false;
        s.ActiveCompanionId = (int)CompanionId.OGBambiSprite;
        var progress = new CompanionProgress { CompanionId = CompanionId.OGBambiSprite, Level = 9 };
        s.CompanionProgressData[(int)CompanionId.OGBambiSprite] = progress;
        PanicAftermath.EndQuiet();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            tube.AvatarSwitchScheduler = (_, fire) => { fire(); return () => { }; };
            tube.SwitchToCompanionAvatar(CompanionId.OGBambiSprite);
            Task.Run(() => CompanionCore.AddCompanionXP(CompanionProgress.GetXPForLevel(9), "test")).GetAwaiter().GetResult();
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
            Assert.Contains("10", tube.FindControl<TextBlock>("TxtAvatarLevel")!.Text);
            Assert.Contains("level 10", tube.ChatHistory.Last().Text);   // owner call A: kept silently in history
            var sounds = new System.Collections.Generic.List<bool>();
            tube.SpeechAudioPlayer = (playSound, _, _, spoken) => { sounds.Add(playSound); spoken?.Invoke(); };
            PanicAftermath.ArmQuiet();
            progress.Level = 29; progress.CurrentXP = 0;
            CompanionCore.AddCompanionXP(CompanionProgress.GetXPForLevel(29), "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("level 30", tube.ChatHistory.Last().Text);
            Assert.False(tube.HasBubbleUp);
            Assert.Empty(sounds);
            PanicAftermath.EndQuiet();
            tube.Hide();   // WPF Speech.cs:365,481: a hidden tube still logs the line, only the bubble is skipped
            progress.Level = 19; progress.CurrentXP = 0;
            CompanionCore.AddCompanionXP(CompanionProgress.GetXPForLevel(19), "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("level 20", tube.ChatHistory.Last().Text);
            Assert.False(tube.HasBubbleUp);
        }
        finally { shell.Close(); PanicAftermath.EndQuiet(); CoreMods.ActiveModPackageProvider = previousPackage; CoreMods.ActiveModIdProvider = previousId; }
        return Task.CompletedTask;
    });
}
