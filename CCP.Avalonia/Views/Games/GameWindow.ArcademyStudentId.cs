// THE STUDENT ID: init.profile, profile frames and link-discord.
// WPF ArcademyHostService.cs:1047-1070, 4364-4377, 4416-4525, 5902-5906.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    /// <summary>Tests only: the Discord account the chip reads and links (WPF App.Discord, Core DiscordAccount).</summary>
    internal static Func<bool> ArcademyDiscordLinked = () => AccountSeed.Discord?.IsAuthenticated == true;
    internal static Func<Action<string>, CancellationToken, Task> ArcademyDiscordSignIn = (open, ct) =>
        AccountSeed.Discord is { } d ? d.SignInAsync(open, ct) : throw new InvalidOperationException("Discord service unavailable");
    internal static TimeSpan ArcademyLinkDeadline = TimeSpan.FromSeconds(120);   // WPF :4359 LinkDeadline
    private CancellationTokenSource? _arcademyLink;
    internal Task? ArcademyLastLink { get; private set; }

    // WPF :1047-1070. ponytail: avatarUrl is always null - ArcademyAvatarCache (WPF JPEG re-encode) is not in Core,
    // so the card keeps its PHOTO PENDING plate and there is no KickAvatarRefresh (WPF :532, :4387-4401, :5860).
    private static object ArcademyProfile(AppSettings s)
    {
        try
        {
            var name = CoreAccount.DisplayName;
            var share = (s.ArcademyPresenceShare ?? "").Trim().ToLowerInvariant();   // WPF :5902-5906 clamp
            return new
            {
                name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                avatarUrl = (string?)null,
                discordLinked = ArcademyDiscordLinked(),
                presenceShare = Array.IndexOf(AppSettings.ArcademyPresenceShares, share) >= 0 ? share : "off",
            };
        }
        catch (Exception ex)
        {
            Log.Debug("[Arcademy] profile unavailable: {E}", ex.Message);
            return new { name = (string?)null, avatarUrl = (string?)null, discordLinked = false, presenceShare = "off" };
        }
    }

    // WPF :4364-4377: result is linked/cancelled/failed after a link attempt, absent otherwise.
    private void ArcademyPushProfile(string? result = null)
    {
        if (!_closing) Post(new { type = "profile", profile = ArcademyProfile(CoreSettings.Current), result });
    }

    // WPF :4416-4492: one link in flight; an already-linked account takes the short path; exactly one reply.
    private void ArcademyLinkDiscord()
    {
        if (_arcademyLink != null) { Log.Debug("[Arcademy] link-discord ignored - a link is already open"); return; }
        if (ArcademyDiscordLinked())
        {
            ArcademyDiscordRung();
            ArcademyPushProfile("linked");
            return;
        }
        var cts = _arcademyLink = new CancellationTokenSource();
        cts.CancelAfter(ArcademyLinkDeadline);
        Action<string> open = url =>
        {
            if (ExternalOpener.Allowed(url)) Dispatcher.UIThread.Post(() => _ = ExternalOpener.OpenAsync(this, url));
        };
        ArcademyLastLink = Run();

        async Task Run()
        {
            string result;
            try
            {
                await ArcademyDiscordSignIn(open, cts.Token);
                // Core's SignInAsync returns without linking while another flow runs (IsVerifying).
                result = ArcademyDiscordLinked() ? "linked" : "cancelled";
            }
            // Core ends a cancelled or timed-out browser wait as a TimeoutException.
            catch (Exception ex) when (ex is OperationCanceledException || cts.IsCancellationRequested) { result = "cancelled"; }
            catch (Exception ex) { Log.Warning("[Arcademy] Discord link failed: {E}", ex.Message); result = "failed"; }
            if (_arcademyLink != cts) { cts.Dispose(); return; }   // panic or teardown already answered the page
            _arcademyLink = null;
            cts.Dispose();
            if (result == "linked") ArcademyDiscordRung();
            ArcademyPushProfile(result);
        }
    }

    // WPF :4497-4510: through AppSettings, so the setting echo and the profile frame both follow.
    private static void ArcademyDiscordRung()
    {
        var s = CoreSettings.Current;
        if (s.ArcademyPresenceShare == "discord") return;
        s.ArcademyPresenceShare = "discord";
        CoreSettings.Save();
    }

    // WPF :4516-4525: panic (tellPage) and teardown close an open link-up.
    private void ArcademyCancelLink(bool tellPage)
    {
        var cts = _arcademyLink;
        if (cts == null) return;
        _arcademyLink = null;
        Log.Information("[Arcademy] cancelling the open Discord link-up");
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        if (tellPage) ArcademyPushProfile("cancelled");
    }
}
