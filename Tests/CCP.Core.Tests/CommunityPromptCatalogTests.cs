using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class CommunityPromptCatalogTests
{
    [Fact]
    public async Task DiscoveryCachesRefreshesFallsBackAndBlocksOfflineDownloads()
    {
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        var cache = Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName, "manifest-cache.json");
        var backup = File.Exists(cache) ? File.ReadAllText(cache) : null;
        var handler = new Handler();
        try
        {
            if (File.Exists(cache)) File.Delete(cache);
            using var catalog = new CommunityPromptCatalog(new HttpClient(handler));
            Assert.Single(await catalog.GetAvailableAsync());
            Assert.Single(await catalog.GetAvailableAsync()); Assert.Equal(1, handler.Calls);
            Assert.Single(await catalog.GetAvailableAsync(true)); Assert.Equal(2, handler.Calls);
            handler.Fail = true;
            Assert.Single(await catalog.GetAvailableAsync(true));
            service.Current.OfflineMode = true;
            Assert.Single(await catalog.GetAvailableAsync(true)); Assert.Equal(3, handler.Calls);
            Assert.Null(await catalog.InstallAsync("voice")); Assert.Equal(3, handler.Calls);
            using var cached = new CommunityPromptCatalog(new HttpClient(new Handler { Fail = true }));
            Assert.Single(await cached.GetAvailableAsync());
            service.Current.OfflineMode = false; handler.Fail = false;
            Assert.NotNull(await catalog.InstallAsync("voice"));
            Assert.NotNull(await catalog.InstallAsync("voice"));
            Assert.Single(service.Current.InstalledCommunityPromptIds);
            Assert.False(service.Current.CompanionPrompt.UseCustomPrompt);
            Assert.Null(await catalog.InstallAsync("../unsafe"));
        }
        finally
        {
            InstalledCommunityPrompts.Remove("voice");
            if (backup == null) File.Delete(cache); else File.WriteAllText(cache, backup);
            service.SealForReset(); CoreSettings.ServiceProvider = previous;
        }
    }
    [Fact]
    public void ImportDoesNotAssignOrActivateAndExportIsAnIndependentSnapshot()
    {
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        var file = Path.Combine(Path.GetTempPath(), "ccp-community-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "{\"Id\":\"untrusted\",\"Name\":\"ignored\",\"PromptSettings\":{\"Personality\":\"kind\"}}");
            var prompt = InstalledCommunityPrompts.Import(file)!;
            Assert.Equal(Path.GetFileNameWithoutExtension(file), prompt.Name);
            Assert.NotEqual("untrusted", prompt.Id);
            Assert.Null(service.Current.ActiveCommunityPromptId);
            Assert.NotEqual(prompt.Id, service.Current.GetCompanionPromptId(0));
            service.Current.SlutModeEnabled = false;
            Assert.True(InstalledCommunityPrompts.Activate(prompt.Id));
            Assert.Equal("kind", service.Current.CompanionPrompt.Personality);
            var exported = CommunityPrompt.FromCurrentSettings("Export", "Anonymous", "Description");
            service.Current.CompanionPrompt.Personality = "changed";
            Assert.Equal("kind", exported.PromptSettings.Personality);
            InstalledCommunityPrompts.Remove(prompt.Id);
            Assert.Null(service.Current.ActiveCommunityPromptId);
            Assert.Empty(service.Current.InstalledCommunityPromptIds);
            Assert.Null(InstalledCommunityPrompts.Load(Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName), prompt.Id, null));
            File.WriteAllText(file, "{bad"); Assert.Null(InstalledCommunityPrompts.Import(file));
            service.Current.InstalledCommunityPromptIds.Add("missing");
            InstalledCommunityPrompts.Remove("missing");
            Assert.Empty(service.Current.InstalledCommunityPromptIds);
        }
        finally { File.Delete(file); service.SealForReset(); CoreSettings.ServiceProvider = previous; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("Test offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("manifest")
                    ? "{\"Prompts\":[{\"Id\":\"voice\",\"Name\":\"Kind\"}]}"
                    : "{\"Id\":\"voice\",\"Name\":\"Kind\",\"PromptSettings\":{\"Personality\":\"kind\"}}") });
        }
    }
}
