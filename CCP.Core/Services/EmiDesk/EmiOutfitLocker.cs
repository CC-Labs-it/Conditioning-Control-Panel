using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>The persisted Locker pick, gated by the persisted wallet.</summary>
public sealed class EmiOutfitLocker
{
    private readonly object _gate = new();
    private string? _path;
    private long _stamp = -1;
    private string? _value;

    public string? Read(string path)
    {
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(path);
                long stamp = info.Exists ? info.LastWriteTimeUtc.Ticks ^ info.Length : 0;
                if (_path == path && _stamp == stamp) return _value;
                _path = path; _stamp = stamp; _value = null;
                if (!info.Exists) return null;
                var blob = JObject.Parse(File.ReadAllText(path));
                var name = EmiChains.OutfitName((string?)blob["lockerOutfit"]);
                var sku = name switch
                {
                    "varsity" => "emi_varsity", "labcoat" => "emi_labcoat",
                    "cheer" => "emi_cheer", "swim" => "emi_swim", _ => null
                };
                if (sku != null && blob["wallet"]?["inv"]?[sku] is JObject row && (int?)row["n"] > 0)
                    _value = name;
                return _value;
            }
            catch { _value = null; return null; }
        }
    }
}
