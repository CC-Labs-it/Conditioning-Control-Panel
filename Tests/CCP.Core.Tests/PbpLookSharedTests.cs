using ConditioningControlPanel.Services.PieceByPiece;
using Xunit;

namespace CCP.Core.Tests;

public class PbpLookSharedTests
{
    [Fact]
    public void Pending_pick_round_trips_and_beats_a_newer_shared_timestamp()
    {
        var look = new ChessLook("chess.set.royal-capes", null, null);
        Assert.True(PbpLookRules.IsOwed(PbpLookRules.Write(look, 1234, owed: true)));
        Assert.False(PbpLookRules.IsOwed(PbpLookRules.Write(look, 1234)));
        Assert.Equal((look, false, true), PbpLookRules.Reconcile(look, 100, ChessLook.Empty, 200, owed: true));
    }

    [Fact]
    public void Counter_wear_uses_the_shared_slot_mapping_and_preserves_other_slots()
    {
        var look = new ChessLook("chess.set.royal-capes", "prime.banner.arena", null);
        Assert.Equal("insignia", PbpLookRules.SlotOf("chess.insignia.star"));
        Assert.Equal(look with { Insignia = "chess.insignia.star" }, PbpLookRules.With(look, "chess.insignia.star"));
        Assert.Null(PbpLookRules.SlotOf("chess.unknown"));
        Assert.Equal(look, PbpLookRules.With(look, "chess.unknown"));
    }
}
