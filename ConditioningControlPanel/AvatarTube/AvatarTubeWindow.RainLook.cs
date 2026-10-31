using System;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The wardrobe's Apply landed (RAIN-V2-PLAN 2.3, lane R5): RainLook + RainPackHash changed, so the
    /// emote engine re-resolves its folder (the baked pack, or the embedded clips for the default look)
    /// and re-enters emote mode when the folder moved.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        internal void RefreshRainLook()
        {
            try { Dispatcher.BeginInvoke(new Action(TryUpdateCirceEmoteMode), DispatcherPriority.Normal); }
            catch (Exception ex) { App.Logger?.Debug("Avatar tube: Rain look refresh failed ({Error})", ex.Message); }
        }
    }
}
