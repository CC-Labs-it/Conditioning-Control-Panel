using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiBookNavigationTests : IDisposable
{
    private readonly string? _bookmark = EmiState.Current.BookCard;
    private readonly int _opens = EmiState.Current.CodexOpens;
    public void Dispose()
    {
        EmiState.Current.BookCard = _bookmark;
        EmiState.Current.CodexOpens = _opens;
    }

    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => true; // Already completed tours remain replayable from the book.
        public void Latch(string name) { }
    }

    private static void Setup() => AmbientBeatLoopsTests.Setup();

    private static void Click(EmiBookWindow book, string name) =>
        book.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task RailNamesEveryCardAndDrawsDistinctPixelGlyphsWithEmbeddedFonts() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var book = new EmiBookWindow();
        try
        {
            var seen = new System.Collections.Generic.List<object>();
            for (int tab = 0; tab < 3; tab++)
            {
                Click(book, "Tab" + tab);
                var cards = EmiBookCards.All.Where(c => c.Tab == tab).ToArray();
                var rail = book.FindControl<StackPanel>("Rail")!;
                Assert.Equal(cards.Length, rail.Children.Count);
                for (int i = 0; i < cards.Length; i++)
                {
                    var chip = Assert.IsType<Button>(rail.Children[i]);
                    Assert.Equal(cards[i].Title, ToolTip.GetTip(chip));
                    var image = Assert.IsType<Image>(chip.Content);
                    Assert.NotNull(image.Source);
                    Assert.Equal(32, image.Width);
                    Assert.Equal(32, image.Height);
                    Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(image));
                    seen.Add(image.Source!);
                }
                var last = (Button)rail.Children[^1];
                last.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(cards[^1].Id, book.Painter!.Id);
                Assert.Equal(1, ((Image)((Button)rail.Children[^1]).Content!).Opacity);
            }
            Assert.Equal(EmiBookCards.All.Count, seen.Distinct().Count());
            Assert.Contains("avares://", book.FindControl<TextBlock>("CardTitle")!.FontFamily.ToString());
            Assert.Contains("avares://", book.FindControl<TextBlock>("CardGist")!.FontFamily.ToString());
            foreach (var (name, expected) in new[] { ("CardTitle", "Press Start 2P"), ("CardGist", "Noto Sans Mono") })
            {
                var family = book.FindControl<TextBlock>(name)!.FontFamily;
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var face));
                Assert.Equal(expected, face!.FamilyName);
            }
        }
        finally { book.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public Task TourGateAndClickRespectShellSessionAndActiveTour(bool shellPresent, bool session, bool idle) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var oldActive = CoreTutorial.IsActiveProvider;
        var oldStart = CoreTutorial.StartAction;
        var settingsBefore = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var shell = new MainShellWindow();
        var book = new EmiBookWindow();
        try
        {
            TutorialHeadHooks.ShellProvider = () => shellPresent ? shell : null;
            CoreSession.IsSessionRunningProvider = () => session;
            CoreTutorial.IsActiveProvider = () => !idle;
            string? started = null;
            CoreTutorial.StartAction = name => started = name;
            var card = EmiBookCards.All.First(c => c.Tour != null);
            book.OpenBook(card.Id);
            bool ready = shellPresent && !session && idle;
            Assert.Equal(ready, book.FindControl<Button>("BtnGo")!.IsEnabled);
            Click(book, "BtnGo"); // Even a queued click must recheck the gate.
            Assert.Equal(ready ? card.Tour : null, started);
            Assert.Equal(!ready, book.ClockRunning || book.IsVisible);
        }
        finally
        {
            book.Close(); shell.Close();
            TutorialHeadHooks.ShellProvider = oldShell;
            CoreSession.IsSessionRunningProvider = oldSession;
            CoreTutorial.IsActiveProvider = oldActive;
            CoreTutorial.StartAction = oldStart;
            CoreSettings.ServiceProvider = settingsBefore;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WalkStartsARealTourClosesTheOwnersBookAndPanicAbandonsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var settingsBefore = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var desk = new EmiDeskWindow();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            TutorialService.CompletionStore = new Store();
            TutorialHead.Seed(); TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            CoreSession.IsSessionRunningProvider = () => false;
            var card = EmiBookCards.All.First(c => c.Tour != null);
            desk.OpenBook(card.Id);
            var book = desk.Book!;
            Click(book, "BtnGo");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(desk.Book);
            Assert.False(book.IsVisible);
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal(card.Tour, CoreTutorial.CurrentTourName);
            PanicSurfaces.All.First(s => s.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
        }
        finally
        {
            TutorialHead.ResetForTests(); TutorialService.CompletionStore = null!;
            TutorialHeadHooks.ShellProvider = oldShell; CoreSession.IsSessionRunningProvider = oldSession;
            desk.Close(); shell.Close(); CoreSettings.ServiceProvider = settingsBefore;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplayClosesTheBookRunsTheExistingShowAndPanicStopsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var settingsBefore = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        var desk = new EmiDeskWindow();
        bool clickThrough = FirstShowService.RequireClickThrough;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.MasterVolume = 0;
            TutorialHeadHooks.ShellProvider = () => shell;
            FirstShowService.RequireClickThrough = false;
            desk.OpenBook();
            var book = desk.Book!;
            var replay = book.FindControl<Button>("BtnFirstShow");
            Assert.NotNull(replay);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("first_show_replay"), Assert.IsType<TextBlock>(replay!.Content).Text);
            Click(book, "BtnFirstShow");
            Assert.Null(desk.Book);
            Assert.False(book.IsVisible);
            Assert.True(FirstShowService.IsActive);
            var stage = FirstShowService.Window!;
            Assert.True(stage.IsVisible);
            Assert.True(EmiDeskService.Instance.Window!.PresentationActive);
            PanicSurfaces.All.First(s => s.Id == "first-show").Stop(shell);
            Assert.False(FirstShowService.IsActive);
            Assert.False(stage.IsVisible);
            Assert.False(EmiDeskService.Instance.Window!.PresentationActive);

            // Refused show (native Wayland; headless cannot click-through): the book comes back.
            FirstShowService.RequireClickThrough = true;
            desk.OpenBook();
            Click(desk.Book!, "BtnFirstShow");
            Assert.False(FirstShowService.IsActive);
            Assert.NotNull(desk.Book);
            Assert.True(desk.Book!.IsVisible);
        }
        finally
        {
            FirstShowService.Stop(); FirstShowService.RequireClickThrough = clickThrough;
            EmiDeskService.Instance.Window?.ShutDown();
            desk.Close(); shell.Close(); TutorialHeadHooks.ShellProvider = oldShell;
            CoreSettings.ServiceProvider = settingsBefore;
        }
        return Task.CompletedTask;
    });
}
