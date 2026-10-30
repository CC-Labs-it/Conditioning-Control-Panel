using System;
using System.Linq;
using System.IO;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class DeeperFollowupTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Click(Control host, string name)
    {
        host.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task BothTourButtonsStartDeeperAndPanicEndsTheOverlay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSeen = CoreSettings.Current.HasSeenDeeperWelcome;
        var oldPerformance = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("deeper");
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            foreach (var name in new[] { "BtnDeeperTutorial", "BtnDeeperWelcomeTour" })
            {
                CoreSettings.Current.HasSeenDeeperWelcome = false;
                Click(view, name);
                Assert.True(CoreTutorial.IsActive);
                Assert.Equal("deeper", shell.CurrentTab);
                Assert.Equal(TutorialHead.Service.StepsFor(TutorialType.Deeper)[0].Id, CoreTutorial.CurrentStep!.Id);
                if (name == "BtnDeeperWelcomeTour")
                {
                    Assert.True(CoreSettings.Current.HasSeenDeeperWelcome);
                    Assert.False(view.FindControl<Border>("DeeperWelcomeCard")!.IsVisible);
                }
                var overlay = (TutorialOverlay?)typeof(TutorialHead).GetField("_shellOverlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
                Assert.NotNull(overlay);
                // A live card is never a shown window: it sits in the shell's OverlayLayer.
                Assert.True(overlay!.IsOnStage);
                Assert.Same(shell, overlay.StageWindow);
                PanicSurfaces.All.Single(s => s.Id == "tour").Stop(shell);
                Dispatcher.UIThread.RunJobs();
                Assert.False(CoreTutorial.IsActive);
                Assert.False(overlay.IsOnStage);
            }
        }
        finally
        {
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = oldShell;
            shell.Close();
            CoreSettings.Current.HasSeenDeeperWelcome = oldSeen;
            CoreSettings.Current.PerformanceMode = oldPerformance;
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task ImportButtonAndWindowDropPromoteOnceAndRevealTheRow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldPicker = MainShellWindow.DeeperImportPicker;
        var oldFolder = CoreSettings.Current.DeeperLastDirectory;
        var oldRecent = CoreSettings.Current.DeeperRecentFiles.ToList();
        var oldPerformance = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var source = Directory.CreateTempSubdirectory("ccp-deeper-picker-");
        var shell = new MainShellWindow();
        string? saved = null;
        try
        {
            var input = Path.Combine(source.FullName, "incoming.json");
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "input.mp3" };
            enhancement.Metadata.Name = "Import-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(input, EnhancementSerializer.Save(enhancement));
            var calls = 0;
            MainShellWindow.DeeperImportPicker = owner =>
            {
                Assert.Same(shell, owner);
                calls++;
                return Task.FromResult(new[] { input });
            };
            shell.Show();
            shell.ShowTab("deeper");
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            var model = (DeeperTabViewModel)view.DataContext!;
            Click(view, "BtnDeeperImport");
            var row = Assert.Single(model.FilteredEntries.Where(r => r.Name == enhancement.Metadata.Name));
            saved = row.Entry.FilePath;
            Assert.True(File.Exists(saved));
            Assert.True(row.IsHighlighted);
            Assert.False(row.IsSelected);  // WPF reveals without opening/selecting the row
            Assert.Equal(source.FullName, CoreSettings.Current.DeeperLastDirectory);
            model.ReloadLibrary();
            Assert.True(model.FilteredEntries.Single(r => r.Entry.FilePath == saved).IsHighlighted);
            Click(view, "BtnDeeperImport");
            Assert.Equal(2, calls);
            Assert.Single(model.FilteredEntries.Where(r => r.Name == enhancement.Metadata.Name));
            await shell.ImportDroppedFilesAsync(new[] { saved });
            Assert.Single(model.FilteredEntries.Where(r => r.Name == enhancement.Metadata.Name));
            Assert.True(model.FilteredEntries.Single(r => r.Entry.FilePath == saved).IsHighlighted);
            view.FinishImportReveal();
            Assert.DoesNotContain(model.FilteredEntries, r => r.IsHighlighted);

            // A filtered-out import must not change the user's search or create a row flash.
            model.SetSearch("unrelated-filter");
            shell.ImportEnhancementFiles(new[] { saved });
            Assert.Empty(model.FilteredEntries);
        }
        finally
        {
            MainShellWindow.DeeperImportPicker = oldPicker;
            shell.Close();
            if (saved != null) File.Delete(saved);
            source.Delete(true);
            CoreSettings.Current.DeeperLastDirectory = oldFolder;
            CoreSettings.Current.DeeperRecentFiles = oldRecent;
            CoreSettings.Current.PerformanceMode = oldPerformance;
        }
    });

}
