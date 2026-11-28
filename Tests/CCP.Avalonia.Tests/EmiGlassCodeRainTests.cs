using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassCodeRainTests
{
    [Fact]
    public Task PanicDuringOfferClearsTheGlass() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var bedtime = EmiState.Current.BedtimeUntil;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var desk = new EmiDeskWindow { GlassChannelOverride = "coderain" };
        desk.GlassOfferForTest = _ => desk.StopChainForPanic();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = oldProvider;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, false)]
    public Task PoolAndPanicRespectMotion(MotionLevel motion, bool offered) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldPick = EmiDeskWindow.GlassPick;
        var bedtime = EmiState.Current.BedtimeUntil;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        string[]? pool = null;
        EmiDeskWindow.GlassPick = p => { pool = p.ToArray(); return offered ? "coderain" : "offair"; };
        var desk = new EmiDeskWindow();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = motion;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip);
            Assert.NotNull(pool);
            Assert.Equal(offered, pool!.Contains("coderain"));
            desk.AdvanceGlass(flip.AddMilliseconds(220));
            if (offered)
            {
                Assert.Equal("coderain", desk.GlassChannel);
                var canvas = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
                var columns = canvas.Children.OfType<TextBlock>().ToArray();
                Assert.NotEmpty(columns);
                var before = columns.Select(Canvas.GetTop).ToArray();
                for (int i = 1; i <= 20; i++) desk.AdvanceGlass(flip.AddMilliseconds(220 + i * 100));
                Assert.NotEqual(before, columns.Select(Canvas.GetTop).ToArray());
                desk.StopChainForPanic();
                Assert.False(desk.GlassIsLive);
                Assert.Empty(desk.GlassHost.Children);
                flip = DateTime.UtcNow.AddSeconds(61);
                desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
                Assert.Equal("coderain", desk.GlassChannel);
                CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
                desk.AdvanceGlass(flip.AddMilliseconds(253));
                Assert.False(desk.GlassIsLive);
                Assert.Empty(desk.GlassHost.Children);
            }
        }
        finally
        {
            desk.ShutDown(); EmiDeskWindow.GlassPick = oldPick;
            EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = oldProvider;
        }
        return Task.CompletedTask;
    });
}
