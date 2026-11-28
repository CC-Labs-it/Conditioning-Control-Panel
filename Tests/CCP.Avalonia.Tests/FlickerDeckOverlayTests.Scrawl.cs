using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FlickerDeckOverlayTests
{
    /// <summary>Lane L5: the Scrawl word, not a hand-made sweep, reaches the stage and knocks the card it crosses.</summary>
    [Fact]
    public Task A_scrawl_word_crossing_a_card_knocks_it() => OnUi((s, _) =>
    {
        var saved = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests, BouncingTextOverlay.ScrawlOn,
            BouncingTextOverlay.RaiseAll, ScreenList.QueryOverride, s.MovingEffectsPop);
        var host = new Window();
        try
        {
            (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests) = (true, true);
            BouncingTextOverlay.ScrawlOn = () => true;
            BouncingTextOverlay.RaiseAll = () => { };
            ScreenList.QueryOverride = _ => new[] { ScrawlOverlayTests.Screen() };
            s.MovingEffectsPop = true;
            var (w, _) = Card();   // device px 400..800 x 300..600
            w.Deck!.BreakAt = 9;
            host.Show();
            BouncingTextOverlay.Start(host);
            var word = BouncingTextOverlay.Engine.Logos[0];
            (word.PosX, word.PosY, word.VelX, word.VelY) = (500, 400, 10, 10);
            Assert.False(w.Deck.Busy);
            BouncingTextOverlay.Advance(0.016);
            Assert.True(w.Deck.Busy);
        }
        finally
        {
            BouncingTextOverlay.Stop();
            host.Close();
            (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests, BouncingTextOverlay.ScrawlOn,
                BouncingTextOverlay.RaiseAll, ScreenList.QueryOverride, s.MovingEffectsPop) = saved;
        }
    });
}
