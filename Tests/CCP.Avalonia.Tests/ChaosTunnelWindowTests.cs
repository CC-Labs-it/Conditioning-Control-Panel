using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosTunnelWindowTests
{
    [Fact]
    public async Task Opt_in_opens_below_pinned_chrome_tracks_streak_and_panic_closes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var enabled = settings.ChaosTunnelEnabled;
            var pinned = settings.ChaosPinOnTop;
            var canOpen = ChaosTunnelWindow.CanOpen;
            try
            {
                ChaosTunnelWindow.CanOpen = () => true;
                settings.ChaosPinOnTop = true;
                settings.ChaosTunnelEnabled = false;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosRunHost.ForceShutdown();

                settings.ChaosTunnelEnabled = true;
                settings.ChaosPinOnTop = false;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosRunHost.ForceShutdown();
                settings.ChaosPinOnTop = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                var tunnel = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                Assert.False(tunnel.Topmost);
                Assert.True(ChaosRunHost.Hud!.Topmost);
                var scripts = new List<string>();
                tunnel.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
                tunnel.OnMessage("{\"type\":\"ready\"}");
                Assert.Contains(scripts, s => s.Contains("\"type\":\"run-start\""));
                tunnel.OnTimeout();
                Assert.Same(tunnel, ChaosTunnelWindow.Active);
                ChaosRunHost.State!.Combo = 4;
                Assert.Contains(scripts, s => s.Contains("\"type\":\"streak\"") && s.Contains("\"combo\":4"));
                ChaosRunHost.State.ActIndex = 3;
                Assert.Contains(scripts, s => s.Contains("\"type\":\"zone-hint\"") && s.Contains("\"depth\":3"));
                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosTunnelWindow.Active);

                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                Assert.NotNull(ChaosTunnelWindow.Active);
                ChaosRunHost.RequestStop();
                Assert.Null(ChaosTunnelWindow.Active);
                ChaosRunHost.ForceShutdown();

                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.BeginRun();
                var stalled = Assert.IsType<ChaosTunnelWindow>(ChaosTunnelWindow.Active);
                stalled.OnTimeout();
                Assert.Null(ChaosTunnelWindow.Active);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosTunnelWindow.CanOpen = canOpen;
                settings.ChaosTunnelEnabled = enabled;
                settings.ChaosPinOnTop = pinned;
            }
            return Task.CompletedTask;
        });
    }
}
