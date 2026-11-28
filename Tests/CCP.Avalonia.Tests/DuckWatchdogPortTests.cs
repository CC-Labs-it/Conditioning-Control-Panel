using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DuckWatchdogPortTests
{
    [Fact]
    public void WindowsKeepsLiveOwnerAndRestoresOrphan()
    {
        var old = CoreSettings.Current.MasterVolume;
        var alive = true;
        var d = new WindowsDucker(body => body(new List<DuckSession>()), _ => "", null, () => false, -1)
            { DuckOwnerAlive = () => alive };
        try
        {
            CoreSettings.Current.MasterVolume = 100;
            d.Duck(50);
            d.WatchdogTick();
            Assert.True(d.IsDucked);
            alive = false;
            d.WatchdogTick();
            Assert.False(d.IsDucked);
        }
        finally { d.Shutdown(); d.Drain(); CoreSettings.Current.MasterVolume = old; }
    }

    [Fact]
    public void PulseKeepsLiveOwnerAndRestoresOrphan()
    {
        var old = CoreSettings.Current.MasterVolume;
        var alive = true;
        var d = new LibVlcAudio(_ => "[]") { DuckOwnerAlive = () => alive };
        try
        {
            CoreSettings.Current.MasterVolume = 100;
            d.Duck(50);
            d.WatchdogTick();
            Assert.True(d.IsDucked);
            alive = false;
            d.WatchdogTick();
            Assert.False(d.IsDucked);
        }
        finally { d.Shutdown(); d.Drain(); CoreSettings.Current.MasterVolume = old; }
    }
}
