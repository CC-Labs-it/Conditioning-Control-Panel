using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private AmbientFxCanvas? _profileBubbleBurst;

        /// <summary>WPF OnBubbleLevelUp's second, smaller FireBurstAt(BtnProfileBubble, count: 45).</summary>
        private void FireProfileBubbleBurst()
        {
            try
            {
                if (!IsVisible || !IsActive || WindowState == WindowState.Minimized || !AmbientFxCanvas.Env.AllowParticles) return;
                if (Named<Button>("BtnProfileBubble") is not { IsEffectivelyVisible: true } anchor ||
                    Named<Grid>("EventFxHost") is not { } host) return;
                var rect = new Rect(anchor.Bounds.Size).TransformToAABB(anchor.TransformToVisual(host)!.Value);
                if (!FxBurstAnchorRules.TryResolve((rect.X, rect.Y, rect.Width, rect.Height),
                    (host.Bounds.Width, host.Bounds.Height), FxBurstSpot.Center, out var origin)) return;
                if (_profileBubbleBurst == null)
                {
                    _profileBubbleBurst = new AmbientFxCanvas
                    {
                        Width = 560, Height = 560, IsHitTestVisible = false,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    };
                    host.Children.Add(_profileBubbleBurst);
                    _profileBubbleBurst.UpdateLayout();
                }
                _profileBubbleBurst.Margin = new Thickness(origin.X - 280, origin.Y - 280, 0, 0);
                _profileBubbleBurst.Burst(280, 280, count: 45);
            }
            catch (Exception ex) { Log.Debug("Profile bubble burst failed: {E}", ex.Message); }
        }
    }
}
