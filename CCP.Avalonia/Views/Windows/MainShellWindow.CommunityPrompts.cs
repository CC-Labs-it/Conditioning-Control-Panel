using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private static readonly HashSet<Window> CommunityDialogs = new();
    private static int _communityEpoch;
    internal static void StopCommunityPrompts()
    {
        _communityEpoch++;
        foreach (var dialog in CommunityDialogs.ToArray()) dialog.Close(false);
    }

    internal async Task<bool> ShowCommunityDialogAsync(Window dialog)
    {
        CommunityDialogs.Add(dialog);
        try { return await dialog.ShowDialogSafe<bool?>(this) == true; }
        finally { CommunityDialogs.Remove(dialog); }
    }
    private Task<bool> CommunityMessageAsync(string title, string message) =>
        ShowCommunityDialogAsync(new MessageDialog(title, message, false));
    private async Task RunCommunityActionAsync(Func<Task> action, string errorKey)
    {
        try { await action(); }
        catch (Exception ex) { await CommunityMessageAsync(Loc.Get("title_error"), Loc.GetF(errorKey, ex.Message)); }
    }

    private static CommunityPromptsCore Prompts => CommunityPromptsCore.Shared;

    internal void WireCommunityPrompts(CompanionRoomView room)
    {
        if (room.WorkshopParts is not { } parts) return;
        parts.Roster.PersonalityAssignRequested += async (_, index) => await RunCommunityActionAsync(() => AssignPersonalityAsync(index, room), "msg_failed_to_import_prompt");
        parts.Community.BrowsePromptsRequested += async (_, _) => await RunCommunityActionAsync(() => BrowsePromptsAsync(room), "msg_failed_to_browse_prompts");
        parts.Community.ImportPromptRequested += async (_, _) => await RunCommunityActionAsync(() => ImportPromptAsync(room), "msg_failed_to_import_prompt_error");
        parts.Community.ExportPromptRequested += async (_, _) => await RunCommunityActionAsync(ExportPromptAsync, "msg_failed_to_export_prompt");
        parts.Community.RefreshPromptsRequested += async (_, _) =>
        {
            var button = parts.Community.FindControl<Button>("BtnRefreshPrompts")!;
            button.IsEnabled = false;
            button.Content = "...";
            try { await Prompts.GetAvailablePromptsAsync(true); RefreshCommunityPrompts(room); }
            finally { button.IsEnabled = true; button.Content = Loc.Get("btn_refresh"); }
        };
        // Subscribe only while attached: revisiting the tab must not duplicate advisories.
        EventHandler<int> advisory = (_, count) =>
        {
            var epoch = _communityEpoch;
            Dispatcher.UIThread.Post(async () =>
            {
                if (epoch == _communityEpoch) await CommunityMessageAsync(
                    Loc.Get("community_prompt_warning_title"), Loc.GetF("community_prompt_warning_body", count));
            });
        };
        Prompts.Advisory += advisory;
        Closed += (_, _) => Prompts.Advisory -= advisory;
    }

    internal void RefreshCompanionCommunity()
    {
        var tab = Named<Tabs.CompanionTabView>("CompanionTab");
        if (tab?.FindControl<CompanionRoomView>("Room") is { } room) RefreshCommunityPrompts(room);
    }

    internal void RefreshCommunityPrompts(CompanionRoomView room)
    {
        if (room.WorkshopParts is not { } parts) return;
        var panel = parts.Community.FindControl<StackPanel>("InstalledPromptsPanel")!;
        var placeholder = parts.Community.FindControl<TextBlock>("TxtNoInstalledPrompts")!;
        for (int i = panel.Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(panel.Children[i], placeholder)) panel.Children.RemoveAt(i);
        var installed = Prompts.GetInstalledPrompts();
        placeholder.IsVisible = installed.Count == 0;
        foreach (var prompt in installed) panel.Children.Add(CreateCommunityPromptRow(prompt, room));
        parts.Roster.Refresh();
        room.RefreshPersonality();
    }

    internal Control CreateCommunityPromptRow(CommunityPrompt prompt, CompanionRoomView room)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (prompt.IsActive) name.Children.Add(new TextBlock { Text = "● ", FontSize = 10, Foreground = Brushes.MediumPurple });
        name.Children.Add(new TextBlock { Text = prompt.Name, FontSize = 10, Foreground = Brushes.White,
            FontWeight = prompt.IsActive ? FontWeight.SemiBold : FontWeight.Normal });
        name.Children.Add(new TextBlock { Text = " " + Loc.GetF("label_by_author", prompt.Author), FontSize = 9, Foreground = Brushes.Gray });
        row.Children.Add(name);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (!prompt.IsActive)
        {
            var activate = new Button { Content = Loc.Get("btn_activate"), FontSize = 9, Padding = new Thickness(6, 2), Tag = prompt.Id };
            activate.Click += async (_, _) =>
            {
                if (!await AcknowledgeCommunityPromptAsync(prompt)) return;
                Prompts.ActivatePrompt(prompt.Id);
                RefreshCommunityPrompts(room);
            };
            actions.Children.Add(activate);
        }
        var remove = new Button { Content = "×", FontSize = 12, Padding = new Thickness(4, 0), Tag = prompt.Id };
        ToolTip.SetTip(remove, Loc.Get("btn_uninstall"));
        remove.Click += (_, _) => { Prompts.RemovePrompt(prompt.Id); RefreshCommunityPrompts(room); };
        actions.Children.Add(remove);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private async Task<bool> AcknowledgeCommunityPromptAsync(CommunityPrompt prompt)
    {
        var s = CoreSettings.Current;
        if (!ExplicitContentGate.RequiresAcknowledgement(new PersonalityPreset { PromptSettings = prompt.PromptSettings }, s.SlutModeEnabled)
            || ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt)) return true;
        if (!await ShowCommunityDialogAsync(new ExplicitContentAcknowledgementDialog())) return false;
        if (s.CompanionPrompt != null) { ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt); CoreSettings.Save(); }
        return true;
    }

    private static readonly FilePickerFileType PromptJson = new("JSON files") { Patterns = new[] { "*.json" } };

    private async Task<string?> PickPromptAsync(string title, bool assignment = false)
    {
        IStorageFolder? folder = null;
        if (assignment)
        {
            var own = Path.Combine(CorePaths.EffectiveAssets, "prompts");
            var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
            var initial = Directory.Exists(own) && Directory.EnumerateFiles(own, "*.json").Any() ? own : shipped;
            if (Directory.Exists(initial)) folder = await StorageProvider.TryGetFolderFromPathAsync(initial);
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, SuggestedStartLocation = folder,
            FileTypeFilter = assignment ? new[] { PromptJson } : new[] { PromptJson, FilePickerFileTypes.All }
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task AssignPersonalityAsync(int index, CompanionRoomView room)
    {
        if (index is < 0 or > 4) return;
        var def = CompanionDefinition.GetById(index);
        var file = await PickPromptAsync($"Select AI Personality for {def.Name}", true);
        if (file == null) return;
        var prompt = Prompts.ImportFromFile(file);
        if (prompt == null) return;
        AssignImportedPersonality(index, prompt.Id);
        RefreshCommunityPrompts(room);
        await CommunityMessageAsync( Loc.Get("title_personality_assigned"), Loc.GetF("msg_personality_assigned", def.Name, prompt.Name));
    }

    internal void AssignImportedPersonality(int index, string promptId)
    {
        CoreSettings.Current.SetCompanionPromptId(index, promptId);
        CoreSettings.Save();
        if ((int)CompanionCore.ActiveCompanion == index) CompanionCore.ApplyCompanionPrompt((CompanionId)index);
    }

    private async Task ImportPromptAsync(CompanionRoomView room)
    {
        var file = await PickPromptAsync(Loc.Get("title_import_community_prompt"));
        if (file == null) return;
        var prompt = Prompts.ImportFromFile(file);
        await CommunityMessageAsync( Loc.Get(prompt == null ? "title_error" : "title_imported"), prompt == null
            ? Loc.Get("msg_failed_to_import_prompt_invalid") : Loc.GetF("msg_prompt_imported", prompt.Name, prompt.Author));
        RefreshCommunityPrompts(room);
    }

    private async Task ExportPromptAsync()
    {
        var prompt = Prompts.ExportCurrentSettings("My Custom Personality", Platform.AccountSeed.Patreon?.DisplayName ?? "Anonymous", "A custom AI personality.");
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Get("title_export_community_prompt"), SuggestedFileName = "My_Custom_Personality.json",
            FileTypeChoices = new[] { PromptJson }, DefaultExtension = "json"
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await Prompts.SavePromptToFileAsync(prompt, path);
            await CommunityMessageAsync( Loc.Get("title_exported"), Loc.GetF("msg_prompt_exported", path));
        }
        catch (Exception ex) { await CommunityMessageAsync( Loc.Get("title_error"), Loc.GetF("msg_failed_to_export_prompt", ex.Message)); }
    }

    internal static string CommunityBrowseMessage(System.Collections.Generic.IReadOnlyList<CommunityPromptManifestEntry> prompts)
    {
        var message = Loc.Get("label_available_prompts");
        foreach (var p in prompts.Take(5)) message += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
        if (prompts.Count > 5) message += Loc.GetF("label_and_more_prompts", prompts.Count - 5);
        message += Loc.Get("label_install_first_one");
        return message;
    }

    internal async Task BrowseCompanionCommunityAsync()
    {
        var tab = Named<Tabs.CompanionTabView>("CompanionTab");
        if (tab?.FindControl<CompanionRoomView>("Room") is { } room)
            await RunCommunityActionAsync(() => BrowsePromptsAsync(room), "msg_failed_to_browse_prompts");
    }

    private async Task BrowsePromptsAsync(CompanionRoomView room)
    {
        var epoch = _communityEpoch;
        var available = await Prompts.GetAvailablePromptsAsync();
        if (epoch != _communityEpoch) return;
        var remaining = available.Where(p => !Prompts.InstalledPromptIds.Contains(p.Id)).ToList();
        if (available.Count == 0 || remaining.Count == 0)
        {
            await CommunityMessageAsync( Loc.Get("title_community_prompts"),
                Loc.Get(available.Count == 0 ? "msg_no_community_prompts" : "msg_all_prompts_installed"));
            return;
        }
        var message = CommunityBrowseMessage(remaining);
        if (!await ShowCommunityDialogAsync(new MessageDialog(Loc.Get("title_browse_community_prompts"), message, true,
            okText: Loc.Get("btn_install"), cancelText: Loc.Get("btn_cancel")))) return;
        if (await Prompts.InstallPromptAsync(remaining[0].Id) is not { } prompt) return;
        if (epoch != _communityEpoch) return;
        await CommunityMessageAsync( Loc.Get("title_installed"), Loc.GetF("msg_prompt_installed", prompt.Name));
        RefreshCommunityPrompts(room);
    }
}
