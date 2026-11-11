using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountAudioRoutingTests
{
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(BubbleCountWindow game, string field, object value) => typeof(BubbleCountWindow).GetField(field, Instance)!.SetValue(game, value);
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("close")]
    [InlineData("end")]
    public Task PlayingRoutesOnceAndIgnoresStaleGame(string terminal) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var game = new BubbleCountWindow { Content = new Grid() };
        var player = (MediaPlayer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MediaPlayer));
        GC.SuppressFinalize(player);
        var playerField = typeof(BubbleCountWindow).GetField("_player", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var oldDevice = CoreSettings.Current.AudioOutputDeviceId;
        var calls = 0;
        try
        {
            Set(game, "_gameCompleted", false); playerField.SetValue(null, player);
            CoreSettings.Current.MasterVolume = 37; CoreSettings.Current.AudioOutputDeviceId = "saved-device";
            game.RouteAudio = (actual, volume) => { Assert.Same(player, actual); Assert.Equal(37, volume); Assert.Equal("saved-device", CoreSettings.Current.AudioOutputDeviceId); calls++; };
            var playing = game.NativePlayingHandler(player);
            playing(); playing();
            if (terminal == "close") Set(game, "_closed", true);
            if (terminal == "end") Set(game, "_videoEnded", true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(terminal == "none" ? 1 : 0, calls);
        }
        finally { playerField.SetValue(null, null); CoreSettings.Current.MasterVolume = oldVolume; CoreSettings.Current.AudioOutputDeviceId = oldDevice; game.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BrowserClickRestacksLiveBubblesUnlessClosed(bool close) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var game = new BubbleCountWindow { Content = new Grid() };
        var saved = CountBubble.Restack;
        var calls = new List<Window>();
        try
        {
            game.Show(); Dispatcher.UIThread.RunJobs();
            Set(game, "_gameCompleted", false);
            CountBubble.Restack = (bubble, owner) => { Assert.Same(game, owner); calls.Add(bubble); };
            var bubbles = (List<CountBubble>)typeof(BubbleCountWindow).GetField("_activeBubbles", Instance)!.GetValue(game)!;
            bubbles.Add(new CountBubble(null, 120, 0, 0, new Random(1), null, game));
            bubbles.Add(new CountBubble(null, 120, 0, 0, new Random(2), null, game));
            calls.Clear();
            using var message = JsonDocument.Parse("{\"type\":\"click\"}");
            typeof(BubbleCountWindow).GetMethod("OnBrowserMessage", Instance)!.Invoke(game, new object[] { message.RootElement });
            if (close) game.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(close ? 0 : 2, calls.Count);
            if (!close) Assert.NotSame(calls[0], calls[1]);
        }
        finally { game.Close(); CountBubble.Restack = saved; BubbleCountWindow.ForceCloseAll(); }
        return Task.CompletedTask;
    });
}
