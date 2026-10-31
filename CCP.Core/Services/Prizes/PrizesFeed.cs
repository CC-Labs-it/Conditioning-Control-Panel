using System;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>WPF ProfileSyncService.PrizesFeed: account-bound snapshots through one FIFO supplied
/// by the head. Missing blocks preserve ownership; empty grants revoke it. Never stores or uploads grants.</summary>
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

    /// <summary>Record the account a request is about to be made for; a different account than last time clears what is held.</summary>
    public void NoteAccount(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;
        _post(() => NoteAccountNow(accountId));
    }

    // Runs from the queue. False when accountId is no longer the signed-in account.
    private bool NoteAccountNow(string accountId)
    {
        if (!string.Equals(_currentAccount(), accountId, StringComparison.Ordinal))
        {
            Log.Debug("[Prizes] late call for an account no longer signed in, ignored");
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
            Log.Information("[Prizes] account changed, clearing held ownership");
            _ownership()?.Clear();
        }
        return true;
    }

    /// <summary>
    /// Apply one response's block. Returns true when a snapshot was queued for the account.
    /// A missing block leaves the held state alone; a block answered for another record is
    /// ignored.
    /// </summary>
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
            Log.Debug("[Prizes] {Source} snapshot rev {Revision}, {Count} grants", source, revision, grants.Length);
        });
        return grants != null;
    }

    /// <summary>Logout: forget the account and drop every held grant.</summary>
    public void Clear() => _post(() =>
    {
        lock (_gate) _accountId = null;
        _ownership()?.Clear();
    });
}
