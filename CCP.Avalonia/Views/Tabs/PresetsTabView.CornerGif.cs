using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetsTabView
{
    private string _selectedCornerGifPath = "";
    private readonly DispatcherTimer _cornerGifSizeDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private void InitializeCornerGif()
    {
        ChkCornerGifEnabled.IsCheckedChanged += (_, _) => CornerGifSettings.IsVisible = ChkCornerGifEnabled.IsChecked == true;
        BtnSelectCornerGif.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage) return;
            var images = await storage.TryGetFolderFromPathAsync(Path.Combine(CorePaths.EffectiveAssets, "images"));
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("title_select_corner_gif"), SuggestedStartLocation = images,
                FileTypeFilter = new[] { new FilePickerFileType("GIF files") { Patterns = new[] { "*.gif" } }, FilePickerFileTypes.All },
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) SelectCornerGifPath(path);
        };
        SliderCornerGifSize.ValueChanged += (_, e) =>
        {
            TxtCornerGifSize.Text = $"{(int)e.NewValue}px";
            _cornerGifSizeDebounce.Stop();
            if (App.Sessions?.IsRunning == true) _cornerGifSizeDebounce.Start();
        };
        _cornerGifSizeDebounce.Tick += (_, _) =>
        {
            _cornerGifSizeDebounce.Stop();
            if (App.Sessions?.IsRunning == true) App.Sessions.UpdateCornerGif(size: (int)SliderCornerGifSize.Value);
        };
        SliderCornerGifOpacity.ValueChanged += (_, e) =>
        {
            TxtCornerGifOpacity.Text = $"{(int)e.NewValue}%";
            if (App.Sessions?.IsRunning == true) App.Sessions.UpdateCornerGif(opacity: (int)e.NewValue);
        };
        foreach (var radio in new[] { RbCornerTL, RbCornerTR, RbCornerBL, RbCornerBR })
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && App.Sessions?.IsRunning == true)
                    App.Sessions.UpdateCornerGif(position: SelectedCornerPosition());
            };
        DetachedFromVisualTree += (_, _) => _cornerGifSizeDebounce.Stop();
    }

    internal void SelectCornerGifPath(string path)
    {
        _selectedCornerGifPath = path;
        BtnSelectCornerGif.Content = Path.GetFileName(path);
        if (App.Sessions?.IsRunning == true) App.Sessions.UpdateCornerGif(path: path);
    }

    private CornerPosition SelectedCornerPosition() => RbCornerTL.IsChecked == true ? CornerPosition.TopLeft
        : RbCornerTR.IsChecked == true ? CornerPosition.TopRight
        : RbCornerBR.IsChecked == true ? CornerPosition.BottomRight : CornerPosition.BottomLeft;

    internal void ApplyCornerGifOptions(Session session)
    {
        // WPF StartSession: this opt-in does not clear a template's own prescribed corner GIF.
        if (!session.HasCornerGifOption || ChkCornerGifEnabled.IsChecked != true) return;
        var ss = session.Settings;
        ss.CornerGifEnabled = true;
        ss.CornerGifPath = _selectedCornerGifPath;
        ss.CornerGifPosition = SelectedCornerPosition();
        ss.CornerGifSize = (int)SliderCornerGifSize.Value;
        ss.CornerGifOpacity = (int)SliderCornerGifOpacity.Value;
    }
}
