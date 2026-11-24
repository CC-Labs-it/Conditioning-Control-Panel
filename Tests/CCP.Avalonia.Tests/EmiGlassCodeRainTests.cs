using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassCodeRainTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, false)]
    public Task SaverRunsOnlyAtFullMotionAndStopsOnMotionDrop(MotionLevel motion, bool expected) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var bedtime = EmiState.Current.BedtimeUntil;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var desk = new EmiDeskWindow { GlassChannelOverride = "coderain" };
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = motion;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
            Assert.Equal(expected ? "coderain" : "offair", desk.GlassChannel);
            if (expected)
            {
                var canvas = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
                var glyph = Assert.IsType<TextBlock>(canvas.Children.OfType<TextBlock>().First());
                var before = Canvas.GetTop(glyph);
                desk.AdvanceGlass(flip.AddMilliseconds(340));
                Assert.NotEqual(before, Canvas.GetTop(glyph));
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                desk.AdvanceGlass(flip.AddMilliseconds(373));
                Assert.False(desk.GlassIsLive);
            }
        }
        finally { desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicSurfaceClosesTheLiveSaver() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var bedtime = EmiState.Current.BedtimeUntil;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var service = EmiDeskService.Instance;
        var field = typeof(EmiDeskService).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = field.GetValue(service);
        var desk = new EmiDeskWindow { GlassChannelOverride = "coderain" };
        try
        {
            field.SetValue(service, desk);
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
            Assert.Equal("coderain", desk.GlassChannel);
            PanicSurfaces.All.Single(s => s.Id == "emi-desk-feedback").Stop(null!);
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
        }
        finally { field.SetValue(service, previous); desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old; EmiLineEngine.Instance.ResetForTests(); }
        return Task.CompletedTask;
    });
}
