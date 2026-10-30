using System;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;
namespace ConditioningControlPanel.Services.Prizes;
public sealed class PrizesFeed
{
    private readonly Func<OwnershipService?> _ownership;
    private readonly Func<string?> _currentAccount;
    private readonly Action<Action> _post;
    private readonly object _gate = new();
    private string? _accountId;
    public PrizesFeed(Func<OwnershipService?> ownership, Func<string?> currentAccount, Action<Action> post)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _currentAccount = currentAccount ?? throw new ArgumentNullException(nameof(currentAccount));
        _post = post ?? throw new ArgumentNullException(nameof(post));
    }
    public void NoteAccount(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;
        _post(() => NoteAccountNow(accountId));
    }
    private bool NoteAccountNow(string accountId)
    {
        if (!string.Equals(_currentAccount(), accountId, StringComparison.Ordinal))
        {
            return false;
        }
        bool switched;
        lock (_gate)
        {
            switched = _accountId != null && !string.Equals(_accountId, accountId, StringComparison.Ordinal);
            _accountId = accountId;
        }
        if (switched)
        {
            _ownership()?.Clear();
        }
        return true;
    }
    public bool Apply(string? requestedFor, string? answeredFor, PrizesBlock? prizes, string source)
    {
        if (string.IsNullOrEmpty(requestedFor)) return false;
        string[]? grants = null;
        if (prizes?.Grants != null)
        {
            if (string.Equals(requestedFor, answeredFor, StringComparison.Ordinal))
                grants = prizes.Grants.ToArray();
            else
                Log.Debug("[Prizes] {Source} answered for another account, block ignored", source);
        }
        var revision = prizes?.Revision ?? 0;
        _post(() =>
        {
            if (!NoteAccountNow(requestedFor) || grants == null) return;
            var ownership = _ownership();
            if (ownership == null) return;
            ownership.ApplySnapshot(requestedFor, revision, grants);
        });
        return grants != null;
    }
    public void Clear() => _post(() =>
    {
        lock (_gate) _accountId = null;
        _ownership()?.Clear();
    });
}
