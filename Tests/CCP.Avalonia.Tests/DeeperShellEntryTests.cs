using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class DeeperFollowupTests
{
    [Fact]
    public Task VisibleLibraryTracksExternalEditsAndKeepsReveal() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, Guid.NewGuid() + ".ccpenh.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "missing.mp3" };
        File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
        try
        {
            shell.Show();
            shell.ShowTab("deeper");
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            var model = (DeeperTabViewModel)view.DataContext!;
            model.Highlight(path);
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (model.FilteredEntries.Any(r => r.Entry.FilePath == path && r.Entry.Creator == "External edit"))
                    refreshed.TrySetResult();
            }
            model.PropertyChanged += Observe;
            try
            {
                enhancement.Metadata.Creator = "External edit";
                File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
                await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(model.FilteredEntries.Single(r => r.Entry.FilePath == path).IsHighlighted);
            }
            finally { model.PropertyChanged -= Observe; }
        }
        finally { shell.Close(); File.Delete(path); }
    });

    [Fact]
    public Task PlayerEditorUsesShellOwnershipAndDedupe() => AvaloniaTestDispatcher.RunAsync(() =>
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
            shell.BtnDeeperOpenPlayer_Click();
            var player = Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>());
            player.LoadEnhancementFile(path);
            Assert.Equal(path, player.LoadedFilePath);
            player.FindControl<Button>("BtnOpenInEditor")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(shell.OwnedWindows.OfType<DeeperEditorWindow>());
            shell.OpenDeeperEditorFromPlayer(path);
            Assert.Same(editor, Assert.Single(shell.OwnedWindows.OfType<DeeperEditorWindow>()));
            Assert.Equal(path, editor.LoadedFilePath);
            shell.ShowTab("settings");
            var updated = EnhancementSerializer.LoadFromFile(path);
            updated.Metadata.Creator = "Saved in editor";
            File.WriteAllText(path, EnhancementSerializer.Save(updated));
            editor.Close();
            var model = (DeeperTabViewModel)shell.Named<DeeperTabView>("DeeperTab")!.DataContext!;
            Assert.Contains(model.FilteredEntries, r => r.Entry.FilePath == path && r.Entry.Creator == "Saved in editor");
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close();
            File.Delete(path);

        }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData("edit")]
    [InlineData("play")]
    public Task MediaHandoffRoutesEditAndPlay(string action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var media = Path.Combine(TestUserDataProfile.Root, "handoff.MP4");
        File.WriteAllText(media, "test");
        try
        {
            shell.Show();
            LauncherWindow.RouteHandoff(shell, DeeperFileOpen.Encode(new[] { "--" + action, media }));
            if (action == "edit")
            {
                var editor = Assert.Single(shell.OwnedWindows.OfType<DeeperEditorWindow>());
                var enhancement = (Enhancement)typeof(DeeperEditorWindow).GetField("_enhancement", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                Assert.Equal(media, enhancement.MediaSource);
                Assert.Equal(MediaTypes.Video, enhancement.MediaType);
            }
            else Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>());
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close();
            File.Delete(media);
        }
        return Task.CompletedTask;
    });

}
