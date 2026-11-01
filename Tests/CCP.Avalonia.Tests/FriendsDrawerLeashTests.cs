using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsDrawerLeashTests
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

    // WPF FriendsDrawer.cs:394-395; LeashDrawerSection.cs:37-40,49-71,91-103,113-130.
    [Fact]
    public Task PopupPinsTheExistingSelfCardAndTracksLeashOnlyWhileOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var old = LeashHead.Service;
        var seen = new List<string>();
        var first = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
        var next = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", cutSafety: () => { });
        var block = LeashHeadTests.BlockWithPunishment();
        const string aid = "abcdef0123456789";
        block["me"]!["assignment"] = new JObject
        {
            ["aid"] = aid, ["kind"] = "video", ["size"] = 1, ["day"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["status"] = "open", ["at"] = DateTimeOffset.UtcNow.ToString("o"),
            ["watch"] = new JObject { ["kind"] = "catalogue", ["id"] = "sample", ["title"] = "Sample" },
        };
        first.ApplyBlock(block);
        using var friends = FriendsDrawerTests.Service().Svc;
        await friends.RefreshAsync();
        var drawer = new FriendsDrawer(friends);
        var page = new FriendsDrawer(friends, asPage: true);
        try
        {
            LeashHead.Service = first;
            Assert.Empty(seen);
            Assert.Null(Find<LeashSelfCard>(drawer, "leash-self-card"));
            drawer.OnOpened();
            var card = Assert.IsType<LeashSelfCard>(Find<LeashSelfCard>(drawer, "leash-self-card"));
            Assert.Equal(0, Assert.IsType<StackPanel>(card.Parent).Children.IndexOf(card));
            Assert.Equal(new[] { aid }, seen);
            page.OnOpened();
            Assert.Null(Find<LeashSelfCard>(page, "leash-self-card"));

            drawer.OnClosed();
            first.ApplyBlock(new JObject { ["me"] = null });
            Assert.Same(card, Find<LeashSelfCard>(drawer, "leash-self-card"));
            drawer.OnOpened();
            Assert.Null(Find<LeashSelfCard>(drawer, "leash-self-card"));
            next.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            LeashHead.Service = next;
            drawer.Render();
            var replacement = Assert.IsType<LeashSelfCard>(Find<LeashSelfCard>(drawer, "leash-self-card"));
            var title = Find<TextBlock>(replacement, "leash-self-title");
            first.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            Assert.Same(title, Find<TextBlock>(replacement, "leash-self-title"));
            next.ApplyBlock(new JObject { ["me"] = null });
            Assert.Null(Find<LeashSelfCard>(drawer, "leash-self-card"));
            next.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            var detached = Find<LeashSelfCard>(drawer, "leash-self-card");
            drawer.Unsubscribe();
            next.ApplyBlock(new JObject { ["me"] = null });
            Assert.Same(detached, Find<LeashSelfCard>(drawer, "leash-self-card"));
        }
        finally { page.OnClosed(); drawer.OnClosed(); LeashHead.Service = old; }
    });

    // WPF LeashSelfCard.cs:104-120 and LeashSurfaces.cs:267-272.
    [Fact]
    public Task PopupCutClosesTheLockedWindowAndRunsLocalSafetyWhenOffline() => AvaloniaTestDispatcher.RunAsync(async () =>
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
        var drawer = new FriendsDrawer(friends);
        try
        {
            LeashHead.Service = leash;
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
            drawer.OnOpened();
            Assert.Null(Find<LeashSelfCard>(drawer, "leash-self-card"));
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

    // WPF FriendsRailChip.cs:159,421 opens the popup containing the pinned section.
    [Fact]
    public Task RailClickOpensTheLeashCard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var old = LeashHead.Service;
        var leash = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", cutSafety: () => { });
        leash.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        using var friends = FriendsDrawerTests.Service().Svc;
        await friends.RefreshAsync();
        var chip = new FriendsRailChip(friends) { Width = 200, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
        var window = new Window { Width = 700, Height = 700, Content = chip };
        try
        {
            LeashHead.Service = leash;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var at = chip.TranslatePoint(new Point(24, 24), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(chip.IsOpen);
            Assert.NotNull(Find<LeashSelfCard>(chip.Drawer, "leash-self-card"));
        }
        finally { window.Close(); LeashHead.Service = old; }
    });
}
