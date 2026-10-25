using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

public partial class WorkshopAccordion
{
    private static readonly HashSet<Window> CommunityDialogs = new();
    private static int _communityPanicGeneration;
    internal static void CloseCommunityForPanic()
    {
        _communityPanicGeneration++;
        foreach (var dialog in CommunityDialogs.ToArray()) dialog.Close();
    }
    internal Func<Window, bool, Task<string?>> CommunityPicker { get; set; } = PickCommunityFileAsync;
    internal Func<CommunityPromptCatalog> CommunityCatalogFactory { get; set; } = () => new();

    private static async Task<string?> PickCommunityFileAsync(Window owner, bool export)
    {
        var json = new FilePickerFileType("JSON files (*.json)") { Patterns = new[] { "*.json" } };
        if (export)
            return (await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("title_export_community_prompt"), SuggestedFileName = "My_Custom_Personality.json",
                FileTypeChoices = new[] { json }, DefaultExtension = "json"
            }))?.TryGetLocalPath();
        return (await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("title_import_community_prompt"), AllowMultiple = false,
            FileTypeFilter = new[] { json, FilePickerFileTypes.All }
        })).FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<bool> CommunityMessage(Window owner, string title, string message, string? accept = null)
    {
        var dialog = new MessageDialog(title, message, showCancel: accept != null,
            okText: accept ?? Loc.Get("btn_ok"), cancelText: Loc.Get("btn_cancel"));
        CommunityDialogs.Add(dialog);
        try { return await dialog.ShowDialogSafe<bool?>(owner) == true; }
        finally { CommunityDialogs.Remove(dialog); }
    }
    private void WireCommunity(WorkshopShelfParts parts)
    {
        parts.Community.BrowsePromptsRequested += async (_, _) => await CommunityActionAsync("browse");
        parts.Community.ImportPromptRequested += async (_, _) => await CommunityActionAsync("import");
        parts.Community.ExportPromptRequested += async (_, _) => await CommunityActionAsync("export");
        parts.Community.RefreshPromptsRequested += async (_, _) => await CommunityActionAsync("refresh");
        AttachedToVisualTree += (_, _) => RefreshInstalledPrompts();
        RefreshInstalledPrompts();
    }
    internal async Task CommunityActionAsync(string action)
    {
        if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
        var generation = _communityPanicGeneration;
        var cell = ((WorkshopRuntimeVm)DataContext!).Parts.Community;
        var refresh = cell.FindControl<Button>("BtnRefreshPrompts")!;
        try
        {
            if (action == "import" || action == "export")
            {
                var path = await CommunityPicker(owner, action == "export");
                if (path == null || !owner.IsVisible || generation != _communityPanicGeneration) return;
                if (action == "import")
                {
                    var prompt = InstalledCommunityPrompts.Import(path);
                    RefreshInstalledPrompts();
                    await CommunityMessage(owner, Loc.Get(prompt == null ? "title_error" : "title_imported"),
                        prompt == null ? Loc.Get("msg_failed_to_import_prompt_invalid") : Loc.GetF("msg_prompt_imported", prompt.Name, prompt.Author));
                }
                else
                {
                    var prompt = CommunityPrompt.FromCurrentSettings("My Custom Personality",
                        Platform.AccountSeed.Patreon?.DisplayName ?? "Anonymous", "A custom AI personality.");
                    await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(prompt, Formatting.Indented));
                    if (!owner.IsVisible || generation != _communityPanicGeneration) return;
                    await CommunityMessage(owner, Loc.Get("title_exported"), Loc.GetF("msg_prompt_exported", path));
                }
                return;
            }
            using var catalog = CommunityCatalogFactory();
            if (action == "refresh") { refresh.IsEnabled = false; refresh.Content = "..."; }
            var available = await catalog.GetAvailableAsync(action == "refresh");
            if (!owner.IsVisible || generation != _communityPanicGeneration) return;
            RefreshInstalledPrompts();
            if (action == "refresh") return;
            var remaining = available.Where(p => !CoreSettings.Current.InstalledCommunityPromptIds.Contains(p.Id)).ToList();
            if (remaining.Count == 0)
            {
                await CommunityMessage(owner, Loc.Get("title_community_prompts"),
                    Loc.Get(available.Count == 0 ? "msg_no_community_prompts" : "msg_all_prompts_installed"));
                return;
            }
            var message = Loc.Get("label_available_prompts");
            foreach (var prompt in remaining.Take(5)) message += $"• {prompt.Name} by {prompt.Author}\n  {prompt.Description}\n\n";
            if (remaining.Count > 5) message += Loc.GetF("label_and_more_prompts", remaining.Count - 5);
            message += Loc.Get("label_install_first_one");
            if (await CommunityMessage(owner, Loc.Get("title_browse_community_prompts"), message, Loc.Get("btn_install")) && owner.IsVisible)
            {
                var installed = await catalog.InstallAsync(remaining[0].Id);
                RefreshInstalledPrompts();
                if (installed != null && owner.IsVisible && generation == _communityPanicGeneration)
                    await CommunityMessage(owner, Loc.Get("title_installed"), Loc.GetF("msg_prompt_installed", installed.Name));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Workshop community action failed");
            if (owner.IsVisible && generation == _communityPanicGeneration) await CommunityMessage(owner, Loc.Get("title_error"), Loc.GetF(
                action == "export" ? "msg_failed_to_export_prompt" : action == "import" ? "msg_failed_to_import_prompt_error" : "msg_failed_to_browse_prompts", ex.Message));
        }
        finally
        {
            refresh.IsEnabled = true;
            refresh.Content = new TextBlock { Text = Loc.Get("btn_refresh") };
        }
    }
    internal void RefreshInstalledPrompts()
    {
        if (DataContext is not WorkshopRuntimeVm vm) return;
        this.FindLogicalAncestorOfType<CompanionRoomView>()?.FindControl<MakeHerYoursView>("PersonalityZone")?.ViewModel.Sync();
        var panel = vm.Parts.Community.FindControl<StackPanel>("InstalledPromptsPanel")!;
        var placeholder = vm.Parts.Community.FindControl<TextBlock>("TxtNoInstalledPrompts")!;
        for (int i = panel.Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(panel.Children[i], placeholder)) panel.Children.RemoveAt(i);
        foreach (var id in CoreSettings.Current.InstalledCommunityPromptIds.ToArray())
        {
            var prompt = InstalledCommunityPrompts.Load(Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName), id, CoreSettings.Current.ActiveCommunityPromptId);
            if (prompt == null) continue;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (prompt.IsActive) namePanel.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse
                { Width = 5, Height = 5, Fill = new SolidColorBrush(Color.Parse("#9370DB")), Margin = new Thickness(0, 0, 4, 0) });
            namePanel.Children.Add(new TextBlock { Text = prompt.Name + " " + Loc.GetF("label_by_author", prompt.Author),
                FontSize = 10, Foreground = Brushes.White, FontWeight = prompt.IsActive ? FontWeight.SemiBold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            row.Children.Add(namePanel);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            if (!prompt.IsActive)
            {
                var activate = new Button { Content = Loc.Get("btn_activate"), FontSize = 9, Padding = new Thickness(6, 2) };
                activate.Click += async (_, _) => await ActivateCommunityAsync(prompt);
                actions.Children.Add(activate);
            }
            // ponytail: text multiplication sign matches WPF's uninstall action; it is not a new icon.
            var remove = new Button { Content = "×", Padding = new Thickness(4, 0), FontSize = 12 };
            ToolTip.SetTip(remove, Loc.Get("btn_uninstall"));
            remove.Click += (_, _) => { InstalledCommunityPrompts.Remove(id); RefreshInstalledPrompts(); };
            actions.Children.Add(remove); Grid.SetColumn(actions, 1); row.Children.Add(actions); panel.Children.Add(row);
        }
        placeholder.IsVisible = panel.Children.Count == 1;
    }
    private async Task ActivateCommunityAsync(CommunityPrompt prompt)
    {
        if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
        var previous = CoreSettings.Current.CompanionPrompt;
        if (ExplicitContentGate.RequiresAcknowledgement(new PersonalityPreset { PromptSettings = prompt.PromptSettings }, CoreSettings.Current.SlutModeEnabled)
            && !ExplicitContentGate.IsAlreadyAcknowledged(previous))
        {
            var dialog = new ExplicitContentAcknowledgementDialog();
            CommunityDialogs.Add(dialog);
            try { if (!await dialog.ShowDialogSafe<bool>(owner)) return; }
            finally { CommunityDialogs.Remove(dialog); }
            ExplicitContentGate.MarkAcknowledged(previous); CoreSettings.Save();
        }
        InstalledCommunityPrompts.Activate(prompt.Id);
        RefreshInstalledPrompts();
    }
}
