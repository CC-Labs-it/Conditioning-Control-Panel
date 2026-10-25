using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerDiscoveryTests
{
    private static void Invoke(object obj, string name, params object?[] args) =>
        obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);

    [Fact]
    public Task LibraryDiscoveryLoadsTheProjectAndSidecarWins() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-library-").FullName;
        var media = Path.Combine(dir, Guid.NewGuid() + ".mp3");
        var libraryPath = Path.Combine(App.EnhancementLibrary.LibraryFolder, Guid.NewGuid() + ".ccpenh.json");
        var sidecar = Path.ChangeExtension(media, ".ccpenh.json");
        var open = DeeperLocalAudio.Open;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(null);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            win.Show();
            File.WriteAllText(libraryPath, EnhancementSerializer.Save(new Enhancement
            {
                MediaSource = media, MediaType = MediaTypes.Audio,
                Metadata = new EnhancementMetadata { Name = "Library discovery" }
            }));
            Assert.Equal(libraryPath, App.EnhancementLibrary.FindMatch(media, MediaTypes.Audio)?.FilePath);
            win.OpenLocalMediaFile(media);
            Assert.True(win.LoadedFilePath == libraryPath, win.FindControl<TextBlock>("TxtStatus")!.Text);
            Assert.False(win.FindControl<Button>("BtnCreateNewEnhancement")!.IsVisible);
            Assert.False(win.FindControl<TextBlock>("TxtHint")!.IsVisible);
            File.Copy(libraryPath, sidecar);
            win.OpenLocalMediaFile(media);
            Assert.Equal(sidecar, win.LoadedFilePath);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; File.Delete(libraryPath); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmbeddedProjectIsPromotedOnceAndShowsSixSecondHint() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-embedded-").FullName;
        var source = Path.Combine(dir, "source.mp3");
        var media = Path.Combine(dir, Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(source, new byte[32]);
        var enhancement = new Enhancement
        {
            MediaSource = media, MediaType = MediaTypes.Audio,
            Metadata = new EnhancementMetadata { Name = Guid.NewGuid().ToString("N") }
        };
        Assert.True(EnhancementMediaBundler.Export(enhancement, source, media).Success);
        var open = DeeperLocalAudio.Open;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(null);
        var win = new EnhancementPlayerWindow(null, null);
        string? promoted = null;
        try
        {
            win.Show();
            win.OpenLocalMediaFile(media);
            promoted = win.LoadedFilePath;
            Assert.NotNull(promoted);
            Assert.True(File.Exists(promoted));
            Assert.Equal(App.EnhancementLibrary.LibraryFolder, Path.GetDirectoryName(promoted));
            var hint = win.FindControl<TextBlock>("TxtHint")!;
            Assert.True(hint.IsVisible);
            Assert.Equal(string.Format(Loc.Get("deeper_player_promoted_to_library_fmt"), Path.GetFileName(promoted)), hint.Text);
            var timer = (DispatcherTimer)typeof(EnhancementPlayerWindow)
                .GetField("_promotedClearTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(win)!;
            Assert.Equal(TimeSpan.FromSeconds(6), timer.Interval);
            // Let the actual one-shot timer expire on the headless dispatcher.
            win.FindControl<TextBlock>("TxtStatus")!.Text = "transport";
            await Task.Delay(TimeSpan.FromSeconds(6.2));
            Dispatcher.UIThread.RunJobs();
            Assert.False(hint.IsVisible);
            Assert.False(timer.IsEnabled);
            Assert.Equal("transport", win.FindControl<TextBlock>("TxtStatus")!.Text);
            win.OpenLocalMediaFile(media);
            Assert.Null(win.LoadedFilePath); // existing match: WPF keeps embedded in memory
            Assert.False(hint.IsVisible);
            Assert.Equal(promoted, App.EnhancementLibrary.FindMatch(media, MediaTypes.Audio)!.FilePath);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; if (promoted != null) File.Delete(promoted); Directory.Delete(dir, true); }
    });

    [Fact]
    public Task InvalidDurationsDoNotConsumeTheFirstMeasurementAndCloseStopsHints() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-player-cache-").FullName;
        var media = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(media, new byte[16]);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            win.Show();
            foreach (var invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                Invoke(win, "RememberDurationOnce", media, invalid);
            Assert.False(MediaDurationCache.TryGetCached(media, out _));
            Invoke(win, "RememberDurationOnce", media, 123d);
            Invoke(win, "RememberDurationOnce", media, 999d);
            Assert.True(MediaDurationCache.TryGetCached(media, out var seconds));
            Assert.Equal(123, seconds);
            Invoke(win, "ShowHint", "promotion", 6d);
            var timer = (DispatcherTimer)typeof(EnhancementPlayerWindow)
                .GetField("_promotedClearTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(win)!;
            win.Close();
            Assert.False(timer.IsEnabled);
            Assert.False(win.FindControl<TextBlock>("TxtHint")!.IsVisible);
        }
        finally { win.Close(); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
