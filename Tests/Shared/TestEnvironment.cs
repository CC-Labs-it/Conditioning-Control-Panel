using System;
using System.Globalization;
using System.IO;

namespace CCP.Testing;

/// <summary>Pins CurrentCulture and CurrentUICulture for one test and puts both back on Dispose.
/// For tests that read user-facing text formatted with the current culture: the expected strings
/// are written with a dot, so an it-IT or de-DE machine would otherwise see "1,10x" and fail.</summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name = "en-US")
    {
        var pinned = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = pinned;
        CultureInfo.CurrentUICulture = pinned;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}

/// <summary>Windows only lets an elevated process or Developer Mode create symlinks, so a test that
/// needs one skips with the reason instead of failing on the IOException. Probed once per run.</summary>
public static class Symlinks
{
    private static readonly Lazy<bool> Available = new(Probe);

    public static void RequireOrSkip()
    {
        if (!Available.Value)
            Xunit.Assert.Skip("This process cannot create symbolic links (Windows without Developer Mode or elevation)");
    }

    private static bool Probe()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-symlink-probe-").FullName;
        try
        {
            var target = Path.Combine(dir, "target");
            File.WriteAllText(target, "");
            File.CreateSymbolicLink(Path.Combine(dir, "link"), target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
