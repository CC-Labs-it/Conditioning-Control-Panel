using CCP.Avalonia.Testing;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Dtrh_boot_fallback_restores_original_intro_flag(int completed, bool initial) => AvaloniaTestDispatcher.Run(() =>
    {
        var original = ChaosMeta.State;
        var oldIntro = original.SeenIntroGuide;
        try
        {
            original.SeenIntroGuide = initial;
            Dtrh_BootError_reaches_the_classic_Hud(completed);
            Assert.Same(original, ChaosMeta.State);
            Assert.Equal(initial, original.SeenIntroGuide);
        }
        finally { original.SeenIntroGuide = oldIntro; }
    });
}
