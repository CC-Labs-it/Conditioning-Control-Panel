using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// WPF BarkService hands every decision to Core BarkEngine; the engine only knows what the head
/// feeds it. A dropped seam compiles clean and silently changes behaviour (a muted or speaking
/// avatar gets talked over, clicks_60s never counts, chat stops suppressing barks), so pin each one.
/// </summary>
public class BarkServiceEngineWiringTests
{
    private static string Source() => SourceRoots.ReadProductFile("Services", "Companion", "BarkService.cs");

    [Theory]
    [InlineData(@"EmiAvatarMuted\s*=")]
    [InlineData(@"WhisperPlaying\s*=")]
    [InlineData(@"NarratorPlaying\s*=")]
    [InlineData(@"AvatarSpeaking\s*=")]
    [InlineData(@"AvatarBusy\s*=")]
    [InlineData(@"TierName\s*=")]
    [InlineData(@"PoolRefPhrases\s*=")]
    [InlineData(@"_engine\.LiveField\s*=")]
    [InlineData(@"_engine\.Load\(")]
    [InlineData(@"_engine\.Reload\(")]
    [InlineData(@"_engine\.RegisterAvatarClick\(\)")]
    [InlineData(@"_engine\.NoteUserMessage\(\)")]
    [InlineData(@"_engine\.NoteExternalLineSpoken\(\)")]
    [InlineData(@"_engine\.Decide\(")]
    [InlineData(@"_engine\.DecideIdle\(\)")]
    [InlineData(@"RecordWithArbiter\(pick\.Rule\)")]
    public void BarkService_FeedsTheEngineSeam(string pattern)
    {
        Assert.Matches(new Regex(pattern), Source());
    }
}
