using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1577 ("the heavier the corner GIF, the more it lags"). The corner path has not
/// changed since 7.1.10, so the next report has to name the cost: the source size behind
/// "heavier", the UI-thread time of the header read and the layered Show, the renderer the
/// fullscreen spiral uses, and the decoded frame set with its decode and UI-wait times. These pin
/// the two log lines so a later edit cannot silently drop a fact or follow the Italian comma.
/// </summary>
public class CornerGifDiagLineTests
{
    [Fact]
    public void Source_line_names_size_cost_and_renderer()
    {
        var line = CornerGifMedia.DescribeSource(1280, 720, 15 * 1024 * 1024 + 512 * 1024, 42, 7, unifiedRenderer: false);
        Assert.Equal("source 1280x720, 15.5 MB; UI header read 42 ms, layered Show 7 ms; renderer legacy windows", line);
    }

    [Fact]
    public void Source_line_without_a_file_size_says_so()
    {
        var line = CornerGifMedia.DescribeSource(360, 240, -1, 0, 3, unifiedRenderer: true);
        Assert.Equal("source 360x240, size unknown; UI header read 0 ms, layered Show 3 ms; renderer unified", line);
    }

    [Fact]
    public void Frames_line_names_set_rate_memory_and_both_waits()
    {
        var line = AnimatedWebp.DescribeAttach("CornerGifService: slot 0 frames", 48, 140, 401, 225, 2300, 15, still: false);
        Assert.Equal("CornerGifService: slot 0 frames: 48 frames at 401x225, 140 ms a frame (7.1 fps), ~16 MB kept, decode 2300 ms, UI wait 15 ms", line);
    }

    [Fact]
    public void Frames_line_when_nothing_animated()
    {
        Assert.Equal("t: no animation decoded (still shown), decode 9 ms, UI wait 1 ms",
            AnimatedWebp.DescribeAttach("t", 0, 0, 0, 0, 9, 1, still: true));
        Assert.Equal("t: no animation decoded (nothing shown), decode 9 ms, UI wait 1 ms",
            AnimatedWebp.DescribeAttach("t", 0, 0, 0, 0, 9, 1, still: false));
    }
}
