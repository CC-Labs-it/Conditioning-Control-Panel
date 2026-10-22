using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.PieceByPiece;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Tonight Board's featured chess match (2026-10-14). Asks the PUBLIC <c>GET /v2/pbp/featured</c>
/// (no token) only while <see cref="SetActive"/> says the board is on screen; the server picks the
/// match (an admin pin, else a 20 s rotation over watchable games). Raises <see cref="Changed"/>
/// on the UI thread when the answer moves.
/// </summary>
public static class PbpFeaturedFeed
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private static DispatcherTimer? _timer;
    private static TimeSpan _interval = PbpFeaturedPoll.Base;
    private static bool _active;
    internal static bool Active => _active;
    private static bool _inflight;
    private static DateTime _lastOkUtc = DateTime.MinValue;
    private static DateTime _lastAskUtc = DateTime.MinValue;

    /// <summary>The match the board should show, or <see cref="PbpFeaturedReply.None"/>.</summary>
    public static PbpFeaturedReply Current { get; private set; } = PbpFeaturedReply.None;

    public static event Action? Changed;

    /// <summary>Seam: one round of the wire. The real GET by default.</summary>
    internal static Func<CancellationToken, Task<(int Status, string? Body)>> Fetch { get; set; } = HttpFetchAsync;

    /// <summary>Starts or stops the poll. UI thread only (the timer is a DispatcherTimer).</summary>
    public static void SetActive(bool active)
    {
        if (active == _active) return;
        _active = active;
        if (!active) { _timer?.Stop(); return; }

        _timer ??= new DispatcherTimer();
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        // A quick tab hop does not ask again before the interval is up.
        var wait = _interval - (DateTime.UtcNow - _lastAskUtc);
        if (wait <= TimeSpan.Zero) { _timer.Interval = _interval; _ = RoundAsync(); }
        else _timer.Interval = wait;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (!_active) { _timer?.Stop(); return; }
        if (_timer != null) _timer.Interval = _interval;
        _ = RoundAsync();
    }

    private static async Task RoundAsync()
    {
        if (_inflight) return;
        _inflight = true;
        _lastAskUtc = DateTime.UtcNow;
        PbpFeaturedOutcome outcome;
        PbpFeaturedReply fresh;
        try
        {
            using var budget = new CancellationTokenSource(Timeout);
            var (status, body) = await Fetch(budget.Token);
            (outcome, fresh) = PbpFeaturedWire.Parse(status, body);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("PbpFeatured: round failed: {E}", ex.Message);
            (outcome, fresh) = (PbpFeaturedOutcome.Error, PbpFeaturedReply.None);
        }
        finally { _inflight = false; }

        // The continuation runs on the UI thread (the round started there), so the timer is safe.
        var now = DateTime.UtcNow;
        var next = PbpFeaturedPoll.After(Current, outcome, fresh, now - _lastOkUtc);
        if (outcome == PbpFeaturedOutcome.Ok) _lastOkUtc = now;
        _interval = PbpFeaturedPoll.Next(_interval, outcome);
        if (_timer != null && _timer.IsEnabled) _timer.Interval = _interval;

        if (next == Current) return;
        Current = next;
        try { Changed?.Invoke(); }
        catch (Exception ex) { Serilog.Log.Debug("PbpFeatured: Changed handler threw: {E}", ex.Message); }
    }

    private static async Task<(int, string?)> HttpFetchAsync(CancellationToken ct)
    {
        using var res = await Http.GetAsync($"{GameWindow.PbpServerBase() ?? throw new InvalidOperationException("No featured server in sandbox")}/v2/pbp/featured", ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ((int)res.StatusCode, text.Length > 16 * 1024 ? null : text);
    }

    /// <summary>Test seam.</summary>
    internal static void ResetForTests()
    {
        _active = false;
        _timer?.Stop();
        _interval = PbpFeaturedPoll.Base;
        _lastOkUtc = DateTime.MinValue;
        _lastAskUtc = DateTime.MinValue;
        Current = PbpFeaturedReply.None;
        Changed = null;
    }
}
