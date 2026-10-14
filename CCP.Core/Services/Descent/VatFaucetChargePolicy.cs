using System;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>The WPF faucet's hold budget, six-rung ladder and daily compact cut, shared by both heads.</summary>
public sealed class VatFaucetChargePolicy
{
    public const int RungCount = 6;
    private DateTime _day;
    private int _pours;

    public int PoursToday(DateTime utcNow)
    {
        if (_day != utcNow.Date) { _day = utcNow.Date; _pours = 0; }
        return _pours;
    }

    public bool IsCompact(DateTime utcNow) => PoursToday(utcNow) >= 3;

    public void NotePour(DateTime utcNow) { PoursToday(utcNow); _pours++; }

    public static double BudgetMilliseconds(int heldXp, bool compact)
        => Math.Max(220, Math.Min(1400, 700 + Math.Max(0, heldXp) / 4.0) * (compact ? .6 : 1));

    public static int RungsAt(double progress)
    {
        progress = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        return Math.Min(RungCount, (int)(progress * RungCount) + 1);
    }
}
