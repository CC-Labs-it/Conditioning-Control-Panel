using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// "What's New" dialog shown once after an update. Uses a fixed window size with a
    /// scrollable notes region and a pinned OK button, so long patch notes can never push
    /// the button off-screen (the old MessageBox-based version could — see ccp-bugs #427).
    ///
    /// <para>A release that moves things around can also hand the dialog a secondary CTA
    /// (<paramref name="tourAction"/>) - v6.8's "Show me around (60s)" upgrade tour. The button
    /// is hidden while the upgrade tour offer is retired. The caller may still supply its
    /// action and label for a future release.</para>
    ///
    /// <para>PORTED from ConditioningControlPanel/Dialogs/WhatsNewDialog.xaml.cs. Deviations:
    ///  - <c>DialogResult = true</c> becomes <c>Close(true)</c>; Avalonia carries the result
    ///    through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>Visibility.Visible</c> becomes <c>IsVisible = true</c>.
    ///  - <c>BtnTour.Content = text</c> becomes <c>TourLabel.Text = text</c>: the button holds a
    ///    TextBlock so Avalonia does not eat an underscore as an access key.
    ///  - <c>Dispatcher.BeginInvoke(..., DispatcherPriority.Normal)</c> becomes
    ///    <c>Dispatcher.UIThread.Post(..., DispatcherPriority.Normal)</c>, the same queued post.
    ///  - <c>App.Logger</c> becomes Serilog's static <c>Log</c>.</para>
    /// </summary>
    public partial class WhatsNewDialog : Window
    {
        // WPF retired the upgrade tour offer in 6.11.0.
        private const bool UpgradeTourOffered = false;
        private readonly Action? _tourAction;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog,
        /// with a sample tour action.</summary>
        internal WhatsNewDialog() : this(
            "v7.0.0 — The Spiral",
            "• The Avalonia head now renders every ported view headless on Linux.\n" +
            "• Companion overlays no longer stutter when the spiral is at full intensity.\n" +
            "• Fixed a crash on startup when no audio device was present.\n" +
            "• Session logs keep their history across an update.\n\n" +
            "Thanks for sticking around. There is more coming.",
            () => { })
        { }

        public WhatsNewDialog(string title, string notes,
                              Action? tourAction = null, string? tourButtonText = null)
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<TextBlock>("TxtTitle")!.Text = title;
            this.FindControl<TextBlock>("TxtNotes")!.Text = notes;

            this.FindControl<Button>("BtnOk")!.Click += (_, _) => BtnOk_Click();

            var btnTour = this.FindControl<Button>("BtnTour")!;
            btnTour.Click += (_, _) => BtnTour_Click();

            _tourAction = UpgradeTourOffered ? tourAction : null;
            if (tourAction != null)
            {
                this.FindControl<TextBlock>("TourLabel")!.Text = string.IsNullOrWhiteSpace(tourButtonText)
                    ? "Show me around (60s)"
                    : tourButtonText;
                btnTour.IsVisible = _tourAction != null;
            }
        }

        private void BtnOk_Click() => Close(true);

        private void BtnTour_Click()
        {
            Close(true);

            // QUEUED, never called inline: Close() only starts the unwind, so invoking here would
            // run the tour from inside this dialog's modal message loop - before ShowDialog()
            // returns to the caller and before the caller's finally block releases
            // IsStartupDialogShowing. A Normal-priority post runs after the whole call stack that
            // opened us has finished, which is exactly what the tour needs.
            var action = _tourAction;
            if (action == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                try { action(); }
                catch (Exception ex) { Log.Warning(ex, "What's New: the tour action threw"); }
            }, DispatcherPriority.Normal);
        }
    }
}
