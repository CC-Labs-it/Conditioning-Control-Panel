using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Settings · Audio and the dashboard audio card, driven from the shell the user opens:
/// both surfaces write settings through one binder and repaint each other (WPF HomeAudio mirror).</summary>
public sealed class AudioSettingsTests
{
    [Fact]
    public void ExternalMasterVolumeWritesRepaintWithoutLayoutOrAudioSideEffects() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.MasterVolume = 37;
        // Empty owner: the binder's dials need no font/layout to prove a model-driven repaint.
        var owner = new Border();
        var master = new Slider { Maximum = 100 };
        var text = new TextBlock();
        var binder = new AudioSettingsBinder(owner, master, text, new Slider(), new TextBlock(),
            new CheckBox(), new Slider(), new TextBlock(), new CheckBox(), new ComboBox(), new Button(), new Button());
        var window = new Window { Content = owner };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(37, master.Value);
            s.MasterVolume = 23; // same publication as CoreIntensityRamp.Tick
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(23, master.Value);
            Assert.Equal("23%", text.Text);
            s.MasterVolume = 37; // Stop restores its snapshot
            Assert.Equal(37, master.Value);
            window.Content = null; // detached binders must not keep observing settings
            s.MasterVolume = 12;
            Assert.Equal(37, master.Value);
            window.Content = owner;
            Assert.Equal(12, master.Value);
            service.RestoreFrom(new AppSettings { MasterVolume = 55 });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(55, master.Value);
            s.MasterVolume = 99;
            Assert.Equal(55, master.Value); // old instance unsubscribed
            service.Current.MasterVolume = 42;
            Assert.Equal("42%", text.Text);
        }
        finally { window.Close(); CoreSettings.ServiceProvider = oldProvider; }
    });

    private static readonly LibVlcAudio.OutputDevice Default = new("", "System default");
    private static readonly LibVlcAudio.OutputDevice Headset = new("alsa_output.usb-headset", "USB Headset");

    [Fact]
    public async Task SettingsAndDashboardShareTheDialsTheDeviceAndTheDiagnostics()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().WithInterFont().With(AppFonts.Options()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.MasterVolume = 37; s.VideoVolume = 22; s.DuckingLevel = 61;
            s.AudioDuckingEnabled = true; s.ExcludeBambiCloudFromDucking = true;
            s.AudioOutputDeviceId = "stale-id"; s.AudioOutputDeviceName = "usb headset";   // WPF falls back to the name
            s.Haptics.AudioSync.Enabled = true; s.Haptics.AudioSync.ManualLatencyOffsetMs = -40;
            var (oldEnumerate, oldShow, oldPlay) = (AudioSettingsBinder.Enumerate, AudioSettingsBinder.ShowDiagnostics, CoreAudio.PlayOneShotProvider);
            AudioSettingsBinder.Enumerate = () => new[] { Default, Headset };
            string? shown = null;
            var dialog = new TaskCompletionSource();
            AudioSettingsBinder.ShowDiagnostics = (_, text) => { shown = text; dialog.TrySetResult(); return Task.CompletedTask; };
            var played = new List<(string Path, float Volume)>();
            CoreAudio.PlayOneShotProvider = (p, v, _, _, done) => { played.Add((p, v)); done?.Invoke(); };

            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                // The user opens Settings: a hidden tab's ScrollViewer has no content until it is laid out.
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                var audio = shell.AppSettingsPage!.FindControl<AudioSettingsSection>("SectionAudio")!;
                var home = shell.Named<SettingsTabView>("SettingsTab")!;
                T A<T>(string n) where T : Control => audio.FindControl<T>(n)!;
                T H<T>(string n) where T : Control => home.FindControl<T>(n)!;

                // The ↻ button re-enumerates for every surface (the list may be cached from startup).
                Click(H<Button>("HomeBtnAudioOutputRefresh"));
                Assert.Equal(37, A<Slider>("SliderMaster").Value);
                Assert.Equal("37%", A<TextBlock>("TxtMaster").Text);
                Assert.Equal("22%", H<TextBlock>("HomeTxtVideoVolume").Text);
                Assert.Equal("61%", A<TextBlock>("TxtDuck").Text);
                Assert.Same(Headset, A<ComboBox>("CmbAudioOutputDevice").SelectedItem);
                Assert.Same(Headset, H<ComboBox>("HomeCmbAudioOutputDevice").SelectedItem);
                Assert.True(A<Border>("AudioSyncLatencyPanel").IsVisible);
                Assert.Equal("-40ms", A<TextBlock>("TxtAudioSyncLatency").Text);
                Assert.Equal("stale-id", s.AudioOutputDeviceId);   // painting writes nothing

                // Settings slider -> settings + the dashboard copy.
                A<Slider>("SliderMaster").Value = 64;
                Assert.Equal(64, s.MasterVolume);
                Assert.Equal(64, H<Slider>("HomeSliderMaster").Value);
                Assert.Equal("64%", H<TextBlock>("HomeTxtMaster").Text);

                // Dashboard edits -> settings + the Settings copy.
                H<Slider>("HomeSliderVideoVolume").Value = 80;
                H<Slider>("HomeSliderDuck").Value = 45;
                H<CheckBox>("HomeChkAudioDuck").IsChecked = false;
                H<CheckBox>("HomeChkExcludeBambiCloudDucking").IsChecked = false;
                Assert.Equal((80, 45, false, false), (s.VideoVolume, s.DuckingLevel, s.AudioDuckingEnabled, s.ExcludeBambiCloudFromDucking));
                Assert.Equal("80%", A<TextBlock>("TxtVideoVolume").Text);
                Assert.Equal("45%", A<TextBlock>("TxtDuck").Text);
                Assert.False(A<CheckBox>("ChkAudioDuck").IsChecked);
                Assert.False(A<CheckBox>("ChkExcludeBambiCloudDucking").IsChecked);

                H<ComboBox>("HomeCmbAudioOutputDevice").SelectedItem = Default;
                Assert.Equal(("", "System default"), (s.AudioOutputDeviceId, s.AudioOutputDeviceName));
                Assert.Same(Default, A<ComboBox>("CmbAudioOutputDevice").SelectedItem);
                A<ComboBox>("CmbAudioOutputDevice").SelectedItem = Headset;
                Assert.Equal(Headset.Id, s.AudioOutputDeviceId);

                A<Slider>("SliderAudioSyncLatency").Value = 120;
                Assert.Equal(120, s.Haptics.AudioSync.ManualLatencyOffsetMs);
                Assert.Equal("+120ms", A<TextBlock>("TxtAudioSyncLatency").Text);

                // WPF ramp writes settings outside either binder. Both attached surfaces must repaint,
                // including the restore on Stop, without changing any other audio dial.
                s.MasterVolume = 23;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(23, A<Slider>("SliderMaster").Value);
                Assert.Equal("23%", H<TextBlock>("HomeTxtMaster").Text);
                s.MasterVolume = 64;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("64%", A<TextBlock>("TxtMaster").Text);
                Assert.Equal(80, s.VideoVolume);

                // Test Audio: the WPF diagnostics text in WPF's dialog title, built off the UI thread.
                Click(H<Button>("HomeBtnTestAudio"));
                await dialog.Task.WaitAsync(System.TimeSpan.FromSeconds(30));
                Assert.StartsWith("=== Audio Diagnostics ===", shown);
                Assert.Contains("Audio device: OK (USB Headset)", shown);
                Assert.Contains("Master Volume: 64%", shown);
                Assert.NotEmpty(played);
                Assert.All(played, p => Assert.Equal(0.5f, p.Volume));

                // The "?" opens the Audio help card, as WPF MainWindow.Presets.cs:54.
                var help = A<Button>("HelpBtnAudio");
                Click(help);
                Assert.True(HelpPopover.IsPinned(help));
                Assert.Contains(Descendants(HelpPopover.PopupContent(help)!).OfType<TextBlock>(),
                    t => t.Text == HelpContentService.GetContent("Audio").Title);
                HelpPopover.CloseActive();
            }
            finally
            {
                shell.Close();
                CoreSettings.ServiceProvider = null;
                (AudioSettingsBinder.Enumerate, AudioSettingsBinder.ShowDiagnostics, CoreAudio.PlayOneShotProvider) = (oldEnumerate, oldShow, oldPlay);
            }
        });
    }

    [Fact]
    public void UntickingDashboardDuckRestoresDuckedAppsNow()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().WithInterFont().With(AppFonts.Options()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.AudioDuckingEnabled = true;
            CoreSettings.Current.MasterVolume = 50;
            var oldInstance = LibVlcAudio.Instance;
            var audio = new LibVlcAudio(_ => "[]");   // pactl stubbed: no sink-inputs, no real mixer
            LibVlcAudio.Instance = audio;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                var duck = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<CheckBox>("HomeChkAudioDuck")!;
                Assert.True(duck.IsChecked);
                audio.Duck(80);
                Assert.True(audio.IsDucked);
                duck.IsChecked = false;
                Assert.False(audio.IsDucked);
                audio.Drain();
            }
            finally
            {
                shell.Close();
                LibVlcAudio.Instance = oldInstance;
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static IEnumerable<global::Avalonia.Visual> Descendants(global::Avalonia.Visual v) =>
        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v);

    [Fact]
    public void SinksEnumerateAfterTheSystemDefaultAndOnlyALiveSavedDeviceIsApplied()
    {
        var list = LibVlcAudio.EnumerateOutputDevices(_ =>
            """[{"name":"alsa_output.pci","description":"Built-in Audio"},{"name":"bt.sink","description":""}]""");
        Assert.Equal(new[] { "", "alsa_output.pci", "bt.sink" }, list.Select(d => d.Id));
        Assert.Equal(new[] { "Built-in Audio", "bt.sink" }, list.Skip(1).Select(d => d.Name));
        Assert.Single(LibVlcAudio.EnumerateOutputDevices(_ => throw new System.InvalidOperationException("no pactl")));

        var live = new[] { "a", "b" };
        Assert.Equal("b", LibVlcAudio.PreferredDevice("b", "a", live));
        Assert.Null(LibVlcAudio.PreferredDevice("", "a", live));       // system default: leave it
        Assert.Null(LibVlcAudio.PreferredDevice("b", "b", live));      // already there
        Assert.Null(LibVlcAudio.PreferredDevice("gone", "a", live));   // stale id would route to nowhere
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
