using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

/// <summary>One links editor for the page (immediate save) and prompt dialog (save on acceptance).</summary>
public partial class KnowledgeLinksEditor : UserControl
{
    internal ObservableCollection<KnowledgeBaseLink> Links { get; } = new();
    public bool SaveImmediately { get; set; }
    public event EventHandler? Changed;

    public KnowledgeLinksEditor()
    {
        InitializeComponent();
        LstKnowledgeLinks.ItemsSource = Links;
        Links.CollectionChanged += (_, _) => UpdateEmpty();
        UpdateEmpty();
    }

    public void Load()
    {
        Links.Clear();
        foreach (var link in CoreSettings.Current.GlobalKnowledgeBaseLinks) Links.Add(link);
    }

    public void SaveToSettings()
    {
        var target = CoreSettings.Current.GlobalKnowledgeBaseLinks;
        target.Clear();
        foreach (var link in Links) target.Add(link);
    }

    private void UpdateEmpty() => TxtKnowledgeLinksEmpty.IsVisible = Links.Count == 0;

    private void Commit()
    {
        if (SaveImmediately) { SaveToSettings(); CoreSettings.Save(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new KnowledgeLinkEditorDialog();
        await dialog.ShowDialogSafe(owner);
        if (dialog.Result is not { } link) return;
        Links.Add(link);
        Commit();
    }

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
        {
            Links.Remove(link);
            Commit();
        }
        else if (TopLevel.GetTopLevel(this) is Window owner)
            await MessageDialog.ShowAsync(owner, Loc.Get("btn_remove_selected"), Loc.Get("msg_please_select_a_link_to_remove"));
    }
}
