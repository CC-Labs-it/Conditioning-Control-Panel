using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core StreakFix, the rules both heads' "Fix day" use (WPF MainWindow.QuestsTab.cs BtnFixStreak_Click /
/// StreakFixDay_Click) and the Avalonia head's spend (WPF ProfileSyncService.UseOopsieInsuranceAsync).</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class StreakFixTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-streakfix-").FullName;
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();

    public StreakFixTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, string? Token, string Body)> Seen = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"oopsie_credits\":3}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!.AbsoluteUri, r.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null,
                r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    [Fact]
    public void HasMissedDays_IsAnyEarlierDayOfThisMonthWithoutADaily()
    {
        var today = new DateTime(2026, 10, 4);
        var progress = new QuestProgress();
        Assert.False(StreakFix.HasMissedDays(progress, new DateTime(2026, 10, 1)));   // the 1st has nothing before it
        Assert.True(StreakFix.HasMissedDays(progress, today));
        progress.DailyQuestCompletionDates.AddRange(new[] { new DateTime(2026, 10, 1, 9, 0, 0), new DateTime(2026, 10, 2), new DateTime(2026, 10, 3) });
        Assert.False(StreakFix.HasMissedDays(progress, today));                       // today itself is not "missed"
        Assert.True(StreakFix.HasMissedDays(null, today));
    }

    [Fact]
    public void WireDate_IsInvariantUnderANonGregorianCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");   // Buddhist calendar: year 2569
            Assert.Equal("2026-10-04", StreakFix.WireDate(new DateTime(2026, 10, 4)));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void ApplySpent_TakesTheServerBalanceStampsTheDayOnceAndRecounts()
    {
        using var quests = new QuestService(null, _dir);
        quests.Progress.DailyQuestCompletionDates.Clear();
        var settings = new AppSettings { StreakFixCharges = 4 };
        var day = DateTime.Today.AddDays(-1);

        StreakFix.ApplySpent(settings, quests, day.AddHours(13), credits: 2);
        Assert.Equal(2, settings.StreakFixCharges);
        Assert.True(settings.SeasonalStreakRecoveryUsed);
        Assert.Single(quests.Progress.DailyQuestCompletionDates, d => d == day);

        StreakFix.ApplySpent(settings, quests, day, credits: null);   // an older server: one off ours, no second stamp
        Assert.Equal(1, settings.StreakFixCharges);
        Assert.Single(quests.Progress.DailyQuestCompletionDates, d => d.Date == day);

        settings.StreakFixCharges = 0;
        StreakFix.ApplySpent(settings, null, day, credits: null);
        Assert.Equal(0, settings.StreakFixCharges);                    // never below zero
    }

    [Fact]
    public async Task UseAsync_PostsTheWpfBodyAndReadsTheServerAnswer()
    {
        var fake = new Fake();
        var settings = new AppSettings { UnifiedId = "u_abc", AuthToken = "tok" };
        var fix = new StreakFix(() => settings, fake);

        var (ok, error, credits) = await fix.UseAsync(new DateTime(2026, 10, 4));
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(3, credits);
        var (url, token, body) = Assert.Single(fake.Seen);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/use-oopsie", url);
        Assert.Equal("tok", token);
        Assert.Equal("{\"unified_id\":\"u_abc\",\"fix_date\":\"2026-10-04\"}", body);

        fake.Status = HttpStatusCode.BadRequest;
        fake.Body = "{\"error\":\"No charges\"}";
        Assert.Equal((false, "No charges", (int?)null), await fix.UseAsync(DateTime.Today));
        fake.Body = "oops";
        Assert.Equal((false, "Server error: BadRequest", (int?)null), await fix.UseAsync(DateTime.Today));

        // Signed out: refused before anything reaches the wire.
        settings.UnifiedId = null;
        var (signedOut, why, _) = await fix.UseAsync(DateTime.Today);
        Assert.False(signedOut);
        Assert.Contains("cloud account", why);
        Assert.Equal(3, fake.Seen.Count);
    }
}
