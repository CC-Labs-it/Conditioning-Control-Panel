using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/KnowledgeLinksEditor.xaml.cs:
    /// the global knowledge base links editor (AppSettings.GlobalKnowledgeBaseLinks).
    /// <see cref="SaveImmediately"/> writes every add / remove straight to settings (the Links page);
    /// CompanionPromptEditorDialog hosts it too and saves with the dialog, as WPF's does.
    /// MessageBox -> MessageDialog (awaited, so the clicks are async).
    /// </summary>
    public partial class KnowledgeLinksEditor : UserControl
    {
        private readonly ObservableCollection<KnowledgeBaseLink> _links = new();

        public KnowledgeLinksEditor()
        {
            InitializeComponent();
            LstKnowledgeLinks.ItemsSource = _links;
            _links.CollectionChanged += (_, _) => UpdateEmpty();
            UpdateEmpty();
            BtnAddKnowledgeLink.Click += async (_, _) => await AddAsync();
            BtnRemoveKnowledgeLink.Click += async (_, _) => await RemoveAsync();
        }

        /// <summary>True on the Links page: each edit saves at once.</summary>
        public bool SaveImmediately { get; set; }

        /// <summary>Raised after an add or a remove.</summary>
        public event EventHandler? Changed;

        internal ObservableCollection<KnowledgeBaseLink> Links => _links;

        /// <summary>Test seam: the Add dialog (WPF KnowledgeLinkEditorDialog.ShowDialog). Returns the new link or null.</summary>
        internal Func<Window?, Task<KnowledgeBaseLink?>> PromptForLink { get; set; } = async owner =>
        {
            var dialog = new KnowledgeLinkEditorDialog();
            await dialog.ShowDialogSafe(owner);
            return dialog.Result;
        };

        public void Load()
        {
            _links.Clear();
            foreach (var link in CoreSettings.Current.GlobalKnowledgeBaseLinks) _links.Add(link);
        }

        public void SaveToSettings()
        {
            var links = CoreSettings.Current.GlobalKnowledgeBaseLinks;
            links.Clear();
            foreach (var link in _links) links.Add(link);
        }

        private void UpdateEmpty() => TxtKnowledgeLinksEmpty.IsVisible = _links.Count == 0;

        private void Commit()
        {
            if (SaveImmediately)
            {
                SaveToSettings();
                CoreSettings.Save();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        internal async Task AddAsync()
        {
            var link = await PromptForLink(TopLevel.GetTopLevel(this) as Window);
            if (link == null) return;
            _links.Add(link);
            Commit();
        }

        internal async Task RemoveAsync()
        {
            if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
            {
                _links.Remove(link);
                Commit();
            }
            else if (TopLevel.GetTopLevel(this) is Window owner)
            {
                await MessageDialog.ShowAsync(owner, Loc.Get("btn_remove_selected"), Loc.Get("msg_please_select_a_link_to_remove"));
            }
        }
    }
}
