using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassOfferLandingTests
{
    [Theory]
    [InlineData("offair")]
    [InlineData("browsing")]
    [InlineData("shop")]
    [InlineData("pong")]
    public Task LandingOffersItsChannel(string channel) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var bedtime = EmiState.Current.BedtimeUntil;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var desk = new EmiDeskWindow { GlassChannelOverride = channel };
        string? offered = null;
        int count = 0;
        desk.GlassOfferForTest = id => { offered = id; count++; desk.NoteGlassActivity(); };
        try
        {
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip);
            Assert.Null(offered);
            desk.AdvanceGlass(flip.AddMilliseconds(220));
            Assert.Equal(channel, offered);
            Assert.Equal(1, count);
            Assert.True(desk.GlassIsLive);
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
