using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PortalEffectQueueTests
{
    [Fact]
    public Task PendingStartKeepsThePanicRoute() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var previous = settings.PanicKeyEnabled;
        var bound = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = false;
        settings.PanicKeyEnabled = true;
        PortalPanicShortcut.BindForTests = (_, _) => bound.Task;
        try
        {
            MainShellWindow.StartEffect(() => ran = true);
            PortalPanicShortcut.SetSessionForTests("/test/effect-pending", null);
            MainShellWindow.ReleasePortalWhenUnused();
            Assert.True(PortalPanicShortcut.IsBound);
        }
        finally
        {
            bound.TrySetResult("F8");
            for (int i = 0; i < 200 && !ran; i++) await Task.Delay(10);
            PortalPanicShortcut.BindForTests = null;
            settings.PanicKeyEnabled = previous;
            MainShellWindow.ReleasePortalWhenUnused();
            await PortalPanicShortcut.UnbindAsync();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task StartsWaitInOrderAndKeepThePanicRoute(bool onFirst) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var previous = settings.PanicKeyEnabled;
        var bound = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<bool>();
        settings.PanicKeyEnabled = true;
        PortalPanicShortcut.BindForTests = (_, _) => bound.Task;
        try
        {
            MainShellWindow.StartEffect(() => events.Add(onFirst));
            // The portal has created a session but has not completed BindShortcuts yet.
            PortalPanicShortcut.SetSessionForTests("/test/effect-queue", null);
            MainShellWindow.StartEffect(() => events.Add(!onFirst));
            Assert.Empty(events);
            MainShellWindow.ReleasePortalWhenUnused();
            Assert.True(PortalPanicShortcut.IsBound);
            bound.TrySetResult("F8");
            for (int i = 0; i < 200 && events.Count < 2; i++) await Task.Delay(10);
            Assert.Equal(new[] { onFirst, !onFirst }, events);
        }
        finally
        {
            bound.TrySetResult("F8");
            for (int i = 0; i < 200 && events.Count < 2; i++) await Task.Delay(10);
            PortalPanicShortcut.BindForTests = null;
            settings.PanicKeyEnabled = previous;
            MainShellWindow.ReleasePortalWhenUnused();
            await PortalPanicShortcut.UnbindAsync();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
