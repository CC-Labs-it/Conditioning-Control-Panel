// PORTED from WPF MainWindow/MainWindow.RemoteControl.cs:685-738 (UpdateDirectoryListingStatus), after
// parity/trunk d7a46b6e4: the title-bar pill that says whether a running remote session is listed.
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal readonly record struct DirectoryPill(bool Visible, string Text, string DotHex, string Tip);

        /// <summary>WPF's four states, localized on this head: no session = hidden;
        /// private = grey; listed = directory purple; claimed = green.</summary>
        internal static DirectoryPill DirectoryPillFor(bool active, bool optedIn, bool claimed) =>
            !active ? new DirectoryPill(false, "", "#8A8AA0", "")
            : !optedIn ? new DirectoryPill(true, Loc.Get("remote_directory_private"), "#8A8AA0",
                Loc.Get("remote_directory_private_tip"))
            : claimed ? new DirectoryPill(true, Loc.Get("remote_directory_claimed"), "#00FF88", Loc.Get("remote_directory_claimed_tip"))
            : new DirectoryPill(true, Loc.Get("remote_directory_listed"), "#B47BFF", Loc.Get("remote_directory_listed_tip"));

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
