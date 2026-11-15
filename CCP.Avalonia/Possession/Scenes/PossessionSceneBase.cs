// WPF reference: Services/Possession/Scenes/PossessionSceneBase.cs:45.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Scenes;

namespace ConditioningControlPanel.Avalonia.Possession.Scenes;

internal abstract class PossessionSceneBase : IPossessionScene
{
    public abstract string Id { get; }
    public virtual PossessionRung MinRung => PossessionRung.Melt;
    public virtual int Beats => 3;
    public bool IsRunning { get; private set; }
    private bool _undone = true;
    private bool _named;
    private readonly List<Action> _restores = new();
    private readonly List<PossessionLease> _leases = new();
    private readonly List<IDisposable> _handles = new();
    protected PossessionContext? Ctx { get; private set; }

    public async Task RunAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
    {
        if (IsRunning) return;
        IsRunning = true;
        _undone = false;
        _named = false;
        Ctx = ctx;
        try { await RunCoreAsync(ctx, host, pick ?? (_ => null), ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Possession scene {Id} failed: {Error}", Id, ex.Message);
            await UndoAsync(TimeSpan.Zero).ConfigureAwait(true);
        }
        finally { IsRunning = false; }
    }

    protected abstract Task RunCoreAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct);

    public Task UndoAsync(TimeSpan duration)
    {
        if (_undone) return Task.CompletedTask;
        _undone = true;
        for (int i = _restores.Count - 1; i >= 0; i--)
            try { _restores[i](); } catch (Exception ex) { Serilog.Log.Warning("Possession scene {Id} restore failed: {Error}", Id, ex.Message); }
        _restores.Clear();
        for (int i = _leases.Count - 1; i >= 0; i--)
            try { _leases[i].ReleaseImmediate(); } catch { }
        _leases.Clear();
        foreach (var handle in _handles) try { handle.Dispose(); } catch { }
        _handles.Clear();
        Ctx = null;
        return Task.CompletedTask;
    }

    protected async Task<bool> ChargeAsync(Control? control, CancellationToken ct, int chargeMs = 260)
    {
        if (control == null || ct.IsCancellationRequested || Ctx == null) return false;
        try { await Ctx.Attribution.ChargeAsync(control, ct, chargeMs).ConfigureAwait(true); }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Serilog.Log.Warning("Possession scene {Id} charge failed: {Error}", Id, ex.Message); }
        if (ct.IsCancellationRequested || Ctx == null) return false;
        try { var handle = Ctx.Attribution.Possess(control); if (handle != null) _handles.Add(handle); }
        catch (Exception ex) { Serilog.Log.Warning("Possession scene {Id} possess failed: {Error}", Id, ex.Message); }
        return true;
    }

    protected PossessionLease? Lease(Control? control)
    {
        var lease = PossessionLease.Take(control);
        if (lease != null) _leases.Add(lease);
        return lease;
    }
    protected void OnUndo(Action restore) { if (restore != null) _restores.Add(restore); }
    protected void NameOnce(string? target)
    {
        if (_named || Ctx == null) return;
        _named = true;
        try { Ctx.Name(Id, target); } catch (Exception ex) { Serilog.Log.Warning("Possession scene {Id} name failed: {Error}", Id, ex.Message); }
    }
    protected double Amp(double value) => Ctx?.Photosafe == true ? value * 0.5 : value;
}
