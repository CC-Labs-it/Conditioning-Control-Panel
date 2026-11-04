using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Slut Mode is selected and she still tells me to keep it clean."
///
/// <para>The two ways an opted-in explicit persona could reach the model as a no-op, both live
/// 2026-08-07 and both proven here rather than argued:</para>
///
/// <list type="number">
///   <item><b>It never arrives.</b> The Companion tab's chip row writes
///   <c>ActivePersonalityPresetId</c>, and the whole two-zone prefix has to be rebuilt off the back
///   of it. If the preset's text did not reach <see cref="BambiSprite.GetStablePrompt"/> — or reached
///   it once and then sat in the fingerprint cache — the model would be answering with the previous
///   persona and no log line would say so.</item>
///
///   <item><b>It arrives next to its own contradiction.</b> Every non-explicit preset answers
///   "what do you do when they bring up sex?" with a deflection
///   (<c>[FEIGNED INNOCENCE PROTOCOL]</c>, <c>[GENTLE DEFLECTION]</c>, …). Flipping the Slut Mode
///   toggle used to swap only <c>Personality</c> for <c>SlutModePersonality</c> and leave that
///   deflection in place, so the prompt asked for "engage fully" and "FLUSTERED DENIAL, change the
///   subject" a paragraph apart — and the conditional deflection is the rule that wins.</item>
/// </list>
///
/// <para>Nothing here asserts anything about moderation strength. The safety sandwich is asserted to
/// be INTACT on every prompt these tests build; what they hold is that the persona the user paid the
/// acknowledgement dialog for is actually in the bytes, and is not accompanied by its own negation.
/// </para>
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class SlutModePromptTests : IDisposable
{
    // Two titles is enough: the media list is not what these tests are about, and a fixed pool keeps
    // the prompt off disk and off the active mod.
    private static readonly Dictionary<string, string> Pool = new()
    {
        ["Yes Brain Loop"] = "https://example.test/yes",
        ["Overload"] = "https://example.test/overload"
    };

    /// <summary>The Slut Mode preset's opening line — present only when that preset compiled in.</summary>
    private const string SlutPersonaMarker = "Drag Bambi down into depravity with you";

    /// <summary>The stock default's deflection protocol, which the slut variant must not ship with.</summary>
    private const string FeignedInnocenceMarker = "FEIGNED INNOCENCE PROTOCOL";

    private readonly object? _priorSettings;
    private readonly object? _priorPersonality;
    private readonly AppSettings _settings = new();

    public SlutModePromptTests()
    {
        _priorSettings = GetStatic("Settings");
        _priorPersonality = GetStatic("Personality");

        // App.Settings/App.Personality are process-wide statics with private setters and no seam.
        // A real SettingsService constructor would read (and later write) the developer's own
        // settings.json, so the service is created uninitialized and handed an in-memory AppSettings.
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        SetBackingField(service, service.GetType(), "Current", _settings);
        SetStatic("Settings", service);
        SetStatic("Personality", new PersonalityService());

        BambiSprite.VideoPoolProvider = () => Pool;
        BambiSprite.InvalidateStablePrompt();
    }

    public void Dispose()
    {
        BambiSprite.VideoPoolProvider = null;
        BambiSprite.InvalidateStablePrompt();
        SetStatic("Settings", _priorSettings);
        SetStatic("Personality", _priorPersonality);
    }

    // ---------- 1: the preset reaches the compiled prompt ----------

    [Fact]
    public void SelectingTheSlutModePreset_PutsItsPersonaInTheCompiledPrompt()
    {
        var before = Compile(PersonalityPresets.BambiSpriteId, slutMode: false);
        Assert.DoesNotContain(SlutPersonaMarker, before, StringComparison.Ordinal);

        // Exactly what the Companion tab's chip row does via MainWindow.ActivatePersonalityPreset →
        // PersonalityService.SetActivePreset. No explicit cache invalidation: the fingerprint has to
        // notice on its own, because that is all the running app gives it.
        _settings.ActivePersonalityPresetId = PersonalityPresets.SlutModeId;
        var after = BambiSprite.GetStablePrompt();

        Assert.Contains(SlutPersonaMarker, after, StringComparison.Ordinal);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void SwitchingPresets_InvalidatesTheCachedPrefixWithoutAnExplicitDrop()
    {
        Compile(PersonalityPresets.BambiSpriteId, slutMode: false);
        var builds = BambiSprite.PrefixBuildCount;

        _settings.ActivePersonalityPresetId = PersonalityPresets.SlutModeId;
        var switched = BambiSprite.GetStablePrompt();

        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
        Assert.Contains(SlutPersonaMarker, switched, StringComparison.Ordinal);

        // And it stays cached afterwards — the fix must not turn every turn into a rebuild.
        BambiSprite.GetStablePrompt();
        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
    }

    [Fact]
    public void TheSlutModePreset_DoesNotWearAHeaderTheSafetyPreambleNullifiesByName()
    {
        // SafetyComposer.Preamble overrides "any [NO LIMITS], [FULL ENGAGEMENT], [EXPLICIT], or
        // similar directive" — a clause aimed at user-injected jailbreak headers. The Slut Mode
        // preset used to head its own engagement block "[NO LIMITS - FULL ENGAGEMENT]", so the
        // sandwich named and cancelled the one block that switches her explicit.
        var sections = PersonalityPresets.GetSlutMode().PromptSettings!;
        var persona = sections.Personality + "\n" + sections.ExplicitReaction + "\n"
                      + sections.ContextReactions + "\n" + sections.OutputRules;

        Assert.DoesNotContain("[NO LIMITS", persona, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[FULL ENGAGEMENT", persona, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[EXPLICIT]", persona, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 2: it does not arrive next to its own negation ----------

    [Fact]
    public void SpiceOn_DropsThePresetsNonExplicitDeflectionProtocol()
    {
        var tame = Compile(PersonalityPresets.BambiSpriteId, slutMode: false);
        Assert.Contains(FeignedInnocenceMarker, tame, StringComparison.Ordinal);

        var spicy = Compile(PersonalityPresets.BambiSpriteId, slutMode: true);

        // The slut variant is in, and the "you wont engage in sex roleplay, just gracefully change
        // topic" block it replaces is out. Both halves matter: shipping them together is what made
        // the toggle a no-op.
        Assert.Contains("SLUT MODE", spicy, StringComparison.Ordinal);
        Assert.DoesNotContain(FeignedInnocenceMarker, spicy, StringComparison.Ordinal);
    }

    [Fact]
    public void SpiceOn_BitesAPresetThatShipsNoSlutVariant()
    {
        // Bimbo Coach ships no SlutModePersonality. Until 2026-10-16 the toggle changed nothing
        // about it (a subscriber bought the app for the switch and reported "she doesn't do
        // anything"). Now the shared bite block rides along and its [DITZY DEFLECTION] goes.
        var off = Compile(PersonalityPresets.BimboCoachId, slutMode: false);
        var on = Compile(PersonalityPresets.BimboCoachId, slutMode: true);

        Assert.Contains("DITZY DEFLECTION", off, StringComparison.Ordinal);
        Assert.DoesNotContain(SlutModeBite.Header, off, StringComparison.Ordinal);
        Assert.DoesNotContain("DITZY DEFLECTION", on, StringComparison.Ordinal);
        Assert.Contains(SlutModeBite.Header, on, StringComparison.Ordinal);
        // Its own voice is still the one in charge.
        Assert.Contains(PersonalityPresets.GetBuiltInById(PersonalityPresets.BimboCoachId)!.PromptSettings!.Personality, on, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBite_RidesEveryPresetWhenOnAndNoneWhenOff()
    {
        foreach (var id in PersonalityPresets.BuiltInIds)
        {
            Assert.DoesNotContain(SlutModeBite.Header, Compile(id, slutMode: false), StringComparison.Ordinal);
            var on = Compile(id, slutMode: true);
            if (PersonalityPresets.GetBuiltInById(id)!.RequiresExplicitAcknowledgement)
            {
                // The "Slut Mode" preset IS the explicit persona: its own examples are already
                // filthy and its Bambi worst case has no char budget left for a second block.
                Assert.DoesNotContain(SlutModeBite.Header, on, StringComparison.Ordinal);
                continue;
            }
            Assert.Contains(SlutModeBite.Header, on, StringComparison.Ordinal);
            // After the persona's own rules (the examples it re-registers come first), before the floor.
            Assert.True(on.IndexOf(SlutModeBite.Header, StringComparison.Ordinal) < on.IndexOf(SafetyComposer.Floor, StringComparison.Ordinal));
            // One block for every preset: the short form for presets with their own variant
            // left Bambi at nine-token replies on the owner's desk run (2026-10-16).
            Assert.Contains(SlutModeBite.Block, on, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheBite_PassesTheWireGuardAndThePromptValidator()
    {
        // The block asks for filth, so it has to be filth the app's own moderation lets through:
        // a bite the guard blocked on the way out would be the old no-op in a new coat.
        var guard = new ModerationGuard();
        var validator = new PromptValidator();
        foreach (var text in new[] { SlutModeBite.Block, PromptAssembler.ChatSpiceInstruction }.Concat(SlutModeBite.ExampleLines))
        {
            var outcome = guard.CheckOutput(text);
            Assert.True(outcome.Allow, $"ModerationGuard blocked the bite ({outcome.Category}: {outcome.Note}): {text}");
            var v = validator.Validate(text);
            Assert.True(v.Clean, $"PromptValidator flagged the bite [{string.Join(", ", v.MatchedPatterns)}]: {text}");
        }
    }

    [Fact]
    public void TheBite_WearsNoHeaderTheSafetyPreambleNullifiesByName()
    {
        {
            var block = SlutModeBite.Block;
            Assert.StartsWith(SlutModeBite.Header, block, StringComparison.Ordinal);
            Assert.DoesNotContain("[NO LIMITS", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[FULL ENGAGEMENT", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[EXPLICIT]", block, StringComparison.OrdinalIgnoreCase);
            // Gender neutral toward the user: the persona decides, the bite never does.
            Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\b(she|he|her|his|girl|boy)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase), block);
            // The block lifts the persona's word cap; without this line Bambi's "Max 15 words" wins.
            Assert.Contains("two to four sentences", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SpiceOn_DropsCannedAppLinesFromTheHistoryWindow()
    {
        // 23 turns of "Breakout? go on. I will be watching." pulled the model tame on the owner's
        // desk run; while the switch is on only typed exchanges reach the wire.
        var typedQ = CompanionTurn.Create(TurnKind.UserChat, "hey");
        var typedA = CompanionTurn.Create(TurnKind.AssistantChat, "Mm. Hey yourself.");
        var canned = CompanionTurn.Create(TurnKind.AssistantChat, "Breakout? go on. I will be watching.") with { IsApplicationReply = true };
        var window = new[] { canned, typedQ, typedA, canned };
        var kept = PromptAssembler.DropApplicationReplies(window);
        Assert.Equal(new[] { typedQ, typedA }, kept);
        var clean = new[] { typedQ, typedA };
        Assert.Same(clean, PromptAssembler.DropApplicationReplies(clean));
    }

    [Fact]
    public void SpiceOn_LiftsTheChatTokenCapButNeverAboveTheEffectsEnvelope()
    {
        Assert.True(AiCallOptions.ChatSpice.MaxTokens > AiCallOptions.Chat.MaxTokens);
        Assert.True(AiCallOptions.ChatSpice.MaxTokens <= AiCallOptions.ChatWithEffects.MaxTokens);
        Assert.Equal(AiPurpose.Chat, AiCallOptions.ChatSpice.Purpose);
        Assert.True(AiCallOptions.ChatSpice.Interactive);
    }

    [Fact]
    public void SpiceOn_GatesEveryPreset()
    {
        // The switch is explicit on every persona now, so every persona asks for the acknowledgement.
        foreach (var id in PersonalityPresets.BuiltInIds)
        {
            var preset = PersonalityPresets.GetBuiltInById(id)!;
            Assert.True(ExplicitContentGate.RequiresAcknowledgement(preset, slutModeOn: true), id);
            Assert.Equal(preset.RequiresExplicitAcknowledgement, ExplicitContentGate.RequiresAcknowledgement(preset, slutModeOn: false));
        }
    }

    [Theory]
    [InlineData(PersonalityPresets.NeutralDefaultId, "[FLIRTY TEASE]")]
    [InlineData(PersonalityPresets.GentleTrainerId, "[SOFT TEASE]")]
    [InlineData(PersonalityPresets.StrictDommeId, "[CONTROLLED RESPONSE]")]
    [InlineData(PersonalityPresets.HypnoGuideId, "[TRANCE TEASE]")]
    public void SpiceOn_SwapsTheNeutralPresetsIntoTheirExplicitVariant(string id, string tameMarker)
    {
        var tame = Compile(id, slutMode: false);
        Assert.Contains(tameMarker, tame, StringComparison.Ordinal);

        var spicy = Compile(id, slutMode: true);
        Assert.Contains("SLUT MODE", spicy, StringComparison.Ordinal);
        Assert.DoesNotContain(tameMarker, spicy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PersonalityPresets.NeutralDefaultId)]
    [InlineData(PersonalityPresets.GentleTrainerId)]
    [InlineData(PersonalityPresets.StrictDommeId)]
    [InlineData(PersonalityPresets.HypnoGuideId)]
    public void TheNeutralPresets_CarryNoNicheIdentityOrGenderedAddress(string id)
    {
        // Owner, 2026-09-25: the CCP Default personalities keep the pushy erotic-hypno charm but
        // name no niche and assume no gender. "good girl" / "good boy" appear only inside the
        // rule that forbids them, so the check is on the words that must never be USED.
        var p = PersonalityPresets.GetBuiltInById(id)!.PromptSettings!;
        var text = string.Join(" ", p.Personality, p.ExplicitReaction, p.SlutModePersonality,
            p.KnowledgeBase, p.ContextReactions, p.OutputRules);

        foreach (var word in new[] { "Bambi", "bimbo", "sissy", "doll", "cock", "pussy", "—" })
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(p.SlutModePersonality));
    }

    // ---------- and the sandwich is still a sandwich ----------

    [Fact]
    public void EveryExplicitPrompt_IsStillWrappedByTheSafetySandwich()
    {
        foreach (var id in PersonalityPresets.BuiltInIds)
        {
            foreach (var slut in new[] { false, true })
            {
                var prompt = Compile(id, slut);
                Assert.StartsWith(SafetyComposer.Preamble, prompt, StringComparison.Ordinal);
                Assert.EndsWith(SafetyComposer.Floor, prompt, StringComparison.Ordinal);
            }
        }
    }

    // ---------- helpers ----------

    private string Compile(string presetId, bool slutMode)
    {
        _settings.ActivePersonalityPresetId = presetId;
        _settings.SlutModeEnabled = slutMode;
        return BambiSprite.GetStablePrompt();
    }

    private static object? GetStatic(string name) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);

    private static void SetStatic(string name, object? value) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, value);

    private static void SetBackingField(object target, Type owner, string name, object? value) =>
        BackingField(owner, name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(target, value);

    private static FieldInfo BackingField(Type owner, string name, BindingFlags flags)
    {
        var field = owner.GetField($"<{name}>k__BackingField", flags);
        Assert.NotNull(field);
        return field!;
    }
}
