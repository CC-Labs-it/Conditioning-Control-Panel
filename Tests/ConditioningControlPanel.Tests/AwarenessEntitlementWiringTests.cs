using System;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class AwarenessEntitlementWiringTests
{
    [Fact]
    public void ObserverUsesWpfEntitlementProvider()
        => AssertObserverEntitlementWiring();

    internal static void AssertObserverEntitlementWiring()
    {
        // The Core gate must use the provider and fail closed if it is missing or throws.
        var observer = SourceRoots.ReadProductFile("Services", "Awareness", "AwarenessObserver.cs");
        Assert.Contains("return AwarenessObserverPolicy.IsEnabled(s, HasEntitlement);", observer, StringComparison.Ordinal);
        var policy = SourceRoots.ReadProductFile("Services", "Awareness", "AwarenessObserverPolicy.cs");
        Assert.Contains("settings != null && hasEntitlement && settings.UseAwarenessV2", policy, StringComparison.Ordinal);
        var propertyStart = observer.IndexOf("public static bool HasEntitlement", StringComparison.Ordinal);
        var startMethod = observer.IndexOf("public void Start()", propertyStart, StringComparison.Ordinal);
        Assert.True(propertyStart >= 0 && startMethod > propertyStart, "observer entitlement property not found");
        var property = observer[propertyStart..startMethod];
        Assert.Contains("return EntitlementProvider?.Invoke() == true;", property, StringComparison.Ordinal);
        Assert.Contains("catch { return false; }", property, StringComparison.Ordinal);

        // Pin both operands and the OR in the WPF static constructor, which runs before startup.
        var app = SourceRoots.ReadProductFile("App.xaml.cs");
        var constructorStart = app.IndexOf("static App()", StringComparison.Ordinal);
        Assert.True(constructorStart >= 0, "WPF App static constructor not found");
        var constructorEnd = app.IndexOf("\n        }", constructorStart, StringComparison.Ordinal);
        Assert.True(constructorEnd > constructorStart, "WPF App static constructor end not found");
        var constructor = app[constructorStart..constructorEnd];
        const string provider = @"(?m)^\s*Services\.Awareness\.AwarenessObserver\.EntitlementProvider\s*=\s*\(\)\s*=>\s*Patreon\?\.HasPremiumAccess\s*==\s*true\s*\|\|\s*DailyFree\?\.IsFreeToday\(""awareness""\)\s*==\s*true\s*;";
        Assert.Matches(new Regex(provider), constructor);
    }
}
