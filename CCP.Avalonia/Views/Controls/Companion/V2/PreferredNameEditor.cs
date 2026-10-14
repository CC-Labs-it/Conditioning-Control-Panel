using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

/// <summary>PORTED from WPF Views/Controls/Companion/V2/PreferredNameEditor.cs; the rules are Core <see cref="ConversationRules"/>.</summary>
internal sealed class PreferredNameEditor : StackPanel
{
    private readonly IMemoryStore? _owner;
    internal TextBox NameBox { get; }
    internal Button SaveButton { get; }
    internal TextBlock Notice { get; }

    public PreferredNameEditor()
    {
        Margin = new Thickness(0, 0, 0, 16);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_calls_you"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var row = new DockPanel();
        // A TextBlock inside, not a string: Avalonia's Button reads '_' as an access key.
        SaveButton = new Button { Content = new TextBlock { Text = Loc.Get("companion_memory_edit_save") }, Margin = new Thickness(10, 0, 0, 0), MinWidth = 70 };
        DockPanel.SetDock(SaveButton, Dock.Right);
        row.Children.Add(SaveButton);
        NameBox = new TextBox { Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(Color.FromRgb(37, 26, 48)), Foreground = Brushes.White };
        AutomationProperties.SetName(NameBox, Loc.Get("companion_v2_calls_you"));
        App.Brain?.EnsureCurrentAccount();
        _owner = App.Brain?.Memory;
        NameBox.Text = ConversationRules.PreferredName(_owner);
        SaveButton.IsEnabled = NameBox.IsEnabled = _owner != null;
        SaveButton.Click += (_, _) => Save();
        row.Children.Add(NameBox);
        Children.Add(row);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_name_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch });
        Notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        AutomationProperties.SetLiveSetting(Notice, AutomationLiveSetting.Polite);
        Children.Add(Notice);
    }

    internal void Save()
    {
        App.Brain?.EnsureCurrentAccount();
        var (result, name) = ConversationRules.SavePreferredName(App.Brain?.Memory, _owner, NameBox.Text,
            ConditioningControlPanel.CoreModerationLog.Guard);
        if (result == ConversationRules.NameSave.Stale) { NameBox.Text = string.Empty; IsEnabled = false; return; }
        NameBox.Text = name;
        Notice.Text = Loc.Get(result == ConversationRules.NameSave.Refused ? "companion_v2_name_refused" : "companion_v2_name_saved");
    }
}
