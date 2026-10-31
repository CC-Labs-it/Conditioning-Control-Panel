using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF PersonalityStudio: edit a detached draft, then activate a copy or update an own preset.</summary>
internal sealed class PersonalityStudio : Window
{
    internal PersonalityStudio()
    {
        Title = L("title"); Width = 660; Height = 650; MinWidth = 460; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(22, 17, 32)); Foreground = Brushes.WhiteSmoke;
        var service = PersonalityService.Shared; var settings = CoreSettings.Current;
        var source = service.GetActivePreset();
        var overridden = settings.CompanionPrompt.UseCustomPrompt;
        var draft = PersonalityStudioDraft.Create(source, overridden ? settings.CompanionPrompt : null);
        var own = overridden || source.IsBuiltIn ? null : settings.UserPersonalityPresets.FirstOrDefault(p => p.Id == source.Id);
        var grid = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(new TextBlock { Text = L(own == null ? "intro" : "intro_own"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });
        var form = new StackPanel(); var p = draft.PromptSettings!;
        var name = Field(form, "Name", L("name"), source.Name, 60, 0);
        var voice = Field(form, "Voice", L("voice"), p.Personality, 3600, 180);
        var style = Field(form, "Style", L("style"), p.OutputRules, 700, 80);
        var samples = Field(form, "Samples", L("samples"), string.Join(Environment.NewLine, draft.SampleLines ?? new()), 300, 62);
        var extra = new StackPanel();
        var background = Field(extra, "Background", L("background"), p.KnowledgeBase, 1200, 90);
        var bounds = Field(extra, "Boundaries", L("boundaries"), p.ExplicitReaction, 800, 70);
        var awareness = Field(extra, "Awareness", Loc.Get("setting_screen_awareness_reactions"), p.ContextReactions, 1400, 110);
        form.Children.Add(new Expander { Header = L("advanced"), Content = extra, Margin = new Thickness(0,12,0,0) });
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0,18,0,0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        var cancel = new Button { Name = "Cancel", Content = L("cancel"), IsCancel = true }; cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        var save = new Button { Name = "Save", Content = L(own == null ? "save" : "update") }; actions.Children.Add(save);
        void Validate() => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(voice.Text);
        name.TextChanged += (_, _) => Validate(); voice.TextChanged += (_, _) => Validate(); Validate();
        save.Click += (_, _) => {
            draft.Name = name.Text!.Trim(); p.Personality = voice.Text!.Trim(); p.OutputRules = (style.Text ?? "").Trim();
            p.KnowledgeBase = (background.Text ?? "").Trim(); p.ExplicitReaction = (bounds.Text ?? "").Trim(); p.ContextReactions = (awareness.Text ?? "").Trim();
            draft.SampleLines = PersonalitySamples.Clean((samples.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (own != null) { own.Name = draft.Name; own.PromptSettings = p; own.SampleLines = draft.SampleLines; own.ModifiedAt = DateTime.Now; }
            else settings.UserPersonalityPresets.Add(draft);
            if (!service.SetActivePreset(own?.Id ?? draft.Id)) { if (own == null) settings.UserPersonalityPresets.Remove(draft); return; }
            CoreModsHooks.InvalidateStablePrompt?.Invoke(); Close(true);
        };
        if (own != null) {
            var delete = new Button { Name = "Delete", Content = L("delete") }; footer.Children.Add(delete);
            delete.Click += async (_, _) => {
                if (!await MessageDialog.ConfirmAsync(this, L("title"), string.Format(L("delete_confirm"), own.Name)) || !service.DeletePreset(own.Id)) return;
                CoreModsHooks.InvalidateStablePrompt?.Invoke(); Close();
            };
        }
        Grid.SetRow(footer, 2); grid.Children.Add(footer); Content = grid;
    }
    private static TextBox Field(Panel panel, string id, string label, string text, int max, double height)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,10,0,5) });
        var box = new TextBox { Name = id, Text = text, MaxLength = Math.Max(max, (text?.Length ?? 0) + 400), MinHeight = 32,
            AcceptsReturn = height > 0, TextWrapping = height > 0 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Foreground = Brushes.Black, Background = Brushes.WhiteSmoke, Padding = height > 0 ? new Thickness(8) : new Thickness(8,4), VerticalContentAlignment = VerticalAlignment.Center };
        if (height > 0) { box.Height = height; ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto); }
        AutomationProperties.SetName(box, label); panel.Children.Add(box); return box;
    }
    internal static Task Show(Window owner) => new PersonalityStudio().ShowDialogSafe(owner);
    private static string L(string key) => Loc.Get("companion_studio_" + key);
}
