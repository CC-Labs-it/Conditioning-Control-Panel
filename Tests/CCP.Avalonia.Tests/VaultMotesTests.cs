using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VaultMotesTests
{
    [Fact]
    public Task MotesSpawnInTierColoursRespectGatesAndClearOnStop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Full; s.PerformanceMode = false; s.AutoPerformanceMode = false;
        var fx = new AmbientFxCanvas();
        fx.Arrange(new Rect(0, 0, 600, 400));
        fx.StartLayers(AmbientFxLayers.VaultMotes);
        fx.SetVaultZones(new[] { new VaultZone(new Rect(50, 100, 300, 150), Color.Parse("#6FE8FF"), true) });
        for (int i = 0; i < 30; i++) fx.StepVault(0.05f);
        Assert.InRange(fx.VaultMoteCount, 30, 40);
        var motes = ((Array)Field(fx, "_vault")).Cast<object>().Take(fx.VaultMoteCount).ToList();
        Assert.Contains(motes, m => (uint)Field(m, "Rgb") == 0x6FE8FF && (bool)Field(m, "Diamond"));
        using (var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600, 400)))
        {
            bitmap.Render(fx);
            var pixels = new byte[600 * 400 * 4];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(0, 0, 600, 400), pin.AddrOfPinnedObject(), pixels.Length, 600 * 4); }
            finally { pin.Free(); }
            Assert.Contains(pixels, b => b != 0);
        }
        Set(fx, "_liveBudget", 3);
        fx.StepVault(0.05f);
        Assert.Equal(6, fx.VaultMoteCount);
        Set(fx, "_fogOnly", true);
        fx.StepVault(0.05f);
        Assert.Equal(0, fx.VaultMoteCount);
        Set(fx, "_fogOnly", false); Set(fx, "_liveBudget", 60);
        s.MotionLevel = MotionLevel.Reduced;
        for (int i = 0; i < 80; i++) fx.StepVault(0.05f);
        Assert.InRange(fx.VaultMoteCount, 1, 14);
        Assert.True((bool)typeof(AmbientFxCanvas).GetMethod("ReducedVaultMayRun", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fx, null)!);
        s.MotionLevel = MotionLevel.Off;
        fx.StepVault(0.05f); Assert.Equal(0, fx.VaultMoteCount);
        s.MotionLevel = MotionLevel.Full;
        fx.StepVault(0.5f); Assert.True(fx.VaultMoteCount > 0);
        fx.Stop(); Assert.Equal(0, fx.VaultMoteCount); Assert.False(fx.IsTicking);
        fx.StartLayers(AmbientFxLayers.DustField);
        fx.StepVault(1); Assert.Equal(0, fx.VaultMoteCount);
        return Task.CompletedTask;
    });

    private static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
}
