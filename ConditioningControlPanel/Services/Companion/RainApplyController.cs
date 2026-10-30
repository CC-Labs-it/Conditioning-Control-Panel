using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// The host side of the dressing room's Apply (RAIN-V2-PLAN 2.3-2.5, lane R5). The page posts
/// <c>rain-apply {look}</c>; the host clamps it, names it (<see cref="RainLookHash"/>), and either
/// commits a pack it already has or asks the page to bake one (<c>rain-bake</c>), collecting
/// <c>rain-bake-frame</c> into a <see cref="RainPackSink"/> until <c>rain-bake-done {all:true}</c>.
/// Only then do <c>RainLook</c> + <c>RainPackHash</c> change: a cancel, an error, a closed window or
/// panic drops the temp pack and the previous look keeps playing.
///
/// <para>Frames arrive on the UI thread as base64 PNG; the decode and every disk write run on
/// <see cref="Hooks.Background"/> (one serial queue, so Begin, frames, strips and Commit stay in
/// order). The page renders its CURRENT look, so a look the clamp changed is never baked: it goes
/// back as <c>rain-applied {failed:true, reason:"clamped", look}</c> for the user to see and Apply.</para>
///
/// <para>Progress (fix wave F1c): every clip finished or skipped, plus the page's per-frame
/// <c>rain-bake-progress</c>, goes back as <c>rain-baking {done, total, shown, fraction}</c> (only when the
/// percent or the clip moves) and to <see cref="Hooks.Progress"/>; a commit or a drop sends it null.</para>
/// </summary>
internal sealed class RainApplyController
{
    public const int Fps = 15, Width = 192, Height = 309;
    /// <summary>One frame's base64 cap: a 192x309 RGBA PNG is at most ~240 KB raw, so 1 MB of text is a broken page.</summary>
    internal const int MaxPngChars = 1024 * 1024;
    /// <summary>A clip longer than a minute at 15 fps is a broken page, not a performance.</summary>
    internal const int MaxFramesPerClip = 900;

    internal sealed class Hooks
    {
        public required Action<object> Post { get; init; }
        public required Func<RainPackStore> Store { get; init; }
        public required Func<RainItemAccess> Access { get; init; }
        public required Func<IReadOnlyList<string>> Clips { get; init; }
        /// <summary>Writes RainLook + RainPackHash, saves, and has the tube re-enter emote mode. UI thread.</summary>
        public required Action<string, string> Commit { get; init; }
        /// <summary>Serial off-UI queue. Tests run it inline.</summary>
        public Action<Action> Background { get; init; } = a => a();
        /// <summary>Back to the UI thread. Tests run it inline.</summary>
        public Action<Action> Ui { get; init; } = a => a();
        public Action<string>? Log { get; init; }
        /// <summary>A bake that failed (not a cancel): the toast.</summary>
        public Action? OnFailed { get; init; }
        /// <summary>Bake progress on the UI thread (window title, taskbar); null once it commits or drops.</summary>
        public Action<RainBakeProgress?>? Progress { get; init; }
    }

    private sealed class Bake
    {
        public Bake(string hash, string look) { Hash = hash; Look = look; }
        public string Hash { get; }
        public string Look { get; }
        public volatile bool Dead;
        // Touched only on the background queue.
        public RainPackWriter? Writer;
        public RainPackSink? Sink;
        public readonly Dictionary<string, int> FrameCount = new(StringComparer.Ordinal);
        // Progress: touched only on the UI thread (Handle).
        public int Total;
        public HashSet<string> Clips = new(StringComparer.Ordinal);
        public double ClipFraction;
        public readonly HashSet<string> ClipsDone = new(StringComparer.Ordinal);
        public int LastPercent = -1, LastShown = -1;
        public RainBakeProgress Progress => new(ClipsDone.Count, Total, ClipFraction);
    }

    private readonly Hooks _h;
    private Bake? _bake;

    public RainApplyController(Hooks hooks) => _h = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public bool IsBaking => _bake != null;

    /// <summary>Routes one page frame. True when it was an Apply / bake frame this controller owns.</summary>
    public bool Handle(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "rain-apply": OnApply(o["look"]); return true;
            case "rain-bake-frame": OnFrame(o); return true;
            case "rain-bake-done": OnDone(o); return true;
            case "rain-bake-progress": OnProgress(o); return true;
            default: return false;
        }
    }

    private void OnApply(JToken? lookToken)
    {
        // The page allows one Apply at a time; a second one wins (its rain-bake cancels the old run page side).
        if (_bake != null) Drop("superseded", post: false);
        var raw = lookToken is JObject obj ? obj.ToString(Formatting.None) : "";
        var normalized = RainLookCodec.Normalize(raw);
        var clamped = RainLookClamp.Clamp(raw, _h.Access());
        if (!string.Equals(clamped, normalized, StringComparison.Ordinal))
        {
            _h.Log?.Invoke("apply refused: the look wore pieces the account cannot wear");
            _h.Post(new { type = "rain-applied", failed = true, reason = "clamped", look = LookToken(clamped) });
            return;
        }
        if (RainLookCodec.IsDefault(clamped))
        {
            _h.Commit("", "");
            _h.Post(new { type = "rain-applied", lookHash = "" });
            return;
        }
        var hash = RainLookHash.Compute(clamped);
        var store = _h.Store();
        if (store.TryGetFolder(hash) != null)
        {
            _h.Commit(clamped, hash);
            _h.Post(new { type = "rain-applied", lookHash = hash });
            return;
        }
        var bake = new Bake(hash, clamped);
        _bake = bake;
        _h.Background(() => Guard(bake, () =>
        {
            bake.Writer = store.Begin(hash);
            bake.Sink = new RainPackSink(bake.Writer, Fps, Width, Height);
        }));
        var clips = _h.Clips();
        bake.Clips = new HashSet<string>(clips, StringComparer.Ordinal);
        bake.Total = bake.Clips.Count;
        _h.Post(new { type = "rain-bake", lookHash = hash, clips, fps = Fps, w = Width, h = Height });
        Report(bake);
    }

    /// <summary>The page's per-frame <c>rain-bake-progress {clip, frame, of}</c>: how far into the clip in hand.</summary>
    private void OnProgress(JObject o)
    {
        var bake = Current(o);
        if (bake == null) return;
        var clip = (string?)o["clip"];
        if (!RainPackStore.IsValidClipName(clip) || !bake.Clips.Contains(clip!) || bake.ClipsDone.Contains(clip!)) return;
        if (o["frame"]?.Type != JTokenType.Integer || o["of"]?.Type != JTokenType.Integer) return;
        int frame = (int)o["frame"]!, of = (int)o["of"]!;
        if (of <= 0 || frame < 0) return;
        bake.ClipFraction = Math.Clamp((double)frame / of, 0, 1);
        Report(bake);
    }

    /// <summary>One clip is in (baked or skipped): counts once, so a repeated frame never overshoots.</summary>
    private void ClipDone(Bake bake, string clip)
    {
        if (!bake.Clips.Contains(clip) || !bake.ClipsDone.Add(clip)) return;
        bake.ClipFraction = 0;
        Report(bake);
    }

    /// <summary>Posts <c>rain-baking {done, total, shown, fraction}</c> to the page and raises the hook,
    /// only when the whole percent or the clip in hand moved (a bake posts every frame).</summary>
    private void Report(Bake bake)
    {
        var p = bake.Progress;
        if (p.Percent == bake.LastPercent && p.ClipShown == bake.LastShown) return;
        bake.LastPercent = p.Percent;
        bake.LastShown = p.ClipShown;
        _h.Post(new { type = "rain-baking", lookHash = bake.Hash, done = p.ClipsDone, total = p.ClipsTotal, shown = p.ClipShown, fraction = Math.Round(p.Fraction, 3) });
        _h.Progress?.Invoke(p);
    }

    private void OnFrame(JObject o)
    {
        var bake = Current(o);
        if (bake == null) return;
        var clip = (string?)o["clip"];
        var png = (string?)o["png"];
        int index = o["index"]?.Type == JTokenType.Integer ? (int)o["index"]! : -1;
        if (!RainPackStore.IsValidClipName(clip) || index < 0 || index >= MaxFramesPerClip
            || string.IsNullOrEmpty(png) || png!.Length > MaxPngChars)
        {
            Drop("bad frame", post: true, failed: true);
            return;
        }
        _h.Background(() => Guard(bake, () =>
        {
            bake.Sink!.AddFrame(clip!, index, Convert.FromBase64String(png));
        }));
    }

    private void OnDone(JObject o)
    {
        var bake = Current(o);
        if (bake == null) return;
        if (o["all"]?.Value<bool>() != true)
        {
            var clip = (string?)o["clip"];
            if (!RainPackStore.IsValidClipName(clip)) return;
            ClipDone(bake, clip!);
            if (o["skipped"]?.Value<bool>() == true) return;
            _h.Background(() => Guard(bake, () =>
            {
                if (bake.Sink!.PendingClips.Contains(clip!)) bake.Sink.FinishClip(clip!);
            }));
            return;
        }
        if (o["cancelled"]?.Value<bool>() == true) { Drop("cancelled", post: true); return; }
        if (o["error"] != null && o["error"]!.Type != JTokenType.Null)
        {
            Drop("page error: " + (string?)o["error"], post: true, failed: true);
            return;
        }
        _h.Background(() => Guard(bake, () =>
        {
            bake.Sink!.FinishAll();
            bake.Writer!.Commit(bake.Look);
            _h.Ui(() =>
            {
                if (!ReferenceEquals(_bake, bake) || bake.Dead) return;
                _bake = null;
                _h.Commit(bake.Look, bake.Hash);
                _h.Post(new { type = "rain-applied", lookHash = bake.Hash });
                _h.Progress?.Invoke(null);
            });
        }));
    }

    /// <summary>Window close or panic: drops the temp pack, posts nothing (the page is going away).</summary>
    public void Abort() { if (_bake != null) Drop("closed", post: false); }

    private Bake? Current(JObject o)
    {
        var bake = _bake;
        if (bake == null || bake.Dead) return null;
        return string.Equals((string?)o["lookHash"], bake.Hash, StringComparison.Ordinal) ? bake : null;
    }

    private void Drop(string why, bool post, bool failed = false)
    {
        var bake = _bake;
        if (bake == null) return;
        bake.Dead = true;
        _bake = null;
        _h.Log?.Invoke("bake dropped: " + why);
        _h.Progress?.Invoke(null);
        _h.Background(() => { try { bake.Writer?.Dispose(); } catch (Exception) { } });
        if (post)
        {
            _h.Post(new { type = "rain-bake-cancel" });
            _h.Post(new { type = "rain-applied", failed = true, reason = failed ? "error" : "cancelled" });
        }
        if (failed) _h.OnFailed?.Invoke();
    }

    /// <summary>Runs one queued step; a throw (bad PNG, disk) fails the bake back on the UI thread.</summary>
    private void Guard(Bake bake, Action step)
    {
        if (bake.Dead) return;
        try { step(); }
        catch (Exception ex)
        {
            _h.Log?.Invoke("bake step failed: " + ex.Message);
            _h.Ui(() => { if (ReferenceEquals(_bake, bake)) Drop("step failed", post: true, failed: true); });
        }
    }

    private static JToken LookToken(string normalized)
        => normalized.Length > 0 ? JToken.Parse(normalized) : new JObject { ["rev"] = RainLookCodec.Rev, ["items"] = new JObject(), ["colors"] = new JObject() };

    /// <summary>The page's <c>init</c> frame (RAIN-V2-PLAN 2.5 + lane R4): the applied look, the account's
    /// grants, level and achievements (the same sources as <see cref="RainItemAccess"/>), Apply mode on.
    /// Prices only when the Prize Counter has a cached list. <paramref name="strings"/> = the host's
    /// localised lines for the page's string table (<c>wardrobe-shop.js STR</c>); blanks are left out.</summary>
    internal static JObject BuildInit(string? storedLook, IEnumerable<string>? grants, int level,
        IEnumerable<string>? achievements, string? locale, string motion, bool gentle,
        IReadOnlyDictionary<string, int>? prices, IReadOnlyDictionary<string, string>? strings = null)
    {
        var look = RainLookCodec.Normalize(storedLook);
        var init = new JObject
        {
            ["type"] = "init",
            ["look"] = look.Length > 0 ? JToken.Parse(look) : JValue.CreateNull(),
            ["grants"] = new JArray((grants ?? Array.Empty<string>()).OrderBy(g => g, StringComparer.Ordinal)),
            ["level"] = level,
            ["achievements"] = new JArray((achievements ?? Array.Empty<string>()).ToArray()),
            ["locale"] = locale ?? "en",
            ["motion"] = motion,
            ["gentle"] = gentle,
            ["applyMode"] = true,
        };
        if (prices != null && prices.Count > 0) init["prices"] = JObject.FromObject(prices);
        var lines = new JObject();
        foreach (var kv in strings ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) lines[kv.Key] = kv.Value;
        if (lines.Count > 0) init["strings"] = lines;
        return init;
    }

    /// <summary>Every clip Rain's emotes.json names, first seen first (idle rotation, talk and
    /// expressive pools, click emotes, situations, moods): the clips a pack must cover.</summary>
    internal static IReadOnlyList<string> ClipsFromEmotes(string? emotesJson)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(emotesJson)) return list;
        JObject root;
        try { root = JObject.Parse(emotesJson); } catch (JsonException) { return list; }
        foreach (var section in new[] { "idleRotation", "talking", "expressive", "clickEmotes", "stemPrefix", "mood" })
            if (root[section] is JToken token) Collect(token, list);
        return list;
    }

    private static void Collect(JToken token, List<string> list)
    {
        switch (token)
        {
            case JObject obj:
                foreach (var p in obj.Properties())
                    if (p.Name != "_comment") Collect(p.Value, list);
                break;
            case JArray arr:
                foreach (var t in arr) Collect(t, list);
                break;
            case JValue v when v.Type == JTokenType.String:
                var s = (string?)v;
                if (RainPackStore.IsValidClipName(s) && !list.Contains(s!)) list.Add(s!);
                break;
        }
    }
}

/// <summary>One serial off-UI queue: steps run on the thread pool in the order queued.</summary>
internal sealed class RainSerialQueue
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public void Enqueue(Action step)
    {
        lock (_gate)
            _tail = _tail.ContinueWith(_ => step(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
