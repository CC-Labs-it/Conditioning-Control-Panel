using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1569 #1573 #1575 #1576: idle giggles with the engine off. Every priority giggle names
/// the code that asked for it in the sound ring, so the next report says which caller skips the
/// idle-voice hold.
/// </summary>
public class GiggleRingTagTests
{
    [Fact]
    public void A_known_caller_is_named_in_the_ring_tag()
    {
        var who = RecentSounds.DuckCaller(@"C:\src\Services\Commands\GetBackToMeCommand.cs", "FireAsync");
        Assert.Equal("giggle (GetBackToMeCommand.FireAsync)", AvatarTubeWindow.GiggleRingTag(who));
    }

    [Fact]
    public void An_unknown_caller_keeps_the_plain_tag()
    {
        Assert.Equal("giggle", AvatarTubeWindow.GiggleRingTag(null));
        Assert.Equal("giggle", AvatarTubeWindow.GiggleRingTag(""));
    }

    [Fact]
    public void GigglePriority_takes_its_caller_and_hands_it_to_the_sound()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Speech.cs"));
        Assert.Contains("[System.Runtime.CompilerServices.CallerMemberName] string callerMember", src);
        Assert.Contains("soundCaller: who", src);
        Assert.Contains("PlayGiggleSound(soundCaller)", src);
        Assert.Contains("GiggleRingTag(caller)", src);
    }

    [Fact]
    public void The_plain_Giggle_path_carries_its_caller_through_the_queue_too()
    {
        // The 1-in-5 preset giggle (AutonomyService comments and ~30 other callers) plays the same
        // files; queued lines must keep the caller until they are shown.
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Speech.cs"));
        Assert.Contains("bool unprompted, string? caller)> _speechQueue", src);
        Assert.Equal(2, CountOf(src, "SpeechSource.Preset, emotionLineId, mood, unprompted, who)"));
        Assert.Contains("ShowSpeechBySource(nextText, source, nextEmotionLineId, nextMood, nextUnprompted, nextCaller)", src);
        Assert.Contains("unprompted: unprompted, soundCaller: who);", src);
    }

    private static int CountOf(string s, string needle)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "AvatarTube")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
