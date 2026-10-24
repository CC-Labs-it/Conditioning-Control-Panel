using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>WPF Social > Leash: live cards, with the Friends door when no cards are present.</summary>
public partial class LeashTabView : UserControl
{
    internal LeashDrawerSection Section { get; }
    public LeashTabView() : this(() => LeashHead.Service) { }
    internal LeashTabView(Func<ILeashService?> service)
    {
        InitializeComponent();
        Section = new LeashDrawerSection(service);
        Section.Changed += () => EmptyPanel.IsVisible = !Section.IsVisible;
        SectionHost.Children.Add(Section);
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
    }
    internal bool ShowingEmpty => EmptyPanel.IsVisible;
    internal void OnShown() => Section.Refresh();
}
