using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF CommunityPromptService discovery/cache and download, with head-owned dialogs.</summary>
public sealed class CommunityPromptCatalog : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cache;
    private List<CommunityPromptManifestEntry> _available = new();
    public CommunityPromptCatalog(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _cache = Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName, "manifest-cache.json");
        try { if (File.Exists(_cache)) ReadManifest(File.ReadAllText(_cache)); }
        catch (Exception ex) { Log.Debug(ex, "Could not load prompt catalogue cache"); }
    }
    private void ReadManifest(string json)
    {
        var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);
        if (manifest?.Prompts != null) _available = new(manifest.Prompts);
    }
    public async Task<List<CommunityPromptManifestEntry>> GetAvailableAsync(bool forceRefresh = false)
    {
        if (CoreSettings.Current.OfflineMode || (!forceRefresh && _available.Count > 0)) return _available;
        try
        {
            using var response = await _http.GetAsync("https://codebambi-proxy.vercel.app/prompts/manifest");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                ReadManifest(json);
                Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);
                await File.WriteAllTextAsync(_cache, json);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Could not refresh community prompts"); }
        return _available;
    }
    public async Task<CommunityPrompt?> InstallAsync(string id)
    {
        if (CoreSettings.Current.OfflineMode) return null;
        try
        {
            using var response = await _http.GetAsync("https://codebambi-proxy.vercel.app/prompts/" + Uri.EscapeDataString(id));
            if (!response.IsSuccessStatusCode) return null;
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(await response.Content.ReadAsStringAsync());
            return prompt?.PromptSettings == null ? null : InstalledCommunityPrompts.Install(id, prompt);
        }
        catch (Exception ex) { Log.Warning(ex, "Could not install community prompt"); return null; }
    }
    public void Dispose() => _http.Dispose();
}
