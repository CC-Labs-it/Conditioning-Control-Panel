using System.Collections.Generic;
using ConditioningControlPanel.Services;
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
    ///  - The three interactive tutorials remain in follow-up lane
    ///    wire-views-deeper-new-enhancement-dialog-b: the legacy overlay's native input region
    ///    and automatic advancement need porting before the walkthroughs can be enabled.
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

        public NewEnhancementDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _rbVideo = this.FindControl<RadioButton>("RbVideo")!;
            _rbAudio = this.FindControl<RadioButton>("RbAudio")!;
            _txtSource = this.FindControl<TextBox>("TxtSource")!;
            _txtError = this.FindControl<TextBlock>("TxtError")!;

            this.FindControl<Button>("BtnBrowse")!.Click += async (_, _) => await BrowseAsync();
            this.FindControl<Button>("BtnLocalVideoTutorial")!.Click += (_, _) => { _rbVideo.IsChecked = true; StartInteractiveTutorial(); };
            this.FindControl<Button>("BtnLocalAudioTutorial")!.Click += (_, _) => { _rbAudio.IsChecked = true; StartInteractiveTutorial(); };
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

            StartInteractiveTutorial();
        }

        /// <summary>
        /// ponytail: wire-views-deeper-new-enhancement-dialog-b owns the remaining walkthroughs.
        /// TutorialHead already seeds CoreTutorial and CoreTutorialEvents already carries Part 2.
        /// The legacy TutorialOverlay still lacks automatic control/event advancement and native
        /// partial input regions; opening it here would obscure the Browse/Create controls.
        /// Port those interactions with success-only editor continuation before enabling the tours.
        /// </summary>
        private void StartInteractiveTutorial()
        {
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
            Close(true);
        }
    }
}
