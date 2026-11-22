using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The machine-local draft (ccp-bugs #1667). The form is saved a couple of seconds after an edit and on close;
    /// a draft newer than the last export is offered on open by an inline strip, never a modal. While the strip is
    /// up the old draft is held: autosave waits, and only closing with new edits replaces it.
    /// </summary>
    public partial class ModCreatorWindow
    {
        private static readonly TimeSpan DraftDebounce = TimeSpan.FromSeconds(2);

        private ModCreatorDraftStore? _draftStore;
        private DispatcherTimer? _draftTimer;
        private ModCreatorDraft? _pendingDraft;
        private string? _draftSideDir;
        private bool _draftSuppress = true;
        private bool _draftDirty;
        private bool _changedSinceOpen;

        private void InitDraft()
        {
            try
            {
                _draftStore = ModCreatorDraftStore.ForApp();
                _draftStore.PruneIfNoDraft();
                _pendingDraft = _draftStore.Offer();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft store unavailable"); }

            _draftTimer = new DispatcherTimer { Interval = DraftDebounce };
            _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); if (_pendingDraft == null) SaveDraftNow(closing: false); };

            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => OnFormEdited(e)), true);
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) => OnFormEdited(e)), true);
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, e) => OnFormEdited(e)), true);
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => OnFormEdited(e)), true);
            // Pick-file, add-row and remove-row buttons live in the content area too.
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) => OnFormEdited(e)), true);

            if (_pendingDraft != null)
                ShowDraftStrip(_pendingDraft.SavedAtUtc);
            _draftSuppress = false;
        }

        private void OnFormEdited(RoutedEventArgs e)
        {
            if (_draftSuppress || _draftStore == null) return;
            if (e.OriginalSource is not Visual v || !ContentScroll.IsAncestorOf(v)) return;
            _draftDirty = true;
            _changedSinceOpen = true;
            _draftTimer?.Stop();
            _draftTimer?.Start();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            _draftTimer?.Stop();
            if (_changedSinceOpen) SaveDraftNow(closing: true);
        }

        /// <summary>Side panels only write a resources tree, so they are saved on close, into a fresh folder:
        /// they may be reading from the previous one.</summary>
        private void SaveDraftNow(bool closing)
        {
            if (_draftStore == null || (!_draftDirty && !closing)) return;
            try
            {
                var draft = new ModCreatorDraft
                {
                    SavedAtUtc = DateTime.UtcNow,
                    Manifest = BuildManifestFromForm(),
                    Images = _imageSlots.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!),
                    Audio = _audioSlots.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!),
                    VoiceLines = _voiceLines.Select(v => v.FilePath).ToList(),
                    SideDir = _draftSideDir,
                };
                if (closing)
                {
                    var side = _draftStore.NewSideDirName();
                    WriteSideFilesTo(Path.Combine(_draftStore.Root, side, "resources"));
                    draft.SideDir = side;
                }
                _draftStore.Save(draft);
                if (closing) _draftStore.DropSideDirsExcept(draft.SideDir);
                _draftDirty = false;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft save failed"); }
        }

        private void OnDraftExported()
        {
            _draftTimer?.Stop();
            _draftDirty = false;
            _changedSinceOpen = false;
            try { _draftStore?.MarkExported(DateTime.UtcNow); } catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft clear failed"); }
            if (_pendingDraft != null) { _pendingDraft = null; HideDraftStrip(); }
        }

        private void BtnDraftRestore_Click(object sender, RoutedEventArgs e)
        {
            var draft = _pendingDraft;
            if (draft?.Manifest == null || _draftStore == null) return;
            _draftSuppress = true;
            try
            {
                StopAudioPreview();
                ResetForm();
                PopulateFromManifest(draft.Manifest);
                foreach (var (key, path) in draft.Images)
                    if (_imageSlots.ContainsKey(key) && File.Exists(path)) SetImageSlot(key, path, validate: false);
                foreach (var (key, path) in draft.Audio)
                    if (_audioSlots.ContainsKey(key) && File.Exists(path)) SetAudioSlot(key, path);
                foreach (var path in draft.VoiceLines.Where(File.Exists))
                    AddVoiceLineRow(path);
                if (!string.IsNullOrEmpty(draft.SideDir))
                {
                    var resources = Path.Combine(_draftStore.Root, draft.SideDir, "resources");
                    if (Directory.Exists(resources)) LoadSideFilesFrom(resources);
                }
                _draftSideDir = draft.SideDir;
                NavigateToSection("info");
                UpdateStatusBar();
                TxtStatus.Text = Loc.Get("mod_draft_restored");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft restore failed"); }
            finally { _draftSuppress = false; }
            _pendingDraft = null;
            HideDraftStrip();
        }

        private void BtnDraftDiscard_Click(object sender, RoutedEventArgs e)
        {
            try { _draftStore?.Discard(); } catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft discard failed"); }
            _pendingDraft = null;
            HideDraftStrip();
        }

        // IN: fade + drop 8 px. OUT: the reverse, then collapse. Motion Off shows and hides at once.
        private void ShowDraftStrip(DateTime savedAtUtc)
        {
            TxtDraftStrip.Text = Loc.GetF("mod_draft_strip", savedAtUtc.ToLocalTime().ToString("g"));
            SlideDraftStrip(show: true);
        }

        private void HideDraftStrip() { if (DraftStrip.Visibility == Visibility.Visible) SlideDraftStrip(show: false); }

        private void SlideDraftStrip(bool show)
        {
            DraftStrip.BeginAnimation(OpacityProperty, null);
            DraftStripShift.BeginAnimation(TranslateTransform.YProperty, null);
            DraftStrip.Visibility = show || MotionFx.AllowTransitions ? Visibility.Visible : Visibility.Collapsed;
            if (!MotionFx.AllowTransitions) return;
            var span = TimeSpan.FromMilliseconds(show ? GentleVisuals.RiseMs(220) : GentleVisuals.FadeMs(180));
            var fade = new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, span);
            if (!show) fade.Completed += (_, _) => DraftStrip.Visibility = Visibility.Collapsed;
            DraftStrip.BeginAnimation(OpacityProperty, fade);
            DraftStripShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? -8 : 0, show ? 0 : -8, span)
                { EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn } });
        }
    }
}
