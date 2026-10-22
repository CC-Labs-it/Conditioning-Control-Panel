using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed class FeaturedProvider : BillboardProviderBase
{
    private readonly Action<string> _watch;
    private bool _hooked;
    public FeaturedProvider(Action<string> watch) => _watch = watch;
    public override string Id => "featured";
    public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
    {
        if (!_hooked)
        {
            PbpFeaturedFeed.Changed += RaiseChanged;
            OnDetach(() => PbpFeaturedFeed.Changed -= RaiseChanged);
            _hooked = true;
        }
        var card = FeaturedCards.Decide(PbpFeaturedFeed.Current, Loc);
        return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
    });
    public override void Invoke(string target)
    {
        if (FeaturedCards.TryParseTarget(target, out var id)) _watch(id);
    }
}
