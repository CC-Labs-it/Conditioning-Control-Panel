using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>WPF EmiOffers.SetBedtime: quiet until the next 06:00 local, stored in UTC.</summary>
public static class EmiBedtime
{
    public static DateTime UntilUtc(DateTime localNow)
    {
        var six = localNow.Date.AddHours(6);
        if (localNow >= six) six = six.AddDays(1);
        return six.ToUniversalTime();
    }
}
