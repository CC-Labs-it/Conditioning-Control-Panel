using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
[Collection(CoreSecretsStatics.Name)]
public class SkillPurchaseTests : IDisposable
{
    private readonly Func<string, string?>? _get = ConditioningControlPanel.CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _set = ConditioningControlPanel.CoreSecrets.StoreProvider;
    public SkillPurchaseTests()
    {
        var secrets = new Dictionary<string, string?>();
        (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (n => secrets.GetValueOrDefault(n), (n, v) => secrets[n] = v);
    }
    public void Dispose() => (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (_get, _set);
    private sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Answers = new();
        public readonly List<string> Bodies = new();
        public readonly List<string?> Tokens = new();
        public int SyncCalls; public HttpStatusCode SyncStatus = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sync")) { SyncCalls++; return Json(SyncStatus, "{}")(); }
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            Tokens.Add(request.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null);
            Assert.EndsWith("/v2/user/purchase-skill", request.RequestUri!.AbsolutePath);
            return Answers.Dequeue()();
        }
    }
    private static Func<HttpResponseMessage> Json(HttpStatusCode code, string json) =>
        () => new HttpResponseMessage(code) { Content = new StringContent(json.Replace('\'', '"'), Encoding.UTF8, "application/json") };
    private static readonly SkillDefinition Skill = SkillDefinition.All.First(s => !s.IsSecret && string.IsNullOrEmpty(s.PrerequisiteId));
    private static AppSettings Settings(int points, string? id = "u_1") => new() { SkillPoints = points, UnifiedId = id, AuthToken = "tok", UnlockedSkills = new List<string>() };
    private static (SkillPurchase service, Fake http, List<string> saves) Make(AppSettings s)
    {
        var (http, saves) = (new Fake(), new List<string>());
        return (new SkillPurchase(() => s, http) { Save = () => saves.Add("save") }, http, saves);
    }
    [Fact]
    public async Task ADebitedAnswerLowersTheWalletAndUnlocksTheSkill()
    {
        var s = Settings(Skill.Cost + 5);
        (s.CurrentSeason, s.SeasonStatsSeason) = ("2026-10", null);
        var (service, http, saves) = Make(s);
        int spent = 0; long reconciled = 0; string? unlocked = null;
        service.PointsSpent = c => spent += c;
        service.LifetimeSpentReconciled = t => reconciled = t;
        service.SkillUnlocked += (_, id) => unlocked = id;
        http.Answers.Enqueue(Json(HttpStatusCode.OK,
            "{'success':true,'skill_points':5,'unlocked_skills':['" + Skill.Id + "'],'lifetime_points_spent':321}"));
        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(5, s.SkillPoints);
        Assert.Contains(Skill.Id, s.UnlockedSkills);
        Assert.Equal(Skill.Cost, spent);
        Assert.Equal(Skill.Cost, s.SeasonPointsSpent);
        Assert.Equal("2026-10", s.SeasonStatsSeason);   // WPF EnsureBucket on the first spend
        Assert.Equal(321, reconciled);
        Assert.Equal(Skill.Id, unlocked);
        Assert.NotEmpty(saves);
        Assert.Contains("skill_points", http.Bodies.Single());
        Assert.Contains((Skill.Cost + 5).ToString(), http.Bodies.Single());
    }
    [Fact]
    public async Task RecoveredTokenRetriesOnceAndChangedAccountDiscardsReceipt()
    {
        var s = Settings(Skill.Cost);
        var (service, http, _) = Make(s);
        service.Unauthorized = _ => { s.AuthToken = "recovered"; return Task.FromResult(true); };
        http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));
        var receipt = Json(HttpStatusCode.OK, "{'success':true,'skill_points':0}")();
        http.Answers.Enqueue(() => receipt);
        var result = await service.PurchaseSkillAsync(Skill.Id);
        Assert.True(result.Success, result.Error);
        Assert.Equal(new[] { "tok", "recovered" }, http.Tokens);
        Assert.Throws<ObjectDisposedException>(() => receipt.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        s.SkillPoints = Skill.Cost;
        http.Answers.Enqueue(() =>
        {
            s.UnifiedId = "another-account";
            return Json(HttpStatusCode.OK, "{'success':true,'skill_points':0}")();
        });
        Assert.False((await service.PurchaseSkillAsync(Skill.Id)).Success);
        Assert.Equal(Skill.Cost, s.SkillPoints);
    }
    [Fact]
    public void ImmediateEffectsGrantShieldAndPersistInsurance()
    {
        var s = Settings(0);
        var (service, _, saves) = Make(s);
        service.ApplySkillEffects(s, "good_girl_streak");
        Assert.Equal(1, s.StreakShieldsRemaining);
        Assert.Equal(DateTime.UtcNow.Date, s.LastStreakShieldResetDate);
        var before = s.StreakFixCharges;
        service.ApplySkillEffects(s, "oopsie_insurance");
        Assert.Equal(before + 1, s.StreakFixCharges);
        Assert.Single(saves);
    }
    [Fact]
    public async Task BalanceRefusalNeedsARealSyncBeforeAdoptingTheServerWallet()
    {
        var s = Settings(Skill.Cost);
        var (service, http, _) = Make(s);
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':false,'skill_points':0}"));
        Assert.False((await service.PurchaseSkillAsync(Skill.Id)).Success);
        Assert.Equal(Skill.Cost, s.SkillPoints);
        service.SyncBeforeRetry = () => Task.FromResult(true);
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':false,'skill_points':0}"));
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':false,'skill_points':0}"));
        Assert.False((await service.PurchaseSkillAsync(Skill.Id)).Success);
        Assert.Equal(0, s.SkillPoints);
    }
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData((HttpStatusCode)429, false)]
    [InlineData(HttpStatusCode.Conflict, false)]
    public async Task PurchaseAfterLevelUpWaitsForARealSync(HttpStatusCode status, bool expected)
    {
        var (oldSettings, oldLogin) = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider);
        var store = new SettingsService(); CoreSettings.ServiceProvider = () => store; CoreAccount.IsLoggedInProvider = () => true;
        var s = store.Current; s.UnlockedSkills = new(); (s.UnifiedId, s.AuthToken, s.SkillPoints, s.LastConfirmedServerXp, s.OfflineMode) = ("retry", "tok", Skill.Cost + 5, 0, false);
        var (purchase, http, _) = Make(s); http.SyncStatus = status;
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, http);
        var now = DateTime.UtcNow; int waits = 0; sync.UtcNow = () => now;
        sync.PurchaseRetryDelay = delay => { Assert.InRange(delay, TimeSpan.Zero, SyncPush.Cooldown + TimeSpan.FromMilliseconds(250)); waits++; now += delay; return Task.CompletedTask; };
        try
        {
            sync.MarkLoaded(null); await sync.PushAsync("level-up"); purchase.SyncBeforeRetry = sync.SyncBeforeRetryAsync;
            http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':false,'skill_points':0}"));
            http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':true,'skill_points':5}"));
            Assert.Equal(expected, (await purchase.PurchaseSkillAsync(Skill.Id)).Success);
            Assert.Equal(expected ? 5 : Skill.Cost + 5, s.SkillPoints);
            Assert.Equal(1, waits); Assert.Equal(2, http.SyncCalls);
        }
        finally { sync.Reset(); store.SaveImmediate(); (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider) = (oldSettings, oldLogin); }
    }
    [Fact]
    public async Task ClientFailuresUseLocalizedMessages()
    {
        var language = LocalizationManager.Instance.CurrentLanguage;
        LocalizationManager.Instance.SetLanguage("fr");
        try
        {
            foreach (var (status, json, key) in new[] { (401, "{}", "login_session_expired"), (500, "{}", "dialog_purchase_failed"), (200, "null", "skill_err_invalid_response"), (200, "{'success':false}", "dialog_purchase_failed"), (0, "", "wizard_connect_failed") })
            {
                var (service, http, _) = Make(Settings(Skill.Cost));
                http.Answers.Enqueue(status == 0 ? () => throw new HttpRequestException() : Json((HttpStatusCode)status, json));
                Assert.StartsWith(Loc.Get(key), (await service.PurchaseSkillAsync(Skill.Id)).Error!);
            }
        }
        finally { LocalizationManager.Instance.SetLanguage(language); }
    }
    [Fact]
    public void LifetimeSpendOnlyRises()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "k2-ach-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(dir, "achievements.json"))) { SuppressPopups = true };
            engine.TrackSkillPointsSpent(40);
            engine.ReconcileLifetimePointsSpent(30);
            Assert.Equal(40, engine.Progress.LifetimeSkillPointsSpent);
            engine.ReconcileLifetimePointsSpent(120);
            Assert.Equal(120, engine.Progress.LifetimeSkillPointsSpent);
            Assert.True(engine.Progress.IsUnlocked("window_shopping"));
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
