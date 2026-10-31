using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;

Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(new ProbeLog()).CreateLogger();

// Isolated apphost running the production gate. No UI, real process selection and Kill().
if (args[0] is "marker" or "unknown")
{
    using var marker = args[0] == "marker" ? SingleInstance.RegisterProfileMarker(SingleInstance.SandboxSuffix()) : null;
    Console.WriteLine("READY");
    Console.ReadLine();
    return;
}
SingleInstance.ShowAckTimeoutMs = 500;
var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var instance = SingleInstance.Claim(SingleInstance.SandboxSuffix(),
    _ => args[0] == "wedged" ? never.Task : Task.CompletedTask);
if (instance == null) { Console.WriteLine("ACKED"); return; }
instance.EndStartupPhase();
Console.WriteLine("READY");
Console.ReadLine();

sealed class ProbeLog : Serilog.Core.ILogEventSink
{
    public void Emit(Serilog.Events.LogEvent message) => Console.Error.WriteLine(message.RenderMessage());
}
