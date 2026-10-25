using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.RemoteControl
{
    /// <summary>The wearer's own settings a controller can write during a Remote Control session.</summary>
    public enum RemoteHeld { StrictLock, Spiral, Pink, SpiralOpacity, PinkOpacity, BrainDrain }

    /// <summary>What the strict lock hand-back writes: the live switch and, while a Lockdown runs, the
    /// value Lockdown recorded as the wearer's own (it puts that one back when it ends). Null = leave.</summary>
    public readonly record struct StrictHandBack(bool? Live, bool? LockdownRecorded);

    /// <summary>
    /// The wearer's own values under a Remote Control session (owner, 2026-10-22). The controller's
    /// show_* / set_* / strict lock / Melt commands write into the auto-saved settings; the end of the
    /// session used to write Spiral and Pink OFF (and strict lock off) whatever the wearer had, and a
    /// crash kept the controller's values for good.
    /// <para>
    /// Taken when the session starts, kept on disk (<see cref="FileName"/>) while it runs. Each
    /// controller write records the value it replaced as the wearer's own when the controller had not
    /// written that field yet, or when the value moved since its last write (the wearer, a session or
    /// Lockdown changed it). At the end a field goes back to the wearer's own value only while it still
    /// holds what the controller wrote: anything the wearer changed afterwards stays theirs. Pure.
    /// </para>
    /// </summary>
    public sealed class RemoteHoldings
    {
        public const string FileName = "remote_restore.json";

        public sealed class Slot
        {
            public int Own { get; set; }
            public bool Written { get; set; }
            public int Last { get; set; }
        }

        public static readonly RemoteHeld[] All = (RemoteHeld[])Enum.GetValues(typeof(RemoteHeld));

        [JsonProperty("at")] public long At { get; set; }
        [JsonProperty("slots")] public Dictionary<RemoteHeld, Slot> Slots { get; set; } = new();

        /// <summary>Session start: the wearer's values as they are now.</summary>
        public static RemoteHoldings Take(Func<RemoteHeld, int> own, long atTicks)
        {
            var h = new RemoteHoldings { At = atTicks };
            foreach (var f in All) h.Slots[f] = new Slot { Own = own(f) };
            return h;
        }

        /// <summary>The controller wrote <paramref name="after"/> over <paramref name="before"/>.</summary>
        public void NoteWrite(RemoteHeld field, int before, int after)
        {
            if (!Slots.TryGetValue(field, out var s)) Slots[field] = s = new Slot { Own = before };
            if (!s.Written || before != s.Last) s.Own = before;
            s.Written = true;
            s.Last = after;
        }

        /// <summary>True when the controller wrote this field and has not handed it back yet.</summary>
        public bool Holds(RemoteHeld field) => Slots.TryGetValue(field, out var s) && s.Written;

        /// <summary>Hands one field back: the value to write, or null to leave it (never written by the
        /// controller, already the wearer's, or changed since the controller's last write).</summary>
        public int? HandBack(RemoteHeld field, int current)
        {
            if (!Slots.TryGetValue(field, out var s) || !s.Written) return null;
            s.Written = false;
            return current == s.Last && current != s.Own ? s.Own : null;
        }

        /// <summary>
        /// The strict lock hand-back. While a Lockdown runs it owns the live switch: a strict lock it
        /// forced stays on, and when the controller's value is what Lockdown recorded as the wearer's
        /// own (the controller switched it on before the Lockdown started), that record goes back to
        /// the wearer's real value so Lockdown's own end restores the right one.
        /// </summary>
        public StrictHandBack HandBackStrict(bool current, bool lockdownActive, bool lockdownForcedStrict, bool lockdownRecorded)
        {
            if (!Slots.TryGetValue(RemoteHeld.StrictLock, out var s) || !s.Written) return default;
            s.Written = false;
            bool own = s.Own != 0, last = s.Last != 0;
            if (!lockdownActive) return new StrictHandBack(current == last && current != own ? own : null, null);
            bool? recorded = lockdownRecorded == last && lockdownRecorded != own ? own : null;
            bool? live = !lockdownForcedStrict && current == last && current != own ? own : null;
            return new StrictHandBack(live, recorded);
        }

        /// <summary>The remote end puts the panic key back on, except while a running Lockdown holds it
        /// off: Lockdown's own end restores it (owner, 2026-10-22).</summary>
        public static bool TurnsPanicBackOn(bool panicOn, bool remoteDisabledPanic, bool lockdownActive, bool lockdownDisabledPanic)
            => (!panicOn || remoteDisabledPanic) && !(lockdownActive && lockdownDisabledPanic);

        /// <summary>A remote disable_strict_lock never turns off a strict lock a running Lockdown forced.</summary>
        public static bool RefusesStrictOff(bool lockdownActive, bool lockdownForcedStrict)
            => lockdownActive && lockdownForcedStrict;

        /// <summary>The strict value a controller write replaces: while a Lockdown runs, the one it
        /// recorded as the wearer's own (the live switch is Lockdown's).</summary>
        public static int StrictBefore(bool current, bool lockdownActive, bool lockdownRecorded)
            => (lockdownActive ? lockdownRecorded : current) ? 1 : 0;

        // ---- settings mapping ----------------------------------------------------------------------

        public static int Read(AppSettings s, RemoteHeld f) => f switch
        {
            RemoteHeld.StrictLock => s.StrictLockEnabled ? 1 : 0,
            RemoteHeld.Spiral => s.SpiralEnabled ? 1 : 0,
            RemoteHeld.Pink => s.PinkFilterEnabled ? 1 : 0,
            RemoteHeld.SpiralOpacity => s.SpiralOpacity,
            RemoteHeld.PinkOpacity => s.PinkFilterOpacity,
            RemoteHeld.BrainDrain => s.BrainDrainEnabled ? 1 : 0,
            _ => 0,
        };

        public static void Write(AppSettings s, RemoteHeld f, int v)
        {
            switch (f)
            {
                case RemoteHeld.StrictLock: s.StrictLockEnabled = v != 0; break;
                case RemoteHeld.Spiral: s.SpiralEnabled = v != 0; break;
                case RemoteHeld.Pink: s.PinkFilterEnabled = v != 0; break;
                case RemoteHeld.SpiralOpacity: s.SpiralOpacity = v; break;
                case RemoteHeld.PinkOpacity: s.PinkFilterOpacity = v; break;
                case RemoteHeld.BrainDrain: s.BrainDrainEnabled = v != 0; break;
            }
        }

        /// <summary>Hands back every field except the strict lock (that one asks Lockdown first).
        /// Returns how many it wrote.</summary>
        public int HandBackAllButStrict(AppSettings s)
        {
            var n = 0;
            foreach (var f in All)
            {
                if (f == RemoteHeld.StrictLock) continue;
                if (HandBack(f, Read(s, f)) is int v) { Write(s, f, v); n++; }
            }
            return n;
        }

        // ---- the recovery file ---------------------------------------------------------------------

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        public static RemoteHoldings? FromJson(string json) => JsonConvert.DeserializeObject<RemoteHoldings>(json);

        public static void Save(string dir, RemoteHoldings h)
        {
            try
            {
                var path = Path.Combine(dir, FileName);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, h.ToJson());
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] Could not write {File}", FileName); }
        }

        public static void Clear(string dir)
        {
            try { File.Delete(Path.Combine(dir, FileName)); }
            catch (Exception ex) { Serilog.Log.Debug("[RemoteControl] Could not delete {File}: {E}", FileName, ex.Message); }
        }

        /// <summary>Launch, after Lockdown's own recovery: a file left behind means the app died with a
        /// controller's values in the settings. Hands every field back by the same rule. Returns how many
        /// fields it wrote; the file is deleted either way.</summary>
        public static int HealOnLaunch(string dir, AppSettings? current)
        {
            var path = Path.Combine(dir, FileName);
            if (current == null || !File.Exists(path)) return 0;
            try
            {
                var h = FromJson(File.ReadAllText(path));
                if (h == null) return 0;
                var n = h.HandBackAllButStrict(current);
                if (h.HandBack(RemoteHeld.StrictLock, Read(current, RemoteHeld.StrictLock)) is int v)
                {
                    Write(current, RemoteHeld.StrictLock, v);
                    n++;
                }
                Serilog.Log.Information("[RemoteControl] The last remote session did not end cleanly; handed back {Count} of the wearer's settings", n);
                return n;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[RemoteControl] {File} could not be read; left as is", FileName);
                return 0;
            }
            finally { Clear(dir); }
        }
    }
}
