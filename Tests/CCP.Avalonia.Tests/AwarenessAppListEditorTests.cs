using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessAppListEditorTests
{
    [Fact]
    public void AppNamesCommitOnEnterAndBlurInsteadOfWhileTyping() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.KeywordTriggerAppScope, s.KeywordTriggerApps);
        Window? host = null;
        try
        {
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            s.KeywordTriggerApps = new List<string> { "discord" };
            var view = new AwarenessTabView();
            host = new Window { Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            Assert.Equal("discord", box.Text);
            box.Focus();
            box.Text = " Chrome.exe; chrome, DISCORD.EXE, ; .exe ";
            Assert.Equal(new[] { "discord" }, s.KeywordTriggerApps); // typing must not narrow scope
            Assert.True(box.IsFocused); // else the IsFocused=false check below proves nothing
            host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // real input path
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Chrome", "DISCORD" }, s.KeywordTriggerApps);
            Assert.Equal("Chrome, DISCORD", box.Text);
            Assert.False(box.IsFocused);

            box.Text = "firefox.exe, steam.EXE";
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Assert.Equal(new[] { "firefox", "steam" }, s.KeywordTriggerApps);
            Assert.Equal("firefox, steam", box.Text);

            var unchanged = s.KeywordTriggerApps;
            box.Text = "FIREFOX.EXE; STEAM.exe";
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Assert.Same(unchanged, s.KeywordTriggerApps); // WPF avoids redundant saves (case-insensitive)

            box.Text = " , ; .exe ";
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Assert.Empty(s.KeywordTriggerApps);
            Assert.Equal("", box.Text);
        }
        finally
        {
            host?.Close();
            (s.KeywordTriggerAppScope, s.KeywordTriggerApps) = old;
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task EnterOrBlurCommitsCanonicalAppsAndPersistsWithoutSavingPartialTyping(bool enter) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previous = CoreSettings.ServiceProvider;
            var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
            var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var service = new SettingsService();
            Window? host = null;
            try
            {
                CoreSettings.ServiceProvider = () => service;
                service.Current.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
                service.Current.KeywordTriggerApps = new() { "firefox" };
                service.SaveImmediate();
                var view = new AwarenessTabView();
                host = new Window { Content = view, Width = 1000, Height = 800 };
                host.Show();
                var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
                Assert.Equal("firefox", box.Text);
                box.Text = "  Chrome.exe; DISCORD.EXE, chrome; ms-teams  ";
                Assert.Equal(new[] { "firefox" }, service.Current.KeywordTriggerApps);
                if (enter)
                {
                    Assert.True(box.Focus());
                    Assert.True(box.IsFocused);
                    box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    Assert.False(box.IsFocused); // WPF moves focus off the box on Enter
                }
                else
                    box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                Assert.Equal(new[] { "Chrome", "DISCORD", "ms-teams" }, service.Current.KeywordTriggerApps);
                Assert.Equal("Chrome, DISCORD, ms-teams", box.Text);
                // The editor must queue its own debounced save; flush it deterministically (P08).
                Assert.True(service.HasPendingSave);
                service.SaveImmediate();
                Assert.Equal(service.Current.KeywordTriggerApps, new SettingsService().Current.KeywordTriggerApps);
                // Clearing OnlyListed is valid: the eventual engine must suppress every unidentified/unlisted app.
                box.Text = ", ; .exe";
                box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                Assert.Empty(service.Current.KeywordTriggerApps);
                Assert.Equal(string.Empty, box.Text);
                Assert.Equal(AwarenessAppScope.OnlyListed, service.Current.KeywordTriggerAppScope);
                // Changing to Everywhere hides the list without erasing it.
                box.Text = "zoom.exe";
                box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                var scope = view.FindControl<ComboBox>("CmbAwarenessAppScope")!;
                scope.SelectedItem = scope.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "Everywhere");
                Assert.False(view.FindControl<StackPanel>("AwarenessAppListPanel")!.IsVisible);
                Assert.Equal(new[] { "zoom" }, service.Current.KeywordTriggerApps);
            }
            finally
            {
                host?.Close();
                service.SaveImmediate();
                CoreSettings.ServiceProvider = previous;
                if (saved is null) File.Delete(settingsPath);
                else File.WriteAllBytes(settingsPath, saved);
            }
            return Task.CompletedTask;
        });
}
