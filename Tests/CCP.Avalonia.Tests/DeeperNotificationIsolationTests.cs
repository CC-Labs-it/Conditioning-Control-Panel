using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Deliberately leak notification state in one test, then observe it from the next.
/// The real assembly hook must isolate queue replay, hosts and sticky-key deduplication.
/// Keep this class in shard 0 with DeeperSubmissionStatusTests (the original CI failure).</summary>
[TestCaseOrderer(typeof(ByMethodName))]
public sealed class DeeperNotificationIsolationTests
{
    private const string Key = "deeper-isolation-sticky";

    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
        {
            var fonts = ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] =
                new FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        body();
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_LeavesOrdinaryAndStickyNotificationsQueued() => OnUi(() =>
    {
        AppA.Notifications.Show("previous test ordinary", duration: TimeSpan.FromHours(1));
        AppA.Notifications.ShowSticky(Key, "previous test sticky");
        // No host: deliberately leave both notifications queued for the next test.
    });

    [Fact]
    public Task B_SeesNoQueuedNotificationsAndLeavesALiveStickyKey() => OnUi(() =>
    {
        var host = new StackPanel();
        AppA.Notifications.AttachHost(host);
        Assert.Empty(host.Children); // same assertion that failed in DeeperSubmissionStatusTests
        AppA.Notifications.ShowSticky(Key, "this test sticky");
        Assert.Single(host.Children);
        // Deliberately leave the service attached and its sticky key live.
    });

    [Fact]
    public Task C_SeesNoPreviousHostOrStickyKey() => OnUi(() =>
    {
        // A fresh service queues this notice; the previous service would send it to B's host
        // or suppress it because B left the same key live.
        AppA.Notifications.ShowSticky(Key, "next test sticky");
        var host = new StackPanel();
        AppA.Notifications.AttachHost(host);
        var toast = Assert.IsType<Border>(Assert.Single(host.Children));
        Assert.Null(toast.Tag); // still a real sticky notification with no expiry
    });
}
