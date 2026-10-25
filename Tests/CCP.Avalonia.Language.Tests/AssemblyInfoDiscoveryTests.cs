using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Language.Tests;

/// <summary>
/// xUnit's VSTest adapter discovers this suite by launching its exe with <c>-assemblyInfo</c> and parsing the
/// whole stdout as one JSON object. On loaded Windows runners the teardown outlived xUnit's 1 s shutdown
/// notice, the notice landed on stdout and discovery failed (rgate run 40165552123, "did not return valid
/// JSON (non-object)"). The child here holds a foreground thread until that notice was written, so the slow
/// teardown happens every run, and stdout must still be exactly the JSON object.
/// </summary>
public sealed class AssemblyInfoDiscoveryTests
{
    [Fact]
    public void AssemblyInfoLaunchPrintsOnlyTheJsonEvenWhenTeardownOutlivesXunitsShutdownNotice()
    {
        // Launch exactly like xUnit's OutOfProcessTestProcessLauncherBase: the apphost next to the dll, the
        // arguments in a response file passed as `@@ <file>`, stdin and stdout redirected.
        var dll = typeof(AssemblyInfoDiscoveryTests).Assembly.Location;
        var apphost = dll[..^".dll".Length] + (OperatingSystem.IsWindows() ? ".exe" : "");
        var responseFile = Path.GetTempFileName();
        File.WriteAllLines(responseFile, new[] { "-assemblyInfo" });
        try
        {
            var psi = new ProcessStartInfo(apphost, $"@@ \"{responseFile}\"")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment[AssemblyInfoStdoutGuard.HoldVariable] = "1";

            using var child = Process.Start(psi)!;
            var stderr = child.StandardError.ReadToEndAsync();
            var stdout = child.StandardOutput.ReadToEnd();
            Assert.True(child.WaitForExit(60_000), "the -assemblyInfo child did not exit");

            // The replayed race really happened: xUnit wrote its late line while the hold kept the process alive.
            Assert.Contains(AssemblyInfoStdoutGuard.LateLineMarker, stderr.Result);
            // xUnit's TestProcessLauncherAdapter.GetAssemblyInfo: trim, then the whole text must be one object.
            var text = stdout.Trim(' ', '\r', '\n');
            Assert.True(text.StartsWith('{') && text.EndsWith('}'), "stdout: " + text.Replace("\u001b", "<ESC>"));
            using var json = JsonDocument.Parse(text);
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            Assert.StartsWith("xUnit.net v3", json.RootElement.GetProperty("test-framework").GetString());
            Assert.Equal(0, child.ExitCode);
        }
        finally
        {
            File.Delete(responseFile);
        }
    }
}
