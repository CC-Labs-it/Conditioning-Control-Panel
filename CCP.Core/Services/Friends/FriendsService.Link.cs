using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Friends;

// Friend links (CONTRACT.md "Links"): Copy link, New link, and a pasted link in the add box.
// Every Copy link press asks the server (`link` is cheap and re-mints a link that is no longer
// live, e.g. after New link on the phone or the 25th claim); the per-account cache is only the
// fallback when that call fails, and the self-check in ClaimLinkAsync. A live link near its end
// or out of uses is retired and minted again once before it is handed out.
public sealed partial class FriendsService
{
    private readonly FriendLinkCache _linkCache = new();

    /// <summary>Bumped by New link: a `link` reply that left before it is the retired token and is
    /// neither cached nor handed out.</summary>
    private int _linkGen;

    public async Task<FriendLink?> GetLinkAsync()
    {
        var account = _account();
        if (account == null) return null;
        int gen = _linkGen;
        var link = await _api.LinkAsync();
        if (_account() != account) return null;
        if (gen != _linkGen) return _linkCache.Get(account, _now());
        if (link != null && !FriendsLinkRules.WorthHandingOut(link, _now()))
        {
            // Spent, or about to end: never hand it out as if it had a week left.
            FriendLink? fresh = null;
            if (await _api.LinkRevokeAsync() && _account() == account)
            {
                _linkGen++;
                gen = _linkGen;
                fresh = await _api.LinkAsync();
            }
            if (_account() != account || gen != _linkGen) return null;
            if (!FriendsLinkRules.IsUsable(fresh, _now())) { _linkCache.Clear(); return null; }
            link = fresh;
        }
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
        _linkGen++;
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
