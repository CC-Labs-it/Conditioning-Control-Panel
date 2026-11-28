using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Announcements;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PromoCardWindow (a59e74ee7e, 759f40d621): the parts a promo leaves out are hidden, every way out
/// but panic answers the announcement, panic answers nothing.</summary>
public sealed class PromoCardWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static PromoCard Promo(bool full) => new(PromoCardRules.Layout, full ? "BLACK FRIDAY" : null, full ? "Half off everything" : null,
        full ? "BF50" : null, new[] { new PromoRow("Super", "50% off"), new PromoRow("Plus", null) }, Array.Empty<Uri>(),
        full ? Now.AddHours(26) : null, full ? new PromoCta("Get it", new Uri("https://www.patreon.com/cclabs")) : null);

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.Current.DismissedAnnouncementId;
        CoreSettings.Current.DismissedAnnouncementId = null;
        try { await body(); }
        finally { CoreSettings.Current.DismissedAnnouncementId = old; PromoCardWindow.CloseForPanic(); }
    });

    private static T Named<T>(Window w, string name) where T : Control => w.FindControl<T>(name)!;

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
    }

    [Fact]
    public Task AFullPromoDrawsEveryPartAndABarePromoHidesThem() => Run(() =>
    {
        var full = new PromoCardWindow("a1", "For you", "Message", Promo(true), () => Now);
        Assert.Equal(5, Named<StackPanel>(full, "MenuStack").Children.Count); // code, two rows, button, Not now
        Assert.True(Named<StackPanel>(full, "CountdownPanel").IsVisible);
        Assert.True(Named<Border>(full, "TagStamp").IsVisible);
        Assert.Equal("Half off everything", string.Concat(Named<TextBlock>(full, "HookText").Inlines!.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        var bare = new PromoCardWindow("a1", "For you", "", Promo(false), () => Now);
        Assert.Equal(3, Named<StackPanel>(bare, "MenuStack").Children.Count); // two rows, Not now
        Assert.False(Named<StackPanel>(bare, "HeroRow").IsVisible);
        Assert.False(Named<TextBlock>(bare, "HookText").IsVisible);
        Assert.False(Named<Border>(bare, "TagStamp").IsVisible);
        Assert.False(Named<ScrollViewer>(bare, "MessageScroll").IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NotNowAnswersTheAnnouncementAndPanicAnswersNothing() => Run(async () =>
    {
        PromoCardWindow.ShowFor("a1", "For you", "Message", Promo(true));
        Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(PromoCardWindow.OpenForTests);
        PromoCardWindow.CloseForPanic();
        Assert.Empty(PromoCardWindow.OpenForTests);
        Assert.True(string.IsNullOrEmpty(CoreSettings.Current.DismissedAnnouncementId));
        PromoCardWindow.ShowFor("a2", "For you", "Message", Promo(false));
        Dispatcher.UIThread.RunJobs();
        card = Assert.Single(PromoCardWindow.OpenForTests);
        Named<StackPanel>(card, "MenuStack").Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("a2", CoreSettings.Current.DismissedAnnouncementId);
        await Until(() => PromoCardWindow.OpenForTests.Count == 0);
        Assert.Empty(PromoCardWindow.OpenForTests);
    });

    [Fact]
    public Task AnOfferThatEndsClosesTheCardAndCountsAsSeen() => Run(async () =>
    {
        var now = Now;
        var card = new PromoCardWindow("a3", "For you", "Message", Promo(true), () => now);
        card.Show();
        Dispatcher.UIThread.RunJobs();
        now = Now.AddHours(27);
        await Until(() => PromoCardWindow.OpenForTests.Count == 0);
        Assert.Empty(PromoCardWindow.OpenForTests);
        Assert.Equal("a3", CoreSettings.Current.DismissedAnnouncementId);
    });
}
