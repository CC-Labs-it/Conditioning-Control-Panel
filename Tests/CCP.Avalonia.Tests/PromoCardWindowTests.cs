using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Announcements;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PromoCardWindow (a59e74ee7e, 759f40d621): the parts a promo leaves out are hidden, every way out
/// but panic answers the announcement, panic answers nothing.</summary>
public sealed class PromoCardWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    internal static PromoCard Promo(bool full) => new(PromoCardRules.Layout, full ? "BLACK FRIDAY" : null, full ? "Half off everything" : null,
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
    public Task StickersInHandAreDiecutAndTheArtLayersAreDrawn() => Run(() =>
    {
        IImage pic = new DrawingImage(new GeometryDrawing { Brush = Brushes.HotPink, Geometry = new RectangleGeometry(new Rect(0, 0, 64, 40)) });
        var card = new PromoCardWindow("a4", "For you", "Message", Promo(false), () => Now, new IImage?[] { pic, null, pic });
        Assert.Equal(2, Named<Canvas>(card, "HeroBox").Children.Count); // the one that failed does not draw
        Assert.True(Named<StackPanel>(card, "HeroRow").IsVisible);
        Assert.NotNull(Named<Image>(card, "SpiralImage").Source);
        Assert.Equal(3, Named<Canvas>(card, "SparkleLayer").Children.Count);
        var none = new PromoCardWindow("a4", "For you", "Message", Promo(false), () => Now, new IImage?[] { null });
        Assert.False(Named<StackPanel>(none, "HeroRow").IsVisible);
        return Task.CompletedTask;
    });

    /// <summary>WPF PromoCardWindow.Art.cs ApplyAmbient: the spiral turns on the beat, and with Gentle visuals on
    /// the loop stops (no ticks) and the art holds where it is.</summary>
    [Fact]
    public Task TheSpiralTurnsOnTheBeatAndStopsForGentleVisuals() => Run(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode, s.GentleVisuals);
        var time = new SteppedTime();
        BeatLoop.Time = time;
        try
        {
            (s.MotionLevel, s.PerformanceMode, s.GentleVisuals) = (MotionLevel.Full, false, false);
            var card = new PromoCardWindow("a5", "For you", "Message", Promo(false), () => Now);
            card.Show();
            Dispatcher.UIThread.RunJobs();
            var spiral = Named<Image>(card, "SpiralImage");
            var spin = Assert.IsType<RotateTransform>(spiral.RenderTransform);
            bool Ticking() => BeatLoop.Running.Any(l => l.Owner == spiral);
            void Beat() { time.Advance(TimeSpan.FromSeconds(14)); BeatLoop.StepAll(); }
            Assert.True(Ticking());
            Beat();
            Assert.Equal(36, spin.Angle, 3); // one turn per 140 s
            s.GentleVisuals = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(Ticking());
            Beat();
            Assert.Equal(36, spin.Angle, 3);
            s.GentleVisuals = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Beat();
            Assert.Equal(72, spin.Angle, 3); // resumes where it held
            PromoCardWindow.CloseForPanic();
            Assert.False(Ticking());
        }
        finally
        {
            BeatLoop.Time = TimeProvider.System;
            (s.MotionLevel, s.PerformanceMode, s.GentleVisuals) = old;
        }
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
