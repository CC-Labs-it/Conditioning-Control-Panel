using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Runs a bundled page's own ES module in node, so a test can ask the page whether it accepts what the host sends.</summary>
internal static class PageContract
{
    /// <summary>The origin every hosted page is served from (WebAssetServer.Url); the port is arbitrary.</summary>
    internal const string Origin = "http://127.0.0.1:5173";

    /// <summary>Imports Assets/web/<paramref name="module"/> with <c>location</c> set to the hosted
    /// <paramref name="page"/>, then returns the JSON of <c>(<paramref name="call"/>)(module, arg)</c> (may be async).
    /// Skips when node is not on PATH.</summary>
    internal static JToken Run(string page, string module, string call, JToken arg)
    {
        var file = new System.Uri(Path.Combine(ConditioningControlPanel.Tests.SourceRoots.RepoRoot, "Assets", "web", module)).AbsoluteUri;
        var js = $"globalThis.location = new URL({JsonConvert.ToString(Origin + "/" + page + "?ccp_t=t")}); globalThis.window ??= globalThis;"
            + $"let s = ''; process.stdin.on('data', d => s += d).on('end', async () => {{ const m = await import({JsonConvert.ToString(file)});"
            + $" process.stdout.write(JSON.stringify(await ({call})(m, JSON.parse(s)) ?? null)); }});";
        var psi = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "--input-type=module", "-e", js }) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi)!; }
        catch (Win32Exception) { Assert.Skip("node is not on PATH: the page-contract checks need it"); throw; }
        using (p)
        {
            p.StandardInput.Write(arg.ToString(Formatting.None)); p.StandardInput.Close();
            var err = p.StandardError.ReadToEndAsync(); var output = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(30_000), $"node timed out on {module}");
            Assert.True(p.ExitCode == 0, $"node failed on {module}: {err.Result}");
            return JToken.Parse(output);
        }
    }
}
