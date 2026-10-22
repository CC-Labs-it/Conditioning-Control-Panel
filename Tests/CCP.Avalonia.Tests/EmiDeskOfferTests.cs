using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The offer surface the EMI Desk "Offers" switch governs (WPF EmiDeskWindow.Bubble.cs:577-935,
/// EmiOffers.cs): a pat draws an ask through the probes the desk service installs, the chips wait,
/// a chip runs the effect, Esc/panic drop it, and a line arriving meanwhile parks.
/// </summary>
public sealed class EmiDeskOfferTests
{
    private const string Lines = """
        {"version":1,"moments":{
          "petted":{"priority":3,"pools":["pat"],"askOdds":1,"scripted":true},
          "resized":{"priority":3,"pools":["talk"]},
          "panicPressed":{"hold":true,"holdMs":6000,"pools":["quiet"]}},
         "pools":{"pat":[{"id":"pat","t":"pat line","spice":0}],
                  "talk":[{"id":"talk","t":"parked line","spice":0}],
                  "quiet":[{"id":"quiet","t":"","spice":0}]},
         "asks":[{"id":"ask.pet","moment":"petted","q":"smaller?","face":"o_o","chips":["yes","i'm good"],
                  "yes":{"t":"shrinking.","face":"^_^"},"no":{"t":"staying.","face":"^_^"},
                  "effect":"shrink","spice":0}]}
        """;

    private static Task InDesk(Func<EmiDeskService, EmiDeskWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        var desk = EmiDeskService.Instance;
        var previousEngine = desk.LineEngine;
        // The shrink effect saves her placement into the process-wide EmiState; put it back so a
        // later summon test does not restore this test's corner.
        var st = EmiState.Current;
        var (left, top, monitor) = (st.WinLeftPx, st.WinTopPx, st.Monitor);
        CoreSettings.ServiceProvider = () => settings;
        desk.LineEngine = EmiLineEngine.FromJson(Lines);
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            CoreSettings.Current.EmiDeskOffers = true;
            CoreSettings.Current.EmiDeskWidth = EmiDeskWindow.DefaultBodyWidth;
            await desk.Summon();
            desk.Window!.ApplyBodyWidth(EmiDeskWindow.DefaultBodyWidth);
            // The summon FX lands on real timers; end it the way its last step does (no offer mid-transit).
            typeof(EmiDeskWindow).GetField("_transiting", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(desk.Window, false);
            typeof(EmiDeskWindow).GetProperty(nameof(EmiDeskWindow.InputLocked))!.SetValue(desk.Window, false);
            await body(desk, desk.Window);
        }
        finally
        {
            desk.PanicSilence();
            desk.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            desk.LineEngine = previousEngine;
            (st.WinLeftPx, st.WinTopPx, st.Monitor) = (left, top, monitor);
            CoreSettings.Current.EmiDeskWidth = EmiDeskWindow.DefaultBodyWidth;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    });

    private static void ShowQuestion(EmiDeskService desk, EmiDeskWindow window)
    {
        desk.Fire("petted");
        Assert.True(window.AskLive);
        Assert.Equal(".", window.BubbleText);
        window.AdvanceAsk();
        window.AdvanceAsk();
        Assert.Equal("...", window.BubbleText);
        window.AdvanceAsk();
        Assert.Equal("smaller?", window.BubbleText);
        Assert.Equal("o_o", window.Face);
        Assert.Equal(2, window.AskChips.Count);
    }

    [Fact]
    public Task YesChipRunsTheShrinkEffectAndSpeaksTheReply() => InDesk((desk, window) =>
    {
        ShowQuestion(desk, window);
        window.AskChips[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(window.AskLive);
        Assert.Empty(window.AskChips);
        Assert.Equal(EmiDeskWindow.MinBodyWidth, window.BodyWidth);
        Assert.Equal(EmiDeskWindow.MinBodyWidth, CoreSettings.Current.EmiDeskWidth);
        Assert.Equal(".", window.BubbleText); // the reply's cadence has started
        return Task.CompletedTask;
    });

    [Fact]
    public Task EscapeDropsTheOfferAndReleasesTheParkedLine() => InDesk((desk, window) =>
    {
        ShowQuestion(desk, window);
        desk.Fire("resized"); // arrives while she waits: parks, does not talk over the question
        Assert.Equal("smaller?", window.BubbleText);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

        Assert.False(window.AskLive);
        Assert.Empty(window.AskChips);
        Assert.Equal("-_-", window.Face);
        Assert.Equal(EmiDeskWindow.DefaultBodyWidth, window.BodyWidth);
        window.AdvanceAsk();
        Assert.Equal("...", window.BubbleText);
        window.AdvanceAsk();
        Assert.Null(window.BubbleText);
        window.AdvanceAsk(); // the parked line's beat
        Assert.Equal(".", window.BubbleText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicKillsAWaitingOffer() => InDesk((desk, window) =>
    {
        ShowQuestion(desk, window);
        desk.PanicSilence();
        Assert.False(window.AskLive);
        Assert.Empty(window.AskChips);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OffersSwitchOffMeansNoQuestion() => InDesk((desk, window) =>
    {
        CoreSettings.Current.EmiDeskOffers = false;
        desk.Fire("petted");
        Assert.False(window.AskLive);
        Assert.Equal(".", window.BubbleText); // she says the pat line instead
        return Task.CompletedTask;
    });
}
