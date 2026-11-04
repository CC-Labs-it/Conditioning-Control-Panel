using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainLibraryNoteTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void LegacyNoticeTracksClipsOnRefresh() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var folder = Path.Combine(root, "legacy");
        var primary = Path.Combine(root, "primary");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(primary);
        var previousAssets = CorePaths.EffectiveAssetsProvider;
        CorePaths.EffectiveAssetsProvider = () => primary;
        BrainDrainPlayer.LegacyFolderProvider = () => folder;
        var file = Path.Combine(folder, Guid.NewGuid() + ".wav");
        Window? host = null;
        try
        {
            File.WriteAllText(file, "clip");
            var card = new BrainDrainFeatureControl();
            host = new Window { Content = card }; host.Show(); Dispatcher.UIThread.RunJobs();
            var note = card.FindControl<TextBlock>("TxtLegacyFolderNote")!;
            var path = card.FindControl<TextBlock>("TxtLegacyFolderPath")!;
            Assert.True(note.IsVisible); Assert.True(path.IsVisible); Assert.Equal(folder, path.Text);
            File.Delete(file);
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(note.IsVisible);
            Assert.Equal(note.IsVisible, path.IsVisible);
        }
        finally { host?.Close(); BrainDrainPlayer.LegacyFolderProvider = null; CorePaths.EffectiveAssetsProvider = previousAssets; Directory.Delete(root, true); }
    });

    [Fact]
    public void AudioSettingsRepaintOnChangesAndReplacementWithoutWrites() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        Window? host = null;
        try
        {
            var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
            var card = new BrainDrainFeatureControl(); host = new Window { Content = card };
            host.Show(); Dispatcher.UIThread.RunJobs();
            var old = service.Current;
            old.BrainDrainEnabled = true; old.BrainDrainIntensity = 35; old.BrainDrainVolume = 45; old.BrainDrainHighRefresh = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.FindControl<CheckBox>("ChkEnable")!.IsChecked);
            Assert.Equal(35, card.FindControl<Slider>("SliderIntensity")!.Value);
            Assert.Equal(45, card.FindControl<Slider>("SliderVolume")!.Value);
            Assert.True(card.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked);
            service.RestoreFrom(new AppSettings { BrainDrainEnabled = false, BrainDrainIntensity = 70, BrainDrainVolume = 80, BrainDrainHighRefresh = false });
            var bytes = File.ReadAllBytes(settingsPath); var timestamp = File.GetLastWriteTimeUtc(settingsPath);
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.FindControl<CheckBox>("ChkEnable")!.IsChecked);
            Assert.Equal(70, card.FindControl<Slider>("SliderIntensity")!.Value);
            Assert.Equal(80, card.FindControl<Slider>("SliderVolume")!.Value);
            Assert.False(card.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked);
            old.BrainDrainVolume = 20; Dispatcher.UIThread.RunJobs();
            Assert.Equal(80, card.FindControl<Slider>("SliderVolume")!.Value);
            System.Threading.Thread.Sleep(650);
            Assert.Equal(bytes, File.ReadAllBytes(settingsPath)); Assert.Equal(timestamp, File.GetLastWriteTimeUtc(settingsPath));
        }
        finally
        {
            host?.Close(); CoreSettings.ServiceProvider = null;
            if (saved is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, saved);
        }
    });
}
