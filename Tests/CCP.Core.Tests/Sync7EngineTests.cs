using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class Sync7EngineTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(72, false)]
    [InlineData(73, false)]
    [InlineData(-1, false)]
    public void Grace_uses_only_a_fresh_confirmation(int hours, bool fresh)
    {
        var settings = new AppSettings();
        var at = Now.AddHours(-hours);
        Assert.Equal(fresh, PatreonActiveRule.Restamp(settings, new(true, at, 2), Now));
        Assert.Equal(fresh ? at.AddDays(14) : (DateTime?)null, settings.PatreonPremiumValidUntil);
        Assert.Equal(settings.PatreonPremiumValidUntil, settings.PatreonLabValidUntil);
    }

    [Fact]
    public void Grace_never_shortens_and_basic_does_not_grant_lab()
    {
        var settings = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(20) };
        Assert.False(PatreonActiveRule.Restamp(settings, new(true, Now, 1), Now));
        Assert.Equal(Now.AddDays(20), settings.PatreonPremiumValidUntil);
        Assert.Null(settings.PatreonLabValidUntil);
        Assert.False(PatreonActiveRule.Restamp(settings, new(false, Now, 2), Now));
        Assert.False(PatreonActiveRule.Restamp(settings, new(true, Now, 0), Now));
        Assert.Null(PatreonActiveRule.Parse(JObject.Parse("{}")));
        Assert.False(PatreonActiveRule.IsFresh(PatreonActiveRule.Parse(JObject.Parse(
            "{'patreon_is_active':true,'patreon_tier':2,'patreon_confirmed_at':'bad'}")), Now));
    }

    [Fact]
    public void Typed_profile_renews_even_when_stored_tier_equals_server()
    {
        var at = DateTime.UtcNow.AddHours(-1);
        var settings = new AppSettings { PatreonTier = 2, PatreonPremiumValidUntil = at.AddDays(-1) };
        PatreonActiveRule.RestampProfile(settings, new V2AuthService.V2User
        {
            PatreonTier = 2, PatreonIsActive = true, PatreonConfirmedAtRaw = new JValue(at.ToString("O"))
        }, DateTime.UtcNow);
        Assert.Equal(at.AddDays(14), settings.PatreonPremiumValidUntil);
        Assert.Equal(at.AddDays(14), settings.PatreonLabValidUntil);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Picture_bubble_keeps_configured_burst(bool ambient)
    {
        var settings = new AppSettings { SimultaneousImages = 12, FlashDuration = 7, ImageScale = 150 };
        var payload = new FlashPayload { Ambient = ambient, UseFlashSettings = !ambient, Strength = 0 };
        Assert.Equal((12, 7000, 150), payload.ResolveBurst(settings));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Staying_flashes_reach_forty_independent_of_slider(int slider)
    {
        Assert.Equal(40, FlashStayRule.OnScreen(FlashStayRule.Cap(30, true, true), slider, true));
        Assert.Equal(slider, FlashStayRule.OnScreen(30, slider));
    }
    [Theory]
    [InlineData("127460")]
    [InlineData("113157")]
    [InlineData("90388")]
    [InlineData("101540")]
    [InlineData("116026")]
    [InlineData("132494")]
    [InlineData("132500")]
    [InlineData("132233")]
    [InlineData("79880")]
    [InlineData("118644")]
    [InlineData("87892")]
    [InlineData("32786")]
    public void Shipped_video_pools_exclude_dead_and_vip_links(string id)
    {
        var mods = new[] { BuiltInMods.CCPDefault, BuiltInMods.BambiSleep, BuiltInMods.SissyHypno,
            BuiltInMods.Dronification, BuiltInMods.Locked, BuiltInMods.InfectionControl };
        var urls = mods.SelectMany(m => m.Browser?.DefaultVideoLinks?.Values
            ?? Enumerable.Empty<string>()).Concat(HypnotubeDefaultLinks.KnownVideoTitles.Values);
        Assert.DoesNotContain(urls, url => url.EndsWith("-" + id + ".html", StringComparison.Ordinal));
        Assert.True(BuiltInMods.Locked.Browser!.DefaultVideoLinks!.Count >= 10);
    }

}
