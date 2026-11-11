using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using Xunit;

[assembly: AssemblyFixture(typeof(CCP.Avalonia.Tests.TestApplication))]

namespace CCP.Avalonia.Tests;

// AppBuilder setup is process-wide: no test may win the race with a bare builder
// that omits the bundled fonts. Use the desktop font setup before any UI test.
public sealed class TestApplication
{
    public TestApplication() => AvaloniaTestDispatcher.Run(RenderProof.EnsureSetUp);
}
