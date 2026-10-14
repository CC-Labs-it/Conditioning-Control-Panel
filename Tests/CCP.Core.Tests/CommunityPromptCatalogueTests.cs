using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CommunityPromptCatalogueTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Requests;
        public readonly List<string> Paths = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++; Paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath.EndsWith("manifest")
                ? JsonConvert.SerializeObject(new CommunityPromptsManifest { Prompts = new[] {
                    new CommunityPromptManifestEntry { Id = "p1", Name = "Friendly", Author = "A", Description = "Description" } } })
                : JsonConvert.SerializeObject(new CommunityPrompt { Id = "p1", Name = "Friendly" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task ManifestCacheOfflineInstallAndInstalledMetadataMatchWpf()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-prompt-catalogue-").FullName;
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var handler = new Handler();
        using var http = new HttpClient(handler);
        try
        {
            var s = service.Current;
            s.OfflineMode = false;
            s.InstalledCommunityPromptIds = new List<string>();
            s.CompanionPrompt.Personality = "Exported personality";
            s.CompanionPrompt.CustomDomains["story"] = "Original";
            var exported = CommunityPrompt.FromCurrentSettings("Export", "Author", "Description");
            Assert.Equal("Exported personality", exported.PromptSettings.Personality);
            Assert.True(exported.PromptSettings.UseCustomPrompt);
            exported.PromptSettings.CustomDomains["story"] = "Changed";
            Assert.Equal("Original", s.CompanionPrompt.CustomDomains["story"]);
            using var catalogue = new CommunityPromptCatalogue(http, dir);
            Assert.Equal("Friendly", Assert.Single(await catalogue.GetAvailablePromptsAsync()).Name);
            await catalogue.GetAvailablePromptsAsync();
            Assert.Equal(1, handler.Requests);
            s.OfflineMode = true;
            Assert.Single(await catalogue.GetAvailablePromptsAsync(forceRefresh: true));
            Assert.Null(await catalogue.InstallPromptAsync("p1"));
            Assert.Equal(1, handler.Requests);
            s.OfflineMode = false;
            var installed = await catalogue.InstallPromptAsync("p1");
            Assert.True(installed!.IsInstalled);
            Assert.False(installed.IsActive); // install never implicitly activates
            Assert.Contains("p1", s.InstalledCommunityPromptIds);
            s.ActiveCommunityPromptId = "p1";
            var read = catalogue.GetInstalledPrompt("p1")!;
            Assert.Equal("Friendly", read.Name);
            Assert.True(read.IsActive);
            await catalogue.InstallPromptAsync("p1");
            Assert.Single(s.InstalledCommunityPromptIds);
            Assert.Null(catalogue.GetInstalledPrompt("missing"));
            File.WriteAllText(Path.Combine(dir, "bad.json"), "malformed");
            Assert.Null(catalogue.GetInstalledPrompt("bad"));
            Assert.Null(await catalogue.InstallPromptAsync("../outside"));
            using var cached = new CommunityPromptCatalogue(http, dir);
            s.OfflineMode = true;
            Assert.Single(await cached.GetAvailablePromptsAsync());
            Assert.Equal(new[] { "/prompts/manifest", "/prompts/p1", "/prompts/p1" }, handler.Paths);
        }
        finally { service.SaveImmediate(); CoreSettings.ServiceProvider = old; Directory.Delete(dir, true); }
    }

    [Fact]
    public void BrowserExcludesInstalledAndListsFirstFiveInServerOrder()
    {
        var entries = Enumerable.Range(0, 8).Select(i => new CommunityPromptManifestEntry { Id = $"p{i}", Name = $"Name{i}", Author = "Author", Description = "About" }).ToArray();
        var remaining = CommunityPromptCatalogue.NotInstalled(entries, new[] { "p0" });
        Assert.Equal("p1", remaining[0].Id);
        var message = CommunityPromptCatalogue.BrowseMessage(remaining);
        Assert.Contains("• Name1 by Author\n  About\n\n", message);
        Assert.Contains("Name5", message);
        Assert.DoesNotContain("Name6", message);
        Assert.Contains(ConditioningControlPanel.Localization.Loc.GetF("label_and_more_prompts", 2), message);
    }
}
