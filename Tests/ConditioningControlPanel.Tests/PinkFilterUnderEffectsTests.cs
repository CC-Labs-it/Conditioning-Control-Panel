using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1546: the pink filter can sit under spirals, subliminals, bubbles and bouncing
/// text instead of washing them out. Off keeps today's order.</summary>
public class PinkFilterUnderEffectsTests
{
    [Fact]
    public void Off_by_default_so_nothing_changes_for_anyone_who_never_asks()
        => Assert.False(new AppSettings().PinkFilterUnderEffects);

    [Fact]
    public void Off_keeps_the_tint_over_every_session_effect()
    {
        int z = PinkTintLayer.ZFor(false);
        Assert.Equal(CompositorLayers.PinkTint, z);
        Assert.True(z > CompositorLayers.Spiral && z > CompositorLayers.Subliminal && z > CompositorLayers.BouncingText);
    }

    [Fact]
    public void On_puts_the_tint_under_spirals_subliminals_bubbles_and_text_but_still_over_video_and_flashes()
    {
        int z = PinkTintLayer.ZFor(true);
        Assert.True(z < CompositorLayers.Subliminal);
        Assert.True(z < CompositorLayers.Bubbles);
        Assert.True(z < CompositorLayers.BouncingText);
        Assert.True(z < CompositorLayers.Spiral);
        Assert.True(z > CompositorLayers.Flash);
        Assert.True(z > CompositorLayers.LockCard);
        Assert.True(z > CompositorLayers.MandatoryVideo);
    }

    [Fact]
    public void The_avalonia_values_are_not_renumbered()
    {
        Assert.Equal(40, CompositorLayers.Subliminal);
        Assert.Equal(60, CompositorLayers.Spiral);
        Assert.Equal(70, CompositorLayers.PinkTint);
    }

    [Fact]
    public void A_show_and_the_toggle_both_resort_the_engine()
    {
        var layer = SourceRoots.ReadProductFile("Services", "Compositor", "PinkTintLayer.cs");
        int show = layer.IndexOf("public void Show(", StringComparison.Ordinal);
        int set = layer.IndexOf("public void Set(", StringComparison.Ordinal);
        Assert.True(show >= 0 && set > show);
        Assert.Contains("SyncOrder();", layer.Substring(show, set - show));
        Assert.Contains("_resort?.Invoke();", layer);   // the layer is in Core; the WPF engine re-sort is passed in
        Assert.Contains("App.Compositor!.ResortLayers",
            SourceRoots.ReadProductFile("Services", "Notifications", "OverlayService.cs"));

        var page = SourceRoots.ReadProductFile("Features", "PinkFilterFeatureControl.xaml.cs");
        Assert.Contains("RefreshPinkFilterOrder()", page);
    }

    [Theory]
    [InlineData("en")] [InlineData("it")] [InlineData("de")] [InlineData("es")] [InlineData("fr")]
    [InlineData("pt-BR")] [InlineData("ru")] [InlineData("ja")] [InlineData("ko")] [InlineData("zh-CN")]
    public void The_switch_is_worded_in_every_language(string lang)
    {
        var json = File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json"));
        Assert.Contains("\"setting_pink_filter_under_effects\":", json);
        Assert.Contains("\"tooltip_pink_filter_under_effects\":", json);
    }
}
