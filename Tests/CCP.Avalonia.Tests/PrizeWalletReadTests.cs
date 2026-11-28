using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PrizeWalletReadTests
{
    [Theory]
    [InlineData("new", "old", 1, true)]
    [InlineData("same", "same", 9, false)]
    [InlineData("same", "same", 10, true)]
    [InlineData("", "old", 10, false)]
    public void Due(string account, string last, int minutes, bool expected)
    {
        var now = DateTime.UtcNow;
        Assert.Equal(expected, PrizeWalletRead.WalletReadDue(account, last, now.AddMinutes(-minutes), now));
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal int Count;
        internal string Reply = "{\"ok\":false}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply) });
        }
    }

    [Fact]
    public void ReadRetriesFailureAndOnlyRaisesBalanceAndAppliesGrants() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, GameWindow.CounterHttp, GameWindow.CounterServerBase);
        var wire = new Wire();
        try
        {
            s.UnifiedId = "wallet-test"; s.OfflineMode = false; s.SkillPoints = 100;
            CoreAccount.UnifiedUserId = "wallet-test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "token" : null;
            GameWindow.CounterHttp = () => new HttpClient(wire, false);
            GameWindow.CounterServerBase = () => "http://shop.test";
            PrizeOwnership.ResetHeld();
            var read = new PrizeWalletRead();
            read.Refresh().GetAwaiter().GetResult();
            wire.Reply = "{\"ok\":true,\"sp\":90,\"prizes\":{\"revision\":1,\"grants\":[\"test.prize\"]}}";
            read.Refresh().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, wire.Count);
            Assert.Equal(100, s.SkillPoints);
            Assert.Contains("test.prize", PrizeOwnership.Grants);
            read.Refresh().GetAwaiter().GetResult();
            Assert.Equal(2, wire.Count);
            wire.Reply = "{\"ok\":true,\"sp\":150}";
            new PrizeWalletRead().Refresh().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(150, s.SkillPoints);
        }
        finally
        {
            (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, GameWindow.CounterHttp, GameWindow.CounterServerBase) = old;
            PrizeOwnership.ResetHeld();
        }
    });

    [Fact]
    public void ShellReadsAtInitAndOnProfileLoadedThenUnhooks() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, CoreSecrets.RetrieveProvider, GameWindow.CounterHttp, GameWindow.CounterServerBase);
        var wire = new Wire();
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        try
        {
            s.UnifiedId = "wallet-shell-test"; s.OfflineMode = false;
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "token" : null;
            GameWindow.CounterHttp = () => new HttpClient(wire, false);
            GameWindow.CounterServerBase = () => "http://shop.test";
            shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            shell.Show();
            Assert.Equal(1, wire.Count);
            var field = typeof(AccountSeed).GetField("ProfileLoaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var handler = Assert.Single(((Delegate)field.GetValue(null)!).GetInvocationList(), d => ReferenceEquals(d.Target, shell) && d.Method.Name == "ReadPrizeWallet");
            handler.DynamicInvoke();
            Assert.Equal(2, wire.Count);
            shell.Close();
            Assert.DoesNotContain(((Delegate?)field.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>(), d => ReferenceEquals(d.Target, shell) && d.Method.Name == "ReadPrizeWallet");
            shell = null;
        }
        finally
        {
            shell?.Close();
            (s.UnifiedId, s.OfflineMode, CoreSecrets.RetrieveProvider, GameWindow.CounterHttp, GameWindow.CounterServerBase) = old;
        }
    });

    [Fact]
    public void OfflineAndSignedOutSendNothing() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, CoreSecrets.RetrieveProvider, GameWindow.CounterHttp, GameWindow.CounterServerBase);
        var wire = new Wire();
        try
        {
            GameWindow.CounterHttp = () => new HttpClient(wire, false);
            GameWindow.CounterServerBase = () => "http://shop.test";
            CoreSecrets.RetrieveProvider = _ => "token";
            s.UnifiedId = "wallet-test"; s.OfflineMode = true;
            var read = new PrizeWalletRead();
            read.Refresh().GetAwaiter().GetResult();
            s.OfflineMode = false; s.UnifiedId = null;
            read.Refresh().GetAwaiter().GetResult();
            Assert.Equal(0, wire.Count);
        }
        finally { (s.UnifiedId, s.OfflineMode, CoreSecrets.RetrieveProvider, GameWindow.CounterHttp, GameWindow.CounterServerBase) = old; }
    });
}
