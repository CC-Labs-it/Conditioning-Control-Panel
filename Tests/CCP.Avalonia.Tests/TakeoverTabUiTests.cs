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
        EnsureApp();
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

    // WPF MainWindow.Autonomy.cs:466-492 asks even when a stale saved voice setting is on.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SurpriseMantraRequiresConsentEvenWithAStaleSetting(bool savedOn) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.AutonomyCanTriggerVoiceCommand, s.MicConsentGiven);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            (s.AutonomyCanTriggerVoiceCommand, s.MicConsentGiven) = (savedOn, false);
            shell.ShowTab("bambitakeover");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>().Single();
            tab.SyncFromSettings();
            var toggle = tab.FindControl<CheckBox>("ChkAutonomyVoice")!;
            Assert.False(toggle.IsChecked);

            toggle.IsChecked = true;
            var dialog = Assert.Single(shell.OwnedWindows.OfType<global::ConditioningControlPanel.Avalonia.Views.Dialogs.MicConsentDialog>());
            Assert.False(s.MicConsentGiven);
            dialog.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Assert.False(toggle.IsChecked);
            Assert.False(s.MicConsentGiven);
            Assert.Equal(savedOn, s.AutonomyCanTriggerVoiceCommand);

            toggle.IsChecked = true;
            dialog = Assert.Single(shell.OwnedWindows.OfType<global::ConditioningControlPanel.Avalonia.Views.Dialogs.MicConsentDialog>());
            var next = dialog.FindControl<Button>("BtnNext")!;
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var name in new[] { "ChkConsent1", "ChkConsent2", "ChkConsent3" })
                dialog.FindControl<CheckBox>(name)!.IsChecked = true;
            dialog.FindControl<TextBox>("TxtConfirm")!.Text = "ENABLE";
            Dispatcher.UIThread.RunJobs();
            var enable = dialog.FindControl<Button>("BtnEnable")!;
            Assert.True(enable.IsEnabled);
            enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Assert.True(s.MicConsentGiven);
            Assert.True(s.AutonomyCanTriggerVoiceCommand);
            Assert.True(toggle.IsChecked);
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close();
            (s.AutonomyCanTriggerVoiceCommand, s.MicConsentGiven) = saved;
        }
    });


    private static void EnsureApp()
    {
        if (Application.Current is not null) return;
        var fonts = AppFonts.Options(systemEmoji: false);
        var mappings = new Dictionary<string, global::Avalonia.Media.FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(fonts)
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

}
