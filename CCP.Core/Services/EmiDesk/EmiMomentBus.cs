using System;
using Serilog;
namespace ConditioningControlPanel.Services.EmiDesk;
public sealed record EmiMoment(string Id, object? Context);
public sealed class EmiMomentBus
{
    private readonly EmiLineEngine _engine;
    public EmiMomentBus(EmiLineEngine engine) => _engine = engine;
    public event EventHandler<EmiMoment>? MomentFired;
    public event EventHandler<EmiMoment>? SpeakingRequested;
    public void Fire(string id, object? context, bool isOut)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var moment = new EmiMoment(id, context);
        try { MomentFired?.Invoke(this, moment); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] moment listener failed"); }
        try
        {
            if (isOut) SpeakingRequested?.Invoke(this, moment);
            else if (_engine.IsHoldMoment(id)) _engine.Draw(id, EmiLineEngine.ToCtx(context));
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] moment delivery failed"); }
    }
    public void ReleaseHold(string id)
    {
        try { if (!string.IsNullOrWhiteSpace(id)) _engine.ReleaseHold(id); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] hold release failed"); }
    }
    public (LineDraw? Line, AskDraw? Ask) Draw(EmiMoment moment)
    {
        var ctx = EmiLineEngine.ToCtx(moment.Context);
        var line = _engine.Draw(moment.Id, ctx);
        var ask = _engine.DrawAsk(moment.Id, ctx);
        if (moment.Id is "dismissed" or "appClosing") return (line?.Hold == true ? line : null, null);
        return (line, ask);
    }
}
