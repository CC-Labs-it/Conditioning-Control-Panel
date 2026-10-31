using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ModSwitchRetiresOnlyDtrhWithWpfGracePeriod(bool ready, bool acknowledge) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
            if (Application.Current is null)
                builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            else builder.AfterSetupCallback(builder);
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            try
            {
                CoreModsHooks.CloseDtrhHost = null;
                App.StartMods();
                var next = App.Mods!.ActiveModId == BuiltInMods.SissyHypnoId
                    ? BuiltInMods.BambiSleepId : BuiltInMods.SissyHypnoId;
                var host = new GameWindow(GameWindow.Games["dtrh"]);
                var other = new GameWindow(GameWindow.Games["breakoutdemo"]);
                host.Show();
                other.Show();
                var posted = new List<string>();
                host.Posted += posted.Add;
                if (ready) host.HandleMessage("{\"type\":\"ready\"}");
                posted.Clear();
                App.Mods.ActivateMod(next);
                if (ready)
                {
                    Assert.Contains(posted, p => p.Contains("end-run") && p.Contains("host"));
                    Assert.True(host.IsVisible);
                    Assert.False(host.DtrhReady);
                    if (acknowledge) host.HandleMessage("{\"type\":\"exit-done\"}");
                    else
                    {
                        var clock = Stopwatch.StartNew();
                        while (host.IsVisible && clock.Elapsed < TimeSpan.FromSeconds(3))
                        {
                            await Task.Delay(20);
                        }
                        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(1100));
                    }
                }
                Assert.False(host.IsVisible);
                Assert.True(other.IsVisible);
                Assert.DoesNotContain(posted, p => p.Contains("end-run") && !ready);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                settings.SealForReset();
                CoreSettings.ServiceProvider = null;
            }
        });
}
