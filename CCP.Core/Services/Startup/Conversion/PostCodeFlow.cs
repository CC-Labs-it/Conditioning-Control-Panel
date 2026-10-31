using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Services.Startup.Conversion;
public sealed class PostCodeFlow
{
    public const string RowKey = "post_code_live";
    private DateTime _checkedUtc = DateTime.MinValue;
    private bool _checking;
    private bool _redeeming;
    public async Task<PostCodeLive?> CheckAsync(AppSettings s, DateTime nowUtc,
        Func<Task<PostCodeLive?>> fetch)
    {
        if (_checking || !s.PatreonMember || s.OfflineMode || string.IsNullOrEmpty(s.AuthToken)
            || !Owed(s, nowUtc.ToLocalTime()) || nowUtc - _checkedUtc < TimeSpan.FromMinutes(30)) return null;
        _checking = true;
        _checkedUtc = nowUtc;
        try { return await fetch(); }
        finally { _checking = false; }
    }
    public static bool Owed(AppSettings s, DateTime localDay) =>
        !s.InboxStamps.TryGetValue(RowKey, out var stamp) || stamp != Day(localDay);
    public static bool StampRow(AppSettings s, PostCodeLive? live, bool held, DateTime localDay)
    {
        if (held || live is not { Live: true, Redeemed: false, Feature: not null } || !Owed(s, localDay)) return false;
        s.InboxStamps[RowKey] = Day(localDay);
        return true;
    }
    public async Task<PostCodeRedeem?> RedeemAsync(AppSettings s, string? code,
        Func<string?, Task<PostCodeRedeem>> send, Action save, Action changed, Func<bool>? accept = null)
    {
        if (_redeeming) return null;
        if (PostCodeApi.NormalizeCode(code) == null) return new(false, null, null, "bad_code");
        _redeeming = true;
        try
        {
            var result = await send(code);
            if (accept?.Invoke() == false) return null;
            if (result is { Ok: true, Feature: string feature, UntilUtc: DateTime until })
            {
                s.PostGrants = new Dictionary<string, string>(s.PostGrants, StringComparer.OrdinalIgnoreCase)
                { [feature] = until.ToString("o", CultureInfo.InvariantCulture) };
                save();
                changed();
            }
            return result;
        }
        finally { _redeeming = false; }
    }
    private static string Day(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
