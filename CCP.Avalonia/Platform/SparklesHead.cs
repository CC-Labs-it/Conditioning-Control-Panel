using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Sparkles;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF SparklesClaimService.CreateForApp. Owns cancellation, dispatch and wallet persistence.</summary>
internal sealed class SparklesHead
{
    private readonly object _gate = new();
    private CancellationTokenSource _pending = new();
    private readonly HttpClient? _http;
    private readonly string? _baseUrl;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly Action<Action> _post;
    private readonly Action _save;

    internal SparklesHead(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null,
        Action<Action>? post = null, Action? save = null, string? baseUrl = null)
    {
        _http = http;
        _baseUrl = baseUrl;
        _identity = identity ?? FriendsHead.Identity;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _save = save ?? (() => CoreSettings.Save());
    }

    internal async Task<SparklesClaimOutcome> ClaimAsync(SparklesClaim claim, CancellationToken ct = default)
    {
        CancellationTokenSource linked;
        CancellationToken lifetime;
        lock (_gate)
        {
            lifetime = _pending.Token;
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        }
        using (linked)
        {
            // Retain the token value for a queued write after this method disposes its source.
            var token = linked.Token;
            var url = _baseUrl ?? FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
                Environment.GetEnvironmentVariable(FriendsHead.EnvVar));
            if (url == null) return SparklesClaimOutcome.Refused("signin");
            var service = new SparklesClaimService(_http, _identity, baseUrl: url, adoptSp: (account, sp) =>
                _post(() =>
                {
                    lock (_gate)
                    {
                        if (ct.IsCancellationRequested || lifetime.IsCancellationRequested) return;
                        var settings = CoreSettings.Current;
                        var same = string.Equals(_identity()?.UnifiedId, account, StringComparison.Ordinal);
                        var before = settings.SkillPoints;
                        if (!V2WalletAdoption.Decide(same, false, sp, before, out var next)) return;
                        settings.SkillPoints = next;
                        _save();
                        SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
                    }
                }));
            return await service.ClaimAsync(claim, token).ConfigureAwait(false);
        }
    }

    internal void Stop()
    {
        lock (_gate)
        {
            var old = _pending;
            _pending = new CancellationTokenSource();
            old.Cancel();
            old.Dispose();
        }
    }
}
