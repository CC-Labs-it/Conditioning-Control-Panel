using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionEngineLoginTests
{
    // WPF EngineRoomRuntimeVm.cs:47, EngineRoomDrawer.xaml:119-123 and
    // ConversationPageVm.cs:241-251: both sign-in doors open Settings > Account.
    [Fact]
    public Task SignInDoorsOpenAccountAndPreviewDoesNotNavigate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.AwarenessModeEnabled = false;
        s.AiChatEnabled = true;
        s.CompanionPrompt.AiProvider = AiProviderType.Cloud;
        CoreAccount.UnifiedUserId = null;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            shell.CompleteTabTransition();
            Dispatcher.UIThread.RunJobs();
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var engine = room.FindControl<EngineRoomDrawer>("EngineZone")!;
            var vm = (EngineRoomVm)engine.DataContext!;
            Assert.False(vm.IsLoggedIn);

            // Start on the conversation page before the drawer has been adopted.
            var conversation = shell.GetLogicalDescendants().OfType<ConversationPage>().Single();
            conversation.FindControl<Button>("StartButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            AssertAccount(shell);

            shell.ShowTab("companionai");
            shell.CompleteTabTransition();
            Dispatcher.UIThread.RunJobs();
            var login = engine.GetLogicalDescendants().OfType<Button>().Single(b =>
                b.Content is TextBlock text && text.Text == Loc.Get("companion_engine_login_button"));
            Assert.True(login.IsEffectivelyVisible);
            Assert.NotNull(login.Command);
            login.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = login.TranslatePoint(new Point(login.Bounds.Width / 2, login.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(point, MouseButton.Left);
            shell.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            AssertAccount(shell);

            // The preview uses no-op commands even when hosted inside a live shell.
            shell.ShowTab("companionai");
            shell.CompleteTabTransition();
            var preview = EngineRoomVm.CreatePreview(engine, "LoggedOut");
            preview.LoginCommand!.Execute(null);
            Assert.Equal("companionai", shell.CurrentTab);
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToList()) window.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void AssertAccount(MainShellWindow shell)
    {
        Assert.Equal("appsettings", shell.CurrentTab);
        Assert.True(shell.AppSettingsPage!.FindControl<RadioButton>("SectionPillAccount")!.IsChecked);
    }
}
