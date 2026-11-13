using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeMenuThreadTests
{
    private const string Probe = "CCP_TEST_TUBE_WORKER_INIT";
    [Fact]
    public async Task WorkerInitializedTubeCanRenderItsMenuOnTheUiThread()
    {
        if (Environment.GetEnvironmentVariable(Probe) == "1")
        {
            // The isolation hook has started the UI dispatcher; initialize the tube on a worker.
            await Task.Run(() => RuntimeHelpers.RunClassConstructor(typeof(AvatarTubeWindow).TypeHandle));
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var tube = new AvatarTubeWindow(null);
                var menu = tube.FindControl<ContextMenu>("AvatarContextMenu")!;
                try
                {
                    tube.Show();
                    menu.Open(tube.FindControl<Border>("AvatarBorder") ?? (Control)tube);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(menu.IsOpen);
                    Assert.NotNull(tube.FindControl<MenuItem>("MenuItemEngine")!.Foreground);
                }
                finally
                {
                    menu.Close();
                    tube.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                return Task.CompletedTask;
            });
            return;
        }

        // A fresh process guarantees the worker initializes the type first, regardless of suite order.
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        info.ArgumentList.Add(typeof(TubeMenuThreadTests).Assembly.Location);
        info.ArgumentList.Add("-method");
        info.ArgumentList.Add("CCP.Avalonia.Tests.TubeMenuThreadTests.WorkerInitializedTubeCanRenderItsMenuOnTheUiThread");
        info.ArgumentList.Add("-noColor");
        info.Environment[Probe] = "1";
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("Total: 1, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0", output);
    }
}
