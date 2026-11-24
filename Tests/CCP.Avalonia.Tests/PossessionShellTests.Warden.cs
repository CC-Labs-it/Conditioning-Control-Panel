using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class PossessionShellTests
{
    [Fact]
    public Task WardenStopReleasesDirectorBooking() => RunAsync(async rig =>
    {
        Frame();
        var button = rig.Shell.Named<Button>("BtnClose")!;
        var target = ((IPossessionHost)rig.Shell).Targets.Single(t => ReferenceEquals(t.Visual, button));
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Clock = rig.Clock.AddMinutes(4);
        Tick(rig.Lockdown);
        rig.Director.RequestReactive("dodge", target);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, rig.Director.LiveEffectCount);
        AvApp.Possession = rig.Director;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; tube.Position = new PixelPoint(-500, -500);
            var knock = warden.KnockAsync(target, CancellationToken.None);
            await Task.Delay(100);
            Assert.NotEqual(new PixelPoint(-500, -500), tube.Position);
            warden.Stop();
            Assert.Equal(0, rig.Director.LiveEffectCount);
            Assert.False(target.IsLive);
            await knock;
            Assert.Equal(0, rig.Director.LiveEffectCount);
        }
        finally { warden.Stop(); AvatarTubeWindow.TestLive = null; tube.Close(); }
    });
}
