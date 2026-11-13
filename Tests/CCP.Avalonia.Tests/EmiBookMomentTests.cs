using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiBookMomentTests
{
    private static string? Field(object? value, string name) =>
        value?.GetType().GetProperty(name)?.GetValue(value)?.ToString();

    [Fact]
    public Task BookDetoursFireOnlyAfterSuccess() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldMoment = EmiTargets.Moment;
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var st = EmiState.Current;
        var bookmark = st.BookCard;
        var score = new Dictionary<string, double>(st.OpenScore);
        var usage = new Dictionary<string, long>(st.UsageAt);
        var cards = (EmiBookCard[])AvaloniaEmiBookCards.All;
        var original = cards[0];
        var moments = new List<(string Name, object? Context)>();
        var shell = new MainShellWindow();
        EmiBookWindow? book = null;
        try
        {
            shell.Show();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            CoreSession.IsSessionRunningProvider = () => false;
            EmiTargets.Moment = (name, context) => moments.Add((name, context));

            book = new EmiBookWindow();
            book.OpenBook("the-panic-key");
            book.FindControl<Button>("BtnGo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { "ringPick", "effectFired" }, moments.ConvertAll(m => m.Name));
            Assert.Equal("bookGo", Field(moments[1].Context, "channel"));
            Assert.Equal("settings", Field(moments[1].Context, "target"));
            book.Close();
            moments.Clear();

            cards[0] = original with { Target = "missing", Tour = null };
            book = new EmiBookWindow();
            book.OpenBook("the-ccp");
            book.FindControl<Button>("BtnGo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(moments);
            book.Close();

            cards[0] = original with { Target = "justdrop", Tour = null };
            ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(false);
            book = new EmiBookWindow();
            book.OpenBook("the-ccp");
            book.FindControl<Button>("BtnGo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(moments);
            book.Close();

            cards[0] = original with { Tour = "NoSuchTour" };
            book = new EmiBookWindow();
            book.OpenBook("the-ccp");
            book.FindControl<Button>("BtnGo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(moments);
            book.Close();

            cards[0] = original;
            CoreSession.IsSessionRunningProvider = () => true;
            book = new EmiBookWindow();
            book.OpenBook("the-ccp");
            book.FindControl<Button>("BtnGo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(moments);
            book.Close();

            CoreSession.IsSessionRunningProvider = () => false;
            book = new EmiBookWindow();
            book.OpenBook("the-ccp");
            var tourBook = book;
            EmiTargets.Moment = (name, context) =>
            {
                Assert.True(CoreTutorial.IsActive);
                Assert.False(tourBook.ClockRunning);
                moments.Add((name, context));
            };
            book.FindControl<Button>("BtnGo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(moments);
            Assert.Equal("effectFired", moments[0].Name);
            Assert.Equal("bookTour", Field(moments[0].Context, "channel"));
            Assert.Equal("GettingStarted", Field(moments[0].Context, "tour"));
        }
        finally
        {
            cards[0] = original;
            EmiTargets.Moment = oldMoment;
            CoreSession.IsSessionRunningProvider = oldSession;
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = oldShell;
            book?.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            st.BookCard = bookmark;
            st.OpenScore.Clear(); foreach (var entry in score) st.OpenScore[entry.Key] = entry.Value;
            st.UsageAt.Clear(); foreach (var entry in usage) st.UsageAt[entry.Key] = entry.Value;
        }
        return Task.CompletedTask;
    });
}
