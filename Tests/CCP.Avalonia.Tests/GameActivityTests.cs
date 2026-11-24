using System;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class GameActivityTests
{
    [Fact]
    public void GameHidesAndRestoresSpeech() => AvaloniaTestDispatcher.Run(() =>
    {
        var tube = new AvatarTubeWindow(null);
        var game = new GameWindow(GameWindow.Games["breakoutdemo"]);
        int changes = 0;
        Action changed = () => changes++;
        try
        {
            tube.Show();
            tube.GigglePriority("A line.", playSound: false, aiGenerated: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.ShouldShowBubble());
            Assert.True(tube.BubbleWindow!.IsVisible);
            GameActivity.GameActiveChanged += changed;
            game.Show();
            Assert.Equal(1, changes);
            Assert.False(tube.ShouldShowBubble());
            Assert.False(tube.BubbleWindow.IsVisible);
            game.Close();
            Assert.Equal(2, changes);
            Assert.True(tube.ShouldShowBubble());
            Assert.True(tube.BubbleWindow.IsVisible);
        }
        finally
        {
            GameActivity.GameActiveChanged -= changed;
            game.Close();
            tube.Close();
        }
    });

    [Theory]
    [InlineData("fyp")]
    [InlineData("loom")]
    public void NonGamesDoNotCount(string id) => AvaloniaTestDispatcher.Run(() =>
    {
        var window = new GameWindow(GameWindow.Games[id]);
        int changes = 0;
        Action changed = () => changes++;
        GameActivity.GameActiveChanged += changed;
        try
        {
            window.Show();
            Assert.False(GameActivity.AnyGameActive);
            window.Close();
            Assert.Equal(0, changes);
        }
        finally { GameActivity.GameActiveChanged -= changed; window.Close(); }
    });

    [Fact]
    public void IntakeCounts() => AvaloniaTestDispatcher.Run(() =>
    {
        var window = new IntakeHostWindow();
        int changes = 0;
        Action changed = () => changes++;
        GameActivity.GameActiveChanged += changed;
        try
        {
            window.Show();
            Assert.True(GameActivity.AnyGameActive);
            window.Close();
            Assert.False(GameActivity.AnyGameActive);
            Assert.Equal(2, changes);
        }
        finally { GameActivity.GameActiveChanged -= changed; window.Close(); }
    });
}
