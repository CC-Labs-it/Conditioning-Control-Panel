using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MasterVolumeTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(50, 0.5, 0.25)]
    [InlineData(150, 2, 1)]
    [InlineData(-5, 0.5, 0)]
    [InlineData(100, double.NaN, 0)]
    public void Mind_wipe_multiplies_clamped_dials(int master, double own, float expected)
        => Assert.Equal(expected, AudioGain.MindWipe(master, own), 4);

    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(-10, 100, 0)]
    [InlineData(100, 100, 1)]
    [InlineData(100, 1, 0.05)]
    [InlineData(50, 50, 0.125)]
    [InlineData(150, 150, 1)]
    public void Sound_curves_the_product_with_a_floor_only_above_zero(int own, int master, float expected)
        => Assert.Equal(expected, AudioGain.Sound(own, master), 4);

    [Fact]
    public void Settings_restore_publishes_new_level_and_follows_replacement()
    {
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService(); // test assembly installs an isolated CCP_USERDATA_DIR
        var before = service.Current;
        var heard = new List<int>();
        var watch = new CoreMasterVolumeWatch();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            watch.Changed += heard.Add;
            watch.EnsureHooked();
            var after = new AppSettings { MasterVolume = 0 };
            service.RestoreFrom(after);
            before.MasterVolume = 17;
            after.MasterVolume = 25;
            Assert.Equal(new[] { 0, 25 }, heard);
        }
        finally { CoreSettings.ServiceProvider = provider; }
    }

    [Fact]
    public void Watch_filters_changes_drops_old_settings_and_isolates_failed_listeners()
    {
        var watch = new CoreMasterVolumeWatch();
        var before = new AppSettings();
        var after = new AppSettings();
        var heard = new List<int>();
        watch.Changed += _ => throw new InvalidOperationException();
        watch.Changed += heard.Add;
        watch.Hook(before);
        before.MasterVolume = 0;
        before.VideoVolume = 20;
        watch.Hook(after);
        before.MasterVolume = 12;
        after.MasterVolume = 35;
        Assert.Equal(new[] { 0, 35 }, heard);
    }
}
