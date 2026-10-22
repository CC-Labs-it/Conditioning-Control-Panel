using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>Social &gt; Leash. Hosts a second <see cref="LeashDrawerSection"/> like WPF LeashTabView.xaml.cs:32
/// (the drawer keeps its own), so every action is the section's own. Empty: one line and a button to Friends.</summary>
public partial class LeashTabView : UserControl
{
    internal LeashDrawerSection Section { get; }

    public LeashTabView() : this(() => LeashHead.Service) { }
    internal LeashTabView(Func<ILeashService?> service)
    {
        InitializeComponent();
        Section = new LeashDrawerSection(service);
        Section.Changed += SyncEmpty;
        SectionHost.Children.Add(Section);
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
        // The shell hides tabs with IsVisible; a hidden page stops listening (P01) and redraws on the next show.
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) Section.Unsubscribe(); };
        SyncEmpty();
    }

    internal bool ShowingEmpty => EmptyPanel.IsVisible;
    internal LeashSelfCard? SelfCard => Section.SelfCard;

    private void SyncEmpty() => EmptyPanel.IsVisible = !Section.IsVisible;

    /// <summary>ShowTab("leash"): repaint, and report the offers / today's task on screen.</summary>
    internal void OnShown()
    {
        try { Section.OnShown(); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] page repaint failed: {E}", ex.Message); }
        SyncEmpty();
    }
}
