using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralMotionTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true, false, true, true)]
    [InlineData(MotionLevel.Full, false, false, false, true)]
    [InlineData(MotionLevel.Full, true, true, false, true)]
    [InlineData(MotionLevel.Reduced, true, false, false, true)]
    [InlineData(MotionLevel.Reduced, false, false, false, true)]
    [InlineData(MotionLevel.Off, true, false, false, false)]
    [InlineData(MotionLevel.Off, false, true, false, false)]
    public void RoomGatesRespectUserOsAndGentleMotion(MotionLevel level, bool os, bool gentle,
        bool ambient, bool transitions)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldOs = ClientAreaMotion.AnimationsEnabled;
        var settings = new SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.MotionLevel = level;
            settings.Current.GentleVisuals = gentle;
            settings.Current.PerformanceMode = false;
            settings.Current.AutoPerformanceMode = false;
            ClientAreaMotion.AnimationsEnabled = () => os;
            Assert.Equal(ambient, SpiralTabView.AllowAmbientLoops);
            Assert.Equal(transitions, SpiralTabView.AllowTransitions);
        }
        finally
        {
            settings.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            ClientAreaMotion.AnimationsEnabled = oldOs;
        }
    }
}
