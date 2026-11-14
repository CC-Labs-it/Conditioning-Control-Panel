using System;
using Avalonia;
using Avalonia.Animation;

namespace CCP.Avalonia.Tests;

/// <summary>Hand-stepped animation clock, so transitions advance only when the test says so. Avalonia 12 made
/// IClock internal, and the headless render clock is real time, so this reaches Avalonia's own ClockBase by
/// reflection; a rename there fails loudly here instead of silently passing.</summary>
internal sealed class TestAnimationClock
{
    private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    private static readonly Type Base = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", throwOnError: true)!;
    private static readonly System.Reflection.MethodInfo Pulse = Base.GetMethod("Pulse", Any | System.Reflection.BindingFlags.Instance)!;
    public static readonly AvaloniaProperty Property =
        (AvaloniaProperty)typeof(Animatable).GetField("ClockProperty", Any | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    public readonly object Instance = Activator.CreateInstance(Base, nonPublic: true)!;
    public void Attach(Animatable target) => target.SetValue(Property, Instance);
    public double Now { get; private set; }
    public void Step(double ms = 0)
    {
        Now = ms;
        Pulse.Invoke(Instance, new object[] { TimeSpan.FromMilliseconds(ms) });
    }
}

