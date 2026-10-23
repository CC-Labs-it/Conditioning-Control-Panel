using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>One answer to one <c>station-request</c> (CONTRACT section 2.2, <c>station-result</c>).
/// A host refusal has <see cref="Status"/> 0 and a reason of <c>offline</c>, <c>closed</c> or
/// <c>bad_op</c>; a request that ran out of time is <c>timeout</c>.</summary>
public sealed record BackRoomStationResult(bool Ok, int Status, string? Reason, JToken? Body)
{
    public static BackRoomStationResult Refuse(string reason) => new(false, 0, reason, null);
}

/// <summary>What the host needs from the relay. The service owns one; tests hand in a fake.</summary>
public interface IBackRoomRelay
{
    Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct = default);
}
