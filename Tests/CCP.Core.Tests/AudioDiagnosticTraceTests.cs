using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

[CollectionDefinition("Audio trace report", DisableParallelization = true)]
public sealed class AudioTraceReportCollection { }

[Collection("Audio trace report")]
public sealed class AudioDiagnosticTraceTests
{
    [Fact]
    public void TailKeepsNewestLinesAcrossRotation()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-audio-roll-").FullName;
        try
        {
            var path = Path.Combine(dir, "audio-diag.log");
            File.WriteAllText(path, new string('x', 1_000_000) + "\nold-line\n");
            var trace = new AudioDiagnosticTrace(path);
            trace.Note("first-new");
            trace.Note("last-new");
            var tail = trace.Tail(3);
            Assert.Contains("old-line", tail);
            Assert.Contains("first-new", tail);
            Assert.Contains("last-new", tail);
            Assert.DoesNotContain("old-line", trace.Tail(2));
            Assert.True(File.Exists(path + ".1"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TraceSurvivesLogFloodAndRelaunchAndIsAttachedOnlyToOptedInBugReports()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-audio-trace-").FullName;
        var oldAudio = BugReportService.AudioDiagnosticTailProvider;
        var oldLog = BugReportService.SessionLogFile;
        try
        {
            var path = Path.Combine(dir, "audio-diag.log");
            var trace = new AudioDiagnosticTrace(path);
            var sounds = new RecentSounds { TraceSink = trace.Note };
            sounds.Note("sound", "/private/clip.wav", false, false, DateTime.Now);
            Assert.Contains("engine off: sound started 'clip.wav'", trace.Tail(200));

            var generalLog = Path.Combine(dir, "app.log");
            File.WriteAllText(generalLog, string.Concat(System.Linq.Enumerable.Repeat("routine log activity\n", 2000)));
            var relaunchedTrace = new AudioDiagnosticTrace(path);
            BugReportService.AudioDiagnosticTailProvider = relaunchedTrace.Tail;
            BugReportService.SessionLogFile = () => generalLog;
            var service = new BugReportService();
            Assert.Empty(service.CreateDraft("", "", false).ScrubbedAppLog);
            Assert.Empty(service.CreateDraft("", "", true, ReportKind.Suggestion).ScrubbedAppLog);
            var report = service.CreateDraft("", "", true).ScrubbedAppLog;
            Assert.Contains("===== engine-off audio trace =====", report);
            Assert.Contains("engine off: sound started 'clip.wav'", report);
            Assert.DoesNotContain("/private/clip.wav", report);
        }
        finally
        {
            BugReportService.AudioDiagnosticTailProvider = oldAudio;
            BugReportService.SessionLogFile = oldLog;
            Directory.Delete(dir, true);
        }
    }
}
