using System;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel;

namespace CCP.Avalonia.Tests;

/// <summary>App.StartMods seeds CoreMods' static providers from the test's own ModService. Left seeded,
/// every later test (xunit v3 shuffles class order) sees that test's last mod. Take one before StartMods,
/// dispose it in the test's finally.</summary>
internal sealed class CoreModsSnapshot : IDisposable
{
    private static readonly FieldInfo[] Providers = typeof(CoreMods)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.Name.EndsWith("Provider", StringComparison.Ordinal)).ToArray();

    private readonly object?[] _saved = Providers.Select(f => f.GetValue(null)).ToArray();
    private readonly Func<System.Collections.Generic.IReadOnlyDictionary<string, string>?>? _links =
        CoreModsHooks.KnownVideoLinksProvider;

    private readonly Action<string?>? _modSwitched = CoreModsHooks.ModSwitched;
    private readonly Action? _invalidatePrompt = CoreModsHooks.InvalidateStablePrompt;
    private readonly Action? _closeDtrhHost = CoreModsHooks.CloseDtrhHost;
    private static readonly PropertyInfo Service = typeof(CoreMods).GetProperty(nameof(CoreMods.Service))!;
    private static readonly PropertyInfo AppMods = typeof(ConditioningControlPanel.Avalonia.App)
        .GetProperty("Mods", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _service = Service.GetValue(null);
    private readonly object? _appMods = AppMods.GetValue(null);

    public void Dispose()
    {
        for (var i = 0; i < Providers.Length; i++) Providers[i].SetValue(null, _saved[i]);
        CoreModsHooks.KnownVideoLinksProvider = _links;
        CoreModsHooks.ModSwitched = _modSwitched;
        CoreModsHooks.InvalidateStablePrompt = _invalidatePrompt;
        CoreModsHooks.CloseDtrhHost = _closeDtrhHost;
        Service.SetValue(null, _service);
        AppMods.SetValue(null, _appMods);
    }
}
