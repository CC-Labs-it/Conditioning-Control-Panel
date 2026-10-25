using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessAppListEditorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task EnterOrBlurCommitsCanonicalAppsAndPersistsWithoutSavingPartialTyping(bool enter) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
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
