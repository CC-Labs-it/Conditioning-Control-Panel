using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DisplayNameAskTests
{
    private static Task OnUi(Func<Task> run) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        await run();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public Task Login_preserves_nullable_server_answer(bool? needed) => OnUi(async () =>
    {
        var s = CoreSettings.Current;
        var fields = new[] { "UnifiedId", "UserDisplayName", "IsSeason0Og", "CurrentSeason", "HighestLevelEver", "HasLinkedDiscord", "HasLinkedPatreon", "PatreonTier", "OfflineMode" };
        var old = fields.ToDictionary(n => n, n => s.GetType().GetProperty(n)!.GetValue(s));
        var oldIdentity = CoreAccount.UnifiedUserId;
        try
        {
            s.OfflineMode = true; // no real profile request
            var dialog = new LoginDialog();
            dialog.Succeed(new V2AuthService.V2User { UnifiedId = s.UnifiedId, NeedsDisplayName = needed }, null, null, false);
            await dialog.ProfileLoad;
            Assert.Equal(needed, dialog.Result!.ServerNeedsDisplayName);
        }
        finally
        {
            foreach (var field in fields) s.GetType().GetProperty(field)!.SetValue(s, old[field]);
            CoreAccount.UnifiedUserId = oldIdentity;
        }
    });

    [Fact]
    public Task Required_mode_validates_and_keeps_a_refused_save_open() => OnUi(async () =>
    {
        var calls = 0;
        var dialog = new DisplayNameDialog(null, name => { calls++; return Task.FromResult<string?>("Try another name"); });
        var input = dialog.FindControl<TextBox>("TxtDisplayName")!;
        var confirm = dialog.FindControl<Button>("BtnConfirm")!;
        input.Text = "ab"; Dispatcher.UIThread.RunJobs(); // TextChanged is posted, not raised inline
        Assert.False(confirm.IsEnabled);
        input.Text = "bad!"; Dispatcher.UIThread.RunJobs(); // TextChanged is posted, not raised inline
        Assert.False(confirm.IsEnabled);
        input.Text = "Valid name"; Dispatcher.UIThread.RunJobs(); // TextChanged is posted, not raised inline
        Assert.True(confirm.IsEnabled);
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.CompletedTask;
        Assert.Equal(1, calls);
        Assert.Equal("Try another name", dialog.FindControl<TextBlock>("TxtStatus")!.Text);
        Assert.True(input.IsEnabled);
        Assert.True(confirm.IsEnabled);
        dialog.Close();
    });
}
