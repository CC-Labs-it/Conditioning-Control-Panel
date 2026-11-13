using System;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionGlideTests
{
    [Fact]
    public void PlacementClampsAndStandsBesideTheTarget()
    {
        var work = new PixelRect(100, 200, 1000, 700);
        var size = new PixelSize(200, 300);
        Assert.Equal(new PixelPoint(900, 600), AvatarTubeWindow.ClampPossessionPoint(new PixelPoint(1200, 1000), size, work));
        Assert.Equal(new PixelPoint(100, 200), AvatarTubeWindow.ClampPossessionPoint(new PixelPoint(0, 0), size, work));
        Assert.Equal(new PixelPoint(370, 200), AvatarTubeWindow.PossessionRectPoint(new PixelRect(300, 250, 50, 100), size, work));
        Assert.Equal(new PixelPoint(580, 350), AvatarTubeWindow.PossessionRectPoint(new PixelRect(800, 450, 50, 100), size, work));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("busy")]
    public Task MovementRefusesAnUnavailableTube(string condition) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarEnabled = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        try
        {
            tube.Show();
            if (condition == "disabled") CoreSettings.Current.AvatarEnabled = false;
            if (condition == "hidden") tube.Hide();
            if (condition == "busy") typeof(AvatarTubeWindow).GetField("_isWaitingForAi", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tube, true);
            Assert.False(tube.CanPerformPossessionMove);
            var home = tube.Position;
            await tube.GlideToScreenPointAsync(new PixelPoint(500, 500), CancellationToken.None, false);
            Assert.Equal(home, tube.Position);
        }
        finally { tube.Close(); CoreSettings.ServiceProvider = null; }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task MovementRestoresTheFirstHomeAndUnsupportedMovesDoNothing(bool detached) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = detached;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.Position = new PixelPoint(200, 250);
            tube.PossessionMoveSupportedOverride = false;
            Assert.False(tube.CanPerformPossessionMove);
            await tube.GlideToScreenPointAsync(new PixelPoint(500, 500), CancellationToken.None, false);
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
            tube.PossessionMoveSupportedOverride = true;
            Assert.True(tube.CanPerformPossessionMove);
            await tube.GlideToScreenPointAsync(new PixelPoint(500, 500), CancellationToken.None, false);
            await tube.GlideToScreenPointAsync(new PixelPoint(600, 500), CancellationToken.None, false);
            await tube.ReturnHomeAsync(CancellationToken.None);
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
            await tube.ReturnHomeAsync(CancellationToken.None);
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
        }
        finally { tube.Close(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task CancelledMoveKeepsItsCapturedHome() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        try
        {
            tube.Show();
            tube.Position = new PixelPoint(200, 250);
            using var cts = new CancellationTokenSource(70);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tube.GlideToScreenPointAsync(new PixelPoint(600, 500), cts.Token, false));
            await tube.ReturnHomeAsync(CancellationToken.None);
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
        }
        finally { tube.Close(); CoreSettings.ServiceProvider = null; }
    });
}
