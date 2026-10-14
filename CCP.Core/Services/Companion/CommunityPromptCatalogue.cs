using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>The WPF prompt discovery, installation and installed-file reader, shared by both heads.</summary>
public sealed class CommunityPromptCatalogue : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _folder;
    private List<CommunityPromptManifestEntry> _available = new();
    private static readonly Lazy<CommunityPromptCatalogue> Default = new(() => new());
    public static CommunityPromptCatalogue Shared => Default.Value;
    public event EventHandler<Exception>? Error;

    // HttpClient and folder are the transport/persistence seams for loopback tests and the existing WPF service.
    public CommunityPromptCatalogue(HttpClient? http = null, string? folder = null)
    {
        _ownsHttp = http == null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _folder = folder ?? Path.Combine(CorePaths.UserData, "community-prompts");
        Directory.CreateDirectory(_folder);
        try
        {
            var cache = Path.Combine(_folder, "manifest-cache.json");
            if (File.Exists(cache)) _available = JsonConvert.DeserializeObject<CommunityPromptsManifest>(File.ReadAllText(cache))
                ?.Prompts?.ToList() ?? new();
        }
        catch (Exception ex) { Log.Debug(ex, "Could not load community prompt manifest cache"); }
    }

    public async Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false)
    {
        if (CoreSettings.Current.OfflineMode || (!forceRefresh && _available.Count > 0)) return _available;
        try
        {
            using var response = await _http.GetAsync("https://codebambi-proxy.vercel.app/prompts/manifest");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (JsonConvert.DeserializeObject<CommunityPromptsManifest>(json)?.Prompts is { } prompts)
                {
                    _available = prompts.ToList();
                    await File.WriteAllTextAsync(Path.Combine(_folder, "manifest-cache.json"), json);
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Could not fetch community prompt manifest"); Error?.Invoke(this, ex); }
        return _available;
    }

    // IDs identify one file under community-prompts, never a relative/absolute path.
    private string? PromptPath(string id) => string.IsNullOrEmpty(id) || id is "." or ".."
        || id.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ? null : Path.Combine(_folder, id + ".json");

    public CommunityPrompt? GetInstalledPrompt(string id)
    {
        try
        {
            var path = PromptPath(id);
            if (path == null || !File.Exists(path)) return null;
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(path));
            if (prompt != null) { prompt.IsInstalled = true; prompt.IsActive = id == CoreSettings.Current.ActiveCommunityPromptId; }
            return prompt;
        }
        catch (Exception ex) { Log.Warning(ex, "Could not read installed prompt {Id}", id); return null; }
    }

    public async Task<CommunityPrompt?> InstallPromptAsync(string id)
    {
        var path = PromptPath(id);
        if (CoreSettings.Current.OfflineMode || path == null) return null;
        try
        {
            using var response = await _http.GetAsync("https://codebambi-proxy.vercel.app/prompts/" + Uri.EscapeDataString(id));
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
            if (prompt == null) return null;
            await File.WriteAllTextAsync(path, json);
            var settings = CoreSettings.Current;
            if (!settings.InstalledCommunityPromptIds.Contains(id))
            { settings.InstalledCommunityPromptIds.Add(id); CoreSettings.Save(); }
            prompt.IsInstalled = true;
            return prompt;
        }
        catch (Exception ex) { Log.Warning(ex, "Could not install community prompt {Id}", id); Error?.Invoke(this, ex); return null; }
    }

    public static List<CommunityPromptManifestEntry> NotInstalled(IEnumerable<CommunityPromptManifestEntry> available,
        IReadOnlyCollection<string> installed) => available.Where(p => !installed.Contains(p.Id)).ToList();

    public static string BrowseMessage(IReadOnlyList<CommunityPromptManifestEntry> prompts)
    {
        var message = Loc.Get("label_available_prompts");
        foreach (var p in prompts.Take(5)) message += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
        if (prompts.Count > 5) message += Loc.GetF("label_and_more_prompts", prompts.Count - 5);
        return message + Loc.Get("label_install_first_one");
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
