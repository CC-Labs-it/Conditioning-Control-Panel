using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using System.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class LeashAcceptSnapTests
{
    private sealed class Service : ILeashService
    {
        public bool Available => true;
        public LeashSnapshot Snapshot { get; private set; } = LeashSnapshot.Empty;
        public readonly List<string> Calls = new(), Seen = new();
        public LeashSendStatus Reply = LeashSendStatus.Sent;
        public bool ReleaseOk = true;
        public LeashWatch? Watch;
        public bool ReceiptsSupported { get; set; }
        public event Action<LeashSnapshot>? SnapshotChanged;
        public event Action? ReceiptsChanged;
        public event Action<LeashEvent>? EventArrived;
        public void Emit(LeashEvent e) => EventArrived?.Invoke(e);
        public Punishment? GateDue => null;
        public void Push(MyLeash? me, params HeldLeash[] held) { Snapshot = new(me, held, Array.Empty<LeashOffer>()); SnapshotChanged?.Invoke(Snapshot); }
        public void Receipts() => ReceiptsChanged?.Invoke();
        public void NoteShown(string? id) { if (id != null) Seen.Add(id); }
        public IReadOnlyList<LeashSentItem> SentTo(string id) => new[] { new LeashSentItem("l1", LeashItemKind.Tug, id, null, LeashStep.Seen, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) };
        private Task<LeashSendResult> Send(string call, LeashWatch? watch = null) { Calls.Add(call); Watch = watch; return System.Threading.Tasks.Task.FromResult(new LeashSendResult(Reply)); }
        public Task<LeashSendResult> OfferAsync(string id) => Send("offer");
        public Task<LeashSendResult> AssignAsync(string id, AssignKind k, int size, LeashWatch? watch = null) => Send($"assign:{k}:{size}", watch);
        public Task<LeashSendResult> PunishAsync(string id, PunishKind k, int size, LeashWatch? watch = null) => Send($"punish:{k}:{size}", watch);
        public Task<LeashSendResult> RewardAsync(string id, RewardKind k, string? token = null, int? size = null) => Send($"reward:{k}:{token}:{size}");
        public Task<LeashSendResult> TugAsync(string id) => Send("tug");
        public Task<bool> ReleaseAsync(string id) { Calls.Add("release"); return System.Threading.Tasks.Task.FromResult(ReleaseOk); }
        public Task CutAsync() { Calls.Add("cut"); Push(null); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetIntensityAsync(LeashIntensity i) { Calls.Add("level:" + i); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetDndAsync(LeashDnd d) { Calls.Add("dnd:" + d); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetVideoMaxAsync(int n) { Calls.Add("cap:" + n); return System.Threading.Tasks.Task.CompletedTask; }
        public Task SetRemoteModeAsync(LeashRemoteMode m) => System.Threading.Tasks.Task.CompletedTask;
        public Task CompleteAsync(string id) => System.Threading.Tasks.Task.CompletedTask;
        public Task<bool> PardonAsync(string id) => System.Threading.Tasks.Task.FromResult(false);
        public Task<LeashAnswerResult> AnswerAsync(string id, bool yes, LeashIntensity i) => System.Threading.Tasks.Task.FromResult(LeashAnswerResult.Done);
    }
    [Fact]
    public void AcceptedAnswerOpensSnapAndSwapUnhooksOldService() => LeashGateCardTests.Run(false, (shell, _, _) =>
    {
        var oldName = CoreAccount.DisplayNameProvider;
        var first = new Service(); var second = new Service();
        var from = new LeashPerson("friend", "Juno", null);
        var answer = new LeashEvent("1", LeashEventKind.Answered, from, DateTimeOffset.UtcNow, Accepted: true);
        try
        {
            CoreAccount.DisplayNameProvider = () => "  Alex  ";
            LeashHead.Service = first;
            first.Emit(answer with { Accepted = false });
            first.Emit(answer with { Kind = LeashEventKind.Tug });
            Assert.Null(LeashAskWindow.Current);
            first.Emit(answer);
            var snap = Assert.IsType<LeashSnapCard>(LeashAskWindow.Current?.Snap);
            var labels = snap.GetLogicalDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(labels, x => x.Text == Loc.GetF("leash_snap_holder", "Alex"));
            Assert.Contains(labels, x => x.Text == Loc.GetF("leash_snap_leashed", "Juno"));
            LeashAskWindow.CloseCurrent();
            LeashHead.Service = second;
            first.Emit(answer);
            Assert.Null(LeashAskWindow.Current);
            second.Emit(answer);
            Assert.NotNull(LeashAskWindow.Current?.Snap);
        }
        finally { LeashAskWindow.CloseCurrent(); CoreAccount.DisplayNameProvider = oldName; }
    });
    [Fact]
    public void AcceptedAnswerUsesTheVisiblePanelOrLauncherAsOwner() => LeashGateCardTests.Run(false, (shell, launcher, _) =>
    {
        var service = new Service();
        var answer = new LeashEvent("owner", LeashEventKind.Answered, new LeashPerson("friend", "Juno", null), DateTimeOffset.UtcNow, Accepted: true);
        LeashHead.Service = service;
        try
        {
            shell.Show();
            launcher.Show();
            service.Emit(answer);
            Assert.Same(shell, LeashAskWindow.Current?.Owner);
            LeashAskWindow.CloseCurrent();

            shell.Hide();
            service.Emit(answer);
            Assert.Same(launcher, LeashAskWindow.Current?.Owner);
            LeashAskWindow.CloseCurrent();

            shell.Show();
            shell.WindowState = WindowState.Minimized;
            service.Emit(answer);
            Assert.Same(launcher, LeashAskWindow.Current?.Owner);
            LeashAskWindow.CloseCurrent();

            launcher.Hide();
            service.Emit(answer);
            Assert.NotNull(LeashAskWindow.Current?.Snap);
            Assert.Null(LeashAskWindow.Current!.Owner);
            Assert.Equal(WindowStartupLocation.CenterScreen, LeashAskWindow.Current.WindowStartupLocation);
        }
        finally
        {
            LeashAskWindow.CloseCurrent();
            shell.WindowState = WindowState.Normal;
        }
    });

}
