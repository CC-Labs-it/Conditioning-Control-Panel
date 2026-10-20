using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Friends;

// Friend links (CONTRACT.md "Links"): Copy link, New link, and a pasted link in the add box.
// Every Copy link press asks the server (`link` is cheap and re-mints a link that is no longer
// live, e.g. after New link on the phone or the 25th claim); the per-account cache is only the
// fallback when that call fails, and the self-check in ClaimLinkAsync.
public sealed partial class FriendsService
{
    private readonly FriendLinkCache _linkCache = new();

    public async Task<FriendLink?> GetLinkAsync()
    {
        var account = _account();
        if (account == null) return null;
        var link = await _api.LinkAsync();
        if (_account() != account) return null;
        if (link != null)
        {
            _linkCache.Put(account, link);
            return link;
        }
        return _linkCache.Get(account, _now());
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
