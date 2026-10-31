using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MasterVolumeWatchTests
{
    [Fact]
    public void Hook_FollowsNewSettingsOnce_AndOneFaultyPlayerDoesNotBlockOthers()
    {
        var first = new AppSettings();
        var second = new AppSettings();
        var watch = new MasterVolumeWatch();
        var levels = new List<int>();
        watch.Changed += _ => throw new System.InvalidOperationException("disposed player");
        watch.Changed += levels.Add;
        watch.Hook(first);
        watch.Hook(first);
        first.MasterVolume = 30;
        first.MantraDroneVolume = 25;
        watch.Hook(second);
        first.MasterVolume = 60;
        second.MasterVolume = 0;
        watch.Hook(null);
        second.MasterVolume = 100;
        Assert.Equal(new[] { 30, 0 }, levels);
    }
}
