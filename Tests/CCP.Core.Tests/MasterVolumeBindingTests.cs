using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MasterVolumeBindingTests
{
    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(100, 100, 1)]
    [InlineData(1, 1, 0.05)]
    [InlineData(-10, 100, 0)]
    public void Sound_gain_preserves_mute_and_the_positive_floor(int own, int master, double expected)
        => Assert.Equal(expected, MasterVolumeBinding.SoundGain(own, master), 5);

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(40, 0.5, 0.2)]
    [InlineData(200, 2, 1)]
    [InlineData(100, double.NaN, 0)]
    public void Mind_wipe_gain_is_linear_and_clamped(int master, double own, double expected)
        => Assert.Equal(expected, MasterVolumeBinding.MindWipeGain(master, own), 5);

    [Fact]
    public void Watch_follows_replacement_detaches_old_settings_and_disposes()
    {
        var service = new SettingsService();
        var original = service.Current;
        var levels = new List<int>();
        try
        {
            using var watch = new MasterVolumeBinding(levels.Add, service);
            original.MasterVolume = 37;
            Assert.Equal(37, levels[^1]);
            var replacement = new AppSettings { MasterVolume = 23 };
            service.RestoreFrom(replacement);
            Assert.Equal(23, levels[^1]);
            var count = levels.Count;
            original.MasterVolume = 72;
            Assert.Equal(count, levels.Count);
            replacement.MasterVolume = 0;
            Assert.Equal(0, levels[^1]);
            watch.Dispose();
            count = levels.Count;
            replacement.MasterVolume = 55;
            Assert.Equal(count, levels.Count);
        }
        finally { service.RestoreFrom(original); }
    }
}
