using System;
using Xunit;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class SparkleWalletBootReadTests
{
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"sp\":73}") });
        }
    }

    [Fact]
    public void ReadRaisesWalletAndIsGated() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.SkillPoints);
        var oldSecrets = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        CoreSecrets.RetrieveProvider = _ => "test"; CoreSecrets.StoreProvider = (_, _) => { };
        var oldHttp = GameWindow.CounterHttp; var oldUrl = GameWindow.CounterServerBase;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        try
        {
            s.UnifiedId = Guid.NewGuid().ToString(); s.OfflineMode = false; s.SkillPoints = 1;
            GameWindow.CounterHttp = () => http; GameWindow.CounterServerBase = () => "http://localhost";
            SparkleWalletBootRead.Refresh(); SparkleWalletBootRead.Refresh();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref handler.Calls) == 1, 5000));
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return s.SkillPoints == 73; }, 5000));
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            GameWindow.CounterHttp = oldHttp; GameWindow.CounterServerBase = oldUrl;
            (s.UnifiedId, s.OfflineMode, s.SkillPoints) = old;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = oldSecrets;
        }
    });

    [Fact]
    public void ShellHooksAndUnhooksProfileLoaded() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        bool Subscribed() => (typeof(AccountSeed).GetField("ProfileLoaded", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) as Delegate)?.GetInvocationList().Any(d => ReferenceEquals(d.Target, shell) && d.Method.Name == "OnSparkleWalletProfileLoaded") == true;
        try { Assert.True(Subscribed()); shell.Show(); shell.Close(); Assert.False(Subscribed()); }
        finally { shell.Close(); }
    });

    [Fact]
    public void DueOncePerAccountAndAgainAfterTenMinutes()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(SparkleWalletBootRead.WalletReadDue("a", "a", now, now.AddMinutes(9)));
        Assert.True(SparkleWalletBootRead.WalletReadDue("a", "a", now, now.AddMinutes(10)));
        Assert.True(SparkleWalletBootRead.WalletReadDue("b", "a", now, now));
        Assert.False(SparkleWalletBootRead.WalletReadDue(null, "a", now, now));
    }
}
