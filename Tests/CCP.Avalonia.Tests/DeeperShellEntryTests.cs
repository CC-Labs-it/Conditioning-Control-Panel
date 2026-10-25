using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperShellEntryTests
{
    [Fact]
    public Task WelcomeAndHeaderTourButtonsStartTheExistingPanicRegisteredTour() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSeen = CoreSettings.Current.HasSeenDeeperWelcome;
        var oldVisit = CoreSettings.Current.HasSeenDeeperTab;
        var oldMotion = CoreSettings.Current.MotionLevel;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            CoreSettings.Current.HasSeenDeeperTab = false;
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Reduced;
            shell.ShowTab("settings");
            shell.StartDeeperTabPulse();
            var pulse = typeof(MainShellWindow).GetField("_deeperPulse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.NotNull(pulse.GetValue(shell));
            PanicSurfaces.All.Single(s => s.Id == "deeper-pulse").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(pulse.GetValue(shell));
            Assert.Equal(1, shell.Named<Button>("DoorPlay")!.Opacity);
            shell.ShowTab("deeper");
            Assert.True(CoreSettings.Current.HasSeenDeeperTab);
            shell.StartDeeperTabPulse();
            Assert.Null(pulse.GetValue(shell));
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            shell.BtnDeeperWelcomeTour_Click();
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreSettings.Current.HasSeenDeeperWelcome);
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal("Deeper", CoreTutorial.CurrentTourName);
            Assert.True(TutorialHead.ShellOverlay!.IsVisible);
            PanicSurfaces.All.Single(s => s.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            var button = shell.Named<DeeperTabView>("DeeperTab")!.FindControl<Button>("BtnDeeperTutorial")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreTutorial.IsActive);
            PanicSurfaces.All.Single(s => s.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();

            var source = Path.Combine(TestUserDataProfile.Root, "import-" + Guid.NewGuid().ToString("N") + ".ccpenh.json");
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "missing.mp3" };
            enhancement.Metadata.Name = "Entry test " + Guid.NewGuid().ToString("N");
            File.WriteAllText(source, EnhancementSerializer.Save(enhancement));
            string? imported = null;
            try
            {
                shell.ShowTab("settings");
                shell.ImportDroppedFilesAsync(new[] { source }).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("settings", shell.CurrentTab);   // WPF reveals the row without switching tabs
                var model = (DeeperTabViewModel)shell.Named<DeeperTabView>("DeeperTab")!.DataContext!;
                var row = Assert.Single(model.FilteredEntries.Where(r => r.Entry.Name == enhancement.Metadata.Name));
                imported = row.Entry.FilePath;
                Assert.False(row.IsSelected);                 // ...and without selecting it
                Assert.True(row.IsHighlighted);
                Assert.Empty(shell.ImportEnhancementFiles(new[] { source }).Imported);
                Assert.True(shell.DeeperRevealTimer!.IsEnabled);
                shell.DeeperRevealTimer.Stop();               // the 1800 ms flash must not race the watcher wait
                shell.ShowTab("deeper");
                var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                enhancement.Metadata.Creator = "Updated creator";
                void ObserveRefresh(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (model.FilteredEntries.Any(r => r.Entry.FilePath == imported && r.Entry.Creator == "Updated creator"))
                        refreshed.TrySetResult();
                }
                model.PropertyChanged += ObserveRefresh;
                try
                {
                    File.WriteAllText(imported, EnhancementSerializer.Save(enhancement));
                    await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));   // throws if the visible hub never refreshed
                    Assert.True(model.FilteredEntries.Single(r => r.Entry.FilePath == imported).IsHighlighted);
                }
                finally { model.PropertyChanged -= ObserveRefresh; }
                shell.HandlePendingFileOpen("edit", "missing.MP4");
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(shell.OwnedWindows, w => w is ConditioningControlPanel.Avalonia.Views.Deeper.DeeperEditorWindow);
                foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            }
            finally
            {
                File.Delete(source);
                if (imported != null) File.Delete(imported);
            }
        }
        finally
        {
            TutorialHead.ResetForTests();
            shell.Close();
            TutorialHeadHooks.ShellProvider = oldShell;
            CoreSettings.Current.HasSeenDeeperWelcome = oldSeen;
            CoreSettings.Current.HasSeenDeeperTab = oldVisit;
            CoreSettings.Current.MotionLevel = oldMotion;
        }
    });
}
