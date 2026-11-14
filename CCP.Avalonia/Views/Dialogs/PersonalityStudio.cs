using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Automation;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF Services/Companion/PersonalityStudio.cs:15-205. Editing is detached until Save.</summary>
internal static class PersonalityStudio
{
    internal static PersonalityPreset Draft(PersonalityPreset source, CompanionPromptSettings? activeOverride = null)
    {
        var p = activeOverride ?? source.PromptSettings ?? new CompanionPromptSettings();
        return new PersonalityPreset
        {
            Id = Guid.NewGuid().ToString("N"), Name = source.Name, Description = source.Description,
            IsBuiltIn = false, RequiresExplicitAcknowledgement = source.RequiresExplicitAcknowledgement,
            SampleLines = source.SampleLines?.ToList(),
            PromptSettings = new CompanionPromptSettings
            {
                UseCustomPrompt = true, Personality = p.Personality, ExplicitReaction = p.ExplicitReaction,
                SlutModePersonality = p.SlutModePersonality, KnowledgeBase = p.KnowledgeBase,
                ContextReactions = p.ContextReactions, OutputRules = p.OutputRules
            }
        };
    }

    public static async Task ShowAsync(Window owner)
    {
        var service = PersonalityService.Shared;
        var settings = CoreSettings.Service?.Current;
        if (service == null || settings == null) return;
        var source = service.GetActivePreset();
        var overridden = settings.CompanionPrompt?.UseCustomPrompt == true;
        var draft = Draft(source, overridden ? settings.CompanionPrompt : null);
        var own = overridden ? null : OwnPreset(source, settings.UserPersonalityPresets);
        var dialog = new Window
        {
            Name = "PersonalityStudio", Title = L("title"), Width = 660, Height = 650, MinWidth = 460, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(22, 17, 32)), Foreground = Brushes.WhiteSmoke
        };
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var intro = new TextBlock { Text = L(own != null ? "intro_own" : "intro"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        grid.Children.Add(intro);
        var form = new StackPanel();
        var name = Field(form, L("name"), source.Name, 60, SingleLine);
        var voice = Field(form, L("voice"), draft.PromptSettings!.Personality, 3600, 180);
        var style = Field(form, L("style"), draft.PromptSettings.OutputRules, 700, 80);
        var samples = Field(form, L("samples"), string.Join(Environment.NewLine, draft.SampleLines ?? new List<string>()), 300, 62);
        var advanced = new Expander { Header = L("advanced"), Margin = new Thickness(0, 12, 0, 0) };
        var extra = new StackPanel();
        var background = Field(extra, L("background"), draft.PromptSettings.KnowledgeBase, 1200, 90);
        var bounds = Field(extra, L("boundaries"), draft.PromptSettings.ExplicitReaction, 800, 70);
        var awareness = Field(extra, Loc.Get("setting_screen_awareness_reactions"), draft.PromptSettings.ContextReactions, 1400, 110);
        advanced.Content = extra;
        form.Children.Add(advanced);
        var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1); grid.Children.Add(scroller);
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 18, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        var cancel = new Button { Content = new TextBlock { Text = L("cancel") }, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = new TextBlock { Text = L(own != null ? "update" : "save") }, Padding = new Thickness(16, 8, 16, 8) };
        actions.Children.Add(cancel); actions.Children.Add(save);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        if (own != null)
        {
            var delete = new Button { Content = new TextBlock { Text = L("delete") }, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(delete, Dock.Left);
            delete.Name = "Delete";
            delete.Click += async (_, _) =>
            {
                var asked = await MessageDialog.ConfirmAsync(dialog, L("title"), string.Format(L("delete_confirm"), own.Name),
                    okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"));
                if (!asked || !service.DeletePreset(own.Id)) return;
                BambiSprite.InvalidateStablePrompt();
                dialog.Close();
            };
            footer.Children.Add(delete);
        }
        void Validate() => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(voice.Text);
        name.TextChanged += (_, _) => Validate(); voice.TextChanged += (_, _) => Validate(); Validate();
        save.Click += (_, _) =>
        {
            draft.Name = (name.Text ?? string.Empty).Trim();
            draft.PromptSettings.Personality = (voice.Text ?? string.Empty).Trim();
            draft.PromptSettings.OutputRules = (style.Text ?? string.Empty).Trim();
            draft.PromptSettings.KnowledgeBase = (background.Text ?? string.Empty).Trim();
            draft.PromptSettings.ExplicitReaction = (bounds.Text ?? string.Empty).Trim();
            draft.PromptSettings.ContextReactions = (awareness.Text ?? string.Empty).Trim();
            draft.SampleLines = PersonalitySamples.Clean((samples.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (own != null)
            {
                own.Name = draft.Name;
                own.PromptSettings = draft.PromptSettings;
                own.SampleLines = draft.SampleLines;
                own.ModifiedAt = DateTime.Now;
                if (service.SetActivePreset(own.Id))
                {
                    BambiSprite.InvalidateStablePrompt();
                    dialog.Close(true);
                }
                return;
            }
            settings.UserPersonalityPresets.Add(draft);
            if (!service.SetActivePreset(draft.Id))
            {
                settings.UserPersonalityPresets.Remove(draft);
                return;
            }
            BambiSprite.InvalidateStablePrompt();
            dialog.Close(true);
        };
        cancel.Name = "Cancel"; save.Name = "Save";
        name.Name = "Name"; voice.Name = "Voice"; style.Name = "Style"; samples.Name = "Samples";
        background.Name = "Background"; bounds.Name = "Boundaries"; awareness.Name = "Awareness";
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(false); } };
        dialog.Content = grid;
        await dialog.ShowDialog<bool>(owner);
    }

    private static TextBox Field(Panel panel, string label, string text, int max, double height)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
        var single = height <= SingleLine;
        var box = single
            ? new TextBox
            {
                Text = text, MaxLength = FieldCap(max, text), MinHeight = 32, TextWrapping = TextWrapping.NoWrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Black, Background = Brushes.WhiteSmoke, Padding = new Thickness(8, 4, 8, 4)
            }
            : new TextBox
            {
                Text = text, MaxLength = FieldCap(max, text), Height = height, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black, Background = Brushes.WhiteSmoke, Padding = new Thickness(8)
            };
        ScrollViewer.SetVerticalScrollBarVisibility(box, single ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        AutomationProperties.SetName(box, label);
        panel.Children.Add(box);
        return box;
    }

    /// <summary>
    /// A box's length cap. Never under what it opens with: a mod persona or an old custom prompt
    /// longer than the cap opened as a box that refused every keystroke (#1417).
    /// </summary>
    internal static int FieldCap(int max, string? text)
        => Math.Max(max, (text?.Length ?? 0) + FieldHeadroom);

    internal const int FieldHeadroom = 400;

    private const double SingleLine = 0;

    /// <summary>
    /// The user's own preset behind <paramref name="source"/>, or null for a built-in or mod one.
    /// Matched by id against the user's list, so a built-in that happens to share a name never counts.
    /// </summary>
    internal static PersonalityPreset? OwnPreset(PersonalityPreset? source, IEnumerable<PersonalityPreset>? userPresets)
        => source == null || source.IsBuiltIn ? null : userPresets?.FirstOrDefault(p => p.Id == source.Id);

    private static string L(string key) => Loc.Get("companion_studio_" + key);
}
