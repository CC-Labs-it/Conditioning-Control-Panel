using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages community-created AI personality prompts.
    /// Handles downloading, installing, activating, and sharing prompts.
    /// </summary>
    public class CommunityPromptService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _promptsFolder;
        private readonly CommunityPromptCatalogue _catalogue;
        private bool _disposed;


        // Events
        public event EventHandler<CommunityPrompt>? PromptInstalled;
        public event EventHandler<CommunityPrompt>? PromptActivated;
        public event EventHandler<string>? PromptRemoved;
        public event EventHandler<Exception>? Error;

        /// <summary>
        /// Gets the list of installed prompt IDs.
        /// </summary>
        public IReadOnlyList<string> InstalledPromptIds =>
            App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();

        /// <summary>
        /// Gets the currently active community prompt ID (null = using built-in).
        /// </summary>
        public string? ActivePromptId => App.Settings?.Current?.ActiveCommunityPromptId;

        public CommunityPromptService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _promptsFolder = Path.Combine(App.UserDataPath, "community-prompts");
            _catalogue = new CommunityPromptCatalogue(_httpClient, _promptsFolder);
            _catalogue.Error += (_, error) => Error?.Invoke(this, error);

            // Create prompts folder if needed
            if (!Directory.Exists(_promptsFolder))
            {
                Directory.CreateDirectory(_promptsFolder);
            }


            App.Logger?.Debug("CommunityPromptService initialized. Prompts folder: {Folder}", _promptsFolder);
        }

        #region Manifest & Discovery

        /// <summary>
        /// Fetches available community prompts from the server.
        /// </summary>
        public Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false) =>
            _catalogue.GetAvailablePromptsAsync(forceRefresh);

        #endregion

        #region Install & Remove

        /// <summary>
        /// Downloads and installs a community prompt by ID.
        /// </summary>
        public async Task<CommunityPrompt?> InstallPromptAsync(string promptId)
        {
            var prompt = await _catalogue.InstallPromptAsync(promptId);
            if (prompt != null) PromptInstalled?.Invoke(this, prompt);
            return prompt;
        }

        /// <summary>
        /// Removes an installed prompt.
        /// </summary>
        public void RemovePrompt(string promptId)
        {
            try
            {
                // Remove file
                var filePath = GetPromptFilePath(promptId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Remove from installed list
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    settings.InstalledCommunityPromptIds.Remove(promptId);

                    // Deactivate if this was the active prompt
                    if (settings.ActiveCommunityPromptId == promptId)
                    {
                        settings.ActiveCommunityPromptId = null;
                    }

                    App.Settings?.Save();
                }

                PromptRemoved?.Invoke(this, promptId);
                App.Logger?.Information("Removed community prompt: {Id}", promptId);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error removing prompt {Id}", promptId);
                Error?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Gets an installed prompt by ID.
        /// </summary>
        public CommunityPrompt? GetInstalledPrompt(string promptId) => _catalogue.GetInstalledPrompt(promptId);

        /// <summary>
        /// Gets all installed prompts.
        /// </summary>
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

        #endregion

        #region Activate & Deactivate

        /// <summary>
        /// Activates a community prompt, applying its settings.
        /// </summary>
        public bool ActivatePrompt(string promptId)
        {
            var prompt = GetInstalledPrompt(promptId);
            if (prompt == null)
            {
                App.Logger?.Warning("Cannot activate prompt {Id} - not installed", promptId);
                return false;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return false;

            // CCBill AI Addendum: gate community/asset prompts that ship a SlutModePersonality
            // when SlutMode is currently on. Caller is responsible for the modal acknowledgement;
            // the service refuses activation if the gate has not been cleared.
            var slutModeOn = settings.SlutModeEnabled;
            var probe = new Models.PersonalityPreset { PromptSettings = prompt.PromptSettings };
            if (ExplicitContentGate.RequiresAcknowledgement(probe, slutModeOn)
                && !ExplicitContentGate.IsAlreadyAcknowledged(settings.CompanionPrompt))
            {
                App.Logger?.Warning("ActivatePrompt {Id} refused: explicit-content acknowledgement not granted", promptId);
                return false;
            }

            // P2-H10: run PromptValidator against the user-content fields BEFORE
            // applying them to CompanionPrompt. Soft validator (consistent with the
            // editor surface) - we log every flagged field to moderation.log and
            // surface a non-blocking advisory toast, but the activation proceeds.
            // ModerationGuard at inference time remains the load-bearing layer.
            try
            {
                var validator = App.PromptValidator;
                if (validator != null && prompt.PromptSettings != null)
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
                            App.ModerationLog?.RecordEdit(name, result.MatchedPatterns.Count, surface: "community_prompt");
                        }
                    }

                    if (flaggedFields.Count > 0)
                    {
                        App.Logger?.Information(
                            "CommunityPromptService.ActivatePrompt {Id}: PromptValidator flagged {Count} field(s): {Fields}",
                            promptId, flaggedFields.Count, string.Join(",", flaggedFields));

                        try
                        {
                            var title = Loc.Get("community_prompt_warning_title");
                            var body = Loc.GetF("community_prompt_warning_body", flaggedFields.Count);
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    MessageBox.Show(body, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                                catch (Exception inner)
                                {
                                    App.Logger?.Debug("Community prompt advisory toast failed: {Error}", inner.Message);
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Community prompt advisory dispatch failed: {Error}", ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Validator failures must not block activation - soft surface.
                App.Logger?.Debug("CommunityPromptService PromptValidator pass failed: {Error}", ex.Message);
            }

            // Apply the prompt settings
            settings.CompanionPrompt = prompt.PromptSettings;
            settings.CompanionPrompt.UseCustomPrompt = true;
            settings.ActiveCommunityPromptId = promptId;
            App.Settings?.Save();

            prompt.IsActive = true;
            PromptActivated?.Invoke(this, prompt);

            App.Logger?.Information("Activated community prompt: {Name}", prompt.Name);
            return true;
        }

        /// <summary>
        /// Takes the custom/community prompt off the wire so the active preset owns it again.
        /// Clears BOTH halves of the override: <c>UseCustomPrompt</c> is what the prompt mux reads
        /// (<see cref="BambiSprite.UsesCustomPrompt"/>), and <c>ActiveCommunityPromptId</c> is what
        /// the Companion tab's readout reads — leaving either behind is a UI that disagrees with the
        /// wire. Static so the preset path can call it before <c>App.CommunityPrompts</c> exists;
        /// a null service must never leave a stale override standing. Returns true if anything moved.
        /// The prompt TEXT is deliberately kept, so re-ticking "use custom prompt" restores the edits.
        /// </summary>
        public static bool ClearCustomPromptOverride(AppSettings? settings) =>
            PersonalityService.ClearCustomPromptOverride(settings);

        /// <summary>
        /// Deactivates the current community prompt, reverting to defaults.
        /// </summary>
        public void DeactivatePrompt()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            ClearCustomPromptOverride(settings);
            App.Settings?.Save();

            App.Logger?.Information("Deactivated community prompt, using default/custom settings");
        }

        #endregion

        #region Export & Import

        /// <summary>
        /// Exports the current companion settings as a shareable community prompt.
        /// </summary>
        public CommunityPrompt ExportCurrentSettings(string name, string author, string description)
        {
            var prompt = CommunityPrompt.FromCurrentSettings(name, author, description);
            return prompt;
        }

        /// <summary>
        /// Saves an exported prompt to a file for sharing.
        /// </summary>
        public async Task<string> SavePromptToFileAsync(CommunityPrompt prompt, string filePath)
        {
            var json = JsonConvert.SerializeObject(prompt, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
            App.Logger?.Information("Exported prompt to: {Path}", filePath);
            return filePath;
        }

        /// <summary>
        /// Imports a prompt from a JSON file.
        /// Uses the file name (minus .json) as the prompt name.
        /// </summary>
        public CommunityPrompt? ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    App.Logger?.Warning("Import file not found: {Path}", filePath);
                    return null;
                }

                var json = File.ReadAllText(filePath);
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);

                if (prompt == null)
                {
                    App.Logger?.Warning("Failed to parse prompt file: {Path}", filePath);
                    return null;
                }

                // Generate new ID to avoid conflicts
                prompt.Id = Guid.NewGuid().ToString("N");

                // Use file name as prompt name (minus .json extension)
                prompt.Name = Path.GetFileNameWithoutExtension(filePath);

                // Save to local prompts folder
                var localPath = GetPromptFilePath(prompt.Id);
                File.WriteAllText(localPath, JsonConvert.SerializeObject(prompt, Formatting.Indented));

                // Add to installed list
                var settings = App.Settings?.Current;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(prompt.Id))
                {
                    settings.InstalledCommunityPromptIds.Add(prompt.Id);
                    App.Settings?.Save();
                }

                prompt.IsInstalled = true;
                PromptInstalled?.Invoke(this, prompt);

                App.Logger?.Information("Imported prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error importing prompt from {Path}", filePath);
                Error?.Invoke(this, ex);
                return null;
            }
        }

        #endregion

        #region Helpers

        private string GetPromptFilePath(string promptId)
        {
            return Path.Combine(_promptsFolder, $"{promptId}.json");
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _catalogue.Dispose();
            _httpClient?.Dispose();
        }
    }
}
