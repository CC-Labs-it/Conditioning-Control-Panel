// Adapted from WPF Services/Companion/CommunityPromptService.cs at 73b1ccf.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
namespace ConditioningControlPanel.Services
{
    public class CommunityPromptsCore : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _promptsFolder;
        private readonly string _manifestCachePath;
        private List<CommunityPromptManifestEntry> _availablePrompts = new();
        private bool _disposed;
        private const string PromptsManifestUrl = "https://codebambi-proxy.vercel.app/prompts/manifest";
        private const string PromptsDownloadUrl = "https://codebambi-proxy.vercel.app/prompts";
        public IReadOnlyList<string> InstalledPromptIds =>
            CoreSettings.Current?.InstalledCommunityPromptIds ?? new List<string>();
        public string? ActivePromptId => CoreSettings.Current?.ActiveCommunityPromptId;
        public static CommunityPromptsCore Shared { get; } = new();
        public event EventHandler<int>? Advisory;
        public CommunityPromptsCore(string? folder = null, HttpClient? client = null)
        {
            _httpClient = client ?? new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _promptsFolder = folder ?? Path.Combine(CorePaths.UserData, Companion.InstalledCommunityPrompts.FolderName);
            _manifestCachePath = Path.Combine(_promptsFolder, "manifest-cache.json");
            if (!Directory.Exists(_promptsFolder))
            {
                Directory.CreateDirectory(_promptsFolder);
            }
            LoadCachedManifest();
            Log.Debug("CommunityPromptsCore initialized. Prompts folder: {Folder}", _promptsFolder);
        }
        public async Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false)
        {
            if (CoreSettings.Current?.OfflineMode == true)
            {
                Log.Debug("Offline mode enabled, using cached prompts only");
                return _availablePrompts;
            }
            if (!forceRefresh && _availablePrompts.Count > 0)
            {
                return _availablePrompts;
            }
            try
            {
                var response = await _httpClient.GetAsync(PromptsManifestUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);
                    if (manifest?.Prompts != null)
                    {
                        _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);
                        await File.WriteAllTextAsync(_manifestCachePath, json);
                        Log.Information("Fetched {Count} community prompts from server", _availablePrompts.Count);
                    }
                }
                else
                {
                    Log.Warning("Failed to fetch community prompts manifest: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Error fetching community prompts manifest: {Error}", ex.Message);
            }
            return _availablePrompts;
        }
        private void LoadCachedManifest()
        {
            try
            {
                if (File.Exists(_manifestCachePath))
                {
                    var json = File.ReadAllText(_manifestCachePath);
                    var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);
                    if (manifest?.Prompts != null)
                    {
                        _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);
                        Log.Debug("Loaded {Count} prompts from manifest cache", _availablePrompts.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not load manifest cache: {Error}", ex.Message);
            }
        }
        public async Task<CommunityPrompt?> InstallPromptAsync(string promptId)
        {
            if (CoreSettings.Current?.OfflineMode == true)
            {
                Log.Information("Offline mode enabled, prompt download blocked");
                return null;
            }
            try
            {
                var url = $"{PromptsDownloadUrl}/{promptId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("Failed to download prompt {Id}: {Status}", promptId, response.StatusCode);
                    return null;
                }
                var json = await response.Content.ReadAsStringAsync();
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
                if (prompt == null)
                {
                    Log.Warning("Failed to parse prompt {Id}", promptId);
                    return null;
                }
                var filePath = GetPromptFilePath(promptId);
                await File.WriteAllTextAsync(filePath, json);
                var settings = CoreSettings.Current;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(promptId))
                {
                    settings.InstalledCommunityPromptIds.Add(promptId);
                    CoreSettings.Save();
                }
                prompt.IsInstalled = true;
                Log.Information("Installed community prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error installing prompt {Id}", promptId);
                return null;
            }
        }
        public void RemovePrompt(string promptId)
        {
            try
            {
                var filePath = GetPromptFilePath(promptId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                var settings = CoreSettings.Current;
                if (settings != null)
                {
                    settings.InstalledCommunityPromptIds.Remove(promptId);
                    if (settings.ActiveCommunityPromptId == promptId)
                    {
                        settings.ActiveCommunityPromptId = null;
                    }
                    CoreSettings.Save();
                }
                Log.Information("Removed community prompt: {Id}", promptId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing prompt {Id}", promptId);
            }
        }
        public CommunityPrompt? GetInstalledPrompt(string promptId)
        {
            return Companion.InstalledCommunityPrompts.Load(_promptsFolder, promptId, ActivePromptId);
        }
        public List<CommunityPrompt> GetInstalledPrompts()
        {
            var prompts = new List<CommunityPrompt>();
            foreach (var id in InstalledPromptIds)
            {
                var prompt = GetInstalledPrompt(id);
                if (prompt != null)
                {
                    prompts.Add(prompt);
                }
            }
            return prompts;
        }
        public bool ActivatePrompt(string promptId)
        {
            var prompt = GetInstalledPrompt(promptId);
            if (prompt == null)
            {
                Log.Warning("Cannot activate prompt {Id} - not installed", promptId);
                return false;
            }
            var settings = CoreSettings.Current;
            if (settings == null) return false;
            var slutModeOn = settings.SlutModeEnabled;
            var probe = new Models.PersonalityPreset { PromptSettings = prompt.PromptSettings };
            if (ExplicitContentGate.RequiresAcknowledgement(probe, slutModeOn)
                && !ExplicitContentGate.IsAlreadyAcknowledged(settings.CompanionPrompt))
            {
                Log.Warning("ActivatePrompt {Id} refused: explicit-content acknowledgement not granted", promptId);
                return false;
            }
            try
            {
                var validator = new PromptValidator();
                if (prompt.PromptSettings != null)
                {
                    var ps = prompt.PromptSettings;
                    var fieldsToScan = new (string Name, string Text)[]
                    {
                        ("Personality", ps.Personality ?? string.Empty),
                        ("SlutModePersonality", ps.SlutModePersonality ?? string.Empty),
                        ("KnowledgeBase", ps.KnowledgeBase ?? string.Empty),
                        ("ExplicitReaction", ps.ExplicitReaction ?? string.Empty),
                        ("ContextReactions", ps.ContextReactions ?? string.Empty),
                    };
                    var flaggedFields = new List<string>();
                    foreach (var (name, text) in fieldsToScan)
                    {
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var result = validator.Validate(text);
                        if (!result.Clean)
                        {
                            flaggedFields.Add(name);
                            CoreModerationLog.RecordEdit(name, result.MatchedPatterns.Count, surface: "community_prompt");
                        }
                    }
                    if (flaggedFields.Count > 0)
                    {
                        Log.Information(
                            "CommunityPromptsCore.ActivatePrompt {Id}: PromptValidator flagged {Count} field(s): {Fields}",
                            promptId, flaggedFields.Count, string.Join(",", flaggedFields));
                        Advisory?.Invoke(this, flaggedFields.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("CommunityPromptsCore PromptValidator pass failed: {Error}", ex.Message);
            }
            settings.CompanionPrompt = prompt.PromptSettings;
            settings.CompanionPrompt.UseCustomPrompt = true;
            settings.ActiveCommunityPromptId = promptId;
            CoreSettings.Save();
            prompt.IsActive = true;
            Log.Information("Activated community prompt: {Name}", prompt.Name);
            return true;
        }
        public CommunityPrompt ExportCurrentSettings(string name, string author, string description)
        {
            var prompt = CommunityPrompt.FromCurrentSettings(name, author, description);
            return prompt;
        }
        public async Task<string> SavePromptToFileAsync(CommunityPrompt prompt, string filePath)
        {
            var json = JsonConvert.SerializeObject(prompt, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
            Log.Information("Exported prompt to: {Path}", filePath);
            return filePath;
        }
        public CommunityPrompt? ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Log.Warning("Import file not found: {Path}", filePath);
                    return null;
                }
                var json = File.ReadAllText(filePath);
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
                if (prompt == null)
                {
                    Log.Warning("Failed to parse prompt file: {Path}", filePath);
                    return null;
                }
                prompt.Id = Guid.NewGuid().ToString("N");
                prompt.Name = Path.GetFileNameWithoutExtension(filePath);
                var localPath = GetPromptFilePath(prompt.Id);
                File.WriteAllText(localPath, JsonConvert.SerializeObject(prompt, Formatting.Indented));
                var settings = CoreSettings.Current;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(prompt.Id))
                {
                    settings.InstalledCommunityPromptIds.Add(prompt.Id);
                    CoreSettings.Save();
                }
                prompt.IsInstalled = true;
                Log.Information("Imported prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error importing prompt from {Path}", filePath);
                return null;
            }
        }
        private string GetPromptFilePath(string promptId)
        {
            if (string.IsNullOrEmpty(promptId) || promptId.Contains('/') || promptId.Contains('\\')
                || promptId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid prompt ID");
            return Path.Combine(_promptsFolder, $"{promptId}.json");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient?.Dispose();
        }
    }
}
