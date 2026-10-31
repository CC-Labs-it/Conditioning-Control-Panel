// PORTED from WPF MainWindow/MainWindow.RemoteControl.cs:685-738 (UpdateDirectoryListingStatus), after
// parity/trunk d7a46b6e4: the title-bar pill that says whether a running remote session is listed.
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal readonly record struct DirectoryPill(bool Visible, string Text, string DotHex, string Tip);

        /// <summary>WPF's four states, with WPF's literals (it has no keys for them): no session = hidden;
        /// not opted in = "Remote: private" (grey); opted in = "Remote: listed" (purple); claimed = "Remote: claimed" (green).</summary>
        internal static DirectoryPill DirectoryPillFor(bool active, bool optedIn, bool claimed) =>
            !active ? new DirectoryPill(false, "", "#8A8AA0", "")
            : !optedIn ? new DirectoryPill(true, "Remote: private", "#8A8AA0",
                "Your Remote session is private: you are not listed in the Available Subjects Directory. Goon tables have their own Who sees this table switch.")
            : claimed ? new DirectoryPill(true, "Remote: claimed", "#00FF88", "A controller has claimed your Remote listing.")
            : new DirectoryPill(true, "Remote: listed", "#B47BFF", "Your Remote session is listed in the Available Subjects Directory and waiting to be claimed.");

        /// <summary>The Remote tab calls this whenever its session, its listing or its controller changes.</summary>
        internal void UpdateDirectoryListingStatus(bool active, bool optedIn, bool claimed)
        {
            if (Named<Border>("DirectoryStatusPill") is not { } pill) return;
            var state = DirectoryPillFor(active, optedIn, claimed);
            pill.IsVisible = state.Visible;
            if (!state.Visible) return;
            if (Named<TextBlock>("TxtDirectoryStatus") is { } text) text.Text = state.Text;
            if (Named<Ellipse>("DirectoryStatusDot") is { } dot) dot.Fill = new SolidColorBrush(Color.Parse(state.DotHex));
            ToolTip.SetTip(pill, state.Tip);
        }
    }
}
