using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

/// <summary>WPF KnowledgeLinksEditor (5681c132a): one list for the page and prompt dialog.</summary>
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
        var links = CoreSettings.Current.GlobalKnowledgeBaseLinks;
        links.Clear();
        foreach (var link in Links) links.Add(link);
    }

    private void UpdateEmpty() => TxtKnowledgeLinksEmpty.IsVisible = Links.Count == 0;
    private void Commit()
    {
        if (SaveImmediately) { SaveToSettings(); CoreSettings.Save(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async void AddKnowledgeLink_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new KnowledgeLinkEditorDialog();
        await dialog.ShowDialogSafe(TopLevel.GetTopLevel(this) as Window);
        if (dialog.Result is not { } link) return;
        Links.Add(link);
        Commit();
    }

    private async void RemoveKnowledgeLink_Click(object? sender, RoutedEventArgs e)
    {
        if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
        {
            Links.Remove(link);
            Commit();
        }
        else
            await MessageDialog.ShowAsync((TopLevel.GetTopLevel(this) as Window)!,
                Loc.Get("btn_remove_selected"), Loc.Get("msg_please_select_a_link_to_remove"));
    }
}
