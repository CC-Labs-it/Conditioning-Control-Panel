using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>WPF FxTheme: publish the mod palette once; every ambient surface reads these keys.</summary>
internal static class FxTheme
{
    internal static void ModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(ApplyForActiveMod);

    internal static void ApplyForActiveMod()
    {
        if (Application.Current is not { } app) return;
        // App resources notify every live control, which must happen on the UI thread.
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ApplyForActiveMod); return; }
        var mods = App.Mods;
        Set("FxMist", mods?.GetMistColorRgb());
        Set("FxParticle", mods?.GetParticleColorRgb());
        Set("FxGlow", mods?.GetGlowColorRgb());
        Set("FxFlashTint", mods?.GetFlashTintRgb());
        void Set(string stem, (byte R, byte G, byte B)? rgb)
        {
            var c = rgb is { } v ? Color.FromRgb(v.R, v.G, v.B) : Color.FromRgb(0xFF, 0x69, 0xB4);
            app.Resources[stem + "Color"] = c;
            app.Resources[stem + "Brush"] = new SolidColorBrush(c);
        }
    }
}
