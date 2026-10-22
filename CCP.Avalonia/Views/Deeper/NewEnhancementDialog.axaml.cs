using System.Collections.Generic;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Deeper/NewEnhancementDialog.xaml.cs. Deviations:
    ///  - DialogResult becomes Close(bool); callers use ShowDialog&lt;bool&gt;.
    ///  - OpenFileDialog becomes the StorageProvider file picker.
    ///  - The Part 1 tour card lives in this dialog's OverlayLayer (TutorialOverlay.Stage), not a
    ///    topmost window, so it cannot cover the OS file picker: WPF's hide/restore around Browse
    ///    (NewEnhancementDialog.xaml.cs:60-101) has nothing to hide here. Closing the dialog tears the
    ///    card down through the stage's host-closed handler (WPF OnDialogClosed, :33-44).
    ///    Known-video sample selection is shared with WPF through HypnotubeDefaultLinks.
    /// </summary>
    public partial class NewEnhancementDialog : Window
    {
        public string SelectedMediaType { get; private set; } = MediaTypes.Video;
        public string SelectedSource { get; private set; } = "";

        private readonly RadioButton _rbVideo;
        private readonly RadioButton _rbAudio;
        private readonly TextBox _txtSource;
        private readonly TextBlock _txtError;

        // WPF :19 / :25: the Part 2 tour Create hands to the editor, and the Part 1 card.
        private string? _pendingPart2Tutorial;
        private TutorialOverlay? _activeTutorialOverlay;
        internal TutorialOverlay? ActiveTutorialOverlay => _activeTutorialOverlay;

        public NewEnhancementDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _rbVideo = this.FindControl<RadioButton>("RbVideo")!;
            _rbAudio = this.FindControl<RadioButton>("RbAudio")!;
            _txtSource = this.FindControl<TextBox>("TxtSource")!;
            _txtError = this.FindControl<TextBlock>("TxtError")!;

            this.FindControl<Button>("BtnBrowse")!.Click += async (_, _) => await BrowseAsync();
            this.FindControl<Button>("BtnLocalVideoTutorial")!.Click += (_, _) => { _rbVideo.IsChecked = true; StartInteractiveTutorial(TutorialType.DeeperEditorInteractiveLocalVideo, TutorialType.DeeperEditorInteractiveLocalVideoPart2); };
            this.FindControl<Button>("BtnLocalAudioTutorial")!.Click += (_, _) => { _rbAudio.IsChecked = true; StartInteractiveTutorial(TutorialType.DeeperEditorInteractiveLocalAudio, TutorialType.DeeperEditorInteractiveLocalAudioPart2); };
            this.FindControl<Button>("BtnTryHypnoTubeTutorial")!.Click += (_, _) => BtnTryHypnoTubeTutorial_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnCreate")!.Click += (_, _) => BtnCreate_Click();
        }

        private async System.Threading.Tasks.Task BrowseAsync()
        {
            var isVideo = _rbVideo.IsChecked == true;
            // WPF seeds InitialDirectory from EnhancementLibrary.LastDirectory
            // (NewEnhancementDialog.xaml.cs:57); the editor holds that member on this head.
            IStorageFolder? start = null;
            try { start = await StorageProvider.TryGetFolderFromPathAsync(DeeperEditorWindow.LastDirectory); }
            catch { /* a folder that will not resolve just leaves the picker where the OS had it */ }
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

        /// <summary>WPF StartInteractiveTutorial (:157-176): remember Part 2 (handed off only by a
        /// valid Create), start Part 1 on the seam and put its card on this dialog.</summary>
        private void StartInteractiveTutorial(TutorialType part1, TutorialType part2)
        {
            _pendingPart2Tutorial = part2.ToString();
            try
            {
                if (CoreTutorial.IsActive) CoreTutorial.Skip();
                CoreTutorial.Start(part1.ToString());
                if (!CoreTutorial.IsActive) return;   // unseeded seam: do not dim over nothing
                _activeTutorialOverlay?.Close();
                _activeTutorialOverlay = new TutorialOverlay(this);
                _activeTutorialOverlay.Show();
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to start interactive tutorial Part 1");
            }
        }

        private void BtnCreate_Click()
        {
            var source = _txtSource.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(source))
            {
                _txtError.IsVisible = true;
                return;
            }
            SelectedMediaType = _rbVideo.IsChecked == true ? MediaTypes.Video : MediaTypes.Audio;
            SelectedSource = source;

            // WPF :194-203: only a validated Create arms Part 2, so a fumbled first click cannot
            // leave the flag set to ambush a later, unrelated editor open.
            if (_pendingPart2Tutorial != null)
                CoreTutorialEvents.PendingPart2Tutorial = _pendingPart2Tutorial;
            Close(true);
        }
    }
}
