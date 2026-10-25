namespace ConditioningControlPanel.Services;

/// <summary>Rec.601 luma over BGRA32 pixels. Alpha remains unchanged.</summary>
public static class CardMutePixels
{
    public static void ToGrey(byte[] bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int luma = (bgra[i] * 7471 + bgra[i + 1] * 38470 + bgra[i + 2] * 19595 + 32768) >> 16;
            bgra[i] = bgra[i + 1] = bgra[i + 2] = (byte)luma;
        }
    }
}
