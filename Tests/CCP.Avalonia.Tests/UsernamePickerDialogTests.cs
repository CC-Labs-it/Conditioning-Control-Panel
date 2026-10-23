using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// UsernamePickerDialog against WPF Dialogs/UsernamePickerDialog.xaml.cs: the local name rules,
/// Confirm gating and the Cancel result. Only the paths that end before the 500 ms debounce run, so
/// the hardcoded availability endpoint is never called.
/// </summary>
public sealed class UsernamePickerDialogTests
{
    private static void SetupHeadless()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task LocalRules_GateConfirm_WithWpfTextAndColours() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetupHeadless();
            var dlg = new UsernamePickerDialog();
            dlg.ConfigureForNewUser();
            dlg.Show();
            try
            {
                Assert.False(dlg.FindControl<Border>("OgWelcomePanel")!.IsVisible);
                Assert.False(dlg.FindControl<StackPanel>("SuggestionPanel")!.IsVisible);

                var box = dlg.FindControl<TextBox>("TxtUsername")!;
                var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
                var confirm = dlg.FindControl<Button>("BtnConfirm")!;

                void Expect(string text, string key, IBrush colour)
                {
                    box.Text = text;
                    // MaxLength=30 only limits typing; text set from code must not be truncated.
                    Assert.Equal(text, box.Text);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(Loc.Get(key), status.Text);
                    Assert.Same(colour, status.Foreground);
                    Assert.False(confirm.IsEnabled);
                }

                Expect("ab", "login_name_min_3_chars", Brushes.Orange);
                Expect(new string('x', 31), "login_name_max_30_chars", Brushes.Orange);
                Expect("   ", "login_enter_unique_display_name", Brushes.Gray);

                // A disabled Confirm is a no-op, as WPF's _isAvailable guard.
                Click(confirm);
                Assert.True(dlg.IsVisible);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { dlg.Close(); }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Cancel_ClosesFalse_WithNoName() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SetupHeadless();
            var owner = new Window();
            owner.Show();
            try
            {
                var dlg = new UsernamePickerDialog();
                var shown = dlg.ShowDialog<bool?>(owner);
                dlg.FindControl<TextBox>("TxtUsername")!.Text = "ab";
                Click(dlg.FindControl<Button>("BtnCancel")!);
                Assert.False(await shown);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { owner.Close(); }
        });
}
