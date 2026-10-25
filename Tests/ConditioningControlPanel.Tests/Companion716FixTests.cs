using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.AvatarTubeLayout;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 7.1.6 companion lane: the prompt editor opened on a prompt the companion had stopped
/// using (#1417), the studio could not reach the screen awareness reactions (#1404), and a mod's
/// tube art blocked the mouse beside the detached companion (#1400).
/// </summary>
public class Companion716FixTests
{
    // ---------------------------------------------------------------- #1417: the editor's boxes

    [Fact]
    public void Editor_ShowsTheRunningPersona_WhenTheCustomPromptIsOff()
    {
        // A preset was picked after an old hand edit: the old text is still stored, off the wire.
        Assert.Equal("circe now", ActivePersonaPromptText.EditorField(false, "my old custom prompt", "circe now"));
    }

    [Fact]
    public void Editor_ShowsTheStoredText_WhileTheCustomPromptIsOn()
    {
        Assert.Equal("my custom prompt", ActivePersonaPromptText.EditorField(true, "my custom prompt", "circe now"));
        // An untouched box is stored blank and follows the persona.
        Assert.Equal("circe now", ActivePersonaPromptText.EditorField(true, "", "circe now"));
        Assert.Equal("", ActivePersonaPromptText.EditorField(false, null, null));
    }

    [Fact]
    public void EditorDialog_FillsAndSavesThroughTheRule()
    {
        var src = Source("ConditioningControlPanel", "Dialogs", "CompanionPromptEditorDialog.xaml.cs");
        Assert.Contains("TxtContextReactions.Text = Shown(on, settings.ContextReactions", src);
        Assert.Contains("ActivePersonaPromptText.EditorField(", src);
        // A look with the custom prompt off must not rewrite the stored custom text.
        Assert.Contains("if (_customWasOn || settings.UseCustomPrompt)", src);
    }

    // ------------------------------------------- #1404 / #1417: one edited box keeps the rest

    [Fact]
    public void Wire_UntouchedBoxesFollowThePersona()
    {
        var persona = new CompanionPromptSettings
        {
            Personality = "persona voice", ExplicitReaction = "persona bounds",
            SlutModePersonality = "persona spicy", KnowledgeBase = "persona kb",
            ContextReactions = "persona reactions", OutputRules = "persona rules"
        };
        var custom = new CompanionPromptSettings { UseCustomPrompt = true, ContextReactions = "my reactions" };

        var wire = ActivePersonaPromptText.WireFields(custom, persona)!;

        Assert.Equal("my reactions", wire.ContextReactions);
        Assert.Equal("persona voice", wire.Personality);
        Assert.Equal("persona bounds", wire.ExplicitReaction);
        Assert.Equal("persona spicy", wire.SlutModePersonality);
        Assert.Equal("persona kb", wire.KnowledgeBase);
        Assert.Equal("persona rules", wire.OutputRules);
        Assert.True(wire.UseCustomPrompt);
        // Nothing is written back into the user's settings or the preset.
        Assert.Equal("", custom.Personality ?? "");
        Assert.Equal("persona reactions", persona.ContextReactions);
    }

    [Fact]
    public void Wire_ARewrittenVoiceNeverBorrowsThePersonasSpicyVoice()
    {
        var persona = new CompanionPromptSettings { Personality = "persona voice", SlutModePersonality = "persona spicy" };
        var custom = new CompanionPromptSettings { Personality = "my voice" };

        var wire = ActivePersonaPromptText.WireFields(custom, persona)!;

        Assert.Equal("my voice", wire.Personality);
        Assert.True(string.IsNullOrEmpty(wire.SlutModePersonality));
    }

    [Fact]
    public void Wire_NoPersonaLeavesTheCustomPromptAlone()
    {
        var custom = new CompanionPromptSettings { Personality = "mine" };
        Assert.Same(custom, ActivePersonaPromptText.WireFields(custom, null));
        Assert.Null(ActivePersonaPromptText.WireFields(null, new CompanionPromptSettings()));
    }

    [Fact]
    public void PromptMux_SendsTheFilledCustomPrompt_AndHashesTheSameText()
    {
        var src = SourceRoots.ReadProductFile("Services", "BambiSprite.cs");   // moved to CCP.Core: settings via CoreSettings
        Assert.Contains("PromptSettings = CustomPromptOnWire(CoreSettings.Service?.Current)", src);
        Assert.Contains("Sections(useCustom ? CustomPromptOnWire(s)", src);
        // A community prompt is its author's whole text: never filled.
        Assert.Contains("!string.IsNullOrEmpty(settings!.ActiveCommunityPromptId)) return custom;", src);
    }

    // ------------------------------------------------------------------- #1404: the studio

    [Fact]
    public void Studio_EditsTheScreenAwarenessReactions()
    {
        var src = Source("ConditioningControlPanel", "Services", "Companion", "PersonalityStudio.cs");
        Assert.Contains("Loc.Get(\"setting_screen_awareness_reactions\"), draft.PromptSettings.ContextReactions", src);
        Assert.Contains("draft.PromptSettings.ContextReactions = awareness.Text.Trim();", src);
    }

    [Fact]
    public void Studio_ABoxIsNeverCappedBelowItsOwnText()
    {
        Assert.Equal(3600, PersonalityStudio.FieldCap(3600, "short"));
        var longText = new string('x', 5000);
        Assert.Equal(5000 + PersonalityStudio.FieldHeadroom, PersonalityStudio.FieldCap(3600, longText));
        Assert.Equal(700, PersonalityStudio.FieldCap(700, null));
    }

    // ------------------------------------------------------------------- #1400: tube art haze

    [Fact]
    public void TubeHaze_FaintPixelsGoToZero_VisibleOnesStay()
    {
        // Premultiplied BGRA: alpha 0, 1, 6, 8 (the floor), 9, 255.
        var px = new byte[]
        {
            0, 0, 0, 0,
            1, 1, 1, 1,
            3, 4, 5, 6,
            8, 8, 8, 8,
            9, 9, 9, 9,
            10, 20, 30, 255
        };

        Assert.Equal(3, TubeArtAlpha.Clean(px));

        Assert.Equal(new byte[]
        {
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            9, 9, 9, 9,
            10, 20, 30, 255
        }, px);
    }

    [Fact]
    public void TubeHaze_ScrubClearsAHazyColumn_AndLeavesCleanArtAsItIs()
    {
        // 4 x 2, straight BGRA: a solid tube pixel on the left, alpha 5 haze to the right of it.
        var hazy = new byte[]
        {
            200, 100, 50, 255,   255, 255, 255, 5,   255, 255, 255, 5,   255, 255, 255, 5,
            200, 100, 50, 255,   0, 0, 0, 0,         255, 255, 255, 3,   255, 255, 255, 1
        };
        var art = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null, hazy, 16);
        art.Freeze();

        var scrubbed = Assert.IsAssignableFrom<BitmapSource>(TubeArtAlpha.Scrub(art));
        Assert.NotSame(art, scrubbed);
        Assert.True(scrubbed.IsFrozen);
        var outPx = new byte[32];
        scrubbed.CopyPixels(outPx, 16, 0);
        for (int i = 0; i < 8; i++)
        {
            bool tube = i % 4 == 0;
            Assert.Equal(tube ? (byte)255 : (byte)0, outPx[i * 4 + 3]);
        }
        Assert.Equal(200, outPx[0]);
        // Asked again, the same answer comes back without another pass.
        Assert.Same(scrubbed, TubeArtAlpha.Scrub(art));

        var cleanPx = new byte[] { 1, 2, 3, 255, 0, 0, 0, 0 };
        var clean = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, cleanPx, 8);
        clean.Freeze();
        Assert.Same(clean, TubeArtAlpha.Scrub(clean));
        Assert.Null(TubeArtAlpha.Scrub(null));
    }

    [Fact]
    public void TubeWindow_PaintsOnlyScrubbedArt()
    {
        var src = Source("ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Windowing.cs");
        Assert.Contains("ImgTubeFrame.Source = AvatarTubeLayout.TubeArtAlpha.Scrub(art);", src);
        Assert.DoesNotContain("ImgTubeFrame.Source = art;", src);
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
