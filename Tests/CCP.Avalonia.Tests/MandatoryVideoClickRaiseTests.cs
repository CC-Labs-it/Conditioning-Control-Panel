using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoClickRaiseTests
{
    [Theory]
    [InlineData(false, false, 1, 0)]
    [InlineData(false, true, 1, 1)]
    [InlineData(true, true, 0, 0)]
    public async Task OpenedAppliesFullscreenTreatments(bool floating, bool chaos, int hooks, int styles)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var overlay = new MandatoryVideoOverlay(null);
            typeof(MandatoryVideoOverlay).GetField("<Floating>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(overlay, floating);
            var active = typeof(ChaosRunHost).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic)!;
            var saved = active.GetValue(null);
            int hookCount = 0, styleCount = 0;
            overlay.DenyVideoMouseActivate = _ => hookCount++;
            overlay.MakeVideoNonActivating = _ => styleCount++;
            overlay.ApplyFloatingPassive = _ => { };
            Window? window = null;
            try
            {
                active.SetValue(null, chaos);
                window = overlay.Build(null, false).Window;
                if (floating) window.Show();
                Assert.Equal(hooks, hookCount);
                Assert.Equal(styles, styleCount);
            }
            finally { window?.Close(); active.SetValue(null, saved); }
            return Task.CompletedTask;
        });
    }
}
