using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalFeatureUseTests
{
    [Fact]
    public void Only_displayed_cards_record_feature_use() => AvaloniaTestDispatcher.Run(() =>
    {
        var used = new List<string>();
        Action<string> record = used.Add;
        var provider = CoreSubliminal.ShowProvider;
        var draw = SubliminalWhisperShow.DrawWithOpacity;
        var haptics = CoreHaptics.Service;
        App.FeatureUsed += record;
        try
        {
            App.SeedSubliminalFeatureUse();
            App.SeedSubliminalFeatureUse(); // repeated startup must not duplicate the subscription
            CoreSubliminal.ShowProvider!("ambient"); // no desktop host
            Assert.Empty(used);

            // Hosted Show and classic Present both raise this same event once per card.
            var displayed = typeof(SubliminalOverlay).GetField("Displayed", BindingFlags.Static | BindingFlags.NonPublic)!;
            ((Action)displayed.GetValue(null)!)();
            Assert.Equal(new[] { MemorySignalWriter.FeatureSubliminal }, used);
            var card = new Window();
            SubliminalOverlay.Present(card);
            card.Close();
            Assert.Equal(2, used.Count);
            // Route a custom card through its draw seam, then show it on the classic surface.
            CoreHaptics.Service = null;
            SubliminalWhisperShow.DrawWithOpacity = (_, _) =>
            {
                var custom = new Window();
                SubliminalOverlay.Present(custom);
                custom.Close();
            };
            SubliminalWhisperShow.Custom("custom");
            Assert.Equal(new[] { MemorySignalWriter.FeatureSubliminal, MemorySignalWriter.FeatureSubliminal, MemorySignalWriter.FeatureSubliminal }, used);
        }
        finally
        {
            App.FeatureUsed -= record;
            CoreSubliminal.ShowProvider = provider;
            CoreHaptics.Service = haptics;
            SubliminalWhisperShow.DrawWithOpacity = draw;
            SubliminalOverlay.CloseAll();
        }
    });
}
