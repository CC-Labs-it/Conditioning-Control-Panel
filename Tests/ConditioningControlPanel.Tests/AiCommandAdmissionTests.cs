using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.Commands;
using Xunit;
using Verdict = ConditioningControlPanel.Services.Commands.AiCommandAdmission.Verdict;

namespace ConditioningControlPanel.Tests;

/// <summary>Night hunt 2026-10-22: AI effect commands are untrusted model output and get no more
/// than the user's own switches allow (AiCommandAdmission).</summary>
public class AiCommandAdmissionTests
{
    private static readonly Func<string, bool> NothingBlocked = _ => false;

    private static AiCommandData Cmd(AICommandType type, IAiCommandData? data) => new() { Command = type, Data = data };

    private static readonly AiCommandData Flash = Cmd(AICommandType.flash_image, new FlashImage(3, 2, 100, 100));

    [Fact]
    public void An_allowed_command_runs()
        => Assert.Equal(Verdict.Run, AiCommandAdmission.Decide(Flash, true, true, false, NothingBlocked));

    [Fact]
    public void A_command_without_data_never_runs()
        => Assert.Equal(Verdict.NoData, AiCommandAdmission.Decide(Cmd(AICommandType.flash_image, null), true, true, false, NothingBlocked));

    [Fact]
    public void The_master_switch_refuses_first()
        => Assert.Equal(Verdict.MasterOff, AiCommandAdmission.Decide(Flash, false, true, true, _ => true));

    [Fact]
    public void A_switched_off_effect_is_refused()
        => Assert.Equal(Verdict.EffectOff, AiCommandAdmission.Decide(Flash, true, false, false, NothingBlocked));

    [Fact]
    public void Nothing_runs_inside_the_post_panic_quiet()
        => Assert.Equal(Verdict.PanicQuiet, AiCommandAdmission.Decide(Flash, true, true, true, NothingBlocked));

    public static IEnumerable<object[]> TextCommands() => new[]
    {
        new object[] { Cmd(AICommandType.subliminal, new Subliminal("BAD", 30)) },
        new object[] { Cmd(AICommandType.mantra_lockscreen, new MantraLockscreen("BAD", 2)) },
        new object[] { Cmd(AICommandType.bounce, new Bounce(new List<string> { "fine", "BAD" }, true)) },
        new object[] { Cmd(AICommandType.getbacktome, new GetBackToMe(5, "t", null, "BAD", false)) },
    };

    [Theory]
    [MemberData(nameof(TextCommands))]
    public void Text_inside_an_effect_meets_output_moderation(AiCommandData command)
    {
        Assert.Equal(Verdict.TextBlocked, AiCommandAdmission.Decide(command, true, true, false, t => t == "BAD"));
        Assert.Equal(Verdict.Run, AiCommandAdmission.Decide(command, true, true, false, NothingBlocked));
    }

    [Fact]
    public void Effects_without_text_show_nothing_to_moderate()
    {
        Assert.Empty(AiCommandAdmission.DisplayTexts(Flash));
        Assert.Empty(AiCommandAdmission.DisplayTexts(Cmd(AICommandType.bounce, new Bounce(null!, true))));
    }

    [Fact]
    public void A_follow_up_a_panic_overtook_never_fires()
    {
        Assert.True(AiCommandAdmission.FollowUpStands(4, 4));
        Assert.False(AiCommandAdmission.FollowUpStands(4, 5));
    }

    [Fact]
    public async Task Follow_up_depth_rides_into_the_reply_and_never_back_out()
    {
        Assert.Equal(0, AiCommandAdmission.FollowUpDepth);

        static async Task<int> Reply() { await Task.Yield(); return AiCommandAdmission.FollowUpDepth; }
        static async Task<int> FollowUp() { AiCommandAdmission.EnterFollowUp(1); await Task.Yield(); return await Reply(); }

        Assert.Equal(1, await FollowUp());
        Assert.Equal(0, AiCommandAdmission.FollowUpDepth);
    }

    [Fact]
    public void Bounce_words_are_trimmed_and_capped()
    {
        var many = new List<string> { "  ", "" };
        for (int i = 0; i < 50; i++) many.Add(" w" + i + " ");
        many.Add(new string('x', 5000));
        var words = AiCommandAdmission.BounceWords(many)!;
        Assert.Equal(AiCommandAdmission.MaxBounceWords, words.Count);
        Assert.Equal("w0", words[0]);
        Assert.All(words, w => Assert.InRange(w.Length, 1, AiCommandAdmission.MaxBounceWordChars));
        Assert.Equal(AiCommandAdmission.MaxBounceWordChars, AiCommandAdmission.BounceWords(new[] { new string('y', 999) })![0].Length);
    }

    [Fact]
    public void No_usable_bounce_words_leave_the_users_own()
    {
        Assert.Null(AiCommandAdmission.BounceWords(null));
        Assert.Null(AiCommandAdmission.BounceWords(new[] { " ", "" }));
    }

    [Theory]
    [InlineData(@"C:\ccp\assets", @"C:\ccp\assets\videos\a.mp4", true)]
    [InlineData(@"C:\ccp\assets\", @"C:\ccp\assets\videos\a.mp4", true)]
    [InlineData(@"C:\ccp\assets", @"C:\CCP\ASSETS\a.mp4", true)]
    [InlineData(@"C:\ccp\assets", @"C:\ccp\assets-old\a.mp4", false)]
    [InlineData(@"C:\ccp\assets", @"C:\ccp\assets2\a.mp4", false)]
    [InlineData(@"C:\ccp\assets", @"C:\ccp\assets", false)]
    [InlineData(@"C:\ccp\assets", @"\\server\share\a.mp4", false)]
    [InlineData(@"\\nas\media", @"\\nas\media-private\a.mp4", false)]
    [InlineData(@"\\nas\media", @"\\nas\media\a.mp4", true)]
    [InlineData("", @"C:\a.mp4", false)]
    public void A_named_file_must_sit_inside_the_assets_root(string root, string full, bool inside)
        => Assert.Equal(inside, AiCommandAdmission.IsUnderRoot(root, full));

    [Fact]
    public void A_file_the_user_switched_off_stays_off_for_the_ai()
    {
        var paths = new[] { "videos/off.mp4" };
        var folders = new[] { "videos/private" };
        Assert.True(AiCommandAdmission.IsSwitchedOff(@"videos\off.mp4", paths, folders));
        Assert.True(AiCommandAdmission.IsSwitchedOff(@"videos\private\deep\x.mp4", paths, folders));
        Assert.False(AiCommandAdmission.IsSwitchedOff(@"videos\on.mp4", paths, folders));
        Assert.False(AiCommandAdmission.IsSwitchedOff(@"videos\on.mp4", null, null));
    }

    // ---- wiring ----

    // The commands live in CCP.Core since the split; SourceRoots probes every product root.
    private static string Src(params string[] parts) => SourceRoots.ReadProductFile(parts);

    [Fact]
    public void Nested_follow_up_commands_pass_the_same_gate_and_cap()
    {
        var src = Src("Services", "Commands", "GetBackToMeCommand.cs");
        Assert.Contains("AiCommandService.Admits(subCommand)", src);
        Assert.Contains("AiCommandService.MaxCommandsPerResponse", src);
        Assert.Contains("AiCommandAdmission.FollowUpStands(", src);
        Assert.Contains("AiCommandAdmission.EnterFollowUp(_depth + 1);", src);
        Assert.Contains("aiGenerated: true", src);
    }

    [Fact]
    public void Top_level_commands_use_the_gate_and_the_flowing_depth()
    {
        var src = Src("Services", "Commands", "AiCommandService.cs");
        Assert.Contains("Judge(commandData)", src);
        Assert.Contains("depth: AiCommandAdmission.FollowUpDepth", src);
        Assert.Contains("PanicAftermath.TriggersSilenced()", src);
        Assert.DoesNotContain("depth: 0)", src);
    }

    [Fact]
    public void An_ai_lock_card_follows_the_users_strict_switch()
        // The Core command shows the card through the head's surface; the WPF surface picks strict.
        => Assert.Contains("ShowLockCard(phrase, repeats, customStrict: false)", Src("App.AiEffects.cs"));

    [Fact]
    public void A_named_media_path_uses_the_containment_and_switch_off_rules()
    {
        var src = Src("Services", "Commands", "MediaCommand.cs");
        Assert.Contains("AiCommandAdmission.IsUnderRoot(assetsRoot, fullPath)", src);
        Assert.Contains("AiCommandAdmission.IsSwitchedOff(", src);
        Assert.DoesNotContain("fullPath.StartsWith(assetsRoot", src);
    }
}
