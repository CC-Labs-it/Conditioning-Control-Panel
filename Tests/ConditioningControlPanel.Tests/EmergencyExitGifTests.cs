using System.IO;
using ConditioningControlPanel.Services.EmergencyExit;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Emergency Exit games (jigsaw etc.) draw only the gifs still ticked in the Assets tree.</summary>
public class EmergencyExitGifTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-exit-gifs", "assets");

    [Fact]
    public void An_unticked_gif_never_reaches_the_games()
    {
        var a = Path.Combine(Root, "images", "a.gif");
        var b = Path.Combine(Root, "images", "sub", "b.gif");
        var c = Path.Combine(Root, "images", "other", "b.gif");   // same name, other folder: whole-path identity
        var kept = EmergencyExitHostService.ActiveGifs(new[] { a, b, c }, Root, new[] { "images\\sub\\B.gif" });
        Assert.Equal(new[] { a, c }, kept);
    }

    [Fact]
    public void Nothing_unticked_keeps_every_gif()
    {
        var a = Path.Combine(Root, "images", "a.gif");
        Assert.Equal(new[] { a }, EmergencyExitHostService.ActiveGifs(new[] { a }, Root, null));
    }
}
