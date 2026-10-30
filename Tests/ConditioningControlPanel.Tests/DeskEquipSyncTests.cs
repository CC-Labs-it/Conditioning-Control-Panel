using System;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Contract 3 for the desk family: the PUT body, reading a server copy, and the newer-wins adopt rule.</summary>
public class DeskEquipSyncTests
{
    [Fact]
    public void Put_body_is_the_desk_family()
    {
        var body = DeskEquipSync.BuildPut("u1", new DeskLook("desk.hat.party", null));
        Assert.Equal("u1", (string?)body["unified_id"]);
        Assert.Equal("desk", (string?)body["family"]);
        Assert.Equal("desk.hat.party", (string?)body["value"]!["hat"]);
        Assert.Equal(JTokenType.Null, body["value"]!["toy"]!.Type);
    }

    [Fact]
    public void Reads_the_desk_copy_and_its_stamp()
    {
        var ms = DeskEquipSync.ReadDesk(JObject.Parse(
            "{\"ok\":true,\"equip\":{\"desk\":{\"hat\":\"desk.hat.party\",\"toy\":null},\"updatedAt\":1761800000000}}"));
        Assert.Equal(new DeskLook("desk.hat.party", null), ms!.Value.Look);
        Assert.Equal(1761800000000, ms.Value.UpdatedMs);

        var iso = DeskEquipSync.ReadDesk(JObject.Parse(
            "{\"ok\":true,\"equip\":{\"desk\":{\"toy\":\"desk.toy.snow-globe\"},\"updatedAt\":\"2026-10-30T00:00:00Z\"}}"));
        Assert.Equal(DateTimeOffset.Parse("2026-10-30T00:00:00Z").ToUnixTimeMilliseconds(), iso!.Value.UpdatedMs);

        Assert.Null(DeskEquipSync.ReadDesk(JObject.Parse("{\"ok\":false,\"reason\":\"not_found\"}")));
        Assert.Null(DeskEquipSync.ReadDesk(JObject.Parse("{\"ok\":true,\"equip\":{}}")));
        Assert.Null(DeskEquipSync.ReadDesk(null));
    }

    [Fact]
    public void Only_a_newer_server_copy_is_adopted()
    {
        Assert.True(DeskEquipSync.ShouldAdopt(200, 100));
        Assert.False(DeskEquipSync.ShouldAdopt(100, 100));
        Assert.False(DeskEquipSync.ShouldAdopt(50, 100));
        Assert.False(DeskEquipSync.ShouldAdopt(0, 0));
    }
}
