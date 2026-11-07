using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Tours;
using Xunit;
namespace CCP.Avalonia.Tests;
public class TutorialFlashDemoTests
{
    // WPF Services/TutorialService.cs:927. A missing shell is a safe no-op.
    [Fact]
    public Task SeedInstallsFlashDemoAndPreservesOverrides() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var saved = TutorialHeadHooks.FlashDemo;
        var shell = TutorialHeadHooks.ShellProvider;
        try
        {
            TutorialHeadHooks.FlashDemo = null;
            TutorialHeadHooks.ShellProvider = () => null;
            TutorialHead.Seed();
            Assert.NotNull(TutorialHeadHooks.FlashDemo);
            TutorialHeadHooks.FlashDemo!();
            int calls = 0;
            System.Action demo = () => calls++;
            TutorialHeadHooks.FlashDemo = demo;
            TutorialHead.Seed();
            Assert.Same(demo, TutorialHeadHooks.FlashDemo);
            TutorialHeadHooks.FlashDemo!();
            Assert.Equal(1, calls);
            TutorialHead.Service.Start(TutorialType.ShortWalk);
            TutorialHead.Service.Next();
            Assert.Equal("sw-flash", TutorialHead.Service.CurrentStep!.Id);
            Assert.Equal(2, calls);
        }
        finally { TutorialHead.Service.Skip(); TutorialHeadHooks.FlashDemo = saved; TutorialHeadHooks.ShellProvider = shell; }
        return Task.CompletedTask;
    });
}
