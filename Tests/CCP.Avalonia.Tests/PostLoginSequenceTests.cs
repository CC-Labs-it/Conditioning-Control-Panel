using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public void FailedPostLoginReadStartsHeartbeatAndRetriesOnce() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var pending = AccountSeed.PostLoginAsync();
        Assert.True(sync.HeartbeatRunning);
        lock (wire.Seen) Assert.DoesNotContain(wire.Seen, r => r.Path == "GET /v2/user/profile");
        await pending;
        lock (wire.Seen)
            Assert.Equal(2, wire.Seen.Count(r => r.Path == "GET /v2/user/profile"));
        Assert.Empty(wire.Syncs);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostLoginSuppressesPopupsAndRestoresAfterLoad(bool throws) => WithFreshInstall(async () =>
    {
        SignIn("u1", null);
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
        async Task<bool> Load()
        {
            Assert.True(engine.SuppressPopups);
            await Task.Yield();
            if (throws) throw new InvalidOperationException();
            return true;
        }
        if (throws) await Assert.ThrowsAsync<InvalidOperationException>(() => AccountSeed.PostLoginAsync(Load, engine));
        else await AccountSeed.PostLoginAsync(Load, engine);
        Assert.False(engine.SuppressPopups);
    });
}

public sealed class PostLoginRepaintTests
{
    [Fact]
    public Task ProfileLoadRepaintsOgName() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var old = (s.UserDisplayName, s.IsSeason0Og);
        var loggedIn = CoreAccount.IsLoggedInProvider;
        CoreAccount.IsLoggedInProvider = () => true;
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            var load = new TaskCompletionSource();
            var refresh = shell.RefreshAfterLoginAsync(load.Task);
            s.UserDisplayName = "Adopted";
            s.IsSeason0Og = true;
            load.SetResult();
            await refresh;
            Assert.Equal("⭐ Adopted", shell.SettingsPage!.FindControl<global::Avalonia.Controls.TextBlock>("TxtLoggedInName")!.Text);
        }
        finally
        {
            shell.Close();
            (s.UserDisplayName, s.IsSeason0Og) = old;
            CoreAccount.IsLoggedInProvider = loggedIn;
        }
    });
}
