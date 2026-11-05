// WPF MainWindow.UiUpdates.cs ApplyModFeatureNames and MainWindow.xaml.cs LoadTakeoverImage.
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF xaml.cs:3757 reruns only ApplyModFeatureNames on a language change.
    private void OnModShellLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ApplyModFeatureNames);

    // Repaint only: external activations must not clear a pending choice or reapply presets.
    // WPF xaml.cs:3729-3752: external activation repaints labels, art and palette.
    private void RefreshModShell()
    {
        InitializeModSelector();
        LoadLogo();
        RefreshThemeAwareElements();
        LoadFeatureImages();
        ApplyModFeatureNames();
        Named<Tabs.StudioTabView>("StudioTab")?.RepaintModAwareChrome();
    }

    internal static string ModAwareLabel(string english, string key) =>
        App.Mods?.MakeModAware(english) is string text && text != english ? text : Loc.Get(key);

    private static string CardLabel(string english, string key)
    {
        var text = ModAwareLabel(english, key);
        var i = 0;
        while (i < text.Length && !char.IsLetterOrDigit(text, i))
            i += char.IsSurrogatePair(text, i) ? 2 : 1;
        return i > 0 && i < text.Length ? text[i..] : text;
    }

    // WPF MainWindow.UiUpdates.cs:95-166; takeover chrome xaml.cs:2416-2429.
    private void ApplyModFeatureNames()
    {
        if (SettingsPage is { } dash)
        {
            dash.CardFlash.Title = CardLabel("Flash Images", "section_flash_images");
            dash.CardSubliminal.Title = CardLabel("Subliminals", "section_subliminals_2");
            dash.CardBouncingText.Title = CardLabel("Bouncing Text", "label_bouncing_text");
            dash.CardBubblePop.Title = CardLabel("Bubble Pop", "label_bubble_pop");
            dash.CardLockCard.Title = CardLabel("Lock Card", "label_lock_card");
            dash.CardVault.Title = CardLabel("The Vault", "tab_exclusives");
            dash.ComboVideoBubble.TitleA = CardLabel("Mandatory Video", "section_mandatory_video");
            dash.ComboVideoBubble.TitleB = CardLabel("Bubble Count", "label_bubble_count");
            dash.ComboSpiralPink.TitleA = CardLabel("Spiral Overlay", "label_spiral_overlay");
            dash.ComboSpiralPink.TitleB = CardLabel("Pink Filter", "label_pink_filter");
            dash.ComboMindDrain.TitleA = CardLabel("Mind Wipe", "label_mind_wipe");
            dash.ComboMindDrain.TitleB = CardLabel("Brain Drain", "label_brain_drain");
        }
        var takeover = Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab");
        void Label(string name, string english, string key)
        {
            if (takeover?.FindControl<TextBlock>(name) is { } label) label.Text = ModAwareLabel(english, key);
        }
        Label("TxtAutoFlash", "Flashes", "tab_flashes");
        Label("TxtAutoVideo", "Videos", "tab_videos");
        Label("TxtAutoSubliminal", "Subliminals", "tab_subliminals");
        Label("TxtAutoBubbles", "Bubbles", "label_bubbles");
        Label("TxtAutoPinkFilter", "Pink Filter", "label_pink_filter");
        Label("TxtAutoLockCards", "Lock Cards", "label_lock_card");
        Label("TxtAutoBouncing", "Bouncing", "label_bouncing_text");
        Label("TxtAutoMindwipe", "Mindwipe", "label_mind_wipe");
        var title = App.Mods?.GetTakeoverLabel() ?? Loc.Get("tab_takeover");
        if (takeover?.FindControl<TextBlock>("TxtBambiTakeoverHeader") is { } header) header.Text = title;
        if (takeover?.FindControl<IconLabel>("TxtTakeoverLocked") is { } locked)
        {
            locked.Kind = IconKind.Bot;
            locked.Text = title;
        }
        if (takeover?.FindControl<TextBlock>("TxtTakeoverUnlocked") is { } unlocked) unlocked.Text = title;
        if (takeover?.FindControl<Button>("BtnAutonomyStartStop") is { } button)
            ToolTip.SetTip(button, Loc.GetF("tooltip_start_stop_takeover", title));
    }
}
