using System.Reflection;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBoundPairTests
{
    [Fact]
    public void Channel_gate_uses_bound_cost_and_freeze_is_free() => AvaloniaTestDispatcher.Run(() =>
    {
        var type = typeof(ChaosRunHost);
        var stateField = type.GetField("_state", BindingFlags.Static | BindingFlags.NonPublic)!;
        var freezeField = type.GetField("_freezeRemainingSec", BindingFlags.Static | BindingFlags.NonPublic)!;
        var gate = type.GetMethod("CanChannelDefuse", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldState = stateField.GetValue(null);
        var oldFreeze = freezeField.GetValue(null);
        try
        {
            var state = new ChaosRunState(new ChaosRunConfig()) { Focus = 15 };
            stateField.SetValue(null, state);
            freezeField.SetValue(null, 0.0);
            var bound = new AmbientBubble { Spec = new EffectBubbleSpec { IsLive = true, IsBoundHalf = true } };
            var plain = new AmbientBubble { Spec = new EffectBubbleSpec { IsLive = true } };
            bool Can(AmbientBubble b) => (bool)gate.Invoke(null, new object[] { b })!;
            Assert.True(Can(bound));
            Assert.False(Can(plain));
            state.Focus = 14;
            Assert.False(Can(bound));
            freezeField.SetValue(null, 1.0);
            Assert.True(Can(bound));
            Assert.True(Can(plain));
        }
        finally
        {
            stateField.SetValue(null, oldState);
            freezeField.SetValue(null, oldFreeze);
        }
    });
}
