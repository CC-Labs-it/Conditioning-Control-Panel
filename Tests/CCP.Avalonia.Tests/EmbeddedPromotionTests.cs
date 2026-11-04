using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class DeeperFollowupTests
{
    [Fact]
    public Task EmbeddedPromotionAndExistingMatch() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        App.StartDeeperLibrary();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "source.mp3");
        var media = Path.Combine(dir, "media.mp3");
        File.WriteAllBytes(source, new byte[] { 0, 0, 0, 0 });
        var name = Guid.NewGuid().ToString();
        var embedded = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = media };
        embedded.Metadata.Name = name;
        Assert.True(EnhancementMediaBundler.Export(embedded, source, media).Success);
        var player = new EnhancementPlayerWindow(null, null);
        string? saved = null;
        try
        {
            player.Show();
            player.OpenLocalMediaFile(media);
            saved = player.LoadedFilePath;
            Assert.NotNull(saved);
            Assert.True(File.Exists(saved));
            var hint = player.FindControl<TextBlock>("TxtHint")!;
            Assert.True(hint.IsVisible);
            Assert.Contains(Path.GetFileName(saved), hint.Text);
            Assert.Equal(Dock.Right, DockPanel.GetDock(hint));
            Assert.Equal(420, hint.MaxWidth);
            Assert.Equal(TextTrimming.CharacterEllipsis, hint.TextTrimming);
            Assert.Equal(new Thickness(12, 0, 0, 0), hint.Margin);
            player.LoadEnhancementFromMemory(embedded, "manual");
            Assert.False(hint.IsVisible);
            File.Delete(saved);
            player.OpenLocalMediaFile(media);
            saved = player.LoadedFilePath;
            Assert.NotNull(saved);
            Assert.True(hint.IsVisible);
            await Task.Delay(TimeSpan.FromSeconds(7));
            Assert.False(hint.IsVisible);
            player.OpenLocalMediaFile(media);
            Assert.Null(player.LoadedFilePath);
            Assert.False(hint.IsVisible);
            Assert.Single(Directory.GetFiles(App.DeeperLibrary!.LibraryFolder, name + "*.ccpenh.json"));
        }
        finally
        {
            player.Close();
            if (saved != null) File.Delete(saved);
            Directory.Delete(dir, true);
            App.StopDeeperLibrary();
        }
    });

    [Fact]
    public Task EmbeddedSurvivesFailedSave() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        // CorePaths is initialized by TestUserDataProfile's module initializer, before any test.
        Assert.Equal(TestUserDataProfile.Root, Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"));
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var dir = Path.Combine(TestUserDataProfile.Root, Guid.NewGuid().ToString());
        EnhancementPlayerWindow? player = null;
        string? blocked = null;
        try
        {
            App.StartDeeperLibrary();
            Directory.CreateDirectory(dir);
            var source = Path.Combine(dir, "source.mp3");
            var media = Path.Combine(dir, "media.mp3");
            File.WriteAllBytes(source, new byte[] { 0, 0, 0, 0 });
            var embedded = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = media };
            embedded.Metadata.Name = Guid.NewGuid().ToString();
            Assert.True(EnhancementMediaBundler.Export(embedded, source, media).Success);
            var library = App.DeeperLibrary!;
            blocked = Path.Combine(library.LibraryFolder, library.SuggestedFileName(embedded));
            Directory.CreateDirectory(blocked);
            player = new EnhancementPlayerWindow(null, null);
            player.Show();
            player.OpenLocalMediaFile(media);
            Assert.Null(player.LoadedFilePath);
            Assert.Contains(embedded.Metadata.Name, player.FindControl<TextBlock>("TxtEnhName")!.Text);
            Assert.False(player.FindControl<TextBlock>("TxtHint")!.IsVisible);
        }
        finally
        {
            player?.Close();
            if (blocked != null && Directory.Exists(blocked)) Directory.Delete(blocked, true);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            App.StopDeeperLibrary();
        }
        return Task.CompletedTask;
    });
}
