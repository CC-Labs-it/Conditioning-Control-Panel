using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Buying an enhancement (WPF MainWindow.Enhancements.cs SkillCard_Click over SkillTreeService /
/// ProfileSyncService.PurchaseSkillAsync) against a fake server: never the network.</summary>
public sealed class EnhancementPurchaseTests
{
    private sealed class Wire : HttpMessageHandler
    {
        public readonly Queue<(HttpStatusCode, string)> Replies = new();
        public readonly List<(string? Token, string Body)> Seen = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.EndsWith("/v2/user/purchase-skill", r.RequestUri!.AbsolutePath);
            Seen.Add((r.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null, await r.Content!.ReadAsStringAsync(ct)));
            var (status, json) = Replies.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>A fresh settings service, a signed-in wallet of 1000 and a temp achievement engine as App.Achievements.</summary>
    private static async Task WithAccount(Func<AppSettings, AchievementEngine, Task> body)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldEngine = AchievementsProp.GetValue(null);
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-skill-purchase-").FullName;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            AchievementsProp.SetValue(null, engine);
            var s = CoreSettings.Current;
            (s.UnifiedId, s.AuthToken, s.SkillPoints, s.UnlockedSkills) = ("u1", "tok", 1000, new List<string>());
            (s.SeasonStatsSeason, s.SeasonPointsSpent) = ("2035-02", 5);
            await body(s, engine);
        }
        finally
        {
            AchievementsProp.SetValue(null, oldEngine);
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
            CoreSettings.ServiceProvider = oldSettings;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static SkillDefinition Root => SkillDefinition.All.First(k => !k.IsSecret && string.IsNullOrEmpty(k.PrerequisiteId));

    private static string Ok(int points, string id, long lifetime) =>
        $"{{\"success\":true,\"skill_points\":{points},\"unlocked_skills\":[\"{id}\"],\"lifetime_points_spent\":{lifetime}}}";

    [Fact]
    public Task BalanceRefusalSyncsOnceThenBuysAndCountsPrestigeAndSeason() => AvaloniaTestDispatcher.RunAsync(() => WithAccount(async (s, engine) =>
    {
        var skill = Root;
        var wire = new Wire();
        wire.Replies.Enqueue((HttpStatusCode.OK, $"{{\"success\":false,\"error\":\"Not enough\",\"skill_points\":{skill.Cost - 1}}}"));
        wire.Replies.Enqueue((HttpStatusCode.OK, Ok(1000 - skill.Cost, skill.Id, 250)));
        int syncs = 0;
        var (ok, error) = await SkillPurchase.PurchaseAsync(new V2AuthService(handler: wire), skill.Id, engine,
            () => { syncs++; return Task.FromResult(true); });

        Assert.True(ok, error);
        Assert.Equal(1, syncs);                                   // #1300: one sync, then asked again
        Assert.Equal(2, wire.Seen.Count);
        Assert.All(wire.Seen, r => Assert.Equal("tok", r.Token));
        Assert.Contains("\"skill_points\":1000", wire.Seen[0].Body);   // the local balance, for the server to reconcile
        Assert.Equal(1000 - skill.Cost, s.SkillPoints);           // the server's balance replaces local
        Assert.Contains(skill.Id, s.UnlockedSkills);
        Assert.Equal(250, engine.Progress.LifetimeSkillPointsSpent);   // tracked, then reconciled up to the server total
        Assert.Equal(5 + skill.Cost, s.SeasonPointsSpent);
    }));

    [Fact]
    public Task RefusalThatSurvivesTheSyncAdoptsAndExpiredSessionSaysSo() => AvaloniaTestDispatcher.RunAsync(() => WithAccount(async (s, engine) =>
    {
        var skill = Root;
        var wire = new Wire();
        var refusal = $"{{\"success\":false,\"error\":\"Not enough\",\"skill_points\":{skill.Cost - 1}}}";
        wire.Replies.Enqueue((HttpStatusCode.OK, refusal));
        wire.Replies.Enqueue((HttpStatusCode.OK, refusal));
        var auth = new V2AuthService(handler: wire);
        var (ok, error) = await SkillPurchase.PurchaseAsync(auth, skill.Id, engine, () => Task.FromResult(true));
        Assert.False(ok);
        Assert.Equal("Not enough", error);
        Assert.Equal(skill.Cost - 1, s.SkillPoints);              // only a refusal that survives the sync lowers the wallet
        Assert.Empty(s.UnlockedSkills);
        Assert.Equal(0, engine.Progress.LifetimeSkillPointsSpent);

        s.SkillPoints = 1000;
        wire.Replies.Enqueue((HttpStatusCode.Unauthorized, "{\"error\":\"bad token\"}"));
        (ok, error) = await SkillPurchase.PurchaseAsync(auth, skill.Id, engine, () => Task.FromResult(true));
        Assert.False(ok);
        Assert.StartsWith("Your session has expired.", error);
        Assert.Equal(1000, s.SkillPoints);
    }));

    [Fact]
    public Task KeyboardBuyOnAPurchasableNodeConfirmsBuysBurstsAndRepaintsOwned() => AvaloniaTestDispatcher.RunAsync(() => WithAccount(async (s, engine) =>
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current is null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else builder.AfterSetupCallback(builder);
        var (oldAuth, oldAsk, oldTell) = (EnhancementsTabView.Auth, EnhancementsTabView.Ask, EnhancementsTabView.Tell);
        Window? w = null;
        try
        {
            var skill = Root;
            s.MotionLevel = MotionLevel.Full;
            engine.Progress.LifetimeSkillPointsSpent = 99;        // this purchase crosses a hundred: prestige rank-up
            var wire = new Wire();
            wire.Replies.Enqueue((HttpStatusCode.OK, Ok(1000 - skill.Cost, skill.Id, 99 + skill.Cost)));
            EnhancementsTabView.Auth = () => new V2AuthService(handler: wire);
            var asked = new List<string>();
            EnhancementsTabView.Ask = (_, title, message) => { asked.Add(title + "|" + message); return Task.FromResult(true); };
            EnhancementsTabView.Tell = (_, _, m) => throw new Xunit.Sdk.XunitException("unexpected failure dialog: " + m);

            var tab = new EnhancementsTabView();
            w = new Window { Width = 1400, Height = 700, Content = tab };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Control NodeOf(string id) => tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Single(c => Equals(c.Tag, id));

            var node = NodeOf(skill.Id);
            Assert.True(node.Focusable);
            Assert.NotNull(node.Cursor);
            node.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.NotNull(tab.LastPurchase);
            await tab.LastPurchase!;
            Dispatcher.UIThread.RunJobs();

            var question = Assert.Single(asked);
            Assert.Contains($"{skill.Cost}", question);
            Assert.DoesNotContain("★", question);                 // icon guard: the note's star is stripped
            Assert.Single(wire.Seen);
            Assert.Contains(skill.Id, s.UnlockedSkills);
            Assert.Equal((1, 1), tab.Bursts);                     // node burst + prestige burst
            Assert.NotSame(node, NodeOf(skill.Id));               // repainted
            Assert.False(NodeOf(skill.Id).Focusable);             // owned: no longer a buy target
        }
        finally
        {
            (EnhancementsTabView.Auth, EnhancementsTabView.Ask, EnhancementsTabView.Tell) = (oldAuth, oldAsk, oldTell);
            try { w?.Close(); } catch { }
        }
    }));
}
