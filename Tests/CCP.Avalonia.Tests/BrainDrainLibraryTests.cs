using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF BrainDrainFeatureControl.xaml.cs:37-48,121-198,417-429.
public class BrainDrainLibraryTests
{
    [Fact]
    public Task LibraryRescansOnRefreshAndAttachAndBothButtonsOpenPrimary() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var previous = CorePaths.EffectiveAssetsProvider;
        var shell = ExternalOpener.Shell;
        Window? host = null;
        string? legacyClip = null;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            var folder = BrainDrainAudioPolicy.AudioFolder;
            var card = new BrainDrainFeatureControl();
            Assert.True(Directory.Exists(folder));
            Assert.Equal(folder, card.FindControl<TextBlock>("TxtAudioFolderPath")!.Text);
            // The test host has no legacy clips.
            Assert.True(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            Assert.False(card.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible);
            File.WriteAllText(Path.Combine(folder, "clip.WAV"), "");
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("st4_braindrain_clips_loaded_0", 1),
                card.FindControl<TextBlock>("TxtClipCount")!.Text);
            Assert.False(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            Directory.CreateDirectory(BrainDrainAudioPolicy.LegacyFolder);
            legacyClip = Path.Combine(BrainDrainAudioPolicy.LegacyFolder, Guid.NewGuid() + ".ogg");
            File.WriteAllText(legacyClip, "");
            card.FindControl<Button>("BtnRefreshAudio")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("st4_braindrain_clips_loaded_0", 2),
                card.FindControl<TextBlock>("TxtClipCount")!.Text);
            Assert.True(card.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible);
            Assert.Equal(BrainDrainAudioPolicy.LegacyFolder, card.FindControl<TextBlock>("TxtLegacyFolderPath")!.Text);
            File.Delete(legacyClip);
            File.Delete(Path.Combine(folder, "clip.WAV"));
            host = new Window { Content = card }; host.Show();
            Assert.True(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            int opened = 0;
            ExternalOpener.Shell = path => { Assert.Equal(folder, path); Assert.True(Directory.Exists(path)); opened++; return true; };
            host.Content = null; // no platform launcher: use the suite's shell seam
            foreach (var name in new[] { "BtnOpenAudioFolder", "BtnOpenAudioFolderEmpty" })
            {
                Directory.Delete(folder);
                card.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(2, opened);
        }
        finally { if (legacyClip != null) File.Delete(legacyClip); host?.Close(); ExternalOpener.Shell = shell; CorePaths.EffectiveAssetsProvider = previous; if (Directory.Exists(root)) Directory.Delete(root, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ArtLoadsAndFollowsModSwitchWhileAttached() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Window? host = null;
        try
        {
            var card = new BrainDrainFeatureControl();
            host = new Window { Content = card }; host.Show();
            var hero = card.FindControl<Border>("HeroArt")!;
            var side = card.FindControl<Border>("SideArt")!;
            Assert.IsType<ImageBrush>(hero.Background);
            Assert.IsType<ImageBrush>(side.Background);
            var before = hero.Background;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(before, hero.Background);
            Assert.Same(((ImageBrush)hero.Background!).Source, ((ImageBrush)side.Background!).Source);
            host.Content = null;
            before = hero.Background;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(before, hero.Background);
        }
        finally { host?.Close(); }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // A different class may have bootstrapped the process without bundled fonts.
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter)), out _))
        {
            var builder = AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register);
            builder.AfterSetupCallback(builder);
        }
    }
}
