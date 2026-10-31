// WPF 7.1.5 LauncherWindow.xaml.cs: Media pill (BtnGear_Click :222), shortcut link (:498/:504, timer :34/:49),
// tile shortcut button (Tiles.cs :163/:241/:250). Click cues outside the grid: LauncherWindow.Fx.cs.
using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        /// <summary>WPF _shortcutTextTimer: the link says how it went for 3 s, then asks again.</summary>
        private readonly DispatcherTimer _shortcutTextTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        /// <summary>Tests: the writer. Null = Platform/LauncherShortcutWriter.</summary>
        internal Func<string?, bool>? ShortcutWriter { get; set; }

        /// <summary>The loc key the shortcut link wears right now.</summary>
        internal string ShortcutLinkKey { get; private set; } = "launcher_add_shortcut";

        private void HookLinks()
        {
            _shortcutTextTimer.Tick += (_, _) => ResetShortcutLink();
            Closed += (_, _) => _shortcutTextTimer.Stop();
            ResetShortcutLink();
        }

        // WPF LauncherWindow.xaml.cs:659-675: notes stay over the launcher.
        private Dialogs.WhatsNewDialog? _whatsNew;

        private async void WhatsNew_Click(object? sender, RoutedEventArgs e)
        {
            if (MainShellWindow.LockdownActive || _whatsNew != null) return;
            try
            {
                _whatsNew = new Dialogs.WhatsNewDialog("What's New in v" + CoreReleaseContent.AppVersion,
                    CoreReleaseContent.PatchNotes);
                await _whatsNew.ShowDialogSafe(this);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Launcher] What's New dialog failed; falling back to the settings tab");
                OpenPanel(p => p.ShowTab("settings"));
            }
            finally { _whatsNew = null; }
        }

        internal static void CloseNotesForPanic() => Instance?._whatsNew?.Close();
        internal static void CloseFriendsForPanic() => Instance?.FriendsChip.CloseDrawer();

        internal void ResetShortcutLink()
        {
            _shortcutTextTimer.Stop();
            BindShortcutLink("launcher_add_shortcut");
        }

        /// <summary>Bound, not assigned: a language switch follows whichever line the link shows.</summary>
        private void BindShortcutLink(string key)
        {
            ShortcutLinkKey = key;
            ShortcutLinkText.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
                { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
        }

        private void ShortcutLink_Click(object? sender, RoutedEventArgs e) => WriteShortcut(null);

        /// <summary>WPF ShowShortcutResult(LauncherShortcuts.TryCreateDesktopShortcut(id)).</summary>
        internal void WriteShortcut(string? gameId)
        {
            bool ok;
            try { ok = (ShortcutWriter ?? LauncherShortcutWriter.TryCreateDesktopShortcut)(gameId); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] shortcut for {Id} threw", gameId); ok = false; }
            BindShortcutLink(ok ? "launcher_shortcut_added" : "launcher_shortcut_failed");
            _shortcutTextTimer.Stop();
            _shortcutTextTimer.Start();
        }

        /// <summary>WPF Tiles.cs:163: the round link button at the plate's top right, unseen until
        /// the pointer is on the tile. It handles its own press, so the whole-card Play never fires.</summary>
        private Button ShortcutButton(Border tile, string gameId)
        {
            var btn = new Button
            {
                Content = new IconGlyph { Kind = IconKind.Link, Size = 15 }, Width = 32, Height = 32, Opacity = 0,
                Padding = new Thickness(0), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Res("SurfaceBgBrush"), Foreground = Res("TextSecondaryBrush"),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = "tile-shortcut",
            };
            ToolTip.SetTip(btn, Loc.Get("launcher_add_shortcut"));   // also its accessible name
            AutomationProperties.SetName(btn, Loc.Get("launcher_add_shortcut"));
            btn.Click += (_, e) =>
            {
                e.Handled = true;
                if (MainShellWindow.LockdownActive) return;
                LauncherSfx.Click();   // the window-wide cue skips the games grid
                WriteShortcut(gameId);
            };
            tile.PointerEntered += (_, _) => btn.Opacity = 1;
            tile.PointerExited += (_, _) => btn.Opacity = 0;
            return btn;
        }

        /// <summary>WPF BtnGear_Click: the dialog over the launcher; if it will not open, the panel's app settings.</summary>
        private async void BtnGear_Click(object? sender, RoutedEventArgs e)
        {
            try { await new LauncherMediaDialog { Panel = Panel, OpenAssetBrowser = () => OpenPanel(p => p.ShowTab("assets")) }.ShowDialogSafe(this); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Launcher] Game media dialog failed; falling back to the settings tab");
                OpenPanel(p => p.ShowTab("appsettings"));
            }
        }
    }
}
