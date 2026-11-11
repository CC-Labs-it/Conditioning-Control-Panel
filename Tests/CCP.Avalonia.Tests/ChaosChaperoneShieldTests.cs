using System.Threading.Tasks;
using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosChaperoneShieldTests
{
    [Fact]
    public Task Rectangle_sweep_respects_the_escort() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var field = BubbleOverlay.Field;
        var live = new AmbientBubble { X = 10, Y = 10, Size = 100, Spec = new EffectBubbleSpec { IsLive = true, IsChaperoneLive = true } };
        var escort = new AmbientBubble { X = 500, Y = 500, Size = 100, Spec = new EffectBubbleSpec { IsEscort = true } };
        live.Escort = escort;
        field.Bubbles.Add(live);
        field.Bubbles.Add(escort);
        try
        {
            BubbleOverlay.PopChaosInRect(new Rect(0, 0, 200, 200));
            Assert.False(live.Popping);
            escort.Popping = true;
            BubbleOverlay.PopChaosInRect(new Rect(0, 0, 200, 200));
            Assert.True(live.Deflating);
        }
        finally { field.Bubbles.Remove(live); field.Bubbles.Remove(escort); }
        return Task.CompletedTask;
    });
}
