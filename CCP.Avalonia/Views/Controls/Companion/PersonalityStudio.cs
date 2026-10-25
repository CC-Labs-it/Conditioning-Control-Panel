using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

/// <summary>Adapted from WPF PersonalityStudio: edit a detached copy; cancel changes nothing.</summary>
internal static class PersonalityStudio
{
    private static readonly HashSet<Window> Active = new();
    internal static void CloseForPanic() { foreach (var window in new List<Window>(Active)) window.Close(); }
    private const double SingleLine = 0;
    public static async System.Threading.Tasks.Task Show(Window owner)
    {
        var service = PersonalityService.Shared;
        var settings = CoreSettings.Current;
        if (service == null || settings == null) return;
        var source = service.GetActivePreset();
        var overridden = settings.CompanionPrompt?.UseCustomPrompt == true;
        var draft = PersonalityStudioRules.Draft(source, overridden ? settings.CompanionPrompt : null);
        // One of the user's own: Save updates it in place and Delete is offered. Saving always
        // added a copy before, so editing piled up same-named duplicates nothing could remove (#1481).
        // Not while a community or hand-edited prompt holds the wire: the boxes then show THAT text,
        // and saving it in place would overwrite the preset's own voice for good. That stays a copy.
        var own = overridden ? null : PersonalityStudioRules.OwnPreset(source, settings.UserPersonalityPresets);
        var dialog = new Window
        {
            Title = L("title"), Width = 660, Height = 650, MinWidth = 460, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(22, 17, 32)), Foreground = Brushes.WhiteSmoke
        };
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var intro = new TextBlock { Text = L(own != null ? "intro_own" : "intro"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        grid.Children.Add(intro);
        var form = new StackPanel();
        var name = Field(form, L("name"), source.Name, 60, SingleLine);
        name.Name = "name";
        var voice = Field(form, L("voice"), draft.PromptSettings!.Personality, 3600, 180);
        voice.Name = "voice";
        var style = Field(form, L("style"), draft.PromptSettings.OutputRules, 700, 80);
        style.Name = "style";
        var samples = Field(form, L("samples"), string.Join(Environment.NewLine, draft.SampleLines ?? new List<string>()), 300, 62);
        samples.Name = "samples";
        var advanced = new Expander { Header = L("advanced"), Margin = new Thickness(0, 12, 0, 0) };
        var extra = new StackPanel();
        var background = Field(extra, L("background"), draft.PromptSettings.KnowledgeBase, 1200, 90);
        background.Name = "background";
        var bounds = Field(extra, L("boundaries"), draft.PromptSettings.ExplicitReaction, 800, 70);
        bounds.Name = "bounds";
        // What the companion says about the window in front. The studio copied it unseen, so a
        // copy could never change it (#1404). Same label as the full editor's section.
        var awareness = Field(extra, Loc.Get("setting_screen_awareness_reactions"), draft.PromptSettings.ContextReactions, 1400, 110);
        awareness.Name = "awareness";
        advanced.Content = extra;
        form.Children.Add(advanced);
        var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1); grid.Children.Add(scroller);
        // Delete docks left, the actions right: one row that never lets them overlap.
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 18, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        var cancel = new Button { Content = L("cancel"), IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = L(own != null ? "update" : "save"), Padding = new Thickness(16, 8, 16, 8) };
        save.Name = "Save"; cancel.Name = "Cancel";
        cancel.Click += (_, _) => dialog.Close();
        actions.Children.Add(cancel); actions.Children.Add(save);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        if (own != null)
        {
            var delete = new Button { Content = L("delete"), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
            delete.Name = "Delete";
            DockPanel.SetDock(delete, Dock.Left);
            delete.Click += async (_, _) =>
            {
                if (!await ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog.ConfirmAsync(dialog, L("title"), string.Format(L("delete_confirm"), own.Name), okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"))
                    || !service.DeletePreset(own.Id)) return;
                CoreModsHooks.InvalidateStablePrompt?.Invoke();
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
                // Re-picking it saves settings and fences the old voice off the wire, as a new copy does.
                if (service.SetActivePreset(own.Id))
                {
                    CoreModsHooks.InvalidateStablePrompt?.Invoke();
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
            CoreModsHooks.InvalidateStablePrompt?.Invoke();
            dialog.Close(true);
        };
        dialog.Content = grid;
        Active.Add(dialog);
        try { await dialog.ShowDialogSafe(owner); }
        finally { Active.Remove(dialog); }
    }

    private static TextBox Field(Panel panel, string label, string text, int max, double height)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
        var single = height <= SingleLine;
        // A single line sizes to its text. A fixed 34 px with 8 px padding and a wrap left one
        // clipped line behind a tiny scroll bar, so a typed name could not be read (#1481).
        var box = single
            ? new TextBox
            {
                Text = text, MaxLength = PersonalityStudioRules.FieldCap(max, text), MinHeight = 32, TextWrapping = TextWrapping.NoWrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Black, Background = Brushes.WhiteSmoke, Padding = new Thickness(8, 4, 8, 4)
            }
            : new TextBox
            {
                Text = text, MaxLength = PersonalityStudioRules.FieldCap(max, text), Height = height, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black, Background = Brushes.WhiteSmoke, Padding = new Thickness(8)
            };
        AutomationProperties.SetName(box, label);
        panel.Children.Add(box);
        return box;
    }

    private static string L(string key) => Loc.Get("companion_studio_" + key);
}
