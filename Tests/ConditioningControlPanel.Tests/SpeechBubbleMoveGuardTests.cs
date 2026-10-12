using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1412: a hang dump caught the UI thread inside the speech bubble's SetWindowPos, parked
/// in WPF's synchronous flush to the render thread. The placement cannot loop on its own because of
/// three things this file pins: it never re-enters, it never moves the window to where it already
/// is, and the z-order hook never moves or re-places anything. The move also wears a hang mark and
/// logs a slow wait, so the next report names it.
/// </summary>
public class SpeechBubbleMoveGuardTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "AvatarTube",
            "AvatarTubeWindow.Speech.cs"));
    }

    private static string Body(string src, string signature, string nextMember)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing " + signature);
        int end = src.IndexOf(nextMember, start + signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, "missing " + nextMember);
        return src.Substring(start, end - start);
    }

    private static string Placement() =>
        Body(Source(), "private void ApplySpeechBubblePlacement()", "private double SpeechPopupScale()");

    [Fact]
    public void Placement_never_reenters_and_always_drops_its_guard()
    {
        var body = Placement();
        int guard = body.IndexOf("if (_placingSpeechBubble) return;", StringComparison.Ordinal);
        int set = body.IndexOf("_placingSpeechBubble = true;", StringComparison.Ordinal);
        int fin = body.IndexOf("finally", StringComparison.Ordinal);
        int reset = body.IndexOf("_placingSpeechBubble = false;", StringComparison.Ordinal);
        Assert.True(guard >= 0 && set > guard, "the re-entrancy guard is the first thing placement does");
        Assert.True(fin > set && reset > fin, "the guard is dropped in a finally");
    }

    [Fact]
    public void The_window_moves_only_when_its_spot_changed_and_never_resizes_or_restacks()
    {
        var body = Placement();
        int same = body.IndexOf("if (bx != _bubbleX || by != _bubbleY)", StringComparison.Ordinal);
        int move = body.IndexOf("SetWindowPos(", StringComparison.Ordinal);
        Assert.True(same >= 0 && move > same, "the move sits behind the nothing-changed check");
        Assert.Equal(move, body.LastIndexOf("SetWindowPos(", StringComparison.Ordinal));
        Assert.Contains("SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE", body);
    }

    [Fact]
    public void The_move_wears_a_hang_mark_and_reports_a_slow_wait()
    {
        var body = Placement();
        int mark = body.IndexOf("VideoDiag.UiScope(BubbleMoveMark)", StringComparison.Ordinal);
        int move = body.IndexOf("SetWindowPos(", StringComparison.Ordinal);
        Assert.True(mark >= 0 && move > mark, "the mark opens before the move");
        Assert.Contains("IsSlowBubbleMove(moveMs)", body);
    }

    [Fact]
    public void The_z_order_hook_only_edits_the_pending_position_in_place()
    {
        var hook = Body(Source(), "private IntPtr BubbleWndProc(", "private bool _placingSpeechBubble;");
        Assert.DoesNotContain("SetWindowPos(", hook);
        Assert.DoesNotContain("ApplySpeechBubblePlacement", hook);
        Assert.DoesNotContain(".Show()", hook);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(16, false)]
    [InlineData(249, false)]
    [InlineData(250, true)]
    [InlineData(15000, true)]
    public void Only_a_wait_the_player_could_feel_is_logged(long ms, bool slow)
        => Assert.Equal(slow, AvatarTubeWindow.IsSlowBubbleMove(ms));
}
