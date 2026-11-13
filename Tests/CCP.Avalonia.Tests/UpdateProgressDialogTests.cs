using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>From #3614 (restack b29): the download bar's fill matches WPF's 2 px inset.</summary>
public sealed class UpdateProgressDialogTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void UpdateProgressClampsAndPaintsTheDownloadPercentage() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var dialog = new UpdateProgressDialog();
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var fill = dialog.FindControl<Border>("ProgressFill")!;
            var track = (Border)((Control)fill.Parent!).Parent!;
            var width = track.Bounds.Width - 6; // WPF UpdateProgressDialog.xaml.cs:28
            Assert.True(width > 0);
            Assert.Equal(Loc.Get("dialog_downloading_update"), dialog.Title);
            Assert.False(dialog.CanResize);
            foreach (var (fraction, expected) in new[] { (-0.1, 0), (0.37, 37), (1.2, 100), (0.0, 0) })
            {
                dialog.SetProgress(fraction, ((Control)fill.Parent!).Bounds.Width);
                Assert.Equal($"{expected}%", dialog.FindControl<TextBlock>("TxtProgress")!.Text);
                Assert.Equal(width * expected / 100, fill.Width, 6);
            }
            dialog.SetProgress(0.5, 2); // WPF skips a nonpositive available width.
            Assert.Equal(0, fill.Width);
            Assert.Equal("50%", dialog.FindControl<TextBlock>("TxtProgress")!.Text);
        }
        finally { dialog.Close(); }
    });

}
