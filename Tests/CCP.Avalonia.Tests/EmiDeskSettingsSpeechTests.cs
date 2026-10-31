using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
          "panicPressed":{"hold":true,"holdMs":6000,"pools":["quiet"]},
          "dismissed":{"hold":true,"holdMs":6000,"pools":["sulk"]},
          "wordless":{"priority":3,"pools":["face"]}},
         "pools":{"greeting":[{"id":"hello","t":"hello","spice":0}],
                  "boot":[{"id":"boot","t":"desktop hello","spice":0}],
                  "spicy":[{"id":"spicy","t":"high spice","spice":2,"face":"^_~"}],
                  "quiet":[{"id":"quiet","t":"","spice":0}],
                  "sulk":[{"id":"sulk","t":"","spice":0,"face":"T_T"}],
                  "face":[{"id":"face","t":"","spice":0,"face":"o_o"}]}}
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
        for (int i = 0; i < 3; i++) desk.Window.AdvanceSpeech();
        Assert.Equal("high spice", desk.Window.BubbleText); Assert.Equal("^_~", desk.Window.Face);
        return Task.CompletedTask;
    });
    [Fact]
    public Task SayTypesThenLandsAndCompletesAfterRest() => InDesk(desk =>
    {
        bool done = false;
        var window = desk.Window!;
        window.Say("test line", "^_~", () => done = true);
        Assert.False(done);
        var timer = (DispatcherTimer)typeof(EmiDeskWindow).GetField("_speechTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Equal(TimeSpan.FromMilliseconds(420), timer.Interval); Assert.Equal(".", window.BubbleText); Assert.Equal("0_0", window.Face);
        window.AdvanceSpeech();
        Assert.Equal("..", window.BubbleText);
        window.AdvanceSpeech();
        Assert.Equal("...", window.BubbleText); Assert.Equal(TimeSpan.FromMilliseconds(520), timer.Interval);
        window.AdvanceSpeech();
        Assert.Equal("test line", window.BubbleText); Assert.Equal(TimeSpan.FromMilliseconds(EmiChains.SayHoldMs("test line")), timer.Interval); Assert.Equal("^_~", window.Face); Assert.False(done);
        window.AdvanceSpeech();
        Assert.Null(window.BubbleText); Assert.Equal("0_0", window.Face);
        window.AdvanceSpeech();
        Assert.True(done);
        return Task.CompletedTask;
    });
    [Fact]
    public Task GreetingIsReachableOnSummonAndPanicCutsIt() => InDesk(desk =>
    {
        desk.GreetingDue();
        Assert.Equal(".", desk.Window!.BubbleText); Assert.True(desk.LineEngine.EverSpent("firstContact"));
        desk.GreetNow();
        for (int i = 0; i < 3; i++) desk.Window.AdvanceSpeech();
        Assert.Equal("desktop hello", desk.Window.BubbleText);
        PanicSurfaces.All.Single(x => x.Id == "emi-desk-feedback").Stop(null);
        Assert.True(desk.Window.HoldPending); Assert.Null(desk.Window.BubbleText);
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
    [Fact]
    public Task WordlessRowDrawsItsFaceWithoutAHold() => InDesk(desk =>
    {
        desk.Fire("wordless");
        Assert.Equal("o_o", desk.Window!.Face);
        Assert.Null(desk.Window.BubbleText);
        Assert.False(desk.Window.HoldPending);
        return Task.CompletedTask;
    });
    [Fact]
    public Task HoldPlaysOnDismissedAndDismissCancelsIt() => InDesk(desk =>
    {
        // WPF EmiDeskService.cs:765: a hold plays even on a locked-silent moment.
        var window = desk.Window!;
        desk.Fire("dismissed");
        Assert.Equal("T_T", window.Face);
        Assert.True(window.HoldPending);
        window.RunDismiss();
        Assert.False(window.HoldPending);
        return Task.CompletedTask;
    });
    [Fact]
    public Task PanicWhileAwaySilencesTheNextSummon() => InDesk(async desk =>
    {
        desk.Window!.ShutDown();
        Assert.False(desk.IsOut);
        desk.PanicSilence();
        await desk.Summon();
        desk.GreetingDue();
        desk.Fire("resized");
        Assert.Null(desk.Window!.BubbleText);
    });
    [Fact]
    public void ShippedCatalogueLoadsOutsideTheSourceTree()
    {
        var output = AppContext.BaseDirectory;
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        var target = System.IO.Path.Combine(temp, "Resources", "emi", "desk-lines.json");
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            System.IO.File.Copy(System.IO.Path.Combine(output, "Resources", "emi", "desk-lines.json"), target);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", temp);
            Assert.Equal(temp, AppContext.BaseDirectory);
            var engine = (EmiLineEngine)Activator.CreateInstance(typeof(EmiLineEngine), nonPublic: true)!;
            Assert.True(engine.Ready);
            Assert.Contains("firstContact", engine.MomentIds);
            Assert.True(engine.IsHoldMoment("panicPressed"));
        }
        finally { AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", output); System.IO.Directory.Delete(temp, true); }
    }
    [Fact]
    public Task DirectSayCannotInterruptPresentation() => InDesk(desk =>
    {
        var window = desk.Window!;
        window.BeginPresentation(() => { });
        var face = window.Face;
        bool done = false;
        window.Say("friends notification", "^_~", () => done = true);
        Assert.Null(window.BubbleText);
        Assert.False(window.ChainLive);
        for (int i = 0; i < 6; i++) window.AdvanceSpeech();
        Assert.Equal(face, window.Face);
        Assert.False(done);
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData("panicPressed", "-_-"), InlineData("dismissed", "T_T")]
    public Task RightClickDuringHoldStillReturnsToRest(string moment, string face) => InDesk(async desk =>
    {
        var window = desk.Window!;
        for (int i = 0; window.Transiting && i < 100; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
        Assert.False(window.Transiting);
        desk.Fire(moment);
        Assert.Equal(face, window.Face);
        var body = window.FindControl<Control>("BodyRoot")!;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        body.RaiseEvent(new PointerReleasedEventArgs(body, pointer, window, default, 0,
            new PointerPointProperties(0, PointerUpdateKind.RightButtonReleased), 0, MouseButton.Right));
        Assert.True(window.RingOpen);
        Assert.True(window.HoldPending);
        Assert.Equal(face, window.Face);
        window.ExpireHold();
        Assert.Equal(EmiChains.RestFace, window.Face);
        Assert.False(window.HoldPending);
    });
}
