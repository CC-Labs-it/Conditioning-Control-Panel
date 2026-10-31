using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AvailableSubjectsTabView
{
    private DispatcherTimer? _racePoll;
    private CancellationTokenSource? _raceStop;
    internal Func<CancellationToken, Task<RaceOpenReply>> FetchRace = ct =>
    {
        var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
            Environment.GetEnvironmentVariable(FriendsHead.EnvVar));
        return url == null ? Task.FromResult(RaceOpenReply.Empty)
            : new RaceOpenApi(identity: FriendsHead.Identity, baseUrl: url).FetchAsync(ct);
    };
    internal Action<string> OpenRace = room => GameWindow.JoinRace(room);
    internal bool RacePolling => _raceStop != null;
    internal TimeSpan? RacePollInterval => _racePoll?.Interval;

    internal void StartRaceRows()
    {
        if (!IsVisible || _raceStop != null) return;
        _raceStop = new CancellationTokenSource();
        _racePoll = new DispatcherTimer { Interval = LobbyPollPlan.Base };
        _racePoll.Tick += async (_, _) => await RefreshRaceRows();
        _racePoll.Start();
        _ = RefreshRaceRows();
    }

    internal void StopRaceRows()
    {
        _racePoll?.Stop();
        _racePoll = null;
        _raceStop?.Cancel();
        _raceStop?.Dispose();
        _raceStop = null;
    }

    private bool _raceBusy;
    internal async Task RefreshRaceRows()
    {
        if (_raceBusy || _raceStop == null) return;
        _raceBusy = true;
        var stop = _raceStop;
        try
        {
            var signedIn = ReadGates().SignedIn;
            var reply = signedIn ? await FetchRace(stop.Token) : RaceOpenReply.Empty;
            if (stop.IsCancellationRequested || !ReferenceEquals(stop, _raceStop)) return;
            var snapshot = LobbyMerge.Build(null, null, null, FriendsHead.Service?.Snapshot.Friends,
                signedIn, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), reply);
            PaintRaceSnapshot(snapshot, signedIn && !reply.Ok);
            if (_racePoll != null) _racePoll.Interval = LobbyPollPlan.Next(_racePoll.Interval, !signedIn || reply.Ok);
        }
        catch (OperationCanceledException) { }
        catch { if (!stop.IsCancellationRequested) PaintRaceSnapshot(LobbySnapshot.Empty, true); }
        finally { _raceBusy = false; }
    }

    // This poll owns race rows only. Other Lobby producers keep their rows.
    private void PaintRaceSnapshot(LobbySnapshot races, bool error)
    {
        static LobbyRow[] Merge(IReadOnlyList<LobbyRow> old, IReadOnlyList<LobbyRow> fresh) =>
            old.Where(r => r.Game != LobbyGame.Race).Concat(fresh.Where(r => r.Game == LobbyGame.Race)).ToArray();
        PaintSnapshot(races with { Open = Merge(_snapshot.Open, races.Open),
            Playing = Merge(_snapshot.Playing, races.Playing), Friends = Merge(_snapshot.Friends, races.Friends) }, ReadGates(), error);
    }

    private void RaceJoin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LobbyCardView card }) PressRaceJoin(card.Row);
    }

    internal void PressRaceJoin(LobbyRow row)
    {
        if (row.Game != LobbyGame.Race || !row.CanJoin || !RaceJoinLink.IsRoomId(row.Key)) return;
        if (!ReadGates().SignedIn)
        {
            if (TopLevel.GetTopLevel(this) is MainShellWindow shell) _ = shell.OpenUnifiedLoginDialog();
            return;
        }
        OpenRace(row.Key!);
    }
}
