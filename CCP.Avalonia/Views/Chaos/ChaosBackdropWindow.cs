using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosBackdropService.cs:78-120: a non-topmost, click-absorbing primary-screen plate.
internal sealed class ChaosBackdropWindow : Window
{
    private static readonly string[] Scenes = { "alley_background", "alley_deep", "dollhouse_background", "dollhouse_backroom_background", "paradise_background" };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    internal static ChaosBackdropWindow? Active { get; private set; }
    internal static Bitmap? CurrentSource => Active?._image.Source as Bitmap;
    internal int Depth { get; private set; }

    private ChaosBackdropWindow()
    {
        SystemDecorations = WindowDecorations.None;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _image;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;
        }
        Closed += (_, _) => { if (Active == this) Active = null; };
    }

    internal static void ShowForRun(ChaosPlayMode mode, int depth)
    {
        if (mode != ChaosPlayMode.Story || !CoreSettings.Current.NarrativeModeEnabled || !CoreSettings.Current.BackdropEnabled)
        { CloseActive(); return; }
        var w = Active ??= new ChaosBackdropWindow();
        w.SetDepth(depth);
        if (!w.IsVisible) w.Show();
    }

    internal void SetDepth(int depth)
    {
        Depth = depth;
        _image.Source = ChaosArt.Resolve("backdrops", Scenes[Math.Clamp(depth - 1, 0, Scenes.Length - 1)])
            ?? ChaosArt.Resolve("backdrops", "depth" + depth);
        Opacity = Math.Clamp(CoreSettings.Current.BackdropOpacity, 0, 1);
    }

    internal static void CloseActive()
    {
        var w = Active;
        Active = null;
        w?.Close();
    }
}
