using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Managed-only tests: no font manager, desktop or real font file is opened.</summary>
public sealed class FontGuardTests
{
    [Theory]
    [InlineData("Cascadia Mono", "Consolas, Courier New")]
    [InlineData("Consolas", "Cascadia Mono, Courier New")]
    [InlineData("Courier New", "Cascadia Mono, Consolas")]
    [InlineData("", FontGuard.MonoChain)]
    public void BrokenLinksAreRemovedWithoutReorderingFallbacks(string broken, string expected) =>
        Assert.Equal(expected, FontGuard.FilterChain(FontGuard.MonoChain,
            name => name.Equals(broken, StringComparison.OrdinalIgnoreCase)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    [InlineData(FontGuard.MonoChain)]
    public void EmptyOrEntirelyBrokenChainsHaveTheWpfFloor(string? chain) =>
        Assert.Equal("Segoe UI", FontGuard.FilterChain(chain, _ => true));

    [Fact]
    public void TrimmingAndCaseMatchingNeverRemoveABundledFace() =>
        Assert.Equal("avares://CCP.Avalonia/Resources/fonts#Fredoka, Courier New",
            FontGuard.FilterChain(" avares://CCP.Avalonia/Resources/fonts#Fredoka , conSOLas, , Courier New ",
                name => name.Equals("Consolas", StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void StartupProbesOnceContinuesAfterFailuresAndPublishesTheGuardedChain()
    {
        var calls = new List<string>();
        var guard = new FontGuard(name =>
        {
            calls.Add(name);
            if (name is "Cascadia Mono" or "Segoe UI Emoji") throw new UnauthorizedAccessException();
        });
        var resources = new ResourceDictionary();
        guard.Verify(resources, windows: true);
        Assert.Equal(new[] { "Cascadia Mono", "Consolas", "Courier New", "Segoe MDL2 Assets", "Segoe UI Emoji" }, calls);
        Assert.Equal(2, guard.BrokenFamilies.Count);
        Assert.True(guard.IsBroken("  cascadia mono "));
        Assert.False(guard.IsBroken(null));
        Assert.Equal(new[] { "Consolas", "Courier New" }, Assert.IsType<FontFamily>(resources[FontGuard.MonoResourceKey]).FamilyNames);
        Assert.Equal("Segoe UI", guard.Sanitize(null));
        Assert.Equal("./#Fredoka, Consolas", guard.Sanitize("./#Fredoka, Cascadia Mono, Consolas"));

        var replacement = new FontFamily(AppFonts.Mono);
        resources[FontGuard.MonoResourceKey] = replacement;
        guard.Verify(resources, windows: true);
        Assert.Equal(5, calls.Count);
        Assert.Same(replacement, resources[FontGuard.MonoResourceKey]); // second call really is a no-op
    }

    private sealed class LogSink : ILogEventSink
    {
        internal readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public void AnUnreadableFontLogsOneWarningForTheWholeSession()
    {
        var sink = new LogSink();
        var oldLogger = Log.Logger;
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            Log.Logger = logger;
            var guard = new FontGuard(name => { if (name == "Cascadia Mono") throw new IOException(); });
            var resources = new ResourceDictionary();
            guard.Verify(resources, windows: true);
            guard.Verify(resources, windows: true);
            var warning = Assert.Single(sink.Events, e => e.Level == LogEventLevel.Warning);
            Assert.Equal("Cascadia Mono", Assert.IsType<ScalarValue>(warning.Properties["Family"]).Value);
        }
        finally { Log.Logger = oldLogger; }
    }

    [Fact]
    public void EveryBrokenMonoLinkPublishesSegoeInsteadOfAnEmptyFamily()
    {
        var guard = new FontGuard(_ => throw new IOException());
        var resources = new ResourceDictionary();
        guard.Verify(resources, windows: true);
        Assert.Equal("Segoe UI", Assert.IsType<FontFamily>(resources[FontGuard.MonoResourceKey]).Name);
        Assert.Equal(5, guard.BrokenFamilies.Count);
    }

    [Fact]
    public void OffWindowsUsesTheExistingBundledMappingsWithoutOpeningSystemFonts()
    {
        var calls = 0;
        var guard = new FontGuard(_ => calls++);
        var resources = new ResourceDictionary();
        guard.Verify(resources, windows: false);
        Assert.Equal(0, calls);
        Assert.Empty(guard.BrokenFamilies);
        Assert.Equal(new[] { "Cascadia Mono", "Consolas", "Courier New" }, Assert.IsType<FontFamily>(resources[FontGuard.MonoResourceKey]).FamilyNames);
        Assert.Equal("  Consolas, Courier New  ", guard.Sanitize("  Consolas, Courier New  "));
    }
}
