using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Services;
using Serilog;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class BugReportAudioDiagnosticsTests
{
    [Theory]
    [InlineData(true), InlineData(false)]
    public void DraftUsesAvaloniaLogAndKeepsSampledEngineOffAudio(bool rollover)
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData); var dir = Path.Combine(CorePaths.UserData, "logs");
        Directory.CreateDirectory(dir); var id = Guid.NewGuid().ToString("N");
        var av = Path.Combine(dir, "ccp-avalonia-99999999_" + id + ".log"); var wpf = Path.Combine(dir, "session-99999999_" + id + ".log");
        var saved = (Log.Logger, BugReportService.AppLogFileSelector, BugReportService.SessionLogFile,
            BugReportService.DiagnosticTailProvider, BugReportService.FlightRecorderDump,
            BugReportService.NewestFlightRecorderDump, CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider);
        var file = new Program.ReportLogFile();
        var logger = new LoggerConfiguration().WriteTo.File(av, fileSizeLimitBytes: rollover ? 10000 : 1000000,
            rollOnFileSizeLimit: true, hooks: file).CreateLogger();
        try
        {
            File.WriteAllText(wpf, "stale-wpf-marker"); Log.Logger = logger;
            CoreSession.IsEngineRunningProvider = CoreSession.IsSessionRunningProvider = () => false;
            (BugReportService.SessionLogFile, BugReportService.DiagnosticTailProvider,
                BugReportService.FlightRecorderDump, BugReportService.NewestFlightRecorderDump) = (null, null, null, null);
            Program.ConfigureBugReportLogging(file); Log.Information("{Padding}", new string('x', rollover ? 11000 : 1000001));
            var first = file.CurrentFile; Log.Information("next file");
            Assert.NotNull(first); Assert.NotEqual(first, file.CurrentFile);
            CoreAudioDiagnostics.Note("report-" + id, "C:clip.wav");
            if (rollover) { Log.Information("{Padding}", new string('x', 11000)); Log.Information("after rollover"); }
            else for (var i = 0; i < 2100; i++) Log.Information("x");
            var service = new BugReportService(); var draft = service.CreateDraft("", "", includeAppLog: true);
            Assert.Contains("[VideoDiag] AUDIO", draft.ScrubbedAppLog); Assert.Contains($"engine off: report-{id} started 'clip.wav'", draft.ScrubbedAppLog);
            Assert.DoesNotContain("stale-wpf-marker", draft.ScrubbedAppLog); BugReportService.SessionLogFile = () => wpf;
            Assert.Equal(file.CurrentFile, BugReportService.PickLogFile(dir)); Program.ConfigureBugReportLogging(new Program.ReportLogFile());
            Assert.Null(BugReportService.PickLogFile(dir)); Assert.DoesNotContain("stale-wpf-marker", service.CreateDraft("", "", true).ScrubbedAppLog);
            BugReportService.AppLogFileSelector = null; Assert.Equal(wpf, BugReportService.PickLogFile(dir));
            Assert.Empty(service.CreateDraft("", "", includeAppLog: false).ScrubbedAppLog);
            Assert.Empty(service.CreateDraft("", "", includeAppLog: true, ReportKind.Suggestion).ScrubbedAppLog);
        }
        finally
        {
            (Log.Logger, BugReportService.AppLogFileSelector, BugReportService.SessionLogFile,
                BugReportService.DiagnosticTailProvider, BugReportService.FlightRecorderDump,
                BugReportService.NewestFlightRecorderDump, CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider) = saved;
            logger.Dispose();
            foreach (var path in Directory.EnumerateFiles(dir, "ccp-avalonia-99999999_" + id + "*.log")) File.Delete(path);
            File.Delete(wpf);
        }
    }
    [Fact]
    public void ReportKeepsAudioAcrossProcesses()
    {
        var shared = Environment.GetEnvironmentVariable("CCP_AUDIO_RESTART_TEST"); var mode = Environment.GetEnvironmentVariable("CCP_AUDIO_RESTART_MODE");
        if (shared != null && mode != null)
        {
            var dir = Path.Combine(CorePaths.UserData, "logs"); Directory.CreateDirectory(dir);
            if (mode == "read") foreach (var path in Directory.GetFiles(shared)) File.Copy(path, Path.Combine(dir, Path.GetFileName(path)));
            var file = new Program.ReportLogFile();
            using (var logger = new LoggerConfiguration().WriteTo.File(Path.Combine(dir, "ccp-avalonia-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10000, rollOnFileSizeLimit: true, hooks: file).CreateLogger())
            {
                Log.Logger = logger; Program.ConfigureBugReportLogging(file);
                if (mode == "write")
                {
                    CoreSession.IsEngineRunningProvider = CoreSession.IsSessionRunningProvider = () => false;
                    for (var i = 0; i < 205; i++) CoreAudioDiagnostics.Note("bounded-" + i, new string('x', 3000));
                    Assert.Equal(200, File.ReadAllLines(Path.Combine(dir, "audio-diag.log")).Length); CoreAudioDiagnostics.Note("restart-probe", "/private/restart.wav");
                    Log.Information("{Padding}", new string('x', 11000));
                    for (var i = 0; i < 2100; i++) Log.Information("ordinary {Number}", i);
                }
                else Assert.Contains("restart-probe started 'restart.wav'", new BugReportService().CreateDraft("", "", true).ScrubbedAppLog);
            }
            if (mode == "write") foreach (var path in Directory.GetFiles(dir)) File.Copy(path, Path.Combine(shared, Path.GetFileName(path)));
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "ccp-audio-restart-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            foreach (var step in new[] { "write", "read" })
            {
                var host = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
                var start = new System.Diagnostics.ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(typeof(BugReportAudioDiagnosticsTests).Assembly.Location);
                start.ArgumentList.Add("-method"); start.ArgumentList.Add(typeof(BugReportAudioDiagnosticsTests).FullName + ".ReportKeepsAudioAcrossProcesses");
                start.Environment["CCP_AUDIO_RESTART_TEST"] = folder; start.Environment["CCP_AUDIO_RESTART_MODE"] = step;
                using var process = System.Diagnostics.Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(true); Assert.Fail(step + " process timed out"); }
                Assert.True(process.ExitCode == 0, step + ": " + stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
