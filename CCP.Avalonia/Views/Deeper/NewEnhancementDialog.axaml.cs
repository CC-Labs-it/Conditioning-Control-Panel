using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    // WPF NewEnhancementDialog: Part 1 here, success-only Part 2 in the editor.
    public partial class NewEnhancementDialog : Window
    {
        public string SelectedMediaType { get; private set; } = MediaTypes.Video;
        public string SelectedSource { get; private set; } = "";

        private string? _pendingPart2Tutorial;
        private TutorialOverlay? _activeTutorialOverlay;

        private readonly RadioButton _rbVideo;
        private readonly RadioButton _rbAudio;
        private readonly TextBox _txtSource;

        public NewEnhancementDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _rbVideo = this.FindControl<RadioButton>("RbVideo")!;
            _rbAudio = this.FindControl<RadioButton>("RbAudio")!;
            _txtSource = this.FindControl<TextBox>("TxtSource")!;

            this.FindControl<Button>("BtnBrowse")!.Click += async (_, _) => await BrowseAsync();
            this.FindControl<Button>("BtnLocalVideoTutorial")!.Click += (_, _) => { _rbVideo.IsChecked = true; StartInteractiveTutorial(TutorialType.DeeperEditorInteractiveLocalVideo, TutorialType.DeeperEditorInteractiveLocalVideoPart2); };
            this.FindControl<Button>("BtnLocalAudioTutorial")!.Click += (_, _) => { _rbAudio.IsChecked = true; StartInteractiveTutorial(TutorialType.DeeperEditorInteractiveLocalAudio, TutorialType.DeeperEditorInteractiveLocalAudioPart2); };
            this.FindControl<Button>("BtnTryHypnoTubeTutorial")!.Click += (_, _) => BtnTryHypnoTubeTutorial_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnCreate")!.Click += async (_, _) => await BtnCreateAsync();
            Closed += (_, _) =>
            {
                // Part 2 uses a fresh overlay after layout settles; detach Part 1 now so
                // deferred input from this dialog cannot advance the editor's tour.
                _activeTutorialOverlay?.Close();
                _activeTutorialOverlay = null;
            };
        }

        private async System.Threading.Tasks.Task BrowseAsync()
        {
            var isVideo = _rbVideo.IsChecked == true;
            // WPF seeds InitialDirectory from EnhancementLibrary.LastDirectory
            // (NewEnhancementDialog.xaml.cs:57); the editor holds that member on this head.
            IStorageFolder? start = null;
            try { start = await StorageProvider.TryGetFolderFromPathAsync(DeeperEditorWindow.LastDirectory); }
            catch { /* a folder that will not resolve just leaves the picker where the OS had it */ }
            var overlay = _activeTutorialOverlay;
            var restore = overlay?.IsVisible == true;
            if (restore) overlay!.Hide();
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get(isVideo ? "deeper_dialog_pick_video" : "deeper_dialog_pick_audio"),
                    SuggestedStartLocation = start,
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        isVideo
                            ? new FilePickerFileType("Video files") { Patterns = new[] { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi", "*.m4v" } }
                            : new FilePickerFileType("Audio files") { Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg" } },
                        FilePickerFileTypes.All,
                    },
                });
                if (files.Count > 0)
                    _txtSource.Text = files[0].TryGetLocalPath() ?? files[0].Path.ToString();
            }
            finally
            {
                if (restore && IsVisible && !overlay!.HasClosed && ReferenceEquals(overlay, _activeTutorialOverlay)) overlay!.Show();
            }
        }

        private void BtnTryHypnoTubeTutorial_Click()
        {
            _rbVideo.IsChecked = true;
            try
            {
                _txtSource.Text = HypnotubeDefaultLinks.SelectTikTokTutorialVideo(
                    CoreModsHooks.KnownVideoLinksProvider?.Invoke() ?? HypnotubeDefaultLinks.KnownVideoTitles);
            }
            catch
            {
                _txtSource.Text = HypnotubeDefaultLinks.SelectTikTokTutorialVideo(null);
            }

            // Mark the flag so a future first-time hint does not double up. Same write WPF makes,
            // and it lands here for the same reason: the user has now seen the walkthrough offer.
            try
            {
                CoreSettings.Current.HasSeenDeeperHTInteractiveTutorial = true;
                CoreSettings.Save();
            }
            catch { /* a settings write must never take the dialog down */ }

            StartInteractiveTutorial(TutorialType.DeeperEditorInteractiveHT, TutorialType.DeeperEditorInteractiveHTPart2);
        }

        private void StartInteractiveTutorial(TutorialType part1, TutorialType part2)
        {
            _pendingPart2Tutorial = part2.ToString();
            try
            {
                _activeTutorialOverlay?.Close();
                if (CoreTutorial.IsActive) CoreTutorial.Skip();
                CoreTutorial.Start(part1.ToString());
                if (!CoreTutorial.IsActive) return;
                var overlay = new TutorialOverlay(this);
                _activeTutorialOverlay = overlay;
                overlay.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_activeTutorialOverlay, overlay)) _activeTutorialOverlay = null;
                };
                overlay.Show();
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to start interactive tutorial Part 1");
            }
        }

        private async System.Threading.Tasks.Task BtnCreateAsync()
        {
            var source = _txtSource.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(source))
            {
                var overlay = _activeTutorialOverlay;
                var restore = overlay?.IsVisible == true;
                if (restore) overlay!.Hide();
                try { await MessageDialog.ShowAsync(this, Loc.Get("deeper_dialog_new_title"), Loc.Get("deeper_dialog_source_required")); }
                finally { if (restore && IsVisible && !overlay!.HasClosed && ReferenceEquals(overlay, _activeTutorialOverlay)) overlay!.Show(); }
                return;
            }
            SelectedMediaType = _rbVideo.IsChecked == true ? MediaTypes.Video : MediaTypes.Audio;
            SelectedSource = source;
            if (_pendingPart2Tutorial != null) CoreTutorialEvents.PendingPart2Tutorial = _pendingPart2Tutorial;
            Close(true);
        }
    }
}
