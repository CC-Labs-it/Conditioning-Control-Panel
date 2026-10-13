using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>Core PanicPolicy.KeptEscapeGoesTo for Down the Rabbit Hole (WPF MainWindow.xaml.cs:1528, Avalonia
/// MainShellWindow.PanicKey.cs TryGamePauseOnEscape), mirroring the race/board/Breakout cases in the WPF
/// PanicPolicyTests, which only run on Windows.</summary>
public class PanicPolicyDtrhTests
{
    [Fact]
    public void DtrhKeptTheEscape_ThePageIsHandedIt()
        => Assert.Equal(KeptEscapePage.Dtrh, KeptEscapeGoesTo(true, false, false, false, false, false, false, true, true));

    [Theory]
    //          claimed raceF  raceR  boardF boardR brkF   brkR   dtrhF  dtrhR
    [InlineData(false, false, false, false, false, false, false, true, true)]   // not kept: a full panic, which closes the game
    [InlineData(true, false, false, false, false, false, false, true, false)]   // DtRH is booting or on its way out
    [InlineData(true, false, false, false, false, false, false, false, true)]   // DtRH is not in front
    [InlineData(true, true, false, false, false, false, false, true, true)]     // the race in front wins, and is booting
    [InlineData(true, false, false, true, false, false, false, true, true)]     // the board in front wins, and is booting
    [InlineData(true, false, false, false, false, true, false, true, true)]     // Breakout in front wins, and is booting
    public void EverythingElse_HandsDtrhNothing(bool claimed, bool raceInFront, bool raceReady, bool boardInFront,
        bool boardReady, bool breakoutInFront, bool breakoutReady, bool dtrhInFront, bool dtrhReady)
        => Assert.Equal(KeptEscapePage.None, KeptEscapeGoesTo(claimed, raceInFront, raceReady, boardInFront, boardReady,
            breakoutInFront, breakoutReady, dtrhInFront, dtrhReady));

    [Fact]
    public void AnotherGameInFrontAndReady_TakesThePress_NotDtrh()
    {
        Assert.Equal(KeptEscapePage.Race, KeptEscapeGoesTo(true, true, true, false, false, false, false, true, true));
        Assert.Equal(KeptEscapePage.Board, KeptEscapeGoesTo(true, false, false, true, true, false, false, true, true));
        Assert.Equal(KeptEscapePage.Breakout, KeptEscapeGoesTo(true, false, false, false, false, true, true, true, true));
    }
}
