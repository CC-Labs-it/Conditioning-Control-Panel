using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EffectPayloadFiring.cs:71-102: popping the Brain Drain bubble shows a timed drain,
/// one at a time, never over the user's own drain, and not with the blur dial at 0.</summary>
public sealed class BubbleBrainDrainMeltTests
{
    [Fact]
    public void MeltPopShowsTimedVeilWithWpfGatesAndPanicClearsIt() => AvaloniaTestDispatcher.Run(() =>
    {
        BubbleMotionPickerTests.Setup();
        var s = CoreSettings.Current;
        var (strength, userUp, skip) = (s.BrainDrainBlurStrength, BubblePayloadHost.UserDrainUp, BubblePayloadHost.SkipPlatformChecksForTests);
        var host = new Window();
        try
        {
            BubblePayloadHost.SkipPlatformChecksForTests = true;
            host.Show();
            BubblePayloadHost.UserDrainUp = () => false;

            s.BrainDrainBlurStrength = 0;   // dial at 0 pays XP only
            BubblePayloadHost.BrainDrainMelt(host);
            Assert.False(BubblePayloadHost.MeltActive);

            BubblePayloadHost.UserDrainUp = () => true;   // the user's own drain wins
            s.BrainDrainBlurStrength = 70;
            BubblePayloadHost.BrainDrainMelt(host);
            Assert.False(BubblePayloadHost.MeltActive);

            BubblePayloadHost.UserDrainUp = () => false;
            BubblePayloadHost.BrainDrainMelt(host);
            Assert.True(BubblePayloadHost.MeltActive);
            var count = BubblePayloadHost.Melt.Count;
            Assert.True(count > 0);
            var fill = (SolidColorBrush)((Border)BubblePayloadHost.Melt[0].Content!).Background!;
            Assert.Equal((byte)(BrainDrainBubble.OverlayOpacity(70) * 0.4 * 255), fill.Color.A);

            BubblePayloadHost.BrainDrainMelt(host);   // one at a time
            Assert.Equal(count, BubblePayloadHost.Melt.Count);

            PanicSurfaces.All.Single(x => x.Id == "bubble-host").Stop(null!);
            Assert.False(BubblePayloadHost.MeltActive);
            Assert.Empty(BubblePayloadHost.Melt);
        }
        finally
        {
            BubblePayloadHost.Stop();
            (s.BrainDrainBlurStrength, BubblePayloadHost.UserDrainUp, BubblePayloadHost.SkipPlatformChecksForTests) = (strength, userUp, skip);
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
