using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF TutorialService.OpenDeeperPlayerWithLastSavedEnhancement: the walkthrough follow-up
/// reuses the shell's one player window (EnhancementPlayerWindow.ShowOrActivate), restack b231 #4701.</summary>
public class TutorialPlayerReuseTests
{
    [Fact]
    public Task TutorialReusesShellPlayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        var old = TutorialHeadHooks.ShellProvider;
        try
        {
            shell.Show(); TutorialHeadHooks.ShellProvider = () => shell;
            var player = shell.ShowOrActivateDeeperPlayer();
            TutorialHeadHooks.OpenDeeperPlayerWithLastSaved();
            Assert.Same(player, Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>()));
            player.Close();
        }
        finally { TutorialHeadHooks.ShellProvider = old; shell.Close(); }
        return Task.CompletedTask;
    });
}
