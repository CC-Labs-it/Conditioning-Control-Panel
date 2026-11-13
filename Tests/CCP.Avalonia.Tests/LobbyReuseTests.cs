using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Tab-switch perf: a re-show or repaint with unchanged lobby data must rebuild nothing,
/// and a changed row must replace only its own card slot.</summary>
public sealed class LobbyReuseTests
{
    [Fact]
    public void UnchangedRefreshKeepsCardsAndContainersAndAChangedRowUpdatesOnlyItsSlot() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var language = LocalizationManager.Instance.CurrentLanguage;
        Window? window = null;
        try
        {
            LocalizationManager.Instance.SetLanguage("en");
            var gates = LobbyGates.From(true, false, false);
            // A poll that never answers: the test alone decides what is painted.
            var page = new AvailableSubjectsTabView { ReadGates = () => gates, CreateLobby = () => new LobbyPoll(new SilentTimer(),
                () => false, Array.Empty<Friend>, null!, null!, null!) };
            var row = new LobbyRow { Key = "one", HostName = "One", Game = LobbyGame.Chess };
            var snapshot = new LobbySnapshot(new[] { row, row with { Key = "two", HostName = "Two" } },
                Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true);
            page.PaintSnapshot(snapshot, gates);
            window = new Window { Width = 1200, Height = 800, Content = page };
            window.Show();
            window.UpdateLayout();
            var lists = new[] { "AvailableSubjectsList", "LobbyPlayingList", "LobbyFriendsList" }
                .Select(n => page.FindControl<ItemsControl>(n)!).ToArray();
            var sources = lists.Select(l => l.ItemsSource).ToArray();
            var cards = lists[0].Items.Cast<object>().ToArray();
            var containers = new[] { lists[0].ContainerFromIndex(0), lists[0].ContainerFromIndex(1) };
            Assert.All(containers, Assert.NotNull);
            var changes = 0;
            foreach (var list in lists)
                ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) => changes++;

            // Every path that repaints on a tab switch: the visibility hook, the shell's entitlement
            // refresh (Repaint) and a poll round that brings the same rows in fresh instances.
            page.IsVisible = false;
            page.IsVisible = true;
            page.Repaint();
            page.PaintSnapshot(snapshot with { Open = snapshot.Open.Select(r => r with { }).ToList() }, gates);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            for (var i = 0; i < lists.Length; i++) Assert.Same(sources[i], lists[i].ItemsSource);
            Assert.Equal(0, changes);
            Assert.Same(cards[0], lists[0].Items[0]);
            Assert.Same(cards[1], lists[0].Items[1]);
            Assert.Same(containers[0], lists[0].ContainerFromIndex(0));
            Assert.Same(containers[1], lists[0].ContainerFromIndex(1));

            page.PaintSnapshot(snapshot with { Open = new[] { row, row with { Key = "two", HostName = "Updated" } } }, gates);
            window.UpdateLayout();
            Assert.Equal(1, changes);
            Assert.Same(sources[0], lists[0].ItemsSource);
            Assert.Same(cards[0], lists[0].Items[0]);
            Assert.Same(containers[0], lists[0].ContainerFromIndex(0));
            Assert.Equal("Updated", Assert.IsType<LobbyCardView>(lists[0].Items[1]).Title);

            // Gates still reach the cards: signing out redraws every slot as locked.
            gates = LobbyGates.From(false, false, false);
            page.PaintSnapshot(snapshot, gates);
            Assert.All(lists[0].Items, c => Assert.True(Assert.IsType<LobbyCardView>(c).Locked));
        }
        finally
        {
            window?.Close();
            LocalizationManager.Instance.SetLanguage(language);
        }
    });

    private sealed class SilentTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void OnUiThread(Action action) { }
    }
}
