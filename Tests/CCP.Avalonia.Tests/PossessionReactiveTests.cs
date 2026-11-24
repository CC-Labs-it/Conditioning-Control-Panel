using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class PossessionShellTests
{
    private sealed class ReactiveProbe(string id, PossessionRole role) : IPossessionEffect
    {
        public string Id => id;
        public PossessionRung MinRung => PossessionRung.Settle;
        public PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public bool IsBig => false;
        public bool UsesFlicker => false;
        public double Weight => 0;
        public TimeSpan HoldFor => TimeSpan.Zero;
        public IReadOnlyList<PossessionRole> Roles => new[] { role };
        public bool IsLive => false;
        public bool CanApply(PossessionContext ctx, PossessionTarget? target) => target?.Role == role;
        public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct) => Task.CompletedTask;
        public Task UndoAsync(TimeSpan duration) => Task.CompletedTask;
    }

    private static PossessionDirectorHead ReactiveHead() => new()
    {
        BoundsOf = (host, visual) => host is IAvaloniaPossessionHost h && visual is Control c && h.BoundsOnGhostLayer(c) is { } r
            ? (r.X, r.Y, r.Width, r.Height) : null,
        ParentOf = node => node is global::Avalonia.Visual v ? global::Avalonia.VisualTree.VisualExtensions.GetVisualParent(v) : null,
    };

    private static bool Subscribed(object source, string field) => ((Delegate?)source.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source))?.GetInvocationList().Any(d => d.Target is PossessionReactive) == true;

    private static void Changed(string name) => typeof(AppSettings).GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(CoreSettings.Current, new object?[] { name });

    [Fact]
    public Task ReactiveHover_StartOnly_AndCloseUnhooks() => RunAsync(rig =>
    {
        Frame();
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Clock = rig.Clock.AddMinutes(4);
        Tick(rig.Lockdown);
        var started = new List<(string, string?)>();
        rig.Director.EffectStarted += (id, key, _) => started.Add((id, key));
        var pointer = ((IAvaloniaPossessionHost)rig.Shell).PossessionPointer;
        var start = rig.Shell.Named<Button>("BtnStart")!;
        var target = ((IPossessionHost)rig.Shell).Targets.Single(t => ReferenceEquals(t.Visual, start));
        Assert.True(Subscribed(pointer, "HoverChanged"));
        var toggle = new ToggleButton { Name = "BtnStartToggle", Width = 100, Height = 40, Content = "Start" };
        PossessionTree.SetRole(toggle, PossessionRole.Button);
        rig.Shell.Named<Grid>("RootGrid")!.Children.Add(toggle);
        Frame();
        rig.Shell.InvalidatePossessionTargets();
        Assert.NotNull(rig.Director.TargetFor(toggle, PossessionRole.Button));
        var now = DateTime.Now;
        pointer.Move(default, toggle, now);
        pointer.Move(default, rig.Shell.Named<Button>("BtnClose")!, now.AddMilliseconds(10));
        Assert.Empty(started);
        pointer.Move(default, start, now.AddMilliseconds(20));
        Assert.Equal(("dodge", target.Key), Assert.Single(started));
        rig.Lockdown.Deactivate();
        rig.Shell.RequestExit(); rig.Shell.Close();
        Assert.False(Subscribed(pointer, "HoverChanged"));
        pointer.Move(default, null, now.AddMilliseconds(30));
        pointer.Move(default, start, now.AddMilliseconds(40));
        Assert.Single(started);
        return Task.CompletedTask;
    }, effects: new IPossessionEffect[] { new ReactiveProbe("dodge", PossessionRole.Button) }, head: ReactiveHead());

    [Fact]
    public Task ReactiveSettings_OnlyDuringHaunt_ExceptConsent_AndCloseUnhooks() => RunAsync(async rig =>
    {
        Frame();
        var started = new List<(string, string?)>();
        bool onUi = false;
        rig.Director.EffectStarted += (id, key, _) => { onUi = Dispatcher.UIThread.CheckAccess(); started.Add((id, key)); };
        Assert.True(Subscribed(CoreSettings.Current, "PropertyChanged"));
        Changed("SomeSetting");
        Assert.Empty(started);
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Clock = rig.Clock.AddMinutes(4);
        Tick(rig.Lockdown);
        var nearest = rig.Director.NearestTarget(PossessionRole.Label);
        Assert.NotNull(nearest);
        foreach (var name in new[] { "LockdownPossessionEnabled", "LockdownPhotosafe", "GentleVisuals", "LockdownTripwires", "LockdownWarden" }) Changed(name);
        Assert.Empty(started);
        await Task.Run(() => Changed("SomeSetting"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(onUi);
        Assert.Equal(("typo", nearest!.Key), Assert.Single(started));
        rig.Lockdown.Deactivate();
        rig.Shell.RequestExit(); rig.Shell.Close();
        Assert.False(Subscribed(CoreSettings.Current, "PropertyChanged"));
        Changed("SomeSetting");
        Assert.Single(started);
    }, effects: new IPossessionEffect[] { new ReactiveProbe("typo", PossessionRole.Label) }, head: ReactiveHead());
}
