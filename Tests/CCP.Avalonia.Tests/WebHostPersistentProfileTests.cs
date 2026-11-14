using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebHostPersistentProfileTests
{
    [Theory]
    [InlineData(typeof(WindowsWebView2EnvironmentRequestedEventArgs))]
    [InlineData(typeof(GtkWebViewEnvironmentRequestedEventArgs))]
    [InlineData(typeof(LinuxWpeWebViewEnvironmentRequestedEventArgs))]
    public Task ProfileIsPerHostAndAppliedToEachEngine(Type engine) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var args = (WebViewEnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(engine);
        var host = new WebHost();
        var folder = Path.Combine("isolated", "bambicloud");
        host.PersistentUserDataFolder = folder;
        host.ApplyPersistentProfile(args);
        switch (args)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                Assert.Equal(folder, windows.UserDataFolder);
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                Assert.Equal(folder, gtk.BaseDataDirectory);
                Assert.Equal(Path.Combine(folder, "cache"), gtk.BaseCacheDirectory);
                Assert.False(gtk.EphemeralDataManager);
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                Assert.Equal(folder, wpe.DataDirectory);
                Assert.Equal(Path.Combine(folder, "cache"), wpe.CacheDirectory);
                break;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnconfiguredHostDoesNotChangeTheEngineProfile() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var args = (GtkWebViewEnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(GtkWebViewEnvironmentRequestedEventArgs));
        args.BaseDataDirectory = "existing-data";
        args.BaseCacheDirectory = "existing-cache";
        args.EphemeralDataManager = true;
        new WebHost().ApplyPersistentProfile(args);
        Assert.Equal("existing-data", args.BaseDataDirectory);
        Assert.Equal("existing-cache", args.BaseCacheDirectory);
        Assert.True(args.EphemeralDataManager);
        return Task.CompletedTask;
    });
}
