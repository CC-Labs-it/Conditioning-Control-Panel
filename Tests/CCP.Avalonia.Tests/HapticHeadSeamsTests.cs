using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF App.xaml.cs:3067: toy connect fires EMI Desk's hapticsConnected moment. The playhead seam
/// (App.xaml.cs:3060) is covered by FunScriptPlayheadTests.</summary>
public sealed class HapticHeadSeamsTests
{
    [Fact]
    public Task ToyConnectFiresEmiDeskHapticsConnectedMoment() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = (HapticService.EmiDeskFire, MockProviderV2.Toast);
        var moments = new List<EmiMoment>();
        EventHandler<EmiMoment> observer = (_, m) => moments.Add(m);
        var desk = EmiDeskService.Instance;
        var s = new HapticSettings { Enabled = true };
        s.EnsureV2Migrated();
        s.V2.Provider("lovense").Enabled = false;
        s.V2.Provider("buttplug").Enabled = false;
        s.V2.Provider("mock").Enabled = true;
        try
        {
            MockProviderV2.Toast = _ => { };
            global::ConditioningControlPanel.Avalonia.App.SeedHapticHeadSeams();
            desk.MomentFired += observer;

            using (var haptics = new HapticService(s))
                Assert.True(await haptics.ConnectAsync());
            var connected = Assert.Single(moments, m => m.Id == "hapticsConnected");
            Assert.NotNull(connected.Context?.GetType().GetProperty("target")?.GetValue(connected.Context));
        }
        finally
        {
            desk.MomentFired -= observer;
            (HapticService.EmiDeskFire, MockProviderV2.Toast) = old;
        }
    });
}
