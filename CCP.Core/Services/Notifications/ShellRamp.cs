using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>WPF Start/StopRampTimer and RampTimer_Tick, including its ramp_restore.json journal.</summary>
public sealed class ShellRamp
{
    private readonly Dictionary<string, int> _bases = new();
    private DateTime _start;
    private static readonly string[] Fields = { "FlashOpacity", "SpiralOpacity", "PinkFilterOpacity", "MasterVolume", "SubAudioVolume", "BrainDrainBlurStrength" };
    private static string Journal => Path.Combine(CorePaths.UserData, "ramp_restore.json");
    public void Start(AppSettings s, DateTime now)
    {
        if (!s.IntensityRampEnabled) return;
        _start = now;
        foreach (var name in Fields) _bases[name] = (int)typeof(AppSettings).GetProperty(name)!.GetValue(s)!;
        try
        {
            var tmp = Journal + ".tmp";
            File.WriteAllText(tmp, new JObject { ["at"] = DateTime.UtcNow.Ticks, ["fields"] = JObject.FromObject(_bases) }.ToString());
            File.Move(tmp, Journal, true);
        }
        catch (Exception ex) { Log.Warning(ex, "Could not write ramp restore journal"); }
    }
    public bool Tick(AppSettings s, DateTime now, bool sessionActive)
    {
        if (_bases.Count == 0) return false;
        var progress = Math.Min((now - _start).TotalMinutes / s.RampDurationMinutes, 1);
        var factor = RampMath.ResolveFactor(s, progress);
        int Value(string key, int cap) => (int)Math.Clamp(_bases[key] * factor, 0, cap);
        if (!sessionActive && s.RampLinkFlashOpacity) s.FlashOpacity = Value("FlashOpacity", 100);
        if (!sessionActive && s.RampLinkSpiralOpacity) s.SpiralOpacity = Value("SpiralOpacity", 100);
        if (!sessionActive && s.RampLinkPinkFilterOpacity) s.PinkFilterOpacity = Value("PinkFilterOpacity", 50);
        if (!sessionActive && s.RampLinkBrainDrain) s.BrainDrainBlurStrength = Value("BrainDrainBlurStrength", 100);
        if (s.RampLinkMasterAudio) s.MasterVolume = Value("MasterVolume", 100);
        if (s.RampLinkSubliminalAudio) s.SubAudioVolume = Value("SubAudioVolume", 100);
        return progress >= 1 && s.EndSessionOnRampComplete && !sessionActive;
    }
    public void Stop(AppSettings s)
    {
        foreach (var pair in _bases) typeof(AppSettings).GetProperty(pair.Key)!.SetValue(s, pair.Value);
        _bases.Clear();
        try { File.Delete(Journal); } catch (Exception ex) { Log.Debug(ex, "Could not clear ramp journal"); }
    }
    public static void HealOnLaunch(AppSettings s)
    {
        if (!File.Exists(Journal)) return;
        try
        {
            var fields = JObject.Parse(File.ReadAllText(Journal))["fields"];
            foreach (var name in Fields)
                if (fields?[name] is { } value)
                    try { typeof(AppSettings).GetProperty(name)!.SetValue(s, value.ToObject<int>()); } catch { /* WPF skips unreadable fields */ }
        }
        catch (Exception ex) { Log.Warning(ex, "Could not read ramp journal"); }
        finally { try { File.Delete(Journal); } catch { } }
    }
}
