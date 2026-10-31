using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesDailyFreeTests
{
    // WPF MainWindow.Exclusives.cs:1022 requests on each repaint, without awaiting
    // the network. App's existing TodayChanged subscription then repaints the gates.
    [Fact]
    public Task RepaintRequestsRefreshWithoutBlockingAndAdoptsTheNextGrant() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var old = (ExclusivesTabView.RefreshDailyFree, CoreEntitlement.IsFreeTodayProvider,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        bool free = false;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.HasLabProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = key => free && key == "haptics";
            ExclusivesTabView.RefreshDailyFree = () => { calls++; return pending.Task; };
            var view = new ExclusivesTabView();
            Assert.Equal(1, calls);
            Assert.False(pending.Task.IsCompleted);
            ExclusiveCardRow Haptics() => view.FindControl<ItemsControl>("ExclusivesShelf")!
                .Items.OfType<ExclusiveCardRow>().Single(r => r.Feature.Key == "haptics");
            Assert.True(Haptics().IsLocked);
            view.RefreshVault();
            Assert.Equal(2, calls); // delegate to Core's throttles, never throttle in the view
            Assert.False(pending.Task.IsCompleted);
            // Same queued repaint that App's TodayChanged subscriber makes after an override.
            free = true;
            pending.SetResult();
            Dispatcher.UIThread.Post(view.RefreshVault);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, calls);
            Assert.False(Haptics().IsLocked);
            Assert.True(Haptics().BadgeFreeToday);
        }
        finally
        {
            pending.TrySetResult();
            (ExclusivesTabView.RefreshDailyFree, CoreEntitlement.IsFreeTodayProvider,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider) = old;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedRefreshKeepsTheShelfUsable(bool asynchronous) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var old = ExclusivesTabView.RefreshDailyFree;
        int calls = 0;
        try
        {
            ExclusivesTabView.RefreshDailyFree = () =>
            {
                calls++;
                if (asynchronous) return Task.FromException(new InvalidOperationException("offline"));
                throw new InvalidOperationException("offline");
            };
            var view = new ExclusivesTabView();
            view.RefreshVault();
            await ExclusivesTabView.RefreshDailyFreeSafelyAsync();
            Assert.Equal(3, calls);
            Assert.NotEmpty(view.FindControl<ItemsControl>("ExclusivesShelf")!.Items.OfType<ExclusiveCardRow>());
            ExclusivesTabView.RefreshDailyFree = null; // unseeded render path
            view.RefreshVault();
            Assert.Equal(3, calls);
        }
        finally { ExclusivesTabView.RefreshDailyFree = old; }
    });

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
