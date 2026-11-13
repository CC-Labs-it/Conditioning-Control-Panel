using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Z4 "Make her yours" reads the live personality and writes through the shell's
/// explicit-content gate (WPF MakeHerYoursRuntimeVm + MainWindow.CompanionRoom.cs:121-178).</summary>
public sealed class CompanionPersonalityZoneTests
{
    private static void Click(Window shell, Control c)
    {
        c.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
        shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static ToggleButton Chip(MakeHerYoursView view, string id) =>
        view.GetLogicalDescendants().OfType<ToggleButton>().First(t => (t.DataContext as PresetChip)?.Id == id);

    private static ExplicitContentAcknowledgementDialog? Gate(Window shell) =>
        shell.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>().SingleOrDefault();

    private sealed class FrozenClock : System.TimeProvider
    {
        public override System.DateTimeOffset GetUtcNow() => new(2026, 10, 12, 12, 0, 0, global::System.TimeSpan.Zero);
    }

    // WPF MainWindow.CompanionRoom.cs:116-143: only a successful change queues the matching bubble.
    [Fact]
    public Task SpiceToggleGivesFeedbackOnlyAfterAChange() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.AvatarEnabled = false;
        s.AvatarMuted = true;
        s.SlutModeEnabled = false;
        s.ActiveCommunityPromptId = null;
        s.CompanionPrompt.UseCustomPrompt = false;
        s.CompanionPrompt.ExplicitContentAcknowledged = false;
        s.ActivePersonalityPresetId = PersonalityService.Shared.GetAllPresets()
            .First(p => ExplicitContentGate.RequiresAcknowledgement(p, true)).Id;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("personality");
            shell.CompleteTabTransition();
            Dispatcher.UIThread.RunJobs();
            s.AvatarEnabled = true;
            shell.ShowAvatarTube();
            Dispatcher.UIThread.RunJobs();
            var tube = Assert.IsType<global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow>(shell.Tube);
            tube.SpeechClock = new FrozenClock();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            var view = shell.GetLogicalDescendants().OfType<MakeHerYoursView>().Single();
            var spice = view.FindControl<ToggleButton>("SpiceToggle")!;

            Click(shell, spice);
            Gate(shell)!.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.SlutModeEnabled);
            Assert.False(tube.IsSpeaking);
            Assert.Equal(0, tube.QueuedSpeechCount);

            ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
            Click(shell, spice);
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.SlutModeEnabled);
            while (tube.TypewriterFrame is { } onFrame) onFrame();
            Assert.Equal(Loc.Get("companion_spice_on_line"), text.Text);
            Assert.True(await shell.SetSlutModeAsync(true)); // unchanged: no second bubble
            Assert.Equal(0, tube.QueuedSpeechCount);

            System.Action? deliver = null;
            tube.SpeechDelayScheduler = (_, fire) => { deliver = fire; return () => { }; };
            tube.EndSpeechBubble();
            Click(shell, spice);
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.SlutModeEnabled);
            Assert.NotNull(deliver); // regular Giggle waits for the previous line's gap
            deliver!();
            while (tube.TypewriterFrame is { } offFrame) offFrame();
            Assert.Equal(Loc.Get("companion_spice_off_line"), text.Text);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    });

    // WPF Speech.cs:463-477: an off-screen toggle skips speech and still earns the gap.
    [Fact]
    public Task SpiceWhileAvatarIsHiddenSkipsSpeechAndKeepsTheQueueGap() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldPath = CoreModArt.AudioOverridePathProvider;
        var oldOneShot = CoreAudio.PlayOneShotProvider;
        var oldStoppable = CoreAudio.PlayStoppableProvider;
        var sound = System.IO.Path.GetTempFileName();
        int plays = 0;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = false;
        s.MasterVolume = 100;
        s.SlutModeEnabled = false;
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        s.ActiveCommunityPromptId = null;
        s.CompanionPrompt.UseCustomPrompt = false;
        ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
        CoreMods.ActiveModIdProvider = () => "test";
        CoreModArt.AudioOverridePathProvider = _ => sound;
        CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => { if (tag == "bubble-fallback") plays++; };
        CoreAudio.PlayStoppableProvider = (_, _, tag, _, _) => { if (tag == "giggle") plays++; return () => { }; };
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("personality");
            shell.CompleteTabTransition();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.NotNull(tube);
            tube.SpeechClock = new FrozenClock();
            var spice = shell.GetLogicalDescendants().OfType<MakeHerYoursView>().Single().FindControl<ToggleButton>("SpiceToggle")!;
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            var before = text.Text;
            shell.SetAvatarEnabled(false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.IsVisible);
            Click(shell, spice);
            Assert.True(s.SlutModeEnabled);
            Assert.False(tube.IsSpeaking);
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Equal(before, text.Text);
            Assert.Equal(0, plays);

            System.Action? deliver = null;
            System.TimeSpan? gap = null;
            tube.SpeechDelayScheduler = (delay, fire) => { gap = delay; deliver = fire; return () => { }; };
            shell.SetAvatarEnabled(true);
            Click(shell, spice);
            Assert.False(s.SlutModeEnabled);
            Assert.Equal(System.TimeSpan.FromSeconds(2), gap);
            Assert.NotNull(deliver);
            deliver!();
            while (tube.TypewriterFrame is { } frame) frame();
            Assert.Equal(Loc.Get("companion_spice_off_line"), text.Text);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
            CoreMods.ActiveModIdProvider = oldMod;
            CoreModArt.AudioOverridePathProvider = oldPath;
            CoreAudio.PlayOneShotProvider = oldOneShot;
            CoreAudio.PlayStoppableProvider = oldStoppable;
            System.IO.File.Delete(sound);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PresetChipsSpiceAndResetGoThroughTheGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            // The first test owns the shared platform; use the desktop's bundled fonts on every entry path.
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        s.ActiveCommunityPromptId = null;
        s.CompanionPrompt.UseCustomPrompt = false;
        s.CompanionPrompt.ExplicitContentAcknowledged = false;
        s.SlutModeEnabled = false;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("personality");   // Companion > Personality hosts the zone (v2)
            shell.CompleteTabTransition();
            Dispatcher.UIThread.RunJobs();
            var view = shell.GetLogicalDescendants().OfType<MakeHerYoursView>().Single();
            var vm = view.ViewModel;
            Assert.Same(shell, vm.Shell);
            // WPF MakeHerYoursView ctor collapses the interview card under Companion v2.
            Assert.True(ConditioningControlPanel.Services.Companion.CompanionExperience.IsV2Enabled);
            Assert.False(view.FindControl<Control>("InterviewSpotlight")!.IsVisible);
            var spice = view.FindControl<ToggleButton>("SpiceToggle")!;

            // Chips are the service's presets, the active one selected; the readout names it.
            var all = PersonalityService.Shared.GetAllPresets();
            Assert.Equal(all.Select(p => p.Id), vm.Presets.Select(p => p.Id));
            Assert.True(Chip(view, PersonalityPresets.NeutralDefaultId).IsChecked);
            var neutral = PersonalityService.Shared.GetPresetById(PersonalityPresets.NeutralDefaultId)!;
            Assert.Equal(Loc.GetF("companion_personality_active_preset_fmt", neutral.Name), vm.ActivePersonalityLine);

            // A plain preset switches (spice off: nothing on the picker asks).
            var plain = all.First(p => p.Id != PersonalityPresets.NeutralDefaultId && !ExplicitContentGate.RequiresAcknowledgement(p, false));
            Click(shell, Chip(view, plain.Id));
            Assert.Equal(plain.Id, s.ActivePersonalityPresetId);
            Assert.False(Chip(view, PersonalityPresets.NeutralDefaultId).IsChecked);

            // With spice on, a preset with an explicit variant asks first; Cancel leaves the old
            // preset active and the chip off.
            s.SlutModeEnabled = true;
            var spicy = all.First(p => p.Id != plain.Id && ExplicitContentGate.RequiresAcknowledgement(p, true));
            Click(shell, Chip(view, spicy.Id));
            var gate = Gate(shell);
            Assert.NotNull(gate);
            gate!.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(plain.Id, s.ActivePersonalityPresetId);
            Assert.False(Chip(view, spicy.Id).IsChecked);
            Assert.True(Chip(view, plain.Id).IsChecked);

            // Spice on over that preset: refused on Cancel.
            s.SlutModeEnabled = false;
            s.ActivePersonalityPresetId = spicy.Id;
            vm.Sync();
            Click(shell, spice);
            Gate(shell)!.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.SlutModeEnabled);
            Assert.False(vm.IsSpiceOn);
            Assert.False(spice.IsChecked);

            // ...and written once acknowledged.
            ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
            Click(shell, spice);
            Assert.Null(Gate(shell));
            Assert.True(s.SlutModeEnabled);
            Assert.True(vm.IsSpiceOn);
            Assert.True(spice.IsChecked);

            // A community prompt shows Reset, and Reset clears the override.
            s.ActiveCommunityPromptId = "community-x";
            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("personality");   // re-read on return (PersonalityPage.OnShown -> Sync)
            shell.CompleteTabTransition();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.CanResetPersonality);
            Assert.Equal(Loc.GetF("companion_personality_active_custom_fmt", "community-x"), vm.ActivePersonalityLine);
            Click(shell, view.FindControl<Button>("BtnResetPersonality")!);
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.False(vm.CanResetPersonality);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
