using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Services/UI/FontGuard.cs: an installed-but-unreadable family is struck out of every chain.</summary>
public sealed class FontGuardTests
{
    [Fact]
    public void FilterChainDropsBrokenLinksAndNeverReturnsEmpty()
    {
        Func<string, bool> broken = f => f is "Cascadia Mono" or "Consolas" or "Courier New";
        Assert.Equal("Consolas, Courier New", FontGuard.FilterChain(FontGuard.MonoChain, f => f == "Cascadia Mono"));
        Assert.Equal("Segoe UI", FontGuard.FilterChain(FontGuard.MonoChain, broken));
        Assert.Equal("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P",
            FontGuard.FilterChain("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas", broken));
        Assert.Equal(FontGuard.MonoChain, FontGuard.FilterChain(FontGuard.MonoChain, null));
    }

    [Fact]
    public Task BrokenCascadiaIsDroppedFromFontMonoAndItsConsumers() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApplication();
        // App.Initialize ran the startup probe (WPF App.xaml.cs:2221).
        Assert.True(FontGuard.Verified);
        var app = Application.Current!;
        Assert.True(app.TryFindResource(FontGuard.MonoResourceKey, out var original));
        var window = new UpdateFailedDialog("t", "m", "System.IO.IOException: detail");
        try
        {
            FontGuard.Reset();
            FontGuard.Verify(f => f == "Cascadia Mono" ? "UnauthorizedAccessException: denied" : null);
            Assert.True(FontGuard.IsBroken("Cascadia Mono"));
            Assert.Equal(new[] { "Consolas", "Courier New" }, FontGuard.Mono.FamilyNames);
            Assert.True(app.TryFindResource(FontGuard.MonoResourceKey, out var published));
            Assert.Equal(new[] { "Consolas", "Courier New" }, Assert.IsType<FontFamily>(published).FamilyNames);
            // A DynamicResource consumer (WPF UpdateFailedDialog.xaml:65) follows the published chain.
            var label = window.FindControl<TextBlock>("TxtDetail")!;
            window.ApplyStyling();
            label.ApplyStyling();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(published, label.FontFamily);
        }
        finally
        {
            window.Close();
            FontGuard.Reset();
            FontGuard.Verify(_ => null);
            app.Resources[FontGuard.MonoResourceKey] = original;
        }
        return Task.CompletedTask;
    });

    private static void EnsureApplication()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
