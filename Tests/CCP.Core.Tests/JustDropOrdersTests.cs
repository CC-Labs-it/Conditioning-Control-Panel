using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class JustDropOrdersTests
{
    [Fact]
    public async Task DrawerCachesSuccessfulReadsAndInvalidatesBeforeNotifying()
    {
        var secret = CoreSecrets.RetrieveProvider;
        var identity = CoreAccount.UnifiedUserId;
        var fetch = JustDropOrdersService.FetchOverride;
        int calls = 0;
        try
        {
            CoreSecrets.RetrieveProvider = _ => "test-token";
            CoreAccount.UnifiedUserId = "test-account";
            JustDropOrdersService.Invalidate();
            JustDropOrdersService.FetchOverride = (id, token, _) =>
            {
                Assert.Equal("test-account", id); Assert.Equal("test-token", token);
                calls++;
                return Task.FromResult<string?>("{\"orders\":[{\"code\":\"A\",\"sizeId\":\"M\"}]}");
            };
            Assert.Equal(15, Assert.Single(await JustDropOrdersService.FetchAsync()).Minutes);
            await JustDropOrdersService.FetchAsync(); Assert.Equal(1, calls);
            Task? refresh = null;
            void Changed() => refresh = JustDropOrdersService.FetchAsync();
            JustDropOrdersService.DrawerChanged += Changed;
            try { JustDropOrdersService.NoteDrawerChanged(); await refresh!; }
            finally { JustDropOrdersService.DrawerChanged -= Changed; }
            Assert.Equal(2, calls);
            JustDropOrdersService.Invalidate();
            JustDropOrdersService.FetchOverride = (_, _, _) => throw new InvalidOperationException("offline");
            Assert.Empty(await JustDropOrdersService.FetchAsync());
            CoreSecrets.RetrieveProvider = _ => null;
            Assert.Empty(await JustDropOrdersService.FetchAsync());
        }
        finally
        {
            CoreSecrets.RetrieveProvider = secret; CoreAccount.UnifiedUserId = identity;
            JustDropOrdersService.FetchOverride = fetch; JustDropOrdersService.Invalidate();
        }
    }

    [Fact]
    public void MalformedDrawersAreEmptyAndUnreplayableRowsAreDropped()
    {
        Assert.Empty(JustDropOrdersService.Parse("not json"));
        Assert.Empty(JustDropOrdersService.Parse("{}"));
        var rows = JustDropOrdersService.Parse("{\"orders\":[{},null,{\"code\":\"A\",\"sizeId\":\"unknown\"},{\"code\":\"B\",\"sizeId\":\"XXL\"}]}");
        Assert.Equal(new[] { "A", "B" }, rows.Select(o => o.Code));
        Assert.Equal(new[] { 0, 60 }, rows.Select(o => o.Minutes));
    }
}
