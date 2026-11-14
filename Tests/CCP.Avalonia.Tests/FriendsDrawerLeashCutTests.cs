using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Restack b148 (#4001's cut slice): the rail drawer's popup is not the shell window, so its
/// own-card Cut ends the task, closes the punish window and says so before the service cut, as the shell does.</summary>
public sealed class FriendsDrawerLeashCutTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static T? Find<T>(Control root, string tag) where T : Control => root.GetLogicalDescendants()
        .OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    private sealed class OfflineWire : ILeashApi
    {
        private readonly List<string> _steps;
        public OfflineWire(List<string> steps) => _steps = steps;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            _steps.Add(op);
            return Task.FromResult<JObject?>(null);
        }
    }

    private sealed class ClosingRunner : ILeashTaskRunner
    {
        private readonly Action _close;
        public ClosingRunner(Action close) => _close = close;
        public bool IsRunning => true;
        public string? RunningPid => "p1";
        public bool Start(Punishment punishment) => false;
        public void Cancel() => _close();
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
    }

    // WPF LeashSelfCard.cs:104-120 and LeashSurfaces.cs:267-272.
    [Fact]
    public Task DrawerCutClosesTheLockedWindowAndRunsLocalSafetyWhenOffline() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var old = LeashHead.Service;
        var oldWeb = LeashPunishWindow.CreateWeb;
        var oldRunner = LeashTaskHost.Runner;
        var notifications = ConditioningControlPanel.Avalonia.App.Notifications;
        var hostField = typeof(ConditioningControlPanel.Avalonia.Helpers.NotificationService)
            .GetField("_host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var oldHost = hostField.GetValue(notifications);
        var noticeHost = new StackPanel();
        var steps = new List<string>();
        var leash = new LeashService(new OfflineWire(steps), () => "u_me",
            cutSafety: () => steps.Add(LeashPunishWindow.Current == null ? "safety" : "window-open"));
        leash.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        using var friends = FriendsDrawerTests.Service().Svc;
        await friends.RefreshAsync();
        LeashHead.Service = leash;
        var drawer = new FriendsDrawer(friends);
        try
        {
            LeashPunishWindow.CreateWeb = false;
            hostField.SetValue(notifications, noticeHost);
            drawer.OnOpened();
            LeashTaskHost.Runner = new ClosingRunner(drawer.OnClosed);
            Assert.True(LeashPunishWindow.OpenUrl("https://example.invalid/", "Vex", locked: true));
            Assert.NotNull(LeashPunishWindow.Current);
            var cut = Assert.IsType<Button>(Find<Button>(drawer, "leash-cut"));
            cut.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await FriendsDrawerTests.Until(() => steps.Contains("cut"));
            Assert.Null(LeashPunishWindow.Current);
            Assert.Null(leash.Snapshot.Me);
            Assert.Equal(new[] { "safety", "cut" }, steps);
            Assert.Contains(noticeHost.GetLogicalDescendants().OfType<TextBlock>(),
                t => t.Text == ConditioningControlPanel.Localization.Loc.Get("leash_cut_done"));
        }
        finally
        {
            LeashPunishWindow.CloseNow();
            LeashPunishWindow.CreateWeb = oldWeb;
            LeashTaskHost.Runner = oldRunner;
            hostField.SetValue(notifications, oldHost);
            drawer.OnClosed();
            LeashHead.Service = old;
        }
    });
}
