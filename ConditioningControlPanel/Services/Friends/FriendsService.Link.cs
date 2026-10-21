using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Friends;

// Friend links (CONTRACT.md "Links"): Copy link, New link, and a pasted link in the add box.
// The live link is cached per account until it nears its end, so a press costs nothing most days.
public sealed partial class FriendsService
{
    private readonly FriendLinkCache _linkCache = new();

    public async Task<FriendLink?> GetLinkAsync()
    {
        var account = _account();
        if (account == null) return null;
        if (_linkCache.Get(account, _now()) is { } cached) return cached;
        var link = await _api.LinkAsync();
        if (link == null || _account() != account) return null;
        _linkCache.Put(account, link);
        return link;
    }

    public async Task<FriendLink?> NewLinkAsync()
    {
        var account = _account();
        if (account == null) return null;
        if (!await _api.LinkRevokeAsync()) return null;
        _linkCache.Clear();
        if (_account() != account) return null;
        return await GetLinkAsync();
    }

    public async Task<ClaimResult> ClaimLinkAsync(string linkOrToken)
    {
        var token = FriendsLinkRules.ParseToken(linkOrToken);
        if (token == null) return ClaimResult.Unusable;
        if (!Available) return ClaimResult.TryLater;
        if (_linkCache.Get(_account(), _now())?.Token == token) return ClaimResult.Self;
        var r = await _api.ClaimAsync(token);
        if (r == ClaimResult.Friends) await RefreshAsync();
        return r;
    }
}
