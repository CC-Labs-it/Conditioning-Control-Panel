using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using IconKind = FluentIcons.Common.Symbol;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class DeeperFollowupTests
{
    [Fact]
    public Task StartupSeedsOnceBeforeAnyShellExists() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = CoreSettings.Current;
        var folder = DeeperLocalLibrary.DefaultFolder;
        Directory.CreateDirectory(folder);
        var demo = Path.Combine(folder, "welcome.ccpenh.json");
        var before = File.Exists(demo) ? File.ReadAllBytes(demo) : null;
        try
        {
            File.Delete(demo);
            settings.HasSeededDeeperDemos = false;
            App.StartDeeperLibrary();
            Assert.True(File.Exists(demo));
            Assert.True(settings.HasSeededDeeperDemos);
            File.WriteAllText(demo, "user edit");
            App.StopDeeperLibrary();
            App.StartDeeperLibrary();
            Assert.Equal("user edit", File.ReadAllText(demo));
        }
        finally
        {
            App.StopDeeperLibrary();
            if (before == null) File.Delete(demo); else File.WriteAllBytes(demo, before);
        }
        Assert.Null(EnhancementResolver.LibraryMatchProvider);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(".ccpenh.json")]
    [InlineData(".CCPENH.JSON")]
    public Task PlayerFindsLibraryMatchAndShellSharesStartupLibrary(string suffix) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var folder = DeeperLocalLibrary.DefaultFolder;
        Directory.CreateDirectory(folder);
        var media = Path.Combine(TestUserDataProfile.Root, Guid.NewGuid() + ".mp4");
        var enhancementPath = Path.Combine(folder, Guid.NewGuid() + suffix);
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.HasSeededDeeperDemos = true;
            App.StartDeeperLibrary();
            File.WriteAllText(media, "test");
            var enhancement = new Enhancement { MediaType = MediaTypes.Video, MediaSource = media };
            enhancement.Metadata.Name = "Library match";
            File.WriteAllText(enhancementPath, EnhancementSerializer.Save(enhancement));
            Assert.Contains(DeeperLocalLibrary.Scan().Entries, e => e.FilePath == enhancementPath);
            CoreSettings.Current.PerformanceMode = true;
            shell = new MainShellWindow();
            shell.Show();
            shell.OpenInDeeperPlayer(media);
            var player = Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>());
            Assert.Equal(enhancementPath, player.LoadedFilePath);
            Assert.Equal(EnhancementDiscoverySource.Library, EnhancementResolver.ResolveForLocalMedia(media).Source);
            shell.ShowTab("deeper");
            Assert.Same(App.DeeperLibrary, typeof(MainShellWindow).GetField("_deeperLibrary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(shell));
        }
        finally
        {
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
            App.StopDeeperLibrary();
            File.Delete(media);
            File.Delete(enhancementPath);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PlayerOpenFailuresShowDetailsFromHubAndRow(bool row) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, Guid.NewGuid() + ".ccpenh.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, EnhancementSerializer.Save(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "missing.mp3" }));
        try
        {
            shell.Show();
            shell.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            shell.DeeperPlayerFactory = () => throw new IOException("player failed");
            var tab = shell.Named<DeeperTabView>("DeeperTab")!;
            if (row)
            {
                var border = tab.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("deeper-row") && b.DataContext is DeeperLibraryRowVm vm && vm.Entry.FilePath == path);
                var play = border.GetVisualDescendants().OfType<Button>().First(b => b.Content is IconGlyph { Kind: IconKind.Play });
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else tab.FindControl<Button>("BtnDeeperOpenPlayer")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("deeper_open_player_failed_title"), dialog.Title);
            Assert.Equal(Loc.GetF("deeper_open_player_failed_fmt", "IOException", "player failed"), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            dialog.Close();
        }
        finally { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); File.Delete(path); }
        return Task.CompletedTask;
    });
}
