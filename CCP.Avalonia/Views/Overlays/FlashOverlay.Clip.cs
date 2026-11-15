using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Flash;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal static partial class FlashOverlay
{
    private sealed class ClipState
    {
        internal readonly FlashOverlayWindow Window;
        internal readonly Bitmap Poster;
        internal readonly string Path;
        internal readonly int Width;
        internal readonly double Speed;
        internal FlashClipPlayer? Player;
        internal bool Attempted, Refused;
        internal ClipState(FlashOverlayWindow window, Bitmap poster, string path, int width, double speed)
            => (Window, Poster, Path, Width, Speed) = (window, poster, path, width, speed);
    }
    private static readonly List<ClipState> Clips = new();
    private static BeatLoop? _clipLoop;
    private static FlashOverlayWindow? _clipOwner;

    internal static void StartClip(FlashOverlayWindow window, Bitmap poster, string? path, int width, double speed)
    {
        if (string.IsNullOrEmpty(path)) return;
        var state = new ClipState(window, poster, path, width, speed);
        Clips.Add(state);
        window.Closed += (_, _) =>
        {
            Clips.Remove(state);
            state.Player?.Dispose();
            if (_clipOwner == window)
            {
                _clipLoop?.Stop(); _clipLoop = null; _clipOwner = null;
                if (Clips.Count > 0) StartClipBeat(Clips[0].Window);
            }
        };
        if (_clipLoop == null) StartClipBeat(window);
        ClipTick();
    }

    private static void StartClipBeat(FlashOverlayWindow owner)
    {
        _clipOwner = owner;
        var first = true; // Start invokes the callback synchronously, even during CloseAll.
        _clipLoop = new BeatLoop(owner, _ => { if (first) first = false; else ClipTick(); }, TimeSpan.FromMilliseconds(50));
        _clipLoop.Start();
    }

    internal static void ClipTick()
    {
        foreach (var state in Clips.ToArray())
        {
            bool showing = !state.Window.IsFadingOut && state.Window.Exit == null && state.Window.Shatter == null;
            if (state.Player is { } player)
            {
                player.Evictable = FlashClipUpgrade.Evictable(showing);
                if (player.IsDisposed)
                {
                    state.Window.SetPicture(state.Poster);
                    if (FlashClipUpgrade.Reclaim(true, true, showing))
                    { state.Player = null; state.Attempted = false; state.Refused = true; }
                }
            }
            if (FlashClipUpgrade.ShouldTryStart(true, true, showing, state.Player != null,
                    state.Attempted, state.Refused, FlashClipPlayer.CanStartFlash))
            {
                state.Player = FlashClipPlayer.TryStart(state.Path, state.Width, state.Speed, state.Window.SetPicture, () => state.Window.SetPicture(state.Poster), out var atCap);
                (state.Attempted, state.Refused) = FlashClipUpgrade.AfterTry(state.Player != null, atCap);
            }
        }
    }
}
