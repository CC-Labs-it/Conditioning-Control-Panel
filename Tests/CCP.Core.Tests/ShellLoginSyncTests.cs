using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public class ShellLoginSyncTests
{
    [Fact]
    public void NoticePreservesAccountWideDescentMeaning()
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        try { loc.SetLanguage("en"); Assert.Equal("+1 XP from your web account. Descending is account-wide now.", loc.GetF("web_xp_claim_notice", "1")); }
        finally { loc.SetLanguage(language); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedClaimChecksAccountAndSessionBeforeRecordingOrPaying(bool sameAccount)
    {
        var old = (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreAccount.IsLoggedInProvider);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = System.Threading.Timeout.Infinite };
        var queue = new Queue<Action>();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreDispatch.PostProvider = queue.Enqueue;
            var s = service.Current;
            s.UnifiedId = "first";
            s.LastWebXpClaimId = null;
            var sync = new SyncPush(() => null, () => false);
            sync.MarkLoaded(null);
            var claim = JObject.Parse("""{"web_xp":{"claim":{"id":"queued","amount":1}}}""");
            var xp = ProfileAdopt.TotalXp(s);
            sync.ClaimWebXp(s, claim);
            Assert.Null(s.LastWebXpClaimId);
            if (sameAccount) { sync.Reset(); sync.MarkLoaded(null); }
            else s.UnifiedId = "second";
            while (queue.TryDequeue(out var pay)) pay();
            Assert.Null(s.LastWebXpClaimId);
            Assert.Equal(xp, ProfileAdopt.TotalXp(s));
            sync.ClaimWebXp(s, claim);
            sync.ClaimWebXp(s, claim);
            while (queue.TryDequeue(out var pay)) pay();
            Assert.Equal("queued", s.LastWebXpClaimId);
            Assert.Equal(xp + 1, ProfileAdopt.TotalXp(s));
        }
        finally
        {
            service.SealForReset();
            (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreAccount.IsLoggedInProvider) = old;
        }
    }

    [Fact]
    public void FirstWebClaimNotifiesOnceOnUiThreadWithWpfCopyAndDuration()
    {
        var old = (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreProgram.NotifyProvider,
            CoreAccount.IsLoggedInProvider);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = System.Threading.Timeout.Infinite };
        var queue = new Queue<Action>();
        var notices = new List<(string Text, string Kind, TimeSpan Duration)>();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreDispatch.PostProvider = queue.Enqueue;
            CoreProgram.NotifyProvider = (text, kind, duration) => notices.Add((text, kind, duration));
            var s = service.Current;
            s.SeenFeatureIntros.Remove("web-xp");
            ProgressionBank.AddClaimed(1);
            ProgressionBank.AddClaimed(2);
            Assert.Empty(notices);
            Assert.DoesNotContain("web-xp", s.SeenFeatureIntros);
            while (queue.TryDequeue(out var show)) show();
            var notice = Assert.Single(notices);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("web_xp_claim_notice", "1"), notice.Text);
            Assert.Equal("Success", notice.Kind);
            Assert.Equal(TimeSpan.FromSeconds(9), notice.Duration);
            Assert.Contains("web-xp", s.SeenFeatureIntros);
            Assert.True(service.HasPendingSave);
            ProgressionBank.AddClaimed(0);
            Assert.Empty(queue);
        }
        finally
        {
            service.SealForReset();
            (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreProgram.NotifyProvider,
                CoreAccount.IsLoggedInProvider) = old;
        }
    }

}
