// ADAPTED from parity/trunk 07ced25ff67e634d7efd67d4060a9a2a74ac651e, ModReskin.LoadFeatureImages.
// WPF MainWindow.xaml.cs:2685-2751. Existing logo/rail/skill-tree hooks stay in their own partials.
using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private const int TileDecodeWidth = 768, WideTileDecodeWidth = 1024;

    // The event is authoritative: uninstalling the active mod also activates CCP Default.
    private void OnModChangedFeatureImages(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(LoadFeatureImages);

    private void LoadFeatureImages()
    {
        if (SettingsPage is not { } dash) return;
        try
        {
            static void Set(FeatureCard? card, Bitmap? art) { if (card != null && art != null) card.Icon = art; }
            static void Split(SplitFeatureCard? card, string a, string b)
            {
                if (card == null) return;
                if (ModArt.TryLoad(a, TileDecodeWidth) is { } artA) card.IconA = artA;
                if (ModArt.TryLoad(b, TileDecodeWidth) is { } artB) card.IconB = artB;
            }
            Set(dash.CardFlash, ModArt.TryLoad("features/flash.png", TileDecodeWidth));
            Set(dash.CardSubliminal, ModArt.TryLoad("features/subliminal.png", TileDecodeWidth));
            Set(dash.CardBouncingText, ModArt.TryLoad("features/bouncing_text.png", TileDecodeWidth));
            Set(dash.CardBubblePop, ModArt.TryLoad("features/Bubble_pop.png", TileDecodeWidth));
            Set(dash.CardLockCard, ModArt.TryLoad("features/Phrase_Lock.png", TileDecodeWidth));
            Set(dash.FindControl<FeatureCard>("CardDeeperEditor"), ModArt.TryLoad("features/deeper_editor.png", TileDecodeWidth));
            Set(dash.CardMystery, ModTileVariant("mysterybox", TileDecodeWidth));
            Set(dash.CardVault, ModTileVariant("vault", WideTileDecodeWidth));
            Split(dash.ComboVideoBubble, "features/mandatory_videos.png", "features/Bubble_count.png");
            Split(dash.ComboSpiralPink, "features/spiral_overlay.png", "features/Pink_filter.png");
            Split(dash.ComboMindDrain, "features/Mind_Wipers.png", "features/brain_drain.png");
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to load Home feature images"); }
    }

    private static Bitmap? ModTileVariant(string baseName, int width)
    {
        foreach (var path in ModTileArt.Candidates(baseName, CoreMods.ActiveModId))
            if (ModArt.TryLoad(path, width) is { } image) return image;
        return null;
    }
}
