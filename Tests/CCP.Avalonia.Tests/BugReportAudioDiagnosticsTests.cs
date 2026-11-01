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
    [Fact]
    public void DraftUsesAvaloniaLogAndKeepsSampledEngineOffAudio()
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var dir = Path.Combine(CorePaths.UserData, "logs");
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString("N");
        var av = Path.Combine(dir, "ccp-avalonia-99999999_" + id + ".log");
        var wpf = Path.Combine(dir, "session-99999999_" + id + ".log");
        var saved = (Log.Logger, BugReportService.AppLogFileSelector, BugReportService.SessionLogFile,
            BugReportService.DiagnosticTailProvider, BugReportService.FlightRecorderDump,
            BugReportService.NewestFlightRecorderDump, CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider);
        var file = new Program.ReportLogFile();
        var logger = new LoggerConfiguration().WriteTo.File(av, fileSizeLimitBytes: 10000,
            rollOnFileSizeLimit: true, hooks: file).CreateLogger();
        try
        {
            File.WriteAllText(wpf, "stale-wpf-marker");
            Log.Logger = logger;
            CoreSession.IsEngineRunningProvider = CoreSession.IsSessionRunningProvider = () => false;
            (BugReportService.SessionLogFile, BugReportService.DiagnosticTailProvider,
                BugReportService.FlightRecorderDump, BugReportService.NewestFlightRecorderDump) = (null, null, null, null);
            Program.ConfigureBugReportLogging(file);
            Log.Information("{Padding}", new string('x', 11000));
            var first = file.CurrentFile;
            Log.Information("next file");
            Assert.NotNull(first);
            Assert.NotEqual(first, file.CurrentFile);
            CoreAudioDiagnostics.Note("report-" + id, "C:clip.wav");
            for (var i = 0; i < 120; i++) Log.Information("normal event {Number}", i);
            var service = new BugReportService();
            var draft = service.CreateDraft("", "", includeAppLog: true);
            Assert.Contains("[VideoDiag] AUDIO", draft.ScrubbedAppLog);
            Assert.Contains("started 'clip.wav'", draft.ScrubbedAppLog);
            Assert.DoesNotContain("stale-wpf-marker", draft.ScrubbedAppLog);
            BugReportService.SessionLogFile = () => wpf;
            Assert.Equal(file.CurrentFile, BugReportService.PickLogFile(dir));
            Program.ConfigureBugReportLogging(new Program.ReportLogFile());
            Assert.Null(BugReportService.PickLogFile(dir));
            Assert.DoesNotContain("stale-wpf-marker", service.CreateDraft("", "", true).ScrubbedAppLog);
            BugReportService.AppLogFileSelector = null;
            Assert.Equal(wpf, BugReportService.PickLogFile(dir));
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
}
