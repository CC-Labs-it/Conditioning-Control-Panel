// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs:539-636 (ChkCornerGifEnabled_Changed,
// BtnSelectCornerGif_Click, SliderCornerGifSize/Opacity_ValueChanged, RbCornerPos_Checked,
// GetSelectedCornerPosition) and the corner-GIF lines of StartSession (:1586).

using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class PresetsTabView
    {
        private string _selectedCornerGifPath = "";
        private DispatcherTimer? _cornerGifDebounce;

        /// <summary>The picked file (WPF _selectedCornerGifPath). Test seam.</summary>
        internal string SelectedCornerGifPath => _selectedCornerGifPath;

        private void WireCornerGifOption()
        {
            ChkCornerGifEnabled.IsCheckedChanged += (_, _) => CornerGifSettings.IsVisible = ChkCornerGifEnabled.IsChecked == true;
            BtnSelectCornerGif.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not { } top) return;
                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("title_select_corner_gif"),
                    SuggestedStartLocation = await top.StorageProvider.TryGetFolderFromPathAsync(Path.Combine(CorePaths.EffectiveAssets, "images")),
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("GIF files") { Patterns = new[] { "*.gif" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*.*" } },
                    },
                });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) SetCornerGifPath(path);
            };
            // WPF debounces the size edit 400 ms (a live edit recreates the overlay). Here every
            // live edit re-shows the overlay (frames are decoded at its size), so opacity rides the
            // same debounce; ponytail: WPF's opacity edit is instant.
            SliderCornerGifSize.ValueChanged += (_, e) =>
            {
                TxtCornerGifSize.Text = $"{(int)e.NewValue}px";
                DebounceCornerGifEdit();
            };
            SliderCornerGifOpacity.ValueChanged += (_, e) =>
            {
                TxtCornerGifOpacity.Text = $"{(int)e.NewValue}%";
                DebounceCornerGifEdit();
            };
            // The radio's own corner: its group-mate is still checked while this one's event runs.
            foreach (var (rb, corner) in new[] { (RbCornerTL, CornerPosition.TopLeft), (RbCornerTR, CornerPosition.TopRight),
                         (RbCornerBL, CornerPosition.BottomLeft), (RbCornerBR, CornerPosition.BottomRight) })
                rb.IsCheckedChanged += (_, _) =>
                {
                    if (rb.IsChecked == true) LiveCornerGifEdit(ss => ss.CornerGifPosition = corner);
                };
        }

        /// <summary>WPF BtnSelectCornerGif_Click after the dialog: remember, label, live-swap.</summary>
        internal void SetCornerGifPath(string path)
        {
            _selectedCornerGifPath = path;
            if (BtnSelectCornerGif.Content is IconLabel label)
            {
                label.Kind = IconKind.Folder;
                label.Text = Path.GetFileName(path);
            }
            LiveCornerGifEdit(ss => ss.CornerGifPath = path);
        }

        private void DebounceCornerGifEdit()
        {
            if (App.Sessions is not { IsRunning: true }) return;
            if (_cornerGifDebounce == null)
            {
                _cornerGifDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _cornerGifDebounce.Tick += (_, _) =>
                {
                    _cornerGifDebounce?.Stop();
                    FlushCornerGifEdit();
                };
            }
            _cornerGifDebounce.Stop();
            _cornerGifDebounce.Start();
        }

        /// <summary>The debounced slider values onto a running session. Test seam (the timer's tick).</summary>
        internal void FlushCornerGifEdit() => LiveCornerGifEdit(ss =>
        {
            ss.CornerGifSize = (int)SliderCornerGifSize.Value;
            ss.CornerGifOpacity = (int)SliderCornerGifOpacity.Value;
        });

        /// <summary>WPF's `_sessionEngine.IsRunning` live updates (#474).</summary>
        private static void LiveCornerGifEdit(Action<SessionSettings> edit)
        {
            if (App.Sessions is { IsRunning: true } runner) runner.UpdateCornerGif(edit);
        }

        private CornerPosition SelectedCornerPosition() =>
            RbCornerTL.IsChecked == true ? CornerPosition.TopLeft
            : RbCornerTR.IsChecked == true ? CornerPosition.TopRight
            : RbCornerBR.IsChecked == true ? CornerPosition.BottomRight
            : CornerPosition.BottomLeft;

        /// <summary>WPF StartSession's corner-GIF lines: an opted-in option is written into the
        /// session's settings before the run starts (unticked leaves the template's values).</summary>
        internal void ApplyCornerGifOption(Session session)
        {
            if (!session.HasCornerGifOption || ChkCornerGifEnabled.IsChecked != true) return;
            var ss = session.Settings;
            ss.CornerGifEnabled = true;
            ss.CornerGifPath = _selectedCornerGifPath;
            ss.CornerGifPosition = SelectedCornerPosition();
            ss.CornerGifSize = (int)SliderCornerGifSize.Value;
            ss.CornerGifOpacity = (int)SliderCornerGifOpacity.Value;
        }
    }
}
