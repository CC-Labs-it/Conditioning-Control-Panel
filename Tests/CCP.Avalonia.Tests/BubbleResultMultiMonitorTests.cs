using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleResultMultiMonitorTests
{
    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, -3000)]
    [InlineData(true, false, 0)]
    [InlineData(false, false, -3000)]
    public void ActualClientPixelsCoverTargetAtMapping(bool dual, bool markedPrimary, int offset) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var portrait = MakeScreen(1.3, new PixelRect(offset, -120, 1440, 2560), false);
        var landscape = MakeScreen(1.0, new PixelRect(offset + 1440, 480, 2496, 1404), markedPrimary);
        var screens = new[] { portrait, landscape };
        var oldQuery = ScreenList.QueryOverride;
        var oldDual = CoreSettings.Current.DualMonitorEnabled;
        var platform = (IWindowingPlatform)typeof(AvaloniaLocator).GetMethod("GetService")!
            .Invoke(Locator("Current"), new object[] { typeof(IWindowingPlatform) })!;
        var replacement = DispatchProxy.Create<IWindowingPlatform, BackendProbe>();
        var probe = (BackendProbe)(object)replacement;
        probe.Inner = platform;
        probe.Screens = screens;
        var opened = new List<(bool Typist, PixelRect Rect)>();
        probe.Mapped = w => opened.Add((!w.FindControl<TextBox>("TxtAnswer")!.IsReadOnly,
            new PixelRect(w.Position, new PixelSize((int)Math.Round(w.ClientSize.Width * w.RenderScaling),
                (int)Math.Round(w.ClientSize.Height * w.RenderScaling)))));
        ScreenList.QueryOverride = _ => screens;
        CoreSettings.Current.DualMonitorEnabled = dual;
        Bind(replacement);
        try
        {
            BubbleCountResultWindow.ShowOnAllMonitors(42, true, _ => throw new Exception("Unexpected completion"));
            Assert.Equal(dual ? 2 : 1, opened.Count);
            Assert.Equal((markedPrimary ? landscape : portrait).Bounds, opened.Single(w => w.Typist).Rect);
            if (dual) Assert.Equal((markedPrimary ? portrait : landscape).Bounds, opened.Single(w => !w.Typist).Rect);
        }
        finally
        {
            BubbleCountResultWindow.ForceCloseAll();
            Bind(platform);
            ScreenList.QueryOverride = oldQuery;
            CoreSettings.Current.DualMonitorEnabled = oldDual;
        }
    });

    // The 12.1 reference assembly hides the locator's platform registration API.
    private static object Locator(string name) => typeof(AvaloniaLocator).GetProperty(name)!.GetValue(null)!;
    private static void Bind(IWindowingPlatform platform)
    {
        var binding = typeof(AvaloniaLocator).GetMethod("Bind")!.MakeGenericMethod(typeof(IWindowingPlatform))
            .Invoke(Locator("CurrentMutable"), null)!;
        binding.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(IWindowingPlatform))
            .Invoke(binding, new object[] { platform });
    }

    private static Screen MakeScreen(double scaling, PixelRect bounds, bool primary)
    {
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(bounds.X + 1), "headless-test") })!;
        foreach (var (name, value) in new (string, object)[] { (nameof(Screen.Scaling), scaling),
            (nameof(Screen.Bounds), bounds), (nameof(Screen.WorkingArea), bounds), (nameof(Screen.IsPrimary), primary) })
            typeof(Screen).GetProperty(name)!.SetValue(screen, value);
        return screen;
    }

    // Forward the real headless backend; only emulate the monitor's DPI on Move. Snapshot at
    // platform Show, BEFORE Opened handlers can repair anything. Measure client pixels, not Width.
    public class BackendProbe : DispatchProxy
    {
        public object Inner = null!;
        public Screen[] Screens = null!;
        public Action<BubbleCountResultWindow> Mapped = null!;
        private bool _mapped;
        private IWindowImpl? _self;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "Move")
            {
                Assert.False(_mapped, "A mapped result must never be re-placed by the Opened fallback");
                var point = (PixelPoint)args![0]!;
                var scale = Screens.FirstOrDefault(s => point.X >= s.Bounds.X && point.X < s.Bounds.Right
                    && point.Y >= s.Bounds.Y && point.Y < s.Bounds.Bottom)?.Scaling ?? 1;
                Inner.GetType().GetInterface("IHeadlessWindow")!.GetMethod("SetRenderScaling")!.Invoke(Inner, new object[] { scale });
            }
            if (method.Name == "Show")
            {
                Mapped(BubbleCountResultWindow.OpenWindows.Single(w => ReferenceEquals(w.PlatformImpl, _self)));
                _mapped = true;
            }
            object? result;
            try { result = method.Invoke(Inner, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
            if (method.Name == "CreateWindow")
            {
                var window = DispatchProxy.Create<IWindowImpl, BackendProbe>();
                var child = (BackendProbe)(object)window;
                child.Inner = result!;
                child._self = window;
                child.Screens = Screens;
                child.Mapped = Mapped;
                return window;
            }
            return result;
        }
    }
}
