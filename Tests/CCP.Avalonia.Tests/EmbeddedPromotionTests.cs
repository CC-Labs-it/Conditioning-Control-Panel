using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
        App.StartDeeperLibrary();
        var folder = App.DeeperLibrary!.LibraryFolder;
        var moved = folder + "." + Guid.NewGuid();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "source.mp3");
        var media = Path.Combine(dir, "media.mp3");
        File.WriteAllBytes(source, new byte[] { 0, 0, 0, 0 });
        var embedded = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = media };
        embedded.Metadata.Name = Guid.NewGuid().ToString();
        Assert.True(EnhancementMediaBundler.Export(embedded, source, media).Success);
        var player = new EnhancementPlayerWindow(null, null);
        Directory.Move(folder, moved);
        File.WriteAllText(folder, "block directory creation");
        try
        {
            player.Show();
            player.OpenLocalMediaFile(media);
            Assert.Null(player.LoadedFilePath);
            Assert.Contains(embedded.Metadata.Name, player.FindControl<TextBlock>("TxtEnhName")!.Text);
            Assert.False(player.FindControl<TextBlock>("TxtHint")!.IsVisible);
        }
        finally
        {
            player.Close();
            File.Delete(folder);
            Directory.Move(moved, folder);
            Directory.Delete(dir, true);
            App.StopDeeperLibrary();
        }
        return Task.CompletedTask;
    });
}
