using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class FxPaletteTests
{
    [Fact]
    public Task ModSwitchPublishesPaletteBeforeConsumersAndUsesFallbacks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var resources = Application.Current!.Resources;
        var keys = new[] { "FxMistColor", "FxMistBrush", "FxParticleColor", "FxParticleBrush", "FxGlowColor", "FxGlowBrush", "FxFlashTintColor", "FxFlashTintBrush" };
        var saved = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(keys, k => resources.ContainsKey(k) ? resources[k] : null));
        App.StartMods();
        var mods = App.Mods!;
        var originalId = mods.ActiveModId;
        var target = originalId == BuiltInMods.InfectionControlId ? BuiltInMods.CCPDefaultId : BuiltInMods.InfectionControlId;
        mods.ActivateMod(target);
        var manifest = mods.ActiveMod.Manifest;
        var oldPalette = manifest.FxPalette;
        var oldTheme = manifest.Theme;
        try
        {
            manifest.Theme = new ModTheme { FilterColor = "#123456" };
            manifest.FxPalette = new ModFxPalette { MistColor = "#112233", ParticleColor = "#223344", GlowColor = "#334455", FlashTint = "#445566", MistOpacity = 0.4 };
            mods.ActivateMod(originalId);
            (Color mist, Color particle, Color glow, Color flash, double opacity, Color brush)? observed = null;
            System.EventHandler<ModPackage> handler = (_, _) => observed = (
                AmbientFxCanvas.Env.MistColor, AmbientFxCanvas.Env.ParticleColor,
                AmbientFxCanvas.Env.GlowColor, AmbientFxCanvas.Env.FlashTintColor,
                AmbientFxCanvas.Env.MistOpacity, ((ISolidColorBrush)Application.Current!.Resources["FxGlowBrush"]!).Color);
            CoreMods.ModChanged += handler;
            try { mods.ActivateMod(target); } finally { CoreMods.ModChanged -= handler; }
            Assert.NotNull(observed);
            Assert.Equal((Color.Parse("#112233"), Color.Parse("#223344"), Color.Parse("#334455"),
                Color.Parse("#445566"), 0.4, Color.Parse("#334455")), observed.Value);
            manifest.FxPalette = null;
            mods.ActivateMod(originalId);
            mods.ActivateMod(target);
            Assert.Equal(Color.Parse("#123456"), AmbientFxCanvas.Env.MistColor);
            Assert.Equal(Color.Parse("#123456"), AmbientFxCanvas.Env.GlowColor);
            Assert.Equal(1.0, AmbientFxCanvas.Env.MistOpacity);
            AmbientFxCanvas.Env.ApplyPalette(null);
            Assert.Equal(Color.Parse("#FF69B4"), AmbientFxCanvas.Env.GlowColor);
        }
        finally
        {
            manifest.FxPalette = oldPalette;
            manifest.Theme = oldTheme;
            mods.ActivateMod(originalId);
            for (var i = 0; i < keys.Length; i++)
                if (saved[i] is { } value) resources[keys[i]] = value; else resources.Remove(keys[i]);
        }
        return Task.CompletedTask;
    });
}
