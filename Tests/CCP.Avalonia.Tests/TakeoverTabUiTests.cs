using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>rows-takeover-ui: the Takeover tab's WPF pieces this head was missing, driven from the
/// shell the way a user reaches them - open the tab, read the copy, click the buttons.</summary>
public sealed class TakeoverTabUiTests
{
    [Fact]
    public Task TabShowsWpfCopyAndItsButtonsGoWhereWpfsDo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.AutonomyCanTriggerVoiceCommand, s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled);
        var speech = (CoreSpeech.IsAvailableProvider, CoreSpeech.HasCaptureDeviceProvider, CoreSpeech.ModelStatusProvider);
        var opener = ExternalOpener.Shell;
        var preexisting = SpeechEngine.DefaultModelRoots.Where(Directory.Exists).ToHashSet();
        var launched = new List<string>();
        var shell = new MainShellWindow();
        try
        {
            ExternalOpener.Shell = t => { launched.Add(t); return true; };
            (s.AutonomyCanTriggerVoiceCommand, s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled) = (true, true, false, false);
            // A mic is there, the model is not: the model is the problem.
            CoreSpeech.HasCaptureDeviceProvider = () => true;
            CoreSpeech.IsAvailableProvider = () => false;
            CoreSpeech.ModelStatusProvider = () => CoreSpeechModelStatus.NoModelFound;

            shell.Show();
            shell.ShowTab("bambitakeover");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>().Single();
            T Named<T>(string n) where T : Control => tab.FindControl<T>(n)!;

            // State hero sub line is WPF's Loc copy, both ways.
            shell.SetTakeoverActiveUi(true);
            Assert.Equal(Loc.Get("takeover_status_sub_active"), Named<TextBlock>("TxtTakeoverStatusSub").Text);
            shell.SetTakeoverActiveUi(false);
            Assert.Equal(Loc.Get("takeover_status_sub_dormant"), Named<TextBlock>("TxtTakeoverStatusSub").Text);

            // Mantra Chant hint: no voiced mantras in this mod -> say so (WPF RefreshMantraChantHint).
            Assert.False(global::ConditioningControlPanel.Avalonia.App.MantraVoice.HasVoicedMantras());
            Assert.Equal(Loc.Get("desc_mantra_chant_none"), Named<TextBlock>("TxtMantraChantHint").Text);

            // Model missing: the hint says so and the folder button is up; clicking it opens a model root.
            var models = Named<Button>("BtnAutonomyOpenModels");
            Assert.Equal(Loc.Get("takeover_voice_hint_model_missing"), Named<TextBlock>("TxtAutonomyVoiceHint").Text);
            Assert.True(models.IsVisible);
            models.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(launched, p => SpeechEngine.DefaultModelRoots.Contains(p));

            // audit #1974: a language switch re-translates the code-set hints instead of leaving them stale.
            try
            {
                LocalizationManager.Instance.SetLanguage("de");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("takeover_voice_hint_model_missing"), Named<TextBlock>("TxtAutonomyVoiceHint").Text);
                Assert.Equal(Loc.Get("desc_mantra_chant_none"), Named<TextBlock>("TxtMantraChantHint").Text);
            }
            finally { LocalizationManager.Instance.SetLanguage("en"); Dispatcher.UIThread.RunJobs(); }

            // No mic: a folder will not help, so the button goes (WPF ShowSpeechModelFolderButton).
            CoreSpeech.HasCaptureDeviceProvider = () => false;
            tab.RefreshAutonomyVoiceHint();
            Assert.False(models.IsVisible);

            // "Configure in Settings" lands on Settings -> Devices, as WPF OpenDeviceSettings.
            Named<Button>("BtnOpenDeviceSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.GetLogicalDescendants().OfType<Control>().First(c => c.Name == "AppSettingsTab").IsVisible);
            Assert.True(shell.AppSettingsPage!.FindControl<RadioButton>("SectionPillDevices")!.IsChecked);
        }
        finally
        {
            shell.Close();
            ExternalOpener.Shell = opener;
            (CoreSpeech.IsAvailableProvider, CoreSpeech.HasCaptureDeviceProvider, CoreSpeech.ModelStatusProvider) = speech;
            (s.AutonomyCanTriggerVoiceCommand, s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled) = saved;
            foreach (var root in SpeechEngine.DefaultModelRoots)
                if (!preexisting.Contains(root) && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                    Directory.Delete(root);
        }
        return Task.CompletedTask;
    });
    private sealed class ChantVoice : global::ConditioningControlPanel.Avalonia.Platform.MindWipePlayer.IVoice
    {
        public double Level;
        public bool Stopped;
        public double Volume { set => Level = value; }
        public void Dispose() => Stopped = true;
    }

    [Fact]
    public Task ChantControlsReachPlaybackAndLapseStopsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.AvatarMuted, s.MasterVolume);
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        var chant = MantraChantService.Instance;
        var hooks = (chant.HasVoiced, chant.Next, chant.Resolve, chant.Play);
        var voice = new ChantVoice();
        var shell = new MainShellWindow();
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            (s.MantraChantEnabled, s.AvatarMuted, s.MasterVolume) = (false, false, 80);
            chant.HasVoiced = () => true;
            chant.Next = () => new global::ConditioningControlPanel.Models.MantraEntry { PromptAudio = "ask" };
            chant.Resolve = p => p;
            chant.Play = (_, level, _) => { voice.Volume = level; return voice; };
            shell.Show();
            shell.ShowTab("bambitakeover");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>().Single();
            var toggle = tab.FindControl<CheckBox>("ChkMantraChant")!;
            toggle.IsChecked = true;
            Assert.True(chant.IsRunning);
            tab.FindControl<Slider>("SldMantraChantVolume")!.Value = 25;
            Assert.Equal(.2, voice.Level, 5);
            tab.FindControl<Slider>("SldMantraChantGap")!.Value = 9;
            Assert.Equal(9, s.MantraChantGapSeconds);
            toggle.IsChecked = false;
            Assert.False(chant.IsRunning);
            Assert.True(voice.Stopped);
            toggle.IsChecked = true;
            CoreEntitlement.HasPremiumProvider = () => false;
            shell.RefreshEntitlementVeils(persist: true);
            Assert.False(chant.IsRunning);
            Assert.False(toggle.IsChecked);
        }
        finally
        {
            chant.Stop();
            (chant.HasVoiced, chant.Next, chant.Resolve, chant.Play) = hooks;
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.AvatarMuted, s.MasterVolume) = saved;
            shell.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PermissionsLinkNavigatesAndWallpaperCreditRechecksConsentOnShow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.StrictLockEnabled);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("bambitakeover");
            var tab = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>().Single();
            foreach (var (source, consent, visible) in new[] { ("local", true, false), ("online", false, false), ("online", true, true), ("mixed", true, true) })
            {
                shell.ShowTab("settings");
                (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = (source, consent, false);
                shell.ShowTab("bambitakeover");
                Assert.Equal(visible, tab.FindControl<StackPanel>("TxtWallpaperCredit")!.IsVisible);
            }
            var launched = new List<string>();
            tab.WallpaperCreditOpenerForTests = target => { launched.Add(target); return Task.FromResult(true); };
            s.StrictLockEnabled = false;
            tab.FindControl<Button>("BtnWallpaperCredit")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { "https://scrolller.com" }, launched);
            s.StrictLockEnabled = true;
            tab.FindControl<Button>("BtnWallpaperCredit")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(launched);
            s.StrictLockEnabled = false;
            tab.FindControl<Button>("BtnCompanionPermissions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.GetLogicalDescendants().OfType<Control>().First(c => c.Name == "CompanionTab").IsVisible);
        }
        finally { shell.Close(); (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.StrictLockEnabled) = saved; }
        return Task.CompletedTask;
    });

}
