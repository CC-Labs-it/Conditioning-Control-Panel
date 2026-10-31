using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlansInviteActivationTests
{
    private sealed class Api : IInviteApi
    {
        public readonly TaskCompletionSource<InviteMine> Reply = new();
        public int Reads;
        public Task<InviteMine> MineAsync(CancellationToken ct = default) { Reads++; return Reply.Task; }
        public Task<RedeemOutcome> RedeemAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(RedeemOutcome.Offline);
    }

    private static InviteMine Eligible => new(true, new InviteSnapshot(
        new[] { new InviteSlot("BAMBI-AAAA", InviteSlotState.Open, null, null) }, null, 0));

    [Fact]
    public Task OrdinaryAccountNavigationReadsInvitesAndReentryUsesTheThrottle() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var identity = InviteApi.DefaultIdentity;
        var shell = new MainShellWindow();
        try
        {
            InviteApi.DefaultIdentity = () => ("u_me", "token");
            var api = new Api();
            var panel = shell.PlansVaultView!.FindControl<InvitePanel>("InvitesHost")!;
            panel.Api = () => api;
            var read = new TaskCompletionSource<InviteMine>(TaskCreationOptions.RunContinuationsAsynchronously);
            panel.Read += mine => read.TrySetResult(mine);
            shell.Show();
            shell.OpenAppSettingsSection("account");
            Assert.Equal(1, api.Reads);
            Assert.False(panel.IsVisible); // A pending response cannot show an empty padded card.
            api.Reply.SetResult(Eligible);
            await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(panel.IsVisible);
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "BAMBI-AAAA");
            shell.OpenAppSettingsSection("general");
            shell.OpenAppSettingsSection("account");
            Assert.Equal(1, api.Reads); // Ordinary reentry must not force another read inside 30 seconds.
        }
        finally { shell.Close(); InviteApi.DefaultIdentity = identity; }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task PlansModePreservesSignedOutUnreachableAndEligiblePanelVisibility(bool signedIn, bool eligible) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            AmbientBeatLoopsTests.Setup();
            var identity = InviteApi.DefaultIdentity;
            try
            {
                InviteApi.DefaultIdentity = () => signedIn ? ("u_me", "token") : null;
                var plans = new ExclusivesTabView { PlansMode = true };
                var wrapper = plans.FindControl<Border>("InvitesWrapper")!;
                var panel = plans.FindControl<InvitePanel>("InvitesHost")!;
                Assert.Same(panel, wrapper.Child);
                Assert.True(wrapper.IsVisible);
                Assert.False(panel.IsVisible);
                var api = new Api();
                api.Reply.SetResult(eligible ? Eligible : InviteMine.Unreachable);
                panel.Api = () => api;
                await panel.RefreshAsync();
                Assert.Equal(signedIn ? 1 : 0, api.Reads);
                Assert.Equal(eligible, panel.IsVisible);
                plans.PlansMode = false;
                Assert.False(wrapper.IsVisible);
                Assert.Equal(eligible, panel.IsVisible);
                plans.PlansMode = true;
                Assert.True(wrapper.IsVisible);
                Assert.Equal(eligible, panel.IsVisible); // The mode switch cannot override eligibility.
            }
            finally { InviteApi.DefaultIdentity = identity; }
        });
}
