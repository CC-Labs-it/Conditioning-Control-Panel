using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary><see cref="EmiNames"/>, moved from WPF: a machine key becomes a lowercase display name
/// or null, never the raw key (LINES-SCHEMA 5.3).</summary>
public sealed class EmiNamesTests : IDisposable
{
    // The language is process-wide: restore it so later tests see what they set (PLAYBOOK P02).
    private readonly string _prevLanguage = LocalizationManager.Instance.CurrentLanguage;

    public EmiNamesTests() => LocalizationManager.Instance.Initialize("en");

    public void Dispose() => LocalizationManager.Instance.Initialize(_prevLanguage);

    private static string Lower(string key)
    {
        var v = Loc.Get(key);
        Assert.NotEqual(key, v); // a miss would make every comparison below vacuous
        return v.ToLowerInvariant();
    }

    [Fact]
    public void A_ring_door_is_named_by_its_card_label()
    {
        Assert.Equal(Lower("emi_desk_target_loom"), EmiNames.Feature("loom"));
        // hidden on the Avalonia ring today, still a door with a name
        Assert.Equal(Lower("emi_desk_target_arcademy"), EmiNames.Feature(" arcademy "));
    }

    [Fact]
    public void A_tab_key_resolves_through_its_door_then_the_nav_rail_name()
    {
        Assert.Equal(Lower("emi_desk_target_remote"), EmiNames.Feature("remotecontrol"));
        Assert.Equal(Lower("emi_desk_target_vault"), EmiNames.Feature("exclusives"));
        Assert.Equal(Lower("emi_desk_target_settings"), EmiNames.Feature("appsettings"));
        Assert.Equal(Lower("emi_desk_target_sessions"), EmiNames.Feature("presets"));
        Assert.Equal(Lower("emi_desk_target_videos"), EmiNames.Feature("video")); // a Studio rack key
        // As shipped in WPF: "settings" is also a ring door id, and the ring is asked first, so the
        // tab_dashboard row (MOMENTS 4.E "settings is HOME") is never reached. Pinned, not fixed here.
        Assert.Equal(Lower("emi_desk_target_settings"), EmiNames.Feature("settings"));
        Assert.Equal(Lower("tab_quests"), EmiNames.Feature("quests"));
        Assert.Equal(Lower("tab_lab"), EmiNames.Feature("play"));
    }

    [Fact]
    public void Unknown_keys_are_null_never_the_raw_key()
    {
        Assert.Null(EmiNames.Feature("nosuchdoor"));
        Assert.Null(EmiNames.Feature("  "));
        Assert.Null(EmiNames.Feature(null));
        Assert.Null(EmiNames.Achievement("nosuchachievement"));
    }

    [Fact]
    public void Video_achievement_and_xp_source_text_rules()
    {
        Assert.Equal("deep drop 02", EmiNames.VideoName(@"videos/Deep_Drop-02.mp4"));
        Assert.Equal("two words", EmiNames.VideoName("  Two   Words "));
        Assert.Equal(Lower("achievement_dumb_bimbo_name"), EmiNames.Achievement("dumb_bimbo"));
        Assert.Equal("session complete", EmiNames.XpSource("SessionComplete"));
        Assert.Null(EmiNames.XpSource("Other"));
        Assert.Null(EmiNames.XpSource(null));
    }
}
