using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SheListeningWakeStatusTests
{
    [Fact]
    public void RefreshReportsModelChangesWithoutClaimingAnActiveEngine()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-wake-status-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            AvaloniaTestDispatcher.Run(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var shell = new MainShellWindow { WakeModelRoot = root };
                shell.Show();
                try
                {
                    shell.ShowTab("shelistening");
                    Dispatcher.UIThread.RunJobs();
                    var status = shell.SheListeningPage!.TxtSL_WakeEngineStatus;
                    Assert.Equal(Loc.Get("she_wake_unavailable_status"), status.Text);
                    foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" })
                        File.WriteAllText(Path.Combine(root, name), "");
                    shell.RefreshSheListeningTab();
                    Assert.Equal(Loc.Get("she_wake_model_found_unavailable"), status.Text);
                    File.Delete(Path.Combine(root, "keywords.txt"));
                    shell.RefreshSheListeningTab();
                    Assert.Equal(Loc.Get("she_wake_unavailable_status"), status.Text);
                }
                finally { shell.Close(); }
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
