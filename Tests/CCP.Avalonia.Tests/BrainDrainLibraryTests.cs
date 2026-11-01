using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrainDrainLibraryTests
{
    [Fact]
    public Task LibraryRescansOnAttachAndRefreshAndBothButtonsOpenPrimary() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-ui-" + Guid.NewGuid());
        var legacy = Path.Combine(root, "old");
        var assets = Path.Combine(root, "assets");
        var primary = Path.Combine(assets, "braindrain");
        Window? window = null;
        try
        {
            Directory.CreateDirectory(legacy);
            CorePaths.EffectiveAssetsProvider = () => assets;
            var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
            var opened = new System.Collections.Generic.List<string>();
            ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = p => { opened.Add(p); return true; };
            var card = new BrainDrainFeatureControl(legacy);
            Assert.True(Directory.Exists(primary));
            Assert.Equal(Loc.Get("st4_braindrain_audio_unavailable"), card.FindControl<TextBlock>("TxtAudioUnavailable")!.Text);
            Assert.True(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            Assert.Equal(primary, card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text);
            File.WriteAllText(Path.Combine(legacy, "old.ogg"), "clip");
            window = new Window { Content = card }; window.Show();
            Assert.Equal(Loc.GetF("st4_braindrain_clips_loaded_0", 1), card.FindControl<TextBlock>("TxtClipCount")!.Text);
            Assert.False(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            Assert.True(card.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible);
            Assert.Equal(legacy, card.FindControl<TextBlock>("TxtLegacyFolderPath")!.Text);
            File.WriteAllText(Path.Combine(primary, "new.wav"), "clip");
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Loc.GetF("st4_braindrain_clips_loaded_0", 2), card.FindControl<TextBlock>("TxtClipCount")!.Text);
            File.Delete(Path.Combine(legacy, "old.ogg")); File.Delete(Path.Combine(primary, "new.wav"));
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            Assert.False(card.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible);
            Directory.Delete(primary);
            card.FindControl<Button>("BtnOpenAudioFolder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Directory.Delete(primary);
            card.FindControl<Button>("BtnOpenAudioFolderEmpty")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { primary, primary }, opened);
            Assert.True(Directory.Exists(primary));
            var movedAssets = Path.Combine(root, "moved-assets");
            CorePaths.EffectiveAssetsProvider = () => movedAssets;
            settings.RestoreFrom(new());
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var movedPrimary = Path.Combine(movedAssets, "braindrain");
            Assert.Equal(movedPrimary, card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text);
            card.FindControl<Button>("BtnOpenAudioFolder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(movedPrimary, opened[^1]);
            Assert.True(Directory.Exists(movedPrimary));
        }
        finally { window?.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        return Task.CompletedTask;
    });
}
