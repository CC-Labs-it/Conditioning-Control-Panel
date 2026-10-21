using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WhatMovedCardParityTests
{
    private static Task WithShell(Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.WhatMovedCardShown = 7;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var shell = new MainShellWindow();
        shell.Show();
        try { body(shell); }
        finally { shell.Close(); CoreSettings.ServiceProvider = previous; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("premium", "appsettings", "account")]
    [InlineData("lobby", "availablesubjects", null)]
    [InlineData("monitors", "appsettings", "monitors")]
    [InlineData("tabs", "studio", null)]
    [InlineData("games", "play", null)]
    public Task EveryShowMeButtonClosesAndLandsOnItsRealDestination(string id, string tab, string? section) => WithShell(shell =>
    {
        var card = shell.ShowWhatMovedCard(readMode: true);
        Assert.NotNull(card);
        Assert.True(card!.ReadMode);
        card.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var buttons = card.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Tag is WhatMovedRow).ToList();
        Assert.Equal(WhatMovedPlan.Rows.Count, buttons.Count);
        foreach (var button in buttons)
            Assert.Equal(((WhatMovedRow)button.Tag!).ShowMeName, AutomationProperties.GetName(button));
        var show = buttons.Single(b => ((WhatMovedRow)b.Tag!).Id == id);
        show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, show));
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsVisible);
        Assert.Equal(tab, shell.CurrentTab);
        Assert.Equal(7, CoreSettings.Current.WhatMovedCardShown); // Help replay never spends another offer
        if (section != null)
        {
            var page = shell.AppSettingsPage!;
            Assert.Equal(section, page.CheckedSectionKey);
            var target = page.FindControl<Control>("Section" + char.ToUpperInvariant(section[0]) + section.Substring(1))!;
            var scroll = page.FindControl<ScrollViewer>("SectionScroll")!;
            Assert.True(target.Bounds.Height > 0);
            Assert.True(scroll.Offset.Y > 0);
            var top = target.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
            Assert.InRange(top, -4, 12); // the section starts in the viewport, rather than only lighting a pill
        }
    });

    [Fact]
    public Task FreshInstallSpendsTheOfferSilently() => WithShell(shell =>
    {
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        CoreSettings.Current.WhatMovedCardShown = 0;
        shell.OfferWhatMovedIfNeeded(freshInstall: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, CoreSettings.Current.WhatMovedCardShown);
        Assert.Empty(shell.OwnedWindows.OfType<WhatMovedCard>());
    });

    [Theory]
    [InlineData(Key.Enter, true)]
    [InlineData(Key.Escape, false)]
    public Task PreviewKeysCloseWithoutNavigatingAndEscapeStaysUnhandled(Key key, bool handled) => WithShell(shell =>
    {
        var before = shell.CurrentTab;
        var card = shell.ShowWhatMovedCard(readMode: true)!;
        card.UpdateLayout();
        var show = card.GetVisualDescendants().OfType<Button>().First(b => b.Tag is WhatMovedRow);
        show.Focus();
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = show };
        show.RaiseEvent(e);
        Assert.False(card.IsVisible);
        Assert.Equal(handled, e.Handled);
        Assert.Equal(before, shell.CurrentTab);
        Assert.Equal(7, CoreSettings.Current.WhatMovedCardShown);
    });
}
