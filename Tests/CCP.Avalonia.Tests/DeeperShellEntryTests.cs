using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
    // A second launch during a cold start is acked at once and opens its file once the shell has opened.
    [Fact]
    public Task AMediaHandoffBeforeTheShellOpensIsRoutedOnceItOpens() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var savedPerformance = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var media = Path.Combine(TestUserDataProfile.Root, "early.mp3");
        File.WriteAllText(media, "test");
        var saved = ConditioningControlPanel.Avalonia.App.Handoffs;
        var queue = new ConditioningControlPanel.Avalonia.Platform.HandoffQueue();
        ConditioningControlPanel.Avalonia.App.Handoffs = queue;
        try
        {
            Assert.True(queue.Enqueue(DeeperFileOpen.Encode(new[] { "--edit", media })).IsCompleted);
            ConditioningControlPanel.Avalonia.App.RouteHandoffsOnceOpened(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(DeeperEditorWindow.OpenEditors);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(shell.OwnedWindows.OfType<DeeperEditorWindow>());
            var enhancement = (Enhancement)typeof(DeeperEditorWindow).GetField("_enhancement", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            Assert.Equal(media, enhancement.MediaSource);
            Assert.Equal(MediaTypes.Audio, enhancement.MediaType);
        }
        finally
        {
            ConditioningControlPanel.Avalonia.App.Handoffs = saved;
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            LauncherWindow.Instance?.Close();
            shell.Close();
            CoreSettings.Current.PerformanceMode = savedPerformance;
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
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Assert.True(LauncherWindow.BackToLauncher(shell));
            var launcher = LauncherWindow.Instance!;
            CoreSettings.Current.PerformanceMode = false;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            Assert.False(shell.IsVisible);
            LauncherWindow.RouteHandoff(shell, DeeperFileOpen.Encode(new[] { "--" + action, media }));
            Assert.False(shell.IsVisible);
            Assert.Empty(shell.OwnedWindows);
            Assert.DoesNotContain(DeeperEditorWindow.OpenEditors, e => e.LoadedFilePath == null);
            launcher.StepFx(0.3);
            Assert.True(shell.IsVisible);
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
            LauncherWindow.Instance?.Close();
            shell.Close();
            File.Delete(media);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PlayerReusesAnEditorOutsideTheShellRegistry() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var path = Path.Combine(TestUserDataProfile.Root, "external.ccpenh.json");
        var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" };
        File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
        var external = new DeeperEditorWindow(enhancement, path);
        try
        {
            shell.Show();
            external.Show();
            external.WindowState = WindowState.Minimized;
            shell.BtnDeeperOpenPlayer_Click();
            var player = Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>());
            player.LoadEnhancementFile(path);
            Click(player, "BtnOpenInEditor");
            Assert.Same(external, Assert.Single(DeeperEditorWindow.OpenEditors));
            Assert.Equal(WindowState.Normal, external.WindowState);
        }
        finally
        {
            external.Close();
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close();
            File.Delete(path);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    public Task OpenErrorsUseTranslatedTitlesAndKeepExceptionDetails(string language) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var localization = LocalizationManager.Instance;
        var previous = localization.CurrentLanguage;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            localization.SetLanguage(language);
            foreach (var surface in new[] { "Player", "Editor" })
            {
                var prefix = "deeper_open_" + surface.ToLowerInvariant() + "_failed";
                var title = Loc.Get(prefix + "_title");
                Assert.NotEqual(prefix + "_title", title);
                if (language != "en") Assert.NotEqual("Open " + surface + " failed", title);
                typeof(MainShellWindow).GetMethod("ReportDeeperOpenError", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(shell, new object[] { new IOException("test detail"), surface });
                var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal(title, dialog.Title);
                Assert.Equal(Loc.GetF(prefix + "_fmt", "IOException", "test detail"), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                if (language == "en") Assert.Equal($"Couldn't open Deeper {surface}:\n\nIOException: test detail", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.Contains("IOException: test detail", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                dialog.Close();
            }
        }
        finally { shell.Close(); localization.SetLanguage(previous); }
        return Task.CompletedTask;
    });

    // WPF MainWindow.DeeperTab.cs:613-616,664-667 shows the box even with the panel hidden (first launch
    // behind the launcher, tray): here it opens ownerless, centred on screen, instead of only being logged.
    [Fact]
    public Task OpenErrorsShowWithThePanelHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var savedPerformance = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        MessageDialog? shown = null;
        void OnOpened(object? s, RoutedEventArgs e) => shown ??= s as MessageDialog;
        using var hook = Window.WindowOpenedEvent.AddClassHandler<MessageDialog>(OnOpened);
        try
        {
            Assert.False(shell.IsVisible);
            typeof(MainShellWindow).GetMethod("ReportDeeperOpenError", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, new object[] { new IOException("test detail"), "Editor" });
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(shown);
            Assert.True(shown!.IsVisible);
            Assert.Null(shown.Owner);
            Assert.Equal(Loc.Get("deeper_open_editor_failed_title"), shown.Title);
            Assert.Contains("IOException: test detail", shown.FindControl<TextBlock>("TxtMessage")!.Text);
        }
        finally { shown?.Close(); shell.Close(); CoreSettings.Current.PerformanceMode = savedPerformance; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("edit")]
    [InlineData("play")]
    public Task FirstLaunchMediaKeepsTheLauncherAndConsumesOnce(string action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        CoreSettings.Current.Welcomed = CoreSettings.Current.HasAcceptedAgeVerification = true;
        var shell = new MainShellWindow();
        var media = Path.Combine(TestUserDataProfile.Root, "boot.mp4");
        File.WriteAllText(media, "test");
        Window? opened = null;
        object? Player() => typeof(MainShellWindow).GetField("_deeperPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell);
        try
        {
            var args = new[] { "--" + action, media };
            LauncherWindow.Boot = ConditioningControlPanel.Services.Launcher.LauncherBoot.Decide(args, true, true, false);
            ConditioningControlPanel.Avalonia.App.QueuePendingDeeperFileOpen(shell, args);
            Assert.Equal(ConditioningControlPanel.Services.Launcher.BootDecision.LauncherFirst, LauncherWindow.Boot);
            void Route(object? sender, EventArgs e) { shell.Opened -= Route; LauncherWindow.RouteBoot(shell); }
            shell.Opened += Route;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IsVisible);
            Assert.True(LauncherWindow.Instance!.IsVisible);
            opened = action == "edit" ? Assert.Single(DeeperEditorWindow.OpenEditors) : Assert.IsType<EnhancementPlayerWindow>(Player());
            Assert.True(opened.IsVisible);
            Assert.Null(opened.Owner);
            opened.Close();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(DeeperEditorWindow.OpenEditors);
            Assert.Null(Player());
        }
        finally
        {
            opened?.Close();
            foreach (var editor in DeeperEditorWindow.OpenEditors.ToArray()) editor.Close();
            (Player() as Window)?.Close();
            LauncherWindow.Instance?.Close();
            shell.Close();
            File.Delete(media);
        }
        return Task.CompletedTask;
    });

}

public sealed partial class DeeperFollowupTests
{
    [Fact]
    public Task FreshWelcomeDemoIsSeededAndOpensInEditor() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        CoreSettings.Current.HasSeededDeeperDemos = false;
        CoreSettings.Current.HasSeenDeeperWelcome = false;
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, "welcome.ccpenh.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        File.Delete(path);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("deeper");
            Assert.True(File.Exists(path));
            Assert.True(CoreSettings.Current.HasSeededDeeperDemos);
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            Click(view, "BtnDeeperWelcomeDemo");
            Assert.True(CoreSettings.Current.HasSeenDeeperWelcome);
            var editor = Assert.Single(shell.OwnedWindows.OfType<DeeperEditorWindow>());
            Assert.Equal(path, editor.LoadedFilePath);

            editor.Close();
            File.Delete(path);
            using (var library = new EnhancementLibrary())
                Assert.False(File.Exists(path)); // a deleted demo stays deleted
            File.WriteAllText(path, "user edit");
            CoreSettings.Current.HasSeededDeeperDemos = false;
            using (var library = new EnhancementLibrary())
                Assert.Equal("user edit", File.ReadAllText(path)); // existing files win
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close();
            if (previous == null) File.Delete(path); else File.WriteAllBytes(path, previous);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstRunKeepsAndOpensTheEditedMixedCaseWelcomeDemo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var folder = DeeperLocalLibrary.DefaultFolder;
        Directory.CreateDirectory(folder);
        bool IsWelcome(string path) => string.Equals(Path.GetFileName(path), "welcome.ccpenh.json", StringComparison.OrdinalIgnoreCase);
        var previous = Directory.GetFiles(folder).Where(IsWelcome).Select(path => (path, bytes: File.ReadAllBytes(path))).ToArray();
        var editedPath = Path.Combine(folder, "WELCOME.CCPENH.JSON");
        MainShellWindow? shell = null;
        try
        {
            foreach (var (path, _) in previous) File.Delete(path);
            var demo = EnhancementSerializer.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Resources", "DeeperDemos", "welcome.ccpenh.json"));
            demo.Metadata.Name = "Edited welcome";
            var editedJson = EnhancementSerializer.Save(demo);
            File.WriteAllText(editedPath, editedJson);
            CoreSettings.Current.HasSeededDeeperDemos = false;
            CoreSettings.Current.HasSeenDeeperWelcome = false;
            CoreSettings.Current.PerformanceMode = true;
            ConditioningControlPanel.Avalonia.App.StartDeeperLibrary();
            Assert.True(CoreSettings.Current.HasSeededDeeperDemos);
            Assert.Equal(editedJson, File.ReadAllText(editedPath));
            Assert.Single(Directory.GetFiles(folder), IsWelcome);

            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("deeper");
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            Click(view, "BtnDeeperWelcomeDemo");
            Assert.Equal(editedPath, Assert.Single(shell.OwnedWindows.OfType<DeeperEditorWindow>()).LoadedFilePath);
        }
        finally
        {
            if (shell != null) { foreach (var window in shell.OwnedWindows.ToArray()) window.Close(); shell.Close(); }
            ConditioningControlPanel.Avalonia.App.StopDeeperLibrary();
            foreach (var path in Directory.GetFiles(folder).Where(IsWelcome)) File.Delete(path);
            foreach (var (path, bytes) in previous) File.WriteAllBytes(path, bytes);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WelcomeCardUsesTheWpfDemoCopy() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("deeper");
            var card = shell.Named<DeeperTabView>("DeeperTab")!.FindControl<Border>("DeeperWelcomeCard")!;
            var text = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
            Assert.Contains(Loc.Get("deeper_welcome_card_body"), text);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
