using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The pure Possession rules that moved from the WPF head into Core: the off-limits name rule
/// (moved from ConditioningControlPanel.Tests/PossessionOffLimitsTests.cs) and the active-mod read the
/// toast and rewrite pools now take through <see cref="CoreMods"/> instead of App.Mods.
/// </summary>
public class PossessionPureRulesTests
{
    [Theory]
    [InlineData("LockdownCardBorder")]
    [InlineData("BtnEmergencyExit")]
    [InlineData("TxtSecretExitPhrase")]
    [InlineData("lockdowngate")]            // case does not matter
    [InlineData("PanelEMERGENCY")]
    public void ReservedNames_are_off_limits(string name)
        => Assert.True(PossessionOffLimits.IsReservedName(name));

    [Theory]
    [InlineData("BtnStart")]
    [InlineData("CardFlashSettings")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_names_are_not(string? name)
        => Assert.False(PossessionOffLimits.IsReservedName(name));

    [Fact]
    public void Pools_follow_the_active_mod_from_CoreMods()
    {
        var prev = CoreMods.ActiveModIdProvider;
        try
        {
            CoreMods.ActiveModIdProvider = () => ToastLines.SissyMod;
            Assert.Equal(ToastLines.SissyMod, RewritePools.ActiveModId);
            Assert.Contains(ToastLines.Pick(new Random(1)), ToastLines.PoolFor(ToastLines.SissyMod));

            CoreMods.ActiveModIdProvider = null;
            Assert.Equal(ConditioningControlPanel.Models.BuiltInMods.CCPDefaultId, RewritePools.ActiveModId);
            Assert.Contains(ToastLines.Pick(new Random(1)), ToastLines.PoolFor(null));
        }
        finally { CoreMods.ActiveModIdProvider = prev; }
    }

    // ---- Warden (moved from the WPF Warden) ------------------------------------------------------

    [Fact]
    public void Warden_appears_only_after_the_90s_cooldown_and_stares_after_60s()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(WardenRules.CanAppear(t0, DateTime.MinValue));              // never appeared
        Assert.False(WardenRules.CanAppear(t0.AddSeconds(89), t0));
        Assert.True(WardenRules.CanAppear(t0.AddSeconds(90), t0));
        Assert.False(WardenRules.CanStare(t0.AddSeconds(59), t0));
        Assert.True(WardenRules.CanStare(t0.AddSeconds(60), t0));
    }

    [Fact]
    public void Knock_shoves_12px_toward_the_target_and_stare_centres_the_tube()
    {
        Assert.Equal(12, WardenRules.KnockShove(targetCentreX: 500, tubeCentreX: 100));
        Assert.Equal(-12, WardenRules.KnockShove(targetCentreX: 100, tubeCentreX: 500));
        Assert.Equal(-12, WardenRules.KnockShove(300, 300));                    // dead level: away (WPF '>')
        Assert.Equal((450.0, 300.0), WardenRules.StareSpot(100, 50, 1000, 800, 300, 300));
    }

    [Theory]
    [InlineData(0, "back soon. you are not.", null)]
    [InlineData(1, "stepped out. you did not.", "possession_tube_note_2")]     // lookup echoed the key
    [InlineData(2, "gone a moment. you have longer.", "  ")]
    [InlineData(0, "bald zurück. du nicht.", "bald zurück. du nicht.")]       // a real translation wins
    public void Tube_note_falls_back_to_english_when_the_language_misses_it(int i, string expected, string? localized)
        => Assert.Equal(expected, WardenRules.NoteText(i, localized));

    [Fact]
    public void Stare_fallback_line_names_the_escape_kind()
    {
        Assert.Equal("no. we are not stopping. look at me~", WardenRules.FallbackStareLine(EscapeKinds.Stop));
        Assert.Equal("still trying the door? i can see you doing it~", WardenRules.FallbackStareLine(EscapeKinds.Close));
        Assert.Equal("i am watching you. i have been the whole time~", WardenRules.FallbackStareLine("r4"));
        Assert.Equal("i am watching you. i have been the whole time~", WardenRules.FallbackStareLine(null));
    }

    // ---- Reactive adapter (moved from the WPF PossessionEvents) -----------------------------------

    [Theory]
    [InlineData("LockdownPossessionIntensity", true)]
    [InlineData("LockdownPhotosafe", true)]
    [InlineData("GentleVisuals", true)]                                      // main 46582ed13
    [InlineData("LockdownTripwiresEnabled", true)]
    [InlineData("LockdownWardenEnabled", true)]
    [InlineData("lockdownPossessionEnabled", false)]                            // ordinal, as WPF
    [InlineData("FlashFrequency", false)]
    [InlineData(null, false)]
    public void Only_the_haunts_own_settings_are_exempt_from_the_typo(string? name, bool exempt)
        => Assert.Equal(exempt, PossessionReactiveRules.IsConsentSetting(name));

    [Theory]
    [InlineData("BtnStartSession", null, null, true)]
    [InlineData("BtnGo", "STOP", null, true)]                                   // caption, any case
    [InlineData("BtnGo", "Go", "the Start button", true)]                       // possession tag
    [InlineData("BtnSave", "Save", "", false)]
    [InlineData(null, null, null, false)]
    public void Start_stop_is_read_from_name_caption_or_tag(string? name, string? caption, string? tag, bool expected)
        => Assert.Equal(expected, PossessionReactiveRules.LooksLikeStartStop(name, caption, tag));
}
