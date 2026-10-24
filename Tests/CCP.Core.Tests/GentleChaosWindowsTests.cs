using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class GentleChaosWindowsTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly SettingsService _settings = new();
    public GentleChaosWindowsTests() => CoreSettings.ServiceProvider = () => _settings;
    public void Dispose() => CoreSettings.ServiceProvider = _provider;
    [Theory]
    [InlineData(false, 40)]
    [InlineData(true, 300)]
    public void Pulse_rises_to_peak_then_keeps_260ms_fade(bool gentle, int rise)
    {
        var s = CoreSettings.Current;
        bool saved = s.GentleVisuals;
        try
        {
            s.GentleVisuals = gentle;
            var ramp = ChaosFxEnvelope.Pulse(0, 1);
            Assert.Equal(rise + 260, ramp.DurationMs);
            Assert.Equal(0.36, ramp.Sample(rise / 2.0), 6);
            Assert.Equal(0.72, ramp.Sample(rise), 6);
            Assert.Equal(0.36, ramp.Sample(rise + 130), 6);
        }
        finally { s.GentleVisuals = saved; }
    }

    [Theory]
    [InlineData(100, 1050)]
    [InlineData(2000, 2000)]
    public void Gentle_residue_rises_holds_and_fades_even_when_life_is_short(int life, int end)
    {
        var s = CoreSettings.Current;
        var saved = (s.GentleVisuals, s.GentleFadeSeconds);
        try
        {
            s.GentleVisuals = true;
            s.GentleFadeSeconds = 0.75;
            var model = new ChaosFieldFxModel();
            var frame = new List<FieldFxPrim>();
            model.Residue(10, 20, 30, life, 0, new Random(1));
            model.Frame(0, frame);
            Assert.Equal(0, Assert.Single(frame).Opacity);
            frame.Clear(); model.Frame(150, frame);
            Assert.Equal(0.4, Assert.Single(frame).Opacity, 6);
            frame.Clear(); model.Frame(300, frame);
            Assert.Equal(0.8, Assert.Single(frame).Opacity, 6);
            frame.Clear(); model.Frame(end, frame);
            Assert.Empty(frame);
        }
        finally { (s.GentleVisuals, s.GentleFadeSeconds) = saved; }
    }
}
