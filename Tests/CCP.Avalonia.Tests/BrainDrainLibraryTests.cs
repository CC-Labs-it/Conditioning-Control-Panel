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
using ConditioningControlPanel.Avalonia.Views.Windows;
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
            Assert.Equal(Loc.GetF("st4_braindrain_clips_found_0", 1), card.FindControl<TextBlock>("TxtClipCount")!.Text);
            Assert.False(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            Assert.True(card.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible);
            Assert.Equal(legacy, card.FindControl<TextBlock>("TxtLegacyFolderPath")!.Text);
            File.WriteAllText(Path.Combine(primary, "new.wav"), "clip");
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Loc.GetF("st4_braindrain_clips_found_0", 2), card.FindControl<TextBlock>("TxtClipCount")!.Text);
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

    // WPF BrainDrainFeatureControl.xaml.cs:121-131 resolves the current assets path.
    [Fact]
    public Task AssetFolderSettingRefreshesTheAttachedCardAndOpenButton() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-setting-" + Guid.NewGuid());
        var first = Path.Combine(root, "first"); var second = Path.Combine(root, "second");
        var legacy = Path.Combine(root, "legacy"); Window? window = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(first, "braindrain"));
            Directory.CreateDirectory(Path.Combine(second, "braindrain"));
            File.WriteAllText(Path.Combine(first, "braindrain", "a.mp3"), "clip");
            File.WriteAllText(Path.Combine(second, "braindrain", "b.wav"), "clip");
            File.WriteAllText(Path.Combine(second, "braindrain", "c.ogg"), "clip");
            var settings = new SettingsService(); settings.Current.CustomAssetsPath = first;
            CoreSettings.ServiceProvider = () => settings;
            CorePaths.EffectiveAssetsProvider = () => settings.Current.CustomAssetsPath;
            var opened = new System.Collections.Generic.List<string>();
            ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = p => { opened.Add(p); return true; };
            var card = new BrainDrainFeatureControl(legacy);
            var holder = new Border { Child = card };
            window = new Window { Content = holder }; window.Show();
            Assert.Equal(Path.Combine(first, "braindrain"), card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text);
            holder.IsVisible = false;
            settings.Current.CustomAssetsPath = second;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Path.Combine(second, "braindrain"), card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text);
            Assert.Equal(Loc.GetF("st4_braindrain_clips_found_0", 2), card.FindControl<TextBlock>("TxtClipCount")!.Text);
            holder.IsVisible = true;
            card.FindControl<Button>("BtnOpenAudioFolder")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text, Assert.Single(opened));
            window.Content = null;
            settings.Current.CustomAssetsPath = first;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Path.Combine(second, "braindrain"), card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text);
        }
        finally { window?.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        return Task.CompletedTask;
    });

    // WPF StudioTabView.xaml.cs:303-309,1296-1309 and BrainDrainFeatureControl.xaml.cs:37-44.
    [Fact]
    public Task RackAndTabNavigationRescanFilesAddedWhileHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-nav-" + Guid.NewGuid());
        var clips = Path.Combine(root, "braindrain"); MainShellWindow? shell = null;
        try
        {
            Directory.CreateDirectory(clips);
            var settings = new SettingsService(); settings.Current.CustomAssetsPath = root;
            CoreSettings.ServiceProvider = () => settings;
            CorePaths.EffectiveAssetsProvider = () => settings.Current.CustomAssetsPath;
            shell = new MainShellWindow(); shell.Show(); shell.ShowTab("studio");
            var rack = shell.StudioRack!; rack.FocusRackEntry("braindrain");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var card = rack.FindControl<BrainDrainFeatureControl>("PanelBrainDrain")!;
            var initial = BrainDrainClips.Scan(clips, BrainDrainClips.LegacyFolderPath).Clips.Length;
            rack.FocusRackEntry("flash");
            File.WriteAllText(Path.Combine(clips, Guid.NewGuid() + ".mp3"), "clip");
            rack.FocusRackEntry("braindrain");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.GetF("st4_braindrain_clips_found_0", initial + 1), card.FindControl<TextBlock>("TxtClipCount")!.Text);
            shell.ShowTab("settings");
            File.WriteAllText(Path.Combine(clips, Guid.NewGuid() + ".wav"), "clip");
            shell.ShowTab("studio");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.GetF("st4_braindrain_clips_found_0", initial + 2), card.FindControl<TextBlock>("TxtClipCount")!.Text);
        }
        finally { shell?.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LibraryCopyDescribesDiscoveryWithoutPromisingPlayback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-copy-" + Guid.NewGuid());
        var legacy = Path.Combine(root, "legacy"); Window? window = null;
        try
        {
            Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "old.mp3"), "clip");
            CorePaths.EffectiveAssetsProvider = () => Path.Combine(root, "assets");
            var card = new BrainDrainFeatureControl(legacy);
            window = new Window { Content = card }; window.Show();
            Assert.Equal(Loc.Get("st4_braindrain_visual_only_hint"), card.FindControl<TextBlock>("TxtEnableHint")!.Text);
            Assert.Equal(Loc.Get("st4_braindrain_discovery_hint"), card.FindControl<TextBlock>("TxtFolderHint")!.Text);
            Assert.False(card.FindControl<TextBlock>("TxtFolderContents")!.IsVisible);
            Assert.Equal(Loc.Get("st4_braindrain_legacy_discovery_note"), card.FindControl<TextBlock>("TxtLegacyFolderNote")!.Text);
            Assert.Equal(Loc.Get("st4_braindrain_no_clips_found"), card.FindControl<TextBlock>("TxtNoAudioHint")!.Text);
            Assert.Equal(Loc.Get("st4_braindrain_discovery_hint"), ToolTip.GetTip(card.FindControl<Button>("BtnRefreshAudio")!));
            Assert.Equal(Loc.Get("st4_braindrain_audio_unavailable"), ToolTip.GetTip(card.FindControl<Grid>("AudioRateRow")!));
            Assert.Equal(Loc.Get("st4_braindrain_visual_only_hint"), ToolTip.GetTip(card.FindControl<Grid>("VisualStrengthRow")!));
            Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(card.FindControl<Grid>("HighRefreshRow")!));
            File.Delete(Path.Combine(legacy, "old.mp3"));
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(card.FindControl<Border>("NoAudioHint")!.IsVisible);
        }
        finally { window?.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnportedMeltAndHighRefreshStayInactiveAndExplainWhy() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var card = new BrainDrainFeatureControl(); var window = new Window { Content = card }; window.Show();
        try
        {
            foreach (var (control, label) in new[] { ("ChkMelt", "TxtMeltUnavailable"), ("ChkHighRefresh", "TxtHighRefreshUnavailable") })
            {
                var check = card.FindControl<CheckBox>(control)!;
                Assert.False(check.IsEffectivelyEnabled);
                check.IsEnabled = true; // Session unlock can set the child enabled.
                Assert.False(check.IsEffectivelyEnabled);
                var hint = card.FindControl<TextBlock>(label)!;
                Assert.True(hint.IsEffectivelyVisible);
                Assert.Equal(Loc.Get("exclusives_not_on_this_build"), hint.Text);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
