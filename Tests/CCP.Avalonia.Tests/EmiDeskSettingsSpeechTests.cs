using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskSettingsSpeechTests
{
    private const string Lines = """
        {"version":1,"moments":{
          "firstContact":{"priority":3,"pools":["greeting"],"limit":{"per":"ever","max":1}},
          "desktopFirstBoot":{"priority":3,"pools":["boot"]},
          "resized":{"priority":3,"pools":["spicy"]},
          "panicPressed":{"hold":true,"holdMs":6000,"pools":["quiet"]}},
         "pools":{"greeting":[{"id":"hello","t":"hello","spice":0}],
                  "boot":[{"id":"boot","t":"desktop hello","spice":0}],
                  "spicy":[{"id":"spicy","t":"high spice","spice":2,"face":"^_~"}],
                  "quiet":[{"id":"quiet","t":"","spice":0}]}}
        """;

    private static Task InDesk(Func<EmiDeskService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        var desk = EmiDeskService.Instance;
        var previousEngine = desk.LineEngine;
        CoreSettings.ServiceProvider = () => settings;
        desk.LineEngine = EmiLineEngine.FromJson(Lines);
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            await desk.Summon();
            await body(desk);
        }
        finally
        {
            desk.PanicSilence();
            desk.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            desk.LineEngine = previousEngine;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public Task SpicePickerChangesWhichLinesReachTheWidget() => InDesk(desk =>
    {
        var section = new EmiDeskSettingsSection();
        var picker = section.FindControl<ComboBox>("CmbSpice")!;
        picker.SelectedIndex = 0;
        desk.Fire("resized");
        Assert.Null(desk.Window!.BubbleText);
        picker.SelectedIndex = 2;
        Assert.Equal(2, CoreSettings.Current.EmiDeskSpice);
        desk.Fire("resized");
        Assert.Equal(".", desk.Window.BubbleText);
        desk.Window.AdvanceSpeech();
        desk.Window.AdvanceSpeech();
        desk.Window.AdvanceSpeech();
        Assert.Equal("high spice", desk.Window.BubbleText);
        Assert.Equal("^_~", desk.Window.Face);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SayTypesThenLandsAndCompletesAfterRest() => InDesk(desk =>
    {
        bool done = false;
        var window = desk.Window!;
        window.Say("test line", "^_~", () => done = true);
        Assert.False(done);
        Assert.Equal(".", window.BubbleText);
        Assert.Equal("0_0", window.Face);
        window.AdvanceSpeech();
        Assert.Equal("..", window.BubbleText);
        window.AdvanceSpeech();
        Assert.Equal("...", window.BubbleText);
        window.AdvanceSpeech();
        Assert.Equal("test line", window.BubbleText);
        Assert.Equal("^_~", window.Face);
        Assert.False(done);
        window.AdvanceSpeech();
        Assert.Null(window.BubbleText);
        Assert.Equal("0_0", window.Face);
        window.AdvanceSpeech();
        Assert.True(done);
        return Task.CompletedTask;
    });

    [Fact]
    public Task GreetingIsReachableOnSummonAndPanicCutsIt() => InDesk(desk =>
    {
        // InDesk summoned her: the summon's greeting timer is what greets, not a direct call.
        desk.GreetingDue();
        Assert.Equal(".", desk.Window!.BubbleText);
        Assert.True(desk.LineEngine.EverSpent("firstContact"));
        desk.GreetNow();
        desk.Window.AdvanceSpeech();
        desk.Window.AdvanceSpeech();
        desk.Window.AdvanceSpeech();
        Assert.Equal("desktop hello", desk.Window.BubbleText);

        PanicSurfaces.All.Single(x => x.Id == "emi-desk-speech").Stop(null);
        Assert.Null(desk.Window.BubbleText);
        desk.Window.AdvanceSpeech();
        desk.Fire("resized");
        Assert.Null(desk.Window.BubbleText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HoldShowsQuietFaceThenRevertsToRest() => InDesk(desk =>
    {
        var window = desk.Window!;
        desk.Fire("panicPressed");
        Assert.Equal("-_-", window.Face);
        Assert.Null(window.BubbleText);
        window.ExpireHold();
        Assert.Equal("0_0", window.Face);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CancelNeverCallsSpeechContinuation() => InDesk(desk =>
    {
        bool done = false;
        desk.Window!.Say("test", done: () => done = true);
        desk.Window.CancelChain();
        for (int i = 0; i < 6; i++) desk.Window.AdvanceSpeech();
        Assert.False(done);
        Assert.Null(desk.Window.BubbleText);
        return Task.CompletedTask;
    });
}
