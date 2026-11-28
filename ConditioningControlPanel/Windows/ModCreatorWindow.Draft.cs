using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    /// up the offered draft is never overwritten: autosave and the close save write nothing until it is restored
    /// or discarded (<see cref="ModCreatorDraftGate"/>). One Mod Creator window at a time, since they share the store.
    /// </summary>
    public partial class ModCreatorWindow
    {
        private static readonly TimeSpan DraftDebounce = TimeSpan.FromSeconds(2);

        private static ModCreatorWindow? _openWindow;

        private ModCreatorDraftStore? _draftStore;
        private DispatcherTimer? _draftTimer;
        private ModCreatorDraft? _pendingDraft;
        private string? _draftSideDir;
        private bool _draftSuppress = true;
        private readonly ModCreatorDraftGate _draftGate = new();
        private Task? _draftSaveTask;

        /// <summary>Brings the open Mod Creator forward instead of opening a second one. Runs after the current
        /// input (and any modal the caller closes) so the window is enabled when it activates.</summary>
        public static bool BringOpenToFront()
        {
            var open = _openWindow;
            if (open == null) return false;
            open.Dispatcher.BeginInvoke(() =>
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
            });
            return true;
        }

        private void InitDraft()
        {
            _openWindow = this;
            Closed += (_, _) => { if (ReferenceEquals(_openWindow, this)) _openWindow = null; };
            try
            {
                _draftStore = ModCreatorDraftStore.ForApp();
                _draftStore.PruneIfNoDraft();
                _pendingDraft = _draftStore.Offer();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft store unavailable"); }

            _draftTimer = new DispatcherTimer { Interval = DraftDebounce };
            _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); if (_draftGate.MaySave(closing: false)) SaveDraftNow(closing: false); };

            AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => OnFormEdited(e)), true);
            AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) => OnFormEdited(e)), true);
            AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, e) => OnFormEdited(e)), true);
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) =>
                OnFormEdited(e, e.AddedItems.Count + e.RemovedItems.Count)), true);
            // Pick-file, add-row and remove-row buttons in the side panels; previews are skipped (ModCreatorEditSignal).
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) => OnFormEdited(e)), true);

            if (_pendingDraft != null)
            {
                _draftGate.Offered();
                ShowDraftStrip(_pendingDraft.SavedAtUtc);
            }
            _draftSuppress = false;
        }

        private void OnFormEdited(RoutedEventArgs e, int changedItems = 1)
        {
            if (e.OriginalSource is not Visual v || !ContentScroll.IsAncestorOf(v)) return;
            if (!ModCreatorEditSignal.IsEdit(e.RoutedEvent, e.OriginalSource, changedItems)) return;
            NoteDraftEdit();
        }

        /// <summary>A real value change: typed text, a ticked box, a picked or cleared file, an added or removed row.</summary>
        private void NoteDraftEdit()
        {
            if (_draftSuppress || _draftStore == null) return;
            _draftGate.Edited();
            _draftTimer?.Stop();
            _draftTimer?.Start();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            _draftTimer?.Stop();
            if (_draftGate.MaySave(closing: true)) SaveDraftNow(closing: true);
        }

        private ModCreatorDraft DraftFromForm() => new()
        {
            SavedAtUtc = DateTime.UtcNow,
            Manifest = BuildManifestFromForm(),
            Images = _imageSlots.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!),
            Audio = _audioSlots.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!),
            VoiceLines = _voiceLines.Select(v => v.FilePath).ToList(),
            SideDir = _draftSideDir,
        };

        /// <summary>Autosave copies files (a loaded .ccpmod's temp files) off the UI thread. Side panels only write
        /// a resources tree, so they are saved on close, into a fresh folder: they may be reading from the previous one.
        /// A side copy that failed keeps the previous complete folder instead.</summary>
        private void SaveDraftNow(bool closing)
        {
            var store = _draftStore;
            if (store == null) return;
            try
            {
                var draft = DraftFromForm();
                if (!closing)
                {
                    _draftGate.Saved();
                    var prev = _draftSaveTask ?? Task.CompletedTask;
                    _draftSaveTask = prev.ContinueWith(_ => store.Save(draft), TaskScheduler.Default).ContinueWith(t =>
                    {
                        if (!t.IsFaulted) return;
                        App.Logger?.Warning(t.Exception?.GetBaseException(), "Mod Creator: draft save failed");
                        Dispatcher.BeginInvoke(() => _draftGate.SaveFailed());
                    }, TaskScheduler.Default);
                    return;
                }

                // Closing: let an autosave in flight land first, so it can never write over this one.
                try { _draftSaveTask?.Wait(TimeSpan.FromSeconds(10)); } catch { }
                var side = store.NewSideDirName();
                var complete = WriteSideFilesTo(Path.Combine(store.Root, side, "resources"));
                draft.SideDir = store.SettleSideDir(side, complete, _draftSideDir);
                store.Save(draft);
                store.DropSideDirsExcept(draft.SideDir);
                _draftGate.Saved();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft save failed"); }
        }

        private void OnDraftExported()
        {
            _draftTimer?.Stop();
            _draftGate.Exported();
            try { _draftStore?.MarkExported(DateTime.UtcNow); } catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft clear failed"); }
            if (_pendingDraft != null) { _pendingDraft = null; HideDraftStrip(); }
        }

        /// <summary>Fills the form from a draft. Throws on failure; the caller decides what happens to the offer.</summary>
        private void ApplyDraft(ModCreatorDraft draft, ModCreatorDraftStore store)
        {
            ResetForm();
            PopulateFromManifest(draft.Manifest!);
            foreach (var (key, path) in draft.Images)
                if (_imageSlots.ContainsKey(key) && File.Exists(path)) SetImageSlot(key, path, validate: false);
            foreach (var (key, path) in draft.Audio)
                if (_audioSlots.ContainsKey(key) && File.Exists(path)) SetAudioSlot(key, path);
            foreach (var path in draft.VoiceLines.Where(File.Exists))
                AddVoiceLineRow(path);
            if (!string.IsNullOrEmpty(draft.SideDir))
            {
                var resources = Path.Combine(store.Root, draft.SideDir, "resources");
                if (Directory.Exists(resources)) LoadSideFilesFrom(resources);
            }
            _draftSideDir = draft.SideDir;
        }

        private void BtnDraftRestore_Click(object sender, RoutedEventArgs e)
        {
            var draft = _pendingDraft;
            var store = _draftStore;
            if (draft?.Manifest == null || store == null) return;
            _draftSuppress = true;
            ModCreatorDraft? undo = null;
            try
            {
                StopAudioPreview();
                // The form as it is, so a failed restore can put it back. Side panels go to a fresh side folder.
                try
                {
                    undo = DraftFromForm();
                    undo.SideDir = store.NewSideDirName();
                    if (!WriteSideFilesTo(Path.Combine(store.Root, undo.SideDir, "resources"))) undo = null;
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: form snapshot failed"); undo = null; }

                try
                {
                    ApplyDraft(draft, store);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Mod Creator: draft restore failed");
                    var putBack = false;
                    if (undo != null)
                    {
                        try { ApplyDraft(undo, store); putBack = true; }
                        catch (Exception ex2) { App.Logger?.Warning(ex2, "Mod Creator: form put back failed"); }
                    }
                    // The offer and its strip stay; a half-filled form is never saved over anything.
                    _draftGate.RestoreFailed(putBack);
                    UpdateStatusBar();
                    TxtStatus.Text = Loc.Get("mod_draft_restore_failed");
                    return;
                }

                NavigateToSection("info");
                UpdateStatusBar();
                TxtStatus.Text = Loc.Get("mod_draft_restored");
                _pendingDraft = null;
                _draftGate.Settled();
                HideDraftStrip();
            }
            finally
            {
                _draftSuppress = false;
                // The snapshot's side folder is only needed while the form reads from it.
                if (undo?.SideDir != null && !string.Equals(undo.SideDir, _draftSideDir, StringComparison.OrdinalIgnoreCase))
                    store.DropSideDir(undo.SideDir);
            }
        }

        private void BtnDraftDiscard_Click(object sender, RoutedEventArgs e)
        {
            try { _draftStore?.Discard(keepSideDir: _draftSideDir); } catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft discard failed"); }
            _pendingDraft = null;
            _draftGate.Settled();
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
