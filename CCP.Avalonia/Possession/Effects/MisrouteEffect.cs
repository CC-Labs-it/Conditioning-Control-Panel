// WPF: Services/Possession/Effects/MisrouteEffect.cs:26-233.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class MisrouteEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] DoorRoles = { PossessionRole.TabHeader };
    private string? _from, _to;
    private Func<string, string?>? _hook;
    private CancellationToken _token;

    public override string Id => "misroute";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(20);
    public override IReadOnlyList<PossessionRole> Roles => DoorRoles;
    protected override bool ChargeOnApply => false;

    private static string? Key(Control? door)
    {
        if (door?.Tag is not string key || string.IsNullOrWhiteSpace(key) ||
            key.Equals("webapp", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("lockdown", StringComparison.OrdinalIgnoreCase) ||
            PossessionTree.IsOffLimits(door)) return null;
        return key;
    }

    private static List<Control> Doors(PossessionContext ctx)
    {
        var doors = new List<Control>();
        foreach (var target in ctx.Host.Targets)
            if (target.Role == PossessionRole.TabHeader && target.Visual is Control door && Key(door) != null &&
                door.IsEffectivelyVisible && door.IsEffectivelyEnabled && door.IsHitTestVisible)
                doors.Add(door);
        return doors;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var from = Key(target?.Visual as Control);
        if (from == null || MainShellWindow.PossessionReroute != null) return false;
        return Doors(ctx).Exists(d => !string.Equals(Key(d), from, StringComparison.OrdinalIgnoreCase));
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (MainShellWindow.PossessionReroute != null) return Task.CompletedTask;
        _from = Key(target?.Visual as Control);
        if (_from == null) return Task.CompletedTask;
        var doors = Doors(ctx);
        doors.RemoveAll(d => string.Equals(Key(d), _from, StringComparison.OrdinalIgnoreCase));
        if (doors.Count == 0) return Task.CompletedTask;
        _to = Key(doors[ctx.Rng.Next(doors.Count)]);
        _token = ct;
        _hook = Reroute;
        MainShellWindow.PossessionReroute = _hook;
        return Task.CompletedTask;
    }

    private string? Reroute(string pressed)
    {
        if (Context == null || _to == null || pressed.Equals("lockdown", StringComparison.OrdinalIgnoreCase) ||
            pressed.Equals("webapp", StringComparison.OrdinalIgnoreCase)) return null;
        var to = pressed.Equals(_to, StringComparison.OrdinalIgnoreCase) ? _from : _to;
        if (to == null || pressed.Equals(to, StringComparison.OrdinalIgnoreCase)) return null;
        var doors = Doors(Context);
        if (!doors.Exists(d => string.Equals(Key(d), to, StringComparison.OrdinalIgnoreCase))) return null;
        var source = doors.Find(d => string.Equals(Key(d), pressed, StringComparison.OrdinalIgnoreCase));
        ClearHook();
        if (source != null) _ = FinishAsync(source, _token);
        else _ = UndoAsync(TimeSpan.Zero);
        return to;
    }

    private async Task FinishAsync(Control source, CancellationToken ct)
    {
        try
        {
            var hold = Task.Delay(1400, ct);
            await ChargeAndPossessAsync(source, ct, PossessionTree.GetName(source)).ConfigureAwait(true);
            await hold.ConfigureAwait(true);
            await UndoAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession misroute cleanup failed: {Error}", ex.Message); }
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        ClearHook();
        _from = _to = null;
        return Task.CompletedTask;
    }

    private void ClearHook()
    {
        if (_hook != null && ReferenceEquals(MainShellWindow.PossessionReroute, _hook))
            MainShellWindow.PossessionReroute = null;
        _hook = null;
    }
}
