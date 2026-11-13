using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosBodyBuzzTests
{
    [Fact]
    public async Task ShockwaveTestRestoresIncomingFieldOverlaySettingAndZapTime()
    {
        bool previous = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
        var zapField = typeof(ChaosRunHost).GetField("_lastBodyBuzzZap", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousZap = zapField.GetValue(null);
        var sentinel = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        try
        {
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
            zapField.SetValue(null, sentinel);
            await ShockwaveSelectsRendererAndCancelsHops(false);
            Assert.True(ChaosFieldFxOverlay.SkipPlatformChecksForTests);
            Assert.Equal(sentinel, zapField.GetValue(null));
        }
        finally
        {
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = previous;
            zapField.SetValue(null, previousZap);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ShockwaveSelectsRendererAndCancelsHops(bool skia) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var field = typeof(ChaosRunHost).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
        var old = field.GetValue(null);
        var zapField = typeof(ChaosRunHost).GetField("_lastBodyBuzzZap", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldZap = zapField.GetValue(null);
        var settings = CoreSettings.Current;
        var enabled = settings.ChaosSkiaFxEnabled;
        var rng = ChaosRunHost.Rng;
        var checks = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
        var composited = ChaosSkiaFxOverlay.Composited;
        var fieldChecks = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
        var language = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            settings.ChaosSkiaFxEnabled = skia;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
            var state = new ChaosRunState(new ChaosRunConfig()) { EStimShockwaveChance = 1 };
            field.SetValue(null, state);
            LocalizationManager.Instance.SetLanguage("de");
            ChaosRunHost.Rng = new Random(7);
            BubbleOverlay.BeginChaos(new Border(), _ => { });
            var source = new AmbientBubble { X = -10, Y = -10, Size = 20, Popping = true, Spec = new EffectBubbleSpec() };
            for (int i = 1; i <= 10; i++) BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { X = i * 30 - 10, Y = -10, Size = 20, Spec = new EffectBubbleSpec() });
            var outside = new AmbientBubble { X = 431, Y = -10, Size = 20, Spec = new EffectBubbleSpec() };
            BubbleOverlay.Field.Bubbles.Add(outside);
            ChaosRunHost.BodyBuzz(source);
            Assert.Equal("Körperkribbeln: der Strom breitet sich aus", state.RecentEvents[0]);
            Assert.Equal(8, ChaosRunHost.BodyBuzzHopCount);
            if (skia) Assert.Equal(1, ChaosSkiaFxOverlay.Active!.Fx.RippleCount);
            else Assert.Equal(1, ChaosFieldFxOverlay.Active!.Model.TransientCount);
            await Task.Delay(800);
            Assert.Equal(0, ChaosRunHost.BodyBuzzHopCount);
            Assert.False(outside.Popping);
            Assert.Equal(8, BubbleOverlay.Field.Bubbles.FindAll(b => b.Popping).Count);
            ChaosRunHost.BodyBuzz(source);
            BubbleOverlay.EndChaos();
            Assert.Equal(0, ChaosRunHost.BodyBuzzHopCount);
            ChaosFieldFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.CloseActive();
            field.SetValue(null, new ChaosRunState(new ChaosRunConfig()) { EStimShockwaveChance = 0 });
            ChaosRunHost.BodyBuzz(source);
            Assert.Null(ChaosFieldFxOverlay.Active);
            Assert.Null(ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            BubbleOverlay.EndChaos(); BubbleOverlay.Field.Bubbles.Clear();
            ChaosFieldFxOverlay.CloseActive(); ChaosFieldFxOverlay.SkipPlatformChecksForTests = fieldChecks;
            LocalizationManager.Instance.SetLanguage(language);
            ChaosSkiaFxOverlay.CloseActive(); ChaosEStimOverlay.CloseActive();
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = checks; ChaosSkiaFxOverlay.Composited = composited;
            settings.ChaosSkiaFxEnabled = enabled; ChaosRunHost.Rng = rng; field.SetValue(null, old);
            zapField.SetValue(null, oldZap);
        }
    });
}
