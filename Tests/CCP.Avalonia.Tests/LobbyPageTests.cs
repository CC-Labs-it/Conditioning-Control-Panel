using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LobbyPageTests
{
    private static T Named<T>(Control parent, string name) where T : Control => parent.FindControl<T>(name)!;

    [Fact]
    public void EmptyLobbyHasNoFabricatedRowsAndNarrowButtonsSwitchColumns() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        Assert.Empty(Named<ItemsControl>(page, "AvailableSubjectsList").Items);
        Assert.Equal(Loc.Get("lobby_signed_out"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
        page.SetNarrow(true);
        Assert.True(Named<StackPanel>(page, "LobbyTabStrip").IsVisible);
        Named<Button>(page, "TabFriends").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, page.SelectedColumn);
        Assert.False(Named<Border>(page, "LobbyOpenColumn").IsVisible);
        Assert.True(Named<Border>(page, "LobbyFriendsSection").IsVisible);
        Assert.Equal(3, Grid.GetColumnSpan(Named<Border>(page, "LobbyFriendsSection")));
        page.SetNarrow(false);
        foreach (var (name, index) in new[] { ("LobbyOpenColumn", 0), ("LobbyPlayingSection", 1), ("LobbyFriendsSection", 2) })
        {
            var column = Named<Border>(page, name);
            Assert.True(column.IsVisible);
            Assert.Equal(index, Grid.GetColumn(column));
            Assert.Equal(1, Grid.GetColumnSpan(column));
        }
        Assert.Equal(0.6, Named<Button>(page, "TabOpen").Opacity);
        Assert.Equal(1, Named<Button>(page, "TabFriends").Opacity);
    });

    [Fact]
    public void EveryLobbyPillHasATemplateAndDrawsAtANonZeroSize() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var window = new Window { Width = 900, Height = 700, Content = page };
        window.Show();
        try
        {
            page.SetNarrow(true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var names = new[] { "BtnHostChess", "BtnHostGoon", "BtnBecomeASubject", "TabOpen", "TabPlaying", "TabFriends", "BtnEmptyHostChess", "BtnEmptyHostGoon" };
            foreach (var name in names)
            {
                var pill = Named<Button>(page, name);
                Assert.True(pill.Template is not null, name + " has no Template");
                Assert.True(pill.Bounds.Width > 0 && pill.Bounds.Height > 0, name + " has zero size " + pill.Bounds);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SnapshotPaintsAllThreeListsCountsErrorsAndRemoteGate() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var open = new LobbyRow { Game = LobbyGame.Remote, Key = "r", HostName = "Remote", RemoteTier = "light" };
        var playing = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "A", OpponentName = "B", InitialMs = 300000 };
        var friend = open with { Key = "friend", Friend = true };
        page.PaintSnapshot(new LobbySnapshot(new[] { open }, new[] { playing }, new[] { friend }, true), LobbyGates.From(true, false, false), true);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyOpenCount").Text);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyPlayingCount").Text);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyFriendsCount").Text);
        Assert.Equal(Loc.GetF("lobby_open_count", 2), Named<TextBlock>(page, "TxtLobbyCount").Text);
        Assert.False(Named<Border>(page, "AvailableSubjectsEmptyPanel").IsVisible);
        Assert.True(Named<Border>(page, "AvailableSubjectsErrorPanel").IsVisible);
        Assert.True(Named<TextBlock>(page, "TxtBecomeASubjectSubtitle").IsVisible);
        var row = Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "LobbyPlayingList").Items.Single());
        Assert.Equal("A vs B", row.Title);
        Assert.False(row.HasButton);
        Assert.Contains("5+0", row.Line);
        Assert.True(Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "LobbyFriendsList").Items.Single()).Friend);
    });

    [Fact]
    public void ShowingTheTabAgainRedrawsSignInGateAndLanguage() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        page.ReadGates = () => LobbyGates.From(false, false, false);
        page.IsVisible = false;
        page.IsVisible = true;
        Assert.Equal(Loc.Get("lobby_signed_out"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
        page.IsVisible = false;
        page.ReadGates = () => LobbyGates.From(true, false, false);
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            page.IsVisible = true;
            Assert.Equal(Loc.Get("lobby_empty_open"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
            Assert.True(Named<TextBlock>(page, "TxtBecomeASubjectSubtitle").IsVisible);
            Assert.Equal(Loc.Get("lobby_list_open") + "  0", Named<Button>(page, "TabOpen").Content);
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); }
    });

    [Fact]
    public void SignedOutRemoteHostButtonOpensSignInInsteadOfRemote() => Run(() =>
    {
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(false, false, false);
            shell.ShowTab("availablesubjects");
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.OwnedWindows, w => w is LoginDialog);
            Assert.True(page.IsVisible);
        }
        finally
        {
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            shell.Close();
        }
    });

    [Fact]
    public void RemoteHostButtonChecksGateThenOpensConsentPageWithoutStartingSession() => Run(() =>
    {
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        var refusal = CoreEntitlement.ShowDeniedHandler;
        var denied = 0;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(true, false, false);
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreEntitlement.ShowDeniedHandler = _ => denied++;
            shell.ShowTab("availablesubjects");
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, denied);
            Assert.True(page.IsVisible);
            CoreEntitlement.IsFreeTodayProvider = key => key == "remote";
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Named<RemoteControlTabView>(shell, "RemoteControlTab").IsVisible);
            Assert.False(page.IsVisible);
            Assert.False(Named<CheckBox>(Named<RemoteControlTabView>(shell, "RemoteControlTab"), "ChkRemoteControlEnabled").IsChecked == true);
        }
        finally
        {
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            shell.Close();
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            CoreEntitlement.ShowDeniedHandler = refusal;
        }
    });

    [Fact]
    public void HostLookBindsFaceAndFrameOnGameArtAndClearsForBareRow() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var template = Assert.IsAssignableFrom<IDataTemplate>(page.Resources["LobbyRowTemplate"]);
        var gates = LobbyGates.From(true, true, true);
        var host = new LobbyRow { Game = LobbyGame.Chess, HostName = "ann",
            Look = new PlayerLook("profile.avatar.emi-pixel", "profile.frame.lace") };
        var worn = new LobbyCardView(host, gates);
        Assert.True(worn.HasLook);
        Assert.NotNull(worn.Art);
        Assert.IsType<ImageBrush>(worn.FaceBrush);
        Assert.NotNull(worn.FrameArt);
        Assert.Equal(22 / ShopCosmeticsRegistry.FrameWindowRatio, worn.FrameSize, 6);
        Assert.Equal(-(worn.FrameSize - 22) / 2, worn.FrameMargin.Left, 6);
        var card = template.Build(worn)!;
        card.DataContext = worn;
        var textPanel = card.GetVisualDescendants().OfType<StackPanel>().Single(c => Grid.GetColumn(c) == 1);
        Assert.Equal(VerticalAlignment.Center, textPanel.VerticalAlignment);
        var face = card.GetVisualDescendants().OfType<Grid>().Single(c => Equals(c.Tag, "lobby-face"));
        var frame = face.Children.OfType<Image>().Single();
        Assert.True(face.IsVisible);
        Assert.False(face.IsHitTestVisible);
        Assert.False(face.ClipToBounds);
        Assert.Equal(22d, face.Width);
        Assert.Equal(22d, face.Height);
        var circle = face.Children.OfType<Ellipse>().Single();
        Assert.Equal(Color.Parse("#1C1233"), Assert.IsAssignableFrom<ISolidColorBrush>(circle.Stroke).Color);
        Assert.Equal(1.5, circle.StrokeThickness);
        Assert.IsType<ImageBrush>(circle.Fill);
        Assert.Same(worn.FrameArt, frame.Source);
        Assert.Equal(worn.FrameSize, frame.Width);
        Assert.Equal(worn.FrameMargin, frame.Margin);
        Assert.Equal(new Thickness(0, 0, -7, -7), face.Margin);

        var bare = new LobbyCardView(host with { Look = PlayerLook.None }, gates);
        card.DataContext = bare;
        Assert.False(face.IsVisible);
        Assert.Null(frame.Source);
        Assert.Null(face.Children.OfType<Ellipse>().Single().Fill);
        Assert.False(bare.HasLook);
        Assert.Null(bare.FaceBrush);
        Assert.Null(bare.FrameArt);

        var missing = new LobbyCardView(host with { Look = new PlayerLook("missing", "missing") }, gates);
        Assert.IsType<LinearGradientBrush>(missing.FaceBrush);
        Assert.Null(missing.FrameArt);
        Assert.Null(ModArt.ShopFrame("profile.avatar.emi-pixel"));
        Assert.Null(ModArt.AvatarPreset("profile.frame.lace"));
    });

    [Fact]
    public void GameArtCacheSharesHitsAndMissesPerGame()
    {
        var calls = new System.Collections.Generic.Dictionary<LobbyGame, int>();
        var image = new DrawingImage();
        var cache = new LobbyArtCache(game =>
        {
            calls[game] = (calls.TryGetValue(game, out var count) ? count : 0) + 1;
            return game == LobbyGame.Chess ? image : null;
        });
        foreach (var game in Enum.GetValues<LobbyGame>())
        {
            var first = cache.Get(game);
            var second = cache.Get(game);
            if (game == LobbyGame.Chess) Assert.Same(image, first);
            else Assert.Null(first);
            Assert.Same(first, second);
            Assert.Equal(1, calls[game]);
        }
    }

    [Fact]
    public void GameArtIsSharedAcrossRepeatedBindingsAndReplacementRows() => Run(() =>
    {
        var gates = LobbyGates.From(true, true, true);
        foreach (var game in Enum.GetValues<LobbyGame>())
        {
            var row = new LobbyRow { Game = game, HostName = "ann" };
            var first = new LobbyCardView(row, gates);
            var art = first.Art;
            Assert.NotNull(art);
            Assert.Same(art, first.Art);
            Assert.Same(art, new LobbyCardView(row with { HostName = "bo" }, gates).Art);
        }
    });

    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        body();
        Dispatcher.UIThread.RunJobs();
    });
}
