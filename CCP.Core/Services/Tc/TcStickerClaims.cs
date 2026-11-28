using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles.Claims;

namespace ConditioningControlPanel.Services.Tc.Claims;

/// <summary>
/// Sticker Sparkles, pure rules (handoff-1125/tc-landing/SPARKLES-WIRE.md, owner 2026-11-25): the first time an
/// account unlocks a Sticker Book sticker the server pays its tier price. The host only says which id; the server
/// prices it, once per account per id, ever. The page already shows its own sticker card, so the host posts a
/// <c>sparkles-receipt</c> ONLY when something was credited; every other answer is quiet.
/// </summary>
internal static class TcStickerRules
{
    public const string Game = "tc";
    public const string Mode = "sticker";

    /// <summary>Space between two sends: a burst of stickers stays under the 60 a minute the Sparkles routes allow.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1100);

    /// <summary>Wait before the one retry of a <c>too_fast</c> refusal.</summary>
    public static readonly TimeSpan RetryWait = TimeSpan.FromSeconds(15);

    /// <summary>One sticker claim: a fresh claim id per event, no duration, no detail (the wire ignores both).</summary>
    public static SparklesClaim Claim(string id, string? claimId = null) =>
        SparklesClaim.Create(Game, Mode, SparklesResult.ForSticker(id), 0, claimId: claimId);

    /// <summary>The page hears about a sticker claim only when Sparkles really moved (not shadow, credited above 0).</summary>
    public static bool ShouldPost(SparklesClaimOutcome? o) => o?.Receipt is { Shadow: false, Credited: > 0 };

    /// <summary>The one refusal worth a second send of the SAME claim (same claim id): the per-minute limit.
    /// <c>busy</c>, a pending answer and no answer are already asked again inside the claim service.</summary>
    public static bool ShouldRetry(SparklesClaimOutcome? o) => o != null && o.Receipt == null && o.Reason == "too_fast";

    /// <summary>The server has settled this id for the account (paid, already, unknown, retired): no need to send it
    /// again while the window is open. <c>sticker_cap</c> and refusals may be sent again (a later UTC day pays).</summary>
    public static bool IsFinal(SparklesClaimOutcome? o) => o?.Receipt is { } r && r.Reason != "sticker_cap";

    /// <summary>What a quiet answer is logged as, once per window.</summary>
    public static string QuietKey(SparklesClaimOutcome? o) => o switch
    {
        null => "no-service",
        { Receipt: { Shadow: true } } => "shadow",
        { Receipt: { } r } => string.IsNullOrEmpty(r.Reason) ? "zero" : r.Reason!,
        _ => string.IsNullOrEmpty(o.Reason) ? "refused" : o.Reason!,
    };
}

/// <summary>
/// The sticker claims of one app run, sent ONE AT A TIME with <see cref="TcStickerRules.Gap"/> between them, so a
/// burst of achievement frames never fans out into parallel calls. An id already waiting, in flight or settled this
/// window is dropped; a window may offer at most <see cref="MaxOffersPerWindow"/> ids. A settled claim goes to the
/// callback with whether to post it and whether its quiet reason is the first of its kind this window.
/// </summary>
internal sealed class TcStickerQueue
{
    public const int MaxOffersPerWindow = 120;
    public const int MaxWaiting = 64;

    /// <summary>One settled sticker claim. <paramref name="Owner"/> is what offered it (the host's window).</summary>
    public sealed record Settled(string Id, object? Owner, SparklesClaimOutcome? Outcome, bool Post, bool FirstQuiet);

    private sealed record Item(SparklesClaim Claim, object? Owner, bool Retried);

    private readonly object _lock = new();
    private readonly Queue<Item> _waiting = new();
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly HashSet<string> _quietSaid = new(StringComparer.Ordinal);
    private readonly Func<SparklesClaim, Task<SparklesClaimOutcome?>> _send;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly Action<Settled> _settled;
    private int _offers;
    private bool _running;
    private Task _pump = Task.CompletedTask;

    public TcStickerQueue(Func<SparklesClaim, Task<SparklesClaimOutcome?>> send, Func<TimeSpan, Task> delay, Action<Settled> settled)
    {
        _send = send;
        _delay = delay;
        _settled = settled;
    }

    /// <summary>Done when nothing waits or flies (tests).</summary>
    public Task WhenIdle { get { lock (_lock) return _pump; } }

    /// <summary>A new window: ids may be offered again (the server answers <c>already</c>), quiet reasons are logged
    /// afresh. Claims still waiting from the last window are kept and still paid.</summary>
    public void ResetWindow()
    {
        lock (_lock)
        {
            _held.Clear();
            foreach (var i in _waiting) _held.Add(i.Claim.Result.Id!);
            _quietSaid.Clear();
            _offers = 0;
        }
    }

    /// <summary>Queue one sticker id. False when it is malformed, a repeat, or over this window's caps.</summary>
    public bool Offer(string? id, object? owner)
    {
        if (!SparklesResult.IsStickerId(id)) return false;
        lock (_lock)
        {
            if (_held.Contains(id!) || _offers >= MaxOffersPerWindow || _waiting.Count >= MaxWaiting) return false;
            _held.Add(id!);
            _offers++;
            _waiting.Enqueue(new Item(TcStickerRules.Claim(id!), owner, false));
            if (_running) return true;
            _running = true;
            _pump = Task.Run(PumpAsync);
        }
        return true;
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            Item item;
            lock (_lock)
            {
                if (_waiting.Count == 0) { _running = false; return; }
                item = _waiting.Dequeue();
            }
            SparklesClaimOutcome? outcome;
            try { outcome = await _send(item.Claim).ConfigureAwait(false); }
            catch { outcome = null; }

            var wait = TcStickerRules.Gap;
            if (!item.Retried && TcStickerRules.ShouldRetry(outcome))
            {
                // The SAME claim (same claim id) once more, after the minute limit has eased.
                lock (_lock) _waiting.Enqueue(item with { Retried = true });
                wait = TcStickerRules.RetryWait;
            }
            else
            {
                var id = item.Claim.Result.Id!;
                var post = TcStickerRules.ShouldPost(outcome);
                var firstQuiet = false;
                lock (_lock)
                {
                    if (!TcStickerRules.IsFinal(outcome)) _held.Remove(id);
                    if (!post) firstQuiet = _quietSaid.Add(TcStickerRules.QuietKey(outcome));
                }
                try { _settled(new Settled(id, item.Owner, outcome, post, firstQuiet)); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            try { await _delay(wait).ConfigureAwait(false); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
    }
}
