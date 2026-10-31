using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LobbyReuseTests
{
    [Fact]
    public Task RepeatShowKeepsSourcesCardsAndContainersAndChangedRowOnlyUpdatesItsSlot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var gates = LobbyGates.From(true, false, false);
        var page = new AvailableSubjectsTabView { ReadGates = () => gates };
        var row = new LobbyRow { Key = "one", HostName = "One", Game = LobbyGame.Chess };
        var snapshot = new LobbySnapshot(new[] { row, row with { Key = "two", HostName = "Two" } },
            Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true);
        page.PaintSnapshot(snapshot, gates);
        var window = new Window { Width = 1200, Height = 800, Content = page };
        window.Show();
        try
        {
            window.UpdateLayout();
            var lists = new[] { "AvailableSubjectsList", "LobbyPlayingList", "LobbyFriendsList" }
                .Select(n => page.FindControl<ItemsControl>(n)!).ToArray();
            var sources = lists.Select(l => l.ItemsSource).ToArray();
            var cards = lists[0].Items.Cast<object>().ToArray();
            var container = lists[0].ContainerFromIndex(0);
            Assert.NotNull(container);
            var changes = 0;
            foreach (var list in lists)
                ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) => changes++;
            page.IsVisible = false;
            page.IsVisible = true;
            window.UpdateLayout();
            for (var i = 0; i < lists.Length; i++) Assert.Same(sources[i], lists[i].ItemsSource);
            Assert.Equal(0, changes);
            Assert.Same(cards[0], lists[0].Items[0]);
            Assert.Same(container, lists[0].ContainerFromIndex(0));

            page.PaintSnapshot(snapshot with { Open = new[] { row, row with { Key = "two", HostName = "Updated" } } }, gates);
            Assert.Equal(1, changes);
            Assert.Same(cards[0], lists[0].Items[0]);
            Assert.Same(sources[0], lists[0].ItemsSource);
            Assert.Equal("Updated", Assert.IsType<LobbyCardView>(lists[0].Items[1]).Title);
            Assert.Same(container, lists[0].ContainerFromIndex(0));

            page.IsVisible = false;
            gates = LobbyGates.From(false, false, false);
            page.IsVisible = true;
            Assert.True(Assert.IsType<LobbyCardView>(lists[0].Items[0]).Locked);
            Assert.Equal(Loc.Get("lobby_signed_out"), page.FindControl<TextBlock>("TxtLobbyEmpty")!.Text);
            page.IsVisible = false;
            LocalizationManager.Instance.SetLanguage("de");
            page.IsVisible = true;
            Assert.Equal(Loc.Get("lobby_btn_join"), Assert.IsType<LobbyCardView>(lists[0].Items[0]).ButtonText);
            Assert.Equal(Loc.Get("lobby_list_open") + "  2", page.FindControl<Button>("TabOpen")!.Content);
        }
        finally { window.Close(); LocalizationManager.Instance.SetLanguage("en"); }
        return Task.CompletedTask;
    });
}
