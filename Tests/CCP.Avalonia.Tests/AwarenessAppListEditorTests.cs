using System.Collections.Generic;
using System.Linq;
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
}
