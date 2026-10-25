using System;
using System.IO;
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

[Collection(SessionStatics.Name)]
public sealed class CommunityPromptServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-community-" + Guid.NewGuid().ToString("N"));
    private readonly Func<SettingsService?>? _old = CoreSettings.ServiceProvider;
    private readonly SettingsService _settings = new();
    public CommunityPromptServiceTests()
    {
        CoreSettings.ServiceProvider = () => _settings;
        _settings.Current.InstalledCommunityPromptIds.Clear();
        _settings.Current.ActiveCommunityPromptId = null;
        _settings.Current.OfflineMode = false;
        _settings.Current.SlutModeEnabled = false;
    }
    public void Dispose() { CoreSettings.ServiceProvider = _old; if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string Write(CommunityPrompt prompt)
    {
        var folder = Path.Combine(_root, "community-prompts"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, prompt.Id + ".json"); File.WriteAllText(path, JsonConvert.SerializeObject(prompt)); return path;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            object data = request.RequestUri!.AbsolutePath.EndsWith("manifest")
                ? new CommunityPromptsManifest { Prompts = new[] { new CommunityPromptManifestEntry { Id = "alpha", Name = "Alpha" } } }
                : new CommunityPrompt { Id = "alpha", Name = "Alpha", Author = "Fixture", PromptSettings = new() { Personality = "fixture text" } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(data)) });
        }
    }
    [Fact]
    public async Task ManifestCacheOfflineAndInstallationPreserveInstalledIdentity()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var service = new CommunityPromptService(http, _root);
        Assert.Single(await service.GetAvailablePromptsAsync());
        Assert.Single(await service.GetAvailablePromptsAsync()); Assert.Equal(1, handler.Calls);
        var prompt = await service.InstallPromptAsync("alpha"); Assert.NotNull(prompt); Assert.True(prompt.IsInstalled);
        Assert.Equal(new[] { "alpha" }, service.InstalledPromptIds);
        _settings.Current.OfflineMode = true;
        Assert.Null(await service.InstallPromptAsync("other"));
        using var cached = new CommunityPromptService(http, _root);
        Assert.Single(await cached.GetAvailablePromptsAsync(true)); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public void ImportUsesFilenameNewGuidAndRemovalClearsOnlyActiveId()
    {
        Directory.CreateDirectory(_root);
        var incoming = Path.Combine(_root, "Imported Persona.json");
        File.WriteAllText(incoming, JsonConvert.SerializeObject(new CommunityPrompt { Id = "old-id", Name = "old name", Author = "Fixture" }));
        using var service = new CommunityPromptService(userDataPath: _root);
        var prompt = service.ImportFromFile(incoming)!;
        Assert.NotEqual("old-id", prompt.Id); Assert.Equal("Imported Persona", prompt.Name); Assert.Equal(32, prompt.Id.Length);
        _settings.Current.ActiveCommunityPromptId = prompt.Id; _settings.Current.CompanionPrompt.UseCustomPrompt = true;
        Assert.True(service.GetInstalledPrompt(prompt.Id)!.IsActive);
        service.RemovePrompt(prompt.Id);
        Assert.Null(_settings.Current.ActiveCommunityPromptId); Assert.Empty(service.InstalledPromptIds);
        Assert.True(_settings.Current.CompanionPrompt.UseCustomPrompt); Assert.Null(service.GetInstalledPrompt(prompt.Id));
    }
    [Fact]
    public void ActivationRequiresAcknowledgementAndThenPublishesSettings()
    {
        var prompt = new CommunityPrompt { Id = "gated", PromptSettings = new() { Personality = "fixture", SlutModePersonality = "fixture explicit persona" } };
        Write(prompt); _settings.Current.InstalledCommunityPromptIds.Add(prompt.Id); _settings.Current.SlutModeEnabled = true;
        _settings.Current.CompanionPrompt.ExplicitContentAcknowledged = false;
        using var service = new CommunityPromptService(userDataPath: _root);
        Assert.False(service.ActivatePrompt(prompt.Id)); Assert.Null(_settings.Current.ActiveCommunityPromptId);
        ExplicitContentGate.MarkAcknowledged(_settings.Current.CompanionPrompt);
        Assert.True(service.ActivatePrompt(prompt.Id)); Assert.Equal(prompt.Id, _settings.Current.ActiveCommunityPromptId);
        Assert.True(_settings.Current.CompanionPrompt.UseCustomPrompt); Assert.Equal("fixture", _settings.Current.CompanionPrompt.Personality);
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:outside")]
    public void UnsafeIdsCannotReadOrRemoveOutsideTheStore(string id)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "outside.json"); File.WriteAllText(path, "{}");
        using var service = new CommunityPromptService(userDataPath: _root);
        Assert.Null(service.GetInstalledPrompt(id)); service.RemovePrompt(id); Assert.True(File.Exists(path));
    }
    [Fact]
    public async Task ExportCopiesEditableFieldsAndDomainsWithoutProviderCredentials()
    {
        _settings.Current.CompanionPrompt.Personality = "fixture personality";
        _settings.Current.CompanionPrompt.OpenAiCompatibleApiKey = "fixture-only-key";
        using var service = new CommunityPromptService(userDataPath: _root);
        var prompt = service.ExportCurrentSettings("Name", "Author", "Description");
        Assert.Equal("fixture personality", prompt.PromptSettings.Personality);
        Assert.NotSame(_settings.Current.CompanionPrompt.CustomDomains, prompt.PromptSettings.CustomDomains);
        Assert.NotEqual("fixture-only-key", prompt.PromptSettings.OpenAiCompatibleApiKey);
        var path = Path.Combine(_root, "export.json"); await service.SavePromptToFileAsync(prompt, path);
        Assert.Equal("Name", JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(path))!.Name);
    }
}
