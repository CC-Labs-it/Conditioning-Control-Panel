using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerDiscoveryTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Enhancement Video(string name, string source) => new()
    {
        MediaType = MediaTypes.Video, MediaSource = source, Metadata = new EnhancementMetadata { Name = name }
    };

    // WPF EnhancementPlayerWindow.xaml.cs:472-545: sidecar takes priority over library.
    [Fact]
    public Task LocalMediaFindsLibraryAndPrefersSidecar() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-discovery-").FullName;
        var id = Guid.NewGuid().ToString("N");
        var media = Path.Combine(dir, id + ".webm");
        var sidecar = Path.Combine(dir, id + ".ccpenh.json");
        var libraryDir = DeeperLocalLibrary.DefaultFolder;
        Directory.CreateDirectory(libraryDir);
        var libraryFile = Path.Combine(libraryDir, id + ".ccpenh.json");
        File.WriteAllBytes(media, new byte[8]);
        File.WriteAllText(libraryFile, EnhancementSerializer.Save(Video("Library", media)));
        var w = new EnhancementPlayerWindow(null, null);
        try
        {
            w.Show();
            w.OpenLocalMediaFile(media);
            Assert.Equal(libraryFile, w.LoadedFilePath);
            Assert.Equal(Loc.Get("deeper_player_enh_source_library"), w.FindControl<TextBlock>("TxtEnhSource")!.Text);
            Assert.False(w.FindControl<Button>("BtnCreateNewEnhancement")!.IsVisible);

            File.WriteAllText(sidecar, EnhancementSerializer.Save(Video("Sidecar", media)));
            w.OpenLocalMediaFile(media);
            Assert.Equal(sidecar, w.LoadedFilePath);
            Assert.Equal(Loc.Get("deeper_player_enh_source_sidecar"), w.FindControl<TextBlock>("TxtEnhSource")!.Text);
        }
        finally { w.Close(); File.Delete(libraryFile); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    // WPF :494-528,554-557: promote the embedded enhancement and show the banner.
    [Fact]
    public Task EmbeddedPromotion() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-embedded-").FullName;
        var id = "promotion-" + Guid.NewGuid().ToString("N");
        var media = Path.Combine(dir, id + ".mp4");
        var promoted = Path.Combine(DeeperLocalLibrary.DefaultFolder, id + ".ccpenh.json");
        File.WriteAllBytes(media, new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        Assert.True(EnhancementMediaBundler.Export(Video(id, media), media, media).Success);
        var w = new EnhancementPlayerWindow(null, null);
        try
        {
            w.Show();
            w.OpenLocalMediaFile(media);
            Assert.Equal(promoted, w.LoadedFilePath);
            Assert.True(File.Exists(promoted));
            Assert.Equal(Loc.Get("deeper_player_enh_source_library"), w.FindControl<TextBlock>("TxtEnhSource")!.Text);
            var hint = w.FindControl<TextBlock>("TxtHint")!;
            Assert.True(hint.IsVisible);
            Assert.Equal(Loc.GetF("deeper_player_promoted_to_library_fmt", Path.GetFileName(promoted)), hint.Text);
            w.FindControl<Button>("BtnUnloadEnhancement")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(hint.IsVisible);
            await Task.Delay(TimeSpan.FromSeconds(6.5));
            Assert.False(hint.IsVisible);
        }
        finally { w.Close(); File.Delete(promoted); Directory.Delete(dir, true); }
    });

    // WPF :503-528: an existing match prevents a duplicate but the embedded version wins.
    [Fact]
    public Task ExistingMatchDoesNotReplaceEmbedded() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-existing-").FullName;
        var id = Guid.NewGuid().ToString("N");
        var media = Path.Combine(dir, id + ".mp4");
        var libraryDir = DeeperLocalLibrary.DefaultFolder;
        Directory.CreateDirectory(libraryDir);
        var libraryFile = Path.Combine(libraryDir, id + ".ccpenh.json");
        File.WriteAllBytes(media, new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        File.WriteAllText(libraryFile, EnhancementSerializer.Save(Video("Old", media)));
        var embeddedName = id + "-new";
        Assert.True(EnhancementMediaBundler.Export(Video(embeddedName, media), media, media).Success);
        var w = new EnhancementPlayerWindow(null, null);
        try
        {
            w.Show();
            w.OpenLocalMediaFile(media);
            Assert.Null(w.LoadedFilePath);
            Assert.Equal(embeddedName, w.FindControl<TextBlock>("TxtEnhName")!.Text);
            Assert.Equal(Loc.Get("deeper_player_enh_source_embedded"), w.FindControl<TextBlock>("TxtEnhSource")!.Text);
            Assert.Single(Directory.GetFiles(libraryDir, id + "*.ccpenh.json"));
        }
        finally { w.Close(); File.Delete(libraryFile); File.Delete(Path.Combine(libraryDir, embeddedName + ".ccpenh.json")); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    // WPF :554-592: a promotion hint expires after six seconds.
    [Fact]
    public Task TimedHintExpires() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var w = new EnhancementPlayerWindow(null, null);
        try
        {
            w.Show();
            typeof(EnhancementPlayerWindow).GetMethod("ShowHint", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!.Invoke(w, new object?[] { "Saved", 6d });
            var hint = w.FindControl<TextBlock>("TxtHint")!;
            Assert.True(hint.IsVisible);
            await Task.Delay(TimeSpan.FromSeconds(6.5));
            Assert.False(hint.IsVisible);
        }
        finally { w.Close(); }
    });

    // WPF :520-528: a failed promotion still loads the embedded enhancement for this session.
    [Fact]
    public Task FailedPromotionFallsBackToEmbedded() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-promotion-fail-").FullName;
        var media = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".mp4");
        var name = new string('x', 300); // exceeds the Linux filename limit
        File.WriteAllBytes(media, new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        Assert.True(EnhancementMediaBundler.Export(Video(name, media), media, media).Success);
        var w = new EnhancementPlayerWindow(null, null);
        try
        {
            w.Show();
            w.OpenLocalMediaFile(media);
            Assert.Null(w.LoadedFilePath);
            Assert.Equal(name, w.FindControl<TextBlock>("TxtEnhName")!.Text);
            Assert.Equal(Loc.Get("deeper_player_enh_source_embedded"), w.FindControl<TextBlock>("TxtEnhSource")!.Text);
            Assert.False(w.FindControl<TextBlock>("TxtHint")!.IsVisible);
        }
        finally { w.Close(); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    // WPF :546-592, :1262: missing hint survives status updates until another enhancement loads.
    [Fact]
    public Task MissingHintIsIndependentOfStatusAndClearsOnLoad() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-hint-").FullName;
        var media = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".webm");
        File.WriteAllBytes(media, new byte[8]);
        var w = new EnhancementPlayerWindow(null, null);
        try
        {
            w.Show();
            w.LoadEnhancementFromMemory(Video("Previous", media), "test");
            w.OpenLocalMediaFile(media);
            var hint = w.FindControl<TextBlock>("TxtHint")!;
            Assert.True(hint.IsVisible);
            Assert.Equal(Loc.Get("deeper_player_no_enh_for_media"), hint.Text);
            w.FindControl<Button>("BtnUnloadEnhancement")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(hint.IsVisible);
            Assert.Equal(Loc.Get("deeper_player_no_enh_for_media"), hint.Text);
            Assert.True(w.FindControl<Button>("BtnCreateNewEnhancement")!.IsVisible);
            w.FindControl<TextBlock>("TxtStatus")!.Text = "Playing";
            Assert.True(hint.IsVisible);
            Assert.Equal(Loc.Get("deeper_player_no_enh_for_media"), hint.Text);
            w.LoadEnhancementFromMemory(Video("Loaded", media), "test");
            Assert.False(hint.IsVisible);
        }
        finally { w.Close(); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });
}
