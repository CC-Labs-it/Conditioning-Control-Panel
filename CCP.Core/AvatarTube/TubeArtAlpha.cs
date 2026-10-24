namespace ConditioningControlPanel.AvatarTubeLayout;

/// <summary>WPF 877085a51: invisible tube haze must not own the mouse.</summary>
public static class TubeArtAlpha
{
    public const byte Floor = 8;

    /// <summary>Clear whole premultiplied BGRA pixels with alpha 1..floor; return the changed count.</summary>
    public static int Clean(byte[]? pbgra, byte floor = Floor)
    {
        if (pbgra == null) return 0;
        int changed = 0;
        for (int i = 3; i < pbgra.Length; i += 4)
        {
            byte alpha = pbgra[i];
            if (alpha == 0 || alpha > floor) continue;
            pbgra[i - 3] = 0; pbgra[i - 2] = 0; pbgra[i - 1] = 0; pbgra[i] = 0;
            changed++;
        }
        return changed;
    }
}
