using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>U13 leash-wire: the Core LeashService rides the head's friends poll (WPF App.xaml.cs:2991-3022),
/// seeds Core's seams, and makes the launcher's leash gate (MainShellWindow.LeashBlocksGames) live.</summary>
public sealed class LeashHeadTests
{
    /// <summary>The friends wire: every call answers ok with the given leash block; request bodies are kept.</summary>
    private sealed class Wire : HttpMessageHandler
    {
        public JObject? Leash;
        public readonly List<JObject> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content != null) Bodies.Add(JObject.Parse(await request.Content.ReadAsStringAsync(ct)));
            var o = new JObject { ["ok"] = true, ["online"] = new JArray(), ["inbox"] = new JArray(), ["leash"] = Leash };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(o.ToString(), Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>No leash call may leave the test.</summary>
    internal sealed class NoLeashWire : ILeashApi
    {
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default) => Task.FromResult<JObject?>(new JObject { ["ok"] = true });
    }

    private static JObject P(string id, string name) => new() { ["id"] = id, ["name"] = name, ["avatar"] = null };

    internal static JObject BlockWithPunishment()
    {
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        var pun = new JObject
        {
            ["pid"] = "p1", ["kind"] = "lines", ["size"] = 3, ["from"] = P("u_vex", "Vex"),
            ["at"] = at.ToString("o"), ["expires_at"] = at.AddHours(72).ToString("o"),
        };
        var me = new JObject
        {
            ["holder"] = P("u_vex", "Vex"), ["intensity"] = "strict", ["since"] = at.AddDays(-2).ToString("o"), ["day"] = 3,
            ["dnd_until"] = null, ["remote_mode"] = "ask", ["pending"] = new JArray { pun },
            ["assignment"] = null, ["pardons"] = 1, ["stickers"] = new JArray(),
        };
        return new JObject { ["me"] = me, ["holding"] = new JArray(), ["offers"] = new JArray(), ["events"] = new JArray() };
    }

    [Fact]
    public async Task APendingPunishmentOnTheFriendsPollBlocksTheLauncherUntilSignOutOrExit()
    {
        var (oldGet, oldPut) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();
        (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (n => secrets.GetValueOrDefault(n), (n, v) => secrets[n] = v);
        var s = CoreSettings.Current;
        var (oldId, oldOffline) = (s.UnifiedId, s.OfflineMode);
        var (oldTargets, oldUi, oldTimer, oldGuard) = (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi, LeashTaskRunner.TimerFactory, LeashGuard.IsLeashed);
        var (oldService, oldDue, oldPresent) = (LeashHead.Service, MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider);
        try
        {
            MainShellWindow.PresentLeashGateProvider = null;   // LeashHead.Start brings the panel's gate
            // AuthToken lives in CoreSecrets, which is swapped for `secrets` above and restored in finally:
            // the write never reaches the real store, so there is no old token to put back.
            (s.UnifiedId, s.AuthToken, s.OfflineMode) = ("u_me", "tok", false);
            LeashTaskRunner.TimerFactory = null;
            var wire = new Wire();
            var friends = new FriendsService(new FriendsApi(new HttpClient(wire), FriendsHead.Identity, "http://127.0.0.1:9"),
                () => FriendsHead.Identity()?.UnifiedId, () => true);

            var svc = LeashHead.Start(friends, null, null, new NoLeashWire());

            Assert.NotNull(svc);
            Assert.Same(LeashHead.Targets.Instance, LeashCutSafety.LiveTargets);
            Assert.NotNull(LeashCutSafety.RunOnUi);
            Assert.NotNull(LeashTaskRunner.TimerFactory);
            Assert.NotNull(MainShellWindow.PresentLeashGateProvider);
            Assert.False(MainShellWindow.LeashBlocksGames);
            Assert.False(LeashGuard.IsLeashed());

            wire.Leash = BlockWithPunishment();
            await friends.TickAsync();           // the block comes back on the friends poll
            Assert.True(MainShellWindow.LeashBlocksGames);
            Assert.True(LeashGuard.IsLeashed());
            MainShellWindow.PresentLeashGateProvider = null;   // no gate on the panel: never hold a tile
            Assert.False(MainShellWindow.LeashBlocksGames);
            MainShellWindow.PresentLeashGateProvider = _ => { };

            await friends.TickAsync();           // and the report goes out on the next one
            Assert.NotNull(wire.Bodies.FindLast(b => b["shared"] != null)?["leash_report"]);

            s.UnifiedId = null;                  // signed out: nothing is due for nobody
            Assert.False(MainShellWindow.LeashBlocksGames);
            s.UnifiedId = "u_me";                // back in: the next poll brings the block again
            await friends.TickAsync();
            Assert.True(MainShellWindow.LeashBlocksGames);

            LeashHead.Stop();                    // WPF App.OnExit
            Assert.Null(LeashHead.Service);
            Assert.Null(MainShellWindow.PresentLeashGateProvider);
            Assert.False(MainShellWindow.LeashBlocksGames);
        }
        finally
        {
            (s.UnifiedId, s.OfflineMode) = (oldId, oldOffline);
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldPut);
            (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi, LeashTaskRunner.TimerFactory, LeashGuard.IsLeashed) = (oldTargets, oldUi, oldTimer, oldGuard);
            (LeashHead.Service, MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider) = (oldService, oldDue, oldPresent);
        }
    }

    [Fact]
    public void ASandboxWithoutALoopbackFriendsUrlBuildsNoLeash()
    {
        var (oldService, oldDue) = (LeashHead.Service, MainShellWindow.LeashGateDueProvider);
        try
        {
            MainShellWindow.LeashGateDueProvider = null;
            var friends = new FriendsService(new FriendsApi(new HttpClient(new Wire()), () => null, "http://127.0.0.1:9"), () => null);
            Assert.Null(LeashHead.Start(friends, "/tmp/sandbox", null, new NoLeashWire()));
            Assert.Null(MainShellWindow.LeashGateDueProvider);
        }
        finally { (LeashHead.Service, MainShellWindow.LeashGateDueProvider) = (oldService, oldDue); }
    }

    /// <summary>P04/P11: App startup builds the leash on the friends poll and drops it on exit.</summary>
    [Fact]
    public void AppStartupStartsTheLeashOnTheFriendsPollAndStopsItOnExit()
    {
        var src = Regex.Replace(   // P23: comments and string literals never count
            File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);
        Assert.Matches(@"friends\.Start\([^;]*;\s*try\s*\{\s*Platform\.LeashHead\.Start\(friends,", src);
        Assert.Contains("Platform.LeashHead.Stop()", src);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
