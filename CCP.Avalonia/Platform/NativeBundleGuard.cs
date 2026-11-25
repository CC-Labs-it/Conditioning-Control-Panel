using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/Update/NativeBundleGuard.cs:78-137. Runs before Avalonia or Skia opens a window.
internal static class NativeBundleGuard
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

    internal static bool VerifyOrRepair()
    {
        try
        {
            Exception? failure = null;
            try { using var paint = new SkiaSharp.SKPaint(); paint.IsAntialias = true; }
            catch (DllNotFoundException ex) { failure = ex; }
            catch (BadImageFormatException ex) { failure = ex; }
            catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException or BadImageFormatException) { failure = ex; }
#pragma warning disable IL3000
            var root = NativeBundleRepair.ExtractionRoot(Assembly.GetEntryAssembly()?.Location, Environment.ProcessPath,
                Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"), Path.GetTempPath());
#pragma warning restore IL3000
            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            return VerifyOrRepairCore(failure, root, Environment.ProcessPath, CorePaths.UserData, version, LaunchHelper, ShowGiveUp);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Native bundle guard failed; continuing startup");
            return true;
        }
    }

    internal static bool VerifyOrRepairCore(Exception? failure, string? root, string? exe, string userData,
        string version, Func<string, string, bool> launch, Action<string?, Exception> giveUp)
    {
        if (failure is null)
        {
            NativeBundleRepair.ClearStamp(userData);
            return true;
        }
        Serilog.Log.Error(failure, "Bundled graphics libraries could not load");
        if (root is not null && !string.IsNullOrEmpty(exe) &&
            !NativeBundleRepair.RepairedRecently(userData, version, DateTime.UtcNow))
        {
            NativeBundleRepair.WriteStamp(userData, version, DateTime.UtcNow);
            if (launch(root, exe)) return false;
        }
        try { giveUp(root, failure); }
        catch (Exception ex) { Serilog.Log.Error(ex, "Could not show native repair instructions"); }
        return false;
    }

    private static bool LaunchHelper(string root, string exe)
    {
        try
        {
            var helperDir = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel_Repair");
            Directory.CreateDirectory(helperDir);
            var log = Path.Combine(CorePaths.UserData, "logs", "native-repair.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            var helper = Path.Combine(helperDir, "native-repair.cmd");
            File.WriteAllText(helper, NativeBundleRepair.BuildRepairHelperScript(root, exe, log, Environment.ProcessId), new UTF8Encoding(false));
            return Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{helper}\"")
                { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = false }) != null;
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "Could not launch native repair helper"); return false; }
    }

    private static void ShowGiveUp(string? root, Exception failure)
    {
        var where = root ?? Path.Combine(Path.GetTempPath(), ".net", Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "ConditioningControlPanel");
        MessageBox(IntPtr.Zero,
            "Conditioning Control Panel could not load its graphics libraries, so it can't start.\n\n" +
            "Close the app, delete this temporary folder, then launch again:\n\n" + where +
            "\n\nIt is rebuilt automatically. If that does not help, check whether antivirus quarantined the app's files.\n\nDetails: " + failure.Message,
            "Conditioning Control Panel - startup failed", 0x10);
    }
}
