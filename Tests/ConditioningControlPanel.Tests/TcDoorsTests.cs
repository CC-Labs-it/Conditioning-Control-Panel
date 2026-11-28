using System.Linq;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Tc;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>Terms &amp; Conditioning's doors: one launcher card that is never locked, on the panic
/// registry, and Escape is its pause like every game.</summary>
[Collection(GameSurfacesCollection.Name)]
public class TcDoorsTests
{
    [Fact]
    public void The_tile_is_a_game_that_is_never_locked()
    {
        var tile = LauncherCatalogue.Find(TcHostService.GameId);
        Assert.NotNull(tile);
        Assert.Equal(LauncherDoor.Games, tile!.Door);
        Assert.False(tile.Locked);
        Assert.Equal("launcher_game_tc_title", tile.TitleKey);
    }

    [Fact]
    public void The_tile_shows_only_with_the_game_tree_on_disk()
        => Assert.Equal(TcHostService.TreePresent, LauncherCatalogue.Find(TcHostService.GameId)!.Available);

    [Fact]
    public void Panic_closes_it()
        => Assert.Contains(GameSurfaces.All, s => s.Id == TcHostService.GameId);

    [Fact]
    public void A_kept_Escape_goes_to_the_page_once_it_is_up()
    {
        Assert.Equal(KeptEscapePage.Tc, KeptEscapeGoesTo(true, false, false, false, false, false, false,
            tcInFront: true, tcReady: true));
        Assert.Equal(KeptEscapePage.None, KeptEscapeGoesTo(true, false, false, false, false, false, false,
            tcInFront: true, tcReady: false));
        Assert.Equal(KeptEscapePage.None, KeptEscapeGoesTo(false, false, false, false, false, false, false,
            tcInFront: true, tcReady: true));
    }

    [Fact]
    public void Nothing_is_open_before_the_window_lands()
    {
        Assert.False(TcHostService.IsActive);
        Assert.False(TcHostService.IsInFront);
        Assert.False(TcHostService.IsReady);
        TcHostService.Close();   // idempotent with nothing open
    }
}
