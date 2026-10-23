using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiRingDoorTests
{
    [Fact]
    public void RestoredDoorsFollowLiveAvailabilityAndIntakePass()
    {
        var saved = (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider, JustDropService.DoorAvailable);
        try
        {
            CoreAccount.IsLoggedInProvider = () => false;
            CoreAccount.HasLabAccessProvider = () => false;
            JustDropService.SetServerEnabledForTests(false);
            var intake = EmiTargets.Find("intake");
            var shop = EmiTargets.Find("justdrop");
            Assert.NotNull(intake);
            Assert.NotNull(shop);
            Assert.True(intake.Available);
            Assert.True(intake.Locked);
            Assert.False(shop.Available);
            CoreAccount.HasLabAccessProvider = () => true;
            JustDropService.SetServerEnabledForTests(true);
            Assert.False(intake.Locked);
            Assert.True(shop.Available);
            Assert.False(shop.Locked);
        }
        finally
        {
            (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider) = (saved.Item1, saved.Item2);
            JustDropService.SetServerEnabledForTests(saved.Item3);
        }
    }

    [Fact]
    public Task PicksReachExistingHostsAndPanicClosesThem() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var saved = (CoreAccount.HasLabAccessProvider, CoreAi.IsAvailableProvider, JustDropService.DoorAvailable);
        var state = ConditioningControlPanel.Services.EmiDesk.EmiState.Current;
        var usage = state.Usage.ToArray(); var score = state.OpenScore.ToArray(); var at = state.UsageAt.ToArray();
        var shell = new MainShellWindow();
        EmiTargets.ShellForTests = shell;
        try
        {
            CoreAccount.HasLabAccessProvider = () => true;
            CoreAi.IsAvailableProvider = () => true;
            EmiTargets.Find("intake")!.Open();
            Assert.Single(IntakeHostWindow.Snapshot());
            JustDropService.SetServerEnabledForTests(true);
            EmiTargets.Find("justdrop")!.Open();
            Assert.NotNull(GameWindow.OpenForTests(GameWindow.JustDropId));
            PanicSurfaces.StopAll("test", shell);
            Assert.Empty(IntakeHostWindow.Snapshot());
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic(); GameWindow.CloseAllForPanic(); shell.Close();
            EmiTargets.ShellForTests = null;
            state.Usage.Clear(); foreach (var p in usage) state.Usage[p.Key] = p.Value;
            state.OpenScore.Clear(); foreach (var p in score) state.OpenScore[p.Key] = p.Value;
            state.UsageAt.Clear(); foreach (var p in at) state.UsageAt[p.Key] = p.Value;
            (CoreAccount.HasLabAccessProvider, CoreAi.IsAvailableProvider) = (saved.Item1, saved.Item2);
            JustDropService.SetServerEnabledForTests(saved.Item3);
        }
        return Task.CompletedTask;
    });
}
