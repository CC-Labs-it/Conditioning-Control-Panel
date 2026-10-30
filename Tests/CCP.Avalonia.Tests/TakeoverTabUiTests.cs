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
    private sealed class ChantVoice : MindWipePlayer.IVoice
    {
        internal double Level;
        internal bool Disposed;
        public double Volume { set => Level = value; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public Task ChantControlsAndPanicFollowSettings() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .With(new global::Avalonia.Media.FontManagerOptions {
                    DefaultFamilyName = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter,
                    FontFamilyMappings = new System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily> {
                        ["Segoe UI"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter),
                        ["Segoe UI Black"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter) } })
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.MasterVolume);
        var old = (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider, LibVlcAudio.Instance);
        var id = "chant-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), id);
        var dir = Path.Combine(root, "resources", "sounds", "companion_audio");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "p.mp3"), "audio");
        File.WriteAllText(Path.Combine(dir, "mantras.json"),
            "{\"mantras\":[{\"id\":\"a\",\"phrase\":\"Obey\",\"promptAudio\":\"p.mp3\"}]}");
        var shell = new MainShellWindow();
        try
        {
            s.MantraChantEnabled = false;
            shell.Show();
            shell.ShowTab("bambitakeover");
            Dispatcher.UIThread.RunJobs();
            LibVlcAudio.Instance = null; // exercise the real loop without an audio device
            CoreMods.ActiveModIdProvider = () => id;
            CoreMods.ActiveModPackageProvider = () => new ConditioningControlPanel.Models.ModPackage(
                new ConditioningControlPanel.Models.ModManifest { Id = id }, root, false);
            var tab = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>().Single();
            tab.FindControl<CheckBox>("ChkMantraChant")!.IsChecked = true;
            Assert.True(s.MantraChantEnabled);
            Assert.True(global::ConditioningControlPanel.Avalonia.App.MantraChant.IsRunning);
            tab.FindControl<CheckBox>("ChkMantraChant")!.IsChecked = false;
            Assert.False(global::ConditioningControlPanel.Avalonia.App.MantraChant.IsRunning);
            tab.FindControl<CheckBox>("ChkMantraChant")!.IsChecked = true;
            var voice = new ChantVoice();
            typeof(MantraChantHost).GetField("_voice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(global::ConditioningControlPanel.Avalonia.App.MantraChant, voice);
            s.MasterVolume = 80;
            tab.FindControl<Slider>("SldMantraChantVolume")!.Value = 37;
            tab.FindControl<Slider>("SldMantraChantGap")!.Value = 7;
            Assert.Equal(.296, voice.Level, 8);
            Assert.Equal(37, s.MantraChantVolume);
            Assert.Equal(7, s.MantraChantGapSeconds);
            Assert.Equal("37%", tab.FindControl<TextBlock>("TxtMantraChantVolume")!.Text);
            Assert.Equal("7s", tab.FindControl<TextBlock>("TxtMantraChantGap")!.Text);
            PanicSurfaces.All.Single(x => x.Id == "mantra-chant").Stop(shell);
            Assert.False(s.MantraChantEnabled);
            Assert.False(global::ConditioningControlPanel.Avalonia.App.MantraChant.IsRunning);
            Assert.True(voice.Disposed);
            tab.IsVisible = false;
            tab.IsVisible = true;
            Assert.False(tab.FindControl<CheckBox>("ChkMantraChant")!.IsChecked);
            global::ConditioningControlPanel.Avalonia.App.MantraChant.StopAndDisarm(); // idempotent
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.App.MantraChant.Stop();
            (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.MasterVolume) = saved;
            (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider, LibVlcAudio.Instance) = old;
            shell.Close();
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TabShowsWpfCopyAndItsButtonsGoWhereWpfsDo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .With(new global::Avalonia.Media.FontManagerOptions {
                    DefaultFamilyName = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter,
                    FontFamilyMappings = new System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily> {
                        ["Segoe UI"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter),
                        ["Segoe UI Black"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter) } })
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
}
