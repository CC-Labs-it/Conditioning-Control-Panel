using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlayWebAppCardTests
{
    [Fact]
    public async Task CardLocalizesAndOpensWebAppFromKeyboard()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var loc = LocalizationManager.Instance;
            var language = loc.CurrentLanguage;
            var seen = CoreSettings.Current.SeenFeatureIntros;
            bool hadKey = seen.Remove(MainShellWindow.WebBannerSeenKey);
            var previous = MainShellWindow.OpenPlayWebAppLink;
            string? opened = null;
            MainShellWindow.OpenPlayWebAppLink = (_, url) => { opened = url; return Task.FromResult(true); };
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                w.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                Assert.Null(w.Named<Button>("DoorWebApp"));
                var play = w.Named<PlayTabView>("PlayTab")!;
                var title = play.FindControl<TextBlock>("TxtNavWebAppLabel")!;
                var blurb = play.FindControl<TextBlock>("TxtPlayWebAppBlurb")!;
                loc.SetLanguage("it");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(loc.Get("nav_door_webapp"), title.Text);
                Assert.Equal(loc.Get("play_card_webapp_blurb"), blurb.Text);
                var button = play.FindControl<Button>("BtnPlayWebApp")!;
                Assert.True(button.Focus());
                w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("https://app.cclabs.app", opened);
                Assert.Contains(MainShellWindow.WebBannerSeenKey, seen);
            }
            finally
            {
                w?.Close();
                MainShellWindow.OpenPlayWebAppLink = previous;
                if (!hadKey) seen.Remove(MainShellWindow.WebBannerSeenKey);
                else if (!seen.Contains(MainShellWindow.WebBannerSeenKey)) seen.Add(MainShellWindow.WebBannerSeenKey);
                loc.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
