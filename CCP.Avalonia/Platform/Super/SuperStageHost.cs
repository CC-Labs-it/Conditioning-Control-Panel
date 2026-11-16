using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Platform.Super;

/// <summary>Owns the shared Super stage and follows the engine and Super switches.</summary>
internal static class SuperStageHost
{
    internal static SuperStage Stage { get; } = new();
    internal static Func<bool> EngineRunning = () => CoreEngine.IsVisualRun;
    internal static Func<SuperEffect, bool> EffectOn = SuperAccess.IsOn;
    private static bool _subscribed;

    internal static void Sync()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Sync); return; }
        if (!_subscribed)
        {
            _subscribed = true;
            SuperAccess.Changed += OnChanged;
        }
        bool running = EngineRunning();
        bool steam = running && EffectOn(SuperEffect.Creep);
        bool pool = running && EffectOn(SuperEffect.Vortex);
        if (steam != Stage.SteamOn)
        {
            Stage.SteamOn = steam;
            Stage.DropFields();
        }
        if (pool != Stage.PoolOn)
        {
            Stage.PoolOn = pool;
            Stage.Pool.Reset();
        }
    }

    private static void OnChanged(SuperEffect effect)
    {
        if (effect is SuperEffect.Creep or SuperEffect.Vortex) Sync();
    }

    internal static void Stop()
    {
        Stage.SteamOn = false;
        Stage.PoolOn = false;
        Stage.Reset();
    }
}
