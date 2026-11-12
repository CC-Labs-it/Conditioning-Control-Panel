using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace CCP.Avalonia.Tests;

/// <summary>Isolate the real desk service's state and engine while a headless test summons her.
/// The service and bubble both retain the singleton engine, so preserve its fields in place.</summary>
internal sealed class EmiTestScope : IDisposable
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo Current = typeof(EmiState).GetField("_current", StaticPrivate)!;
    private static readonly FieldInfo Timer = typeof(EmiState).GetField("_saveTimer", StaticPrivate)!;
    private static readonly FieldInfo Context = typeof(EmiState).GetField("_saveCtx", StaticPrivate)!;
    private static readonly FieldInfo Dirty = typeof(EmiState).GetField("_dirty", StaticPrivate)!;
    private static readonly FieldInfo[] EngineFields = typeof(EmiLineEngine).GetFields(InstancePrivate)
        .Where(f => f.Name != "_gate").ToArray();
    private readonly EmiLineEngine _engine = EmiLineEngine.Instance;
    private readonly object _gate;
    private readonly object?[] _engineValues;
    private readonly object? _state;
    private readonly object? _context;
    private readonly string _path;
    private readonly byte[]? _file;

    internal EmiTestScope()
    {
        // Complete any earlier save before swapping the current state. A timer callback can already
        // have posted to the dispatcher even after its timer is disposed.
        DrainSaves();
        _state = Current.GetValue(null);
        _context = Context.GetValue(null);
        _path = EmiState.FilePath;
        _file = File.Exists(_path) ? File.ReadAllBytes(_path) : null;
        var fresh = (EmiLineEngine)Activator.CreateInstance(typeof(EmiLineEngine), nonPublic: true)!;
        _gate = typeof(EmiLineEngine).GetField("_gate", InstancePrivate)!.GetValue(_engine)!;
        lock (_gate)
        {
            _engineValues = EngineFields.Select(f => f.GetValue(_engine)).ToArray();
            foreach (var field in EngineFields) field.SetValue(_engine, field.GetValue(fresh));
        }
        Current.SetValue(null, new EmiState());
        Context.SetValue(null, null);
        Dirty.SetValue(null, false);
    }

    public void Dispose()
    {
        // Finish even callbacks that were already posted, while the test state is still installed.
        DrainSaves();
        lock (_gate)
            for (int i = 0; i < EngineFields.Length; i++) EngineFields[i].SetValue(_engine, _engineValues[i]);
        Current.SetValue(null, _state);
        Context.SetValue(null, _context);
        Dirty.SetValue(null, false);
        if (_file == null) { if (File.Exists(_path)) File.Delete(_path); }
        else File.WriteAllBytes(_path, _file);
    }

    private static void DrainSaves()
    {
        do
        {
            if (Timer.GetValue(null) is System.Threading.Timer timer)
            {
                Timer.SetValue(null, null);
                using var stopped = new ManualResetEvent(false);
                if (timer.Dispose(stopped) && !stopped.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("EMI save timer did not stop");
            }
            Dispatcher.UIThread.RunJobs();
        } while (Timer.GetValue(null) != null);
        if ((bool)Dirty.GetValue(null)!) EmiState.SaveNow();
    }
}
