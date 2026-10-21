using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · COMMUNITY: shared prompt engine, native pickers and WPF-equivalent rows.</summary>
    public partial class WorkshopCommunityCell : UserControl
    {
        // The existing Core service is shared with WPF; only presentation and native pickers live here.
        private CommunityPromptService? _service;
        internal CommunityPromptService Service => _service ??= CreateService();
        internal Func<Task<string?>>? ImportFileForTests { get; set; }
        internal Func<Task<string?>>? ExportFileForTests { get; set; }
        private Window? Owner => TopLevel.GetTopLevel(this) as Window;

        private CommunityPromptService CreateService() => CreateService(() => Owner);

        /// <summary>Also used by Z4's Community link (MakeHerYoursView): WPF routes both to one service.</summary>
        internal static CommunityPromptService CreateService(Func<Window?> ownerOf)
        {
            var service = new CommunityPromptService();
            service.ValidationAdvisory += (_, count) => global::Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (ownerOf() is { } owner)
                    await MessageDialog.ShowAsync(owner, Loc.Get("community_prompt_warning_title"), Loc.GetF("community_prompt_warning_body", count));
            });
            return service;
        }
        public WorkshopCommunityCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopCommunityCellViewModel();

            this.FindControl<Button>("BtnBrowsePrompts")!.Click += async (_, _) => await BrowseAsync();
            this.FindControl<Button>("BtnImportPrompt")!.Click += async (_, _) => await ImportAsync();
            this.FindControl<Button>("BtnExportPrompt")!.Click += async (_, _) => await ExportAsync();
            this.FindControl<Button>("BtnRefreshPrompts")!.Click += async (_, _) => await RefreshAsync();

            // CompanionWheelRelay.Attach(InstalledPromptsScroll) is NOT ported: it works around
            // WPF's ScrollViewer marking every wheel notch handled even when it cannot scroll.
            // Avalonia's ScrollViewer.IsScrollChainingEnabled (default true) already passes an
            // unusable notch to the parent scroll, which is exactly what the relay re-implements.
        }
        internal WorkshopCommunityCell(CommunityPromptService service) : this() => _service = service;
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            RefreshInstalledPrompts();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _service?.Dispose();
            _service = null;
            base.OnDetachedFromVisualTree(e);
        }

        internal void RefreshInstalledPrompts()
        {
            var panel = this.FindControl<StackPanel>("InstalledPromptsPanel")!;
            var placeholder = this.FindControl<TextBlock>("TxtNoInstalledPrompts")!;
            foreach (var child in panel.Children.ToArray())
                if (!ReferenceEquals(child, placeholder)) panel.Children.Remove(child);
            int shown = 0;
            foreach (var prompt in Service.GetInstalledPrompts())
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
                var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (prompt.IsActive) name.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(Color.FromRgb(147, 112, 219)) }); // WPF "● " active dot, drawn (IconGuard)
                name.Children.Add(new TextBlock { Text = prompt.Name, FontSize = 10, Foreground = Brushes.White, FontWeight = prompt.IsActive ? FontWeight.SemiBold : FontWeight.Normal });
                name.Children.Add(new TextBlock { Text = " " + Loc.GetF("label_by_author", prompt.Author), FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 96)) });
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                if (!prompt.IsActive)
                {
                    var activate = new Button { Name = "ActivatePrompt", Content = new TextBlock { Text = Loc.Get("btn_activate") }, Tag = prompt.Id, Background = Brushes.Transparent, BorderThickness = default, FontSize = 9, Padding = new Thickness(6, 2), Foreground = new SolidColorBrush(Color.FromRgb(147, 112, 219)), Cursor = new Cursor(StandardCursorType.Hand) };
                    activate.Click += async (_, _) => await ActivateAsync(prompt.Id);
                    actions.Children.Add(activate);
                }
                var remove = new Button { Name = "RemovePrompt", Content = new IconGlyph { Kind = IconKind.Dismiss, Size = 12 }, Tag = prompt.Id, Background = Brushes.Transparent, BorderThickness = default, Padding = new Thickness(4, 0), Foreground = Brushes.Gray, Cursor = new Cursor(StandardCursorType.Hand) };
                ToolTip.SetTip(remove, Loc.Get("btn_uninstall"));
                global::Avalonia.Automation.AutomationProperties.SetName(remove, Loc.Get("btn_uninstall"));
                remove.Click += (_, _) => { Service.RemovePrompt(prompt.Id); RefreshInstalledPrompts(); };
                actions.Children.Add(remove);
                Grid.SetColumn(actions, 1);
                row.Children.Add(name); row.Children.Add(actions); panel.Children.Add(row); shown++;
            }
            placeholder.IsVisible = shown == 0;
            // WPF UpdateCommunityPromptsUI (MainWindow.CompanionTab.cs:184) re-reads Z4's readout with the rows.
            this.FindAncestorOfType<CompanionRoomView>()?.PersonalityZone.ViewModel.Sync();
        }

        private async Task ActivateAsync(string id)
        {
            var prompt = Service.GetInstalledPrompt(id);
            if (prompt == null) return;
            var settings = CoreSettings.Current;
            if (ExplicitContentGate.RequiresAcknowledgement(new PersonalityPreset { PromptSettings = prompt.PromptSettings }, settings.SlutModeEnabled)
                && !ExplicitContentGate.IsAlreadyAcknowledged(settings.CompanionPrompt))
            {
                if (Owner is not { } owner || !await new ExplicitContentAcknowledgementDialog().ShowDialogSafe<bool>(owner)) return;
                ExplicitContentGate.MarkAcknowledged(settings.CompanionPrompt);
                CoreSettings.Save();
            }
            Service.ActivatePrompt(id);
            RefreshInstalledPrompts();
        }

        private async Task RefreshAsync()
        {
            var button = this.FindControl<Button>("BtnRefreshPrompts")!;
            button.IsEnabled = false; button.Content = new TextBlock { Text = "..." };
            try { await Service.GetAvailablePromptsAsync(forceRefresh: true); RefreshInstalledPrompts(); }
            catch (Exception ex) { Log.Warning(ex, "Community prompt refresh failed"); }
            finally { button.IsEnabled = true; button.Content = new TextBlock { Text = Loc.Get("btn_refresh") }; }
        }

        private async Task TellAsync(string title, string body)
        {
            if (Owner is { } owner) await MessageDialog.ShowAsync(owner, Loc.Get(title), body);
        }

        private Task BrowseAsync() => BrowseAsync(() => Owner, Service, RefreshInstalledPrompts);

        /// <summary>WPF BtnBrowsePrompts_Click (MainWindow.Patreon.cs:2252), shared by this cell and Z4's Community link.</summary>
        internal static async Task BrowseAsync(Func<Window?> ownerOf, CommunityPromptService service, Action installed)
        {
            async Task TellAsync(string title, string body)
            {
                if (ownerOf() is { } owner) await MessageDialog.ShowAsync(owner, Loc.Get(title), body);
            }
            try
            {
                var available = await service.GetAvailablePromptsAsync();
                if (available.Count == 0) { await TellAsync("title_community_prompts", Loc.Get("msg_no_community_prompts")); return; }
                var choices = available.Where(p => !service.InstalledPromptIds.Contains(p.Id)).ToList();
                if (choices.Count == 0) { await TellAsync("title_community_prompts", Loc.Get("msg_all_prompts_installed")); return; }
                string body = Loc.Get("label_available_prompts");
                foreach (var p in choices.Take(5)) body += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
                if (choices.Count > 5) body += Loc.GetF("label_and_more_prompts", choices.Count - 5);
                body += Loc.Get("label_install_first_one");
                if (ownerOf() is { } owner && await MessageDialog.ConfirmAsync(owner, Loc.Get("title_browse_community_prompts"), body, okText: Loc.Get("btn_install"), cancelText: Loc.Get("btn_cancel")))
                {
                    var prompt = await service.InstallPromptAsync(choices[0].Id);
                    if (prompt != null) { await TellAsync("title_installed", Loc.GetF("msg_prompt_installed", prompt.Name)); installed(); }
                }
            }
            catch (Exception ex) { await TellAsync("title_error", Loc.GetF("msg_failed_to_browse_prompts", ex.Message)); }
        }

        private static readonly FilePickerFileType JsonFiles = new("JSON files") { Patterns = new[] { "*.json" } };
        private async Task ImportAsync()
        {
            try
            {
                string? path = null;
                if (ImportFileForTests != null) path = await ImportFileForTests();
                else if (Owner is { } owner)
                    path = (await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.Get("title_import_community_prompt"), FileTypeFilter = new[] { JsonFiles, FilePickerFileTypes.All } })).FirstOrDefault()?.TryGetLocalPath();
                if (path == null) return;
                var prompt = Service.ImportFromFile(path);
                if (prompt == null) { await TellAsync("title_error", Loc.Get("msg_failed_to_import_prompt_invalid")); return; }
                await TellAsync("title_imported", Loc.GetF("msg_prompt_imported", prompt.Name, prompt.Author));
                RefreshInstalledPrompts();
            }
            catch (Exception ex) { await TellAsync("title_error", Loc.GetF("msg_failed_to_import_prompt_error", ex.Message)); }
        }

        private async Task ExportAsync()
        {
            try
            {
                var prompt = Service.ExportCurrentSettings("My Custom Personality", CoreAccount.DisplayName ?? "Anonymous", "A custom AI personality.");
                string? path = null;
                if (ExportFileForTests != null) path = await ExportFileForTests();
                else if (Owner is { } owner)
                    path = (await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Loc.Get("title_export_community_prompt"), SuggestedFileName = "My_Custom_Personality.json", FileTypeChoices = new[] { JsonFiles } }))?.TryGetLocalPath();
                if (path == null) return;
                await Service.SavePromptToFileAsync(prompt, path);
                await TellAsync("title_exported", Loc.GetF("msg_prompt_exported", path));
            }
            catch (Exception ex) { await TellAsync("title_error", Loc.GetF("msg_failed_to_export_prompt", ex.Message)); }
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class WorkshopCommunityCellViewModel
    {
        public string LocBeta => Loc.Get("label_beta");
        public string LocBrowse => Loc.Get("btn_browse");
        public string LocImport => Loc.Get("btn_import");
        public string LocExport => Loc.Get("btn_export");
        public string LocInstalledPrompts => Loc.Get("label_installed_prompts");
        public string LocRefresh => Loc.Get("btn_refresh");
        public string LocNoPromptsInstalled => Loc.Get("label_no_prompts_installed_yet");
    }
}
