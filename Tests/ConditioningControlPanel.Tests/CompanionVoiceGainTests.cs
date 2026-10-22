using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.1.8 "Voice level": the companion voice has its own slider, and a player who never touches
/// it must hear exactly what the app played before.
/// </summary>
public class CompanionVoiceGainTests
{
    public static TheoryData<float> Trims => new()
    {
        CompanionVoiceGain.IdleTrim, CompanionVoiceGain.BarkTrim, CompanionVoiceGain.EventTrim,
        CompanionVoiceGain.NoteTrim, CompanionVoiceGain.PreviewTrim, CompanionVoiceGain.FullTrim,
    };

    [Fact]
    public void TrimsAreTheOnesTheOldCodeUsed()
    {
        Assert.Equal(0.8f, CompanionVoiceGain.IdleTrim);
        Assert.Equal(0.85f, CompanionVoiceGain.BarkTrim);
        Assert.Equal(0.56f, CompanionVoiceGain.EventTrim);
        Assert.Equal(0.9f, CompanionVoiceGain.NoteTrim);
        Assert.Equal(0.7f, CompanionVoiceGain.PreviewTrim);
        Assert.Equal(1f, CompanionVoiceGain.FullTrim);
    }

    [Theory]
    [MemberData(nameof(Trims))]
    public void DefaultLevel_ReproducesTheOldLoudness_BitForBit(float trim)
    {
        for (var master = 0; master <= 100; master++)
        {
            // The line every voice path carried before the slider.
            var masterVolume = master / 100f;
            var old = (float)Math.Pow(masterVolume, 1.5) * trim;

            var now = CompanionVoiceGain.Compute(master, CompanionVoiceGain.DefaultLevel, trim);

            Assert.Equal(BitConverter.SingleToInt32Bits(old), BitConverter.SingleToInt32Bits(now));
        }
    }

    [Fact]
    public void ANewSettingsFile_StartsOnTheDefaultLevel()
    {
        Assert.Equal(CompanionVoiceGain.DefaultLevel, new AppSettings().CompanionVoiceLevel);
        Assert.Equal(1f, CompanionVoiceGain.LevelFactor(CompanionVoiceGain.DefaultLevel));
    }

    [Fact]
    public void Setting_ClampsToTheSliderRange()
    {
        var s = new AppSettings { CompanionVoiceLevel = 250 };
        Assert.Equal(100, s.CompanionVoiceLevel);
        s.CompanionVoiceLevel = -5;
        Assert.Equal(0, s.CompanionVoiceLevel);
    }

    [Fact]
    public void ZeroOnEitherSlider_IsSilence()
    {
        Assert.Equal(0f, CompanionVoiceGain.Compute(100, 0, CompanionVoiceGain.IdleTrim));
        Assert.Equal(0f, CompanionVoiceGain.Compute(0, 100, CompanionVoiceGain.IdleTrim));
        Assert.Equal(0f, CompanionVoiceGain.ApplyLevel(0.85f, 0));
    }

    [Fact]
    public void LevelRisesAllTheWay_AndNeverPassesFullScale()
    {
        foreach (var master in new[] { 10, 50, 100 })
        {
            var last = -1f;
            for (var level = 0; level <= 100; level++)
            {
                var g = CompanionVoiceGain.Compute(master, level, CompanionVoiceGain.BarkTrim);
                Assert.InRange(g, 0f, 1f);
                Assert.True(g >= last, $"master {master}, level {level}: {g} < {last}");
                last = g;
            }
        }
    }

    [Fact]
    public void TopOfTheSlider_LiftsTheVoice_WhenMasterLeavesRoom()
    {
        // The report: the voice is drowned by the engine. At half master the top of the slider
        // doubles the voice; halfway down it halves it.
        var normal = CompanionVoiceGain.Compute(50, CompanionVoiceGain.DefaultLevel, CompanionVoiceGain.IdleTrim);
        Assert.Equal(normal * 2f, CompanionVoiceGain.Compute(50, 100, CompanionVoiceGain.IdleTrim), 5);
        Assert.Equal(normal / 2f, CompanionVoiceGain.Compute(50, 40, CompanionVoiceGain.IdleTrim), 5);
    }

    [Fact]
    public void ApplyLevel_AtTheDefault_LeavesAnyGainAlone()
    {
        foreach (var g in new[] { 0f, 0.05f * 0.85f, 0.3f, 0.85f, 1f })
            Assert.Equal(g, CompanionVoiceGain.ApplyLevel(g, CompanionVoiceGain.DefaultLevel));
    }

    // ---- no voice path keeps the old math --------------------------------------------------

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "AvatarTube")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "ConditioningControlPanel" }.Concat(parts).ToArray()));
    }

    private static string Body(string src, string signature)
    {
        var at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature + " not found");
        var next = src.IndexOf("\n        }", at, StringComparison.Ordinal);
        Assert.True(next > at);
        return src.Substring(at, next - at);
    }

    [Theory]
    [InlineData("private void PlayVoiceLineAudio(string filePath)")]
    [InlineData("private void PlayBarkVoice(string audioPath)")]
    [InlineData("private void PlayPhraseAudio(string audioPath)")]
    [InlineData("private void TriggerBambiCumAndCollapse()")]
    public void EverySpokenPathInTheTube_AsksTheGainRule(string signature)
    {
        var body = Body(Source("AvatarTube", "AvatarTubeWindow.Speech.cs"), signature);
        Assert.Contains("CompanionVoiceGain.Current(", body);
        Assert.DoesNotContain("Math.Pow(masterVolume", body);
    }

    [Fact]
    public void NoteClip_Preview_AndVoicedFlashes_AskTheGainRule()
    {
        var speech = Source("AvatarTube", "AvatarTubeWindow.Speech.cs");
        var note = speech.Substring(speech.IndexOf("\"note-clip\"", StringComparison.Ordinal) - 900, 900);
        Assert.Contains("CompanionVoiceGain.Current(", note);

        Assert.Contains("CompanionVoiceGain.Current(",
            Body(Source("Services", "Companion", "CompanionPhraseService.cs"), "private void PlayAudioFile(string path)"));
        Assert.Contains("CompanionVoiceGain.ApplyLevel(",
            Body(Source("Services", "Flash", "FlashService.cs"), "private double PlaySound(string path, int volumePercent)"));
    }

    [Fact]
    public void TheSliderSitsInTheOpenOnTheCompanionPage_NextToTheVoiceMute()
    {
        var cell = Source("Views", "Controls", "Companion", "Runtime", "WorkshopBehaviorCell.xaml");
        Assert.Contains("x:Name=\"SliderVoiceLevelCompanion\"", cell);
        Assert.Contains("Minimum=\"0\" Maximum=\"100\"", cell);
        Assert.Contains("{loc:Str label_companion_voice_level}", cell);
        Assert.DoesNotContain("MoreFold", cell);
        Assert.DoesNotContain("<Expander", cell);
    }

    [Fact]
    public void BothLocKeys_AreInEveryLanguageFile_AndTranslated()
    {
        var files = Directory.GetFiles(SourceRoots.LanguagesDirectory, "*.json");
        Assert.True(files.Length >= 9);

        string Line(string file, string key) =>
            File.ReadAllLines(file).Single(l => l.TrimStart().StartsWith("\"" + key + "\":", StringComparison.Ordinal)).Trim();

        var en = files.Single(f => Path.GetFileName(f) == "en.json");
        foreach (var key in new[] { "label_companion_voice_level", "label_companion_voice_level_hint" })
        {
            var english = Line(en, key);
            foreach (var f in files.Where(f => f != en))
                Assert.NotEqual(english, Line(f, key));
        }
    }
}
