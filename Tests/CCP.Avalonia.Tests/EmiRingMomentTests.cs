using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
namespace CCP.Avalonia.Tests;
/// <summary>WPF EmiDeskWindow.Ring.cs:287,293: a dismissed fan is a moment, three in a row is another.</summary>
public sealed class EmiRingMomentTests
{
    private const string DismissLines = """{"version":1,"moments":{"ringDismissed":{"priority":3,"pools":["d"]}},"pools":{"d":[{"id":"d1","t":"fine then","spice":0}]}}""";
    private const string ThreeLines = """{"version":1,"moments":{"suggestionIgnored3x":{"priority":3,"pools":["s"]}},"pools":{"s":[{"id":"s1","t":"not landing huh","spice":0}]}}""";
    private static readonly MethodInfo RingClosed =
        typeof(EmiDeskWindow).GetMethod("OnRingClosed", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static Task InDesk(string lines, Func<EmiDeskService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        var desk = EmiDeskService.Instance;
        var previousEngine = desk.LineEngine;
        int previousStreak = EmiState.Current.RingIgnoreStreak;
        CoreSettings.ServiceProvider = () => settings;
        desk.LineEngine = EmiLineEngine.FromJson(lines);
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            await desk.Summon();
            for (int i = 0; desk.Window!.Transiting && i < 100; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            await body(desk);
        }
        finally
        {
            EmiState.Current.RingIgnoreStreak = previousStreak;
            desk.PanicSilence();
            desk.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            desk.LineEngine = previousEngine;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    });
    [Fact]
    public Task DismissingTheFanSpeaksAndAPickStaysQuiet() => InDesk(DismissLines, desk =>
    {
        EmiState.Current.RingIgnoreStreak = 0;
        RingClosed.Invoke(null, new object?[] { null, true });
        Assert.Null(desk.Window!.BubbleText);
        RingClosed.Invoke(null, new object?[] { null, false });
        Assert.Equal(1, EmiState.Current.RingIgnoreStreak);
        Assert.Equal(".", desk.Window.BubbleText);
        for (int i = 0; i < 3; i++) desk.Window.AdvanceSpeech();
        Assert.Equal("fine then", desk.Window.BubbleText);
        return Task.CompletedTask;
    });
    [Fact]
    public Task ThirdDismissalInARowFiresSuggestionIgnored3x() => InDesk(ThreeLines, desk =>
    {
        EmiState.Current.RingIgnoreStreak = 1;
        RingClosed.Invoke(null, new object?[] { null, false });
        Assert.Null(desk.Window!.BubbleText);
        Assert.Equal(2, EmiState.Current.RingIgnoreStreak);
        RingClosed.Invoke(null, new object?[] { null, false });
        Assert.Equal(0, EmiState.Current.RingIgnoreStreak);
        for (int i = 0; i < 3; i++) desk.Window.AdvanceSpeech();
        Assert.Equal("not landing huh", desk.Window.BubbleText);
        return Task.CompletedTask;
    });
}
