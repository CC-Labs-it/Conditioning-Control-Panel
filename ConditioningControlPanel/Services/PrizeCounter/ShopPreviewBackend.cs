using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// THE LOCAL SHOP PREVIEW (lane I, BRIEF "The preview wire"). In a DEBUG build started with
/// <c>CCP_SHOP_PREVIEW=1</c> the Prize Counter's requests are answered here instead of by the server:
/// <c>shop-state</c>, <c>shop-buy</c> (with <c>coupon</c>), <c>shop-equip</c>, <c>shop-gift</c>,
/// <c>shop-stamp</c>, <c>shop-goal</c>. The state lives in <c>&lt;user data&gt;/shop-preview.json</c>
/// (made on first use); the real tabs come from the bundled sample of the server body.
///
/// <para>Walls: nothing here reaches the network, and the preview wallet is its own number. It is never
/// filled from the real balance and never written to it; the only thing it hears from the desktop is
/// the AMOUNT of a credit the desktop just banked (<see cref="Wire"/>). In a Release build
/// <see cref="Enabled"/> is false whatever the environment says, and every caller sits behind
/// <c>#if DEBUG</c> as well (PrizeCounterPreviewWallTests).</para>
/// </summary>
internal sealed class ShopPreviewBackend
{
    public const string EnvVar = "CCP_SHOP_PREVIEW";
    public const string FileName = "shop-preview.json";
    public const string OpEquip = "shop-equip", OpGift = "shop-gift", OpStamp = "shop-stamp", OpGoal = "shop-goal";
    internal const string SampleResource = "ShopPreview.state.sample.json";

    /// <summary>The switch for one value of the env var. Release: always off.</summary>
    internal static bool EnabledFor(string? env)
    {
#if DEBUG
        return env?.Trim() == "1";
#else
        return false;
#endif
    }

    private static readonly Lazy<bool> s_enabled = new(() =>
    {
        try { return EnabledFor(Environment.GetEnvironmentVariable(EnvVar)); } catch { return false; }
    });

    public static bool Enabled => s_enabled.Value;

    private static readonly Lazy<ShopPreviewBackend> s_shared = new(() =>
        new ShopPreviewBackend(Path.Combine(App.UserDataPath, FileName), LoadBundledSample(), null));

    /// <summary>The app's one preview. Only touched when <see cref="Enabled"/>.</summary>
    public static ShopPreviewBackend Shared => s_shared.Value;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly JObject? _sample;
    private readonly Func<DateTimeOffset> _clock;
    private ShopPreviewData? _data;

    /// <param name="path">The preview file.</param>
    /// <param name="sample">A real <c>/v2/shop/state</c> body whose tabs ride behind the effects tab.</param>
    /// <param name="clock">Null = the local clock. Tests pass a fixed one.</param>
    public ShopPreviewBackend(string path, JObject? sample, Func<DateTimeOffset>? clock)
    {
        _path = path;
        _sample = sample;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The bundled server body sample (embedded in DEBUG builds only). Null when absent.</summary>
    internal static JObject? LoadBundledSample()
    {
        try
        {
            using var s = typeof(ShopPreviewBackend).Assembly.GetManifestResourceStream(SampleResource);
            if (s == null) return null;
            using var r = new StreamReader(s);
            return JsonConvert.DeserializeObject(r.ReadToEnd()) as JObject;
        }
        catch (Exception ex) { App.Logger?.Debug("ShopPreview: sample unreadable: {E}", ex.Message); return null; }
    }

    /// <summary>The preview wallet.</summary>
    public int Sp { get { lock (_gate) return Data().Sp; } }

    /// <summary>The row worn in a slot (WornLooks' source). <c>mix</c> answers the Two-tone id while on.</summary>
    public string? Worn(string slot)
    {
        lock (_gate)
        {
            var d = Data();
            if (slot == Prizes.WornLooks.Mix) return d.Mix ? ShopPreviewRules.Twotone : null;
            return d.Equip.TryGetValue(slot, out var id) ? id : null;
        }
    }

    /// <summary>Whether the preview owns a row (the Studio look rows ask, Services/Prizes/LookWardrobe).</summary>
    public bool Owns(string itemId) { lock (_gate) return Data().Owned.ContainsKey(itemId); }

    /// <summary>The preview's worn profile frame and picture (null = none). Display only: the own avatar surfaces read
    /// it through Services/Profile/OwnProfileLook while the preview is on.</summary>
    public (string? Frame, string? Avatar) ProfileWorn()
    {
        lock (_gate)
        {
            var p = Data().Profile;
            return (p.TryGetValue(ShopPreviewRules.ProfileFrame, out var f) ? f : null, p.TryGetValue(ShopPreviewRules.ProfileAvatar, out var a) ? a : null);
        }
    }

    /// <summary>The counter's Wear on an owned profile row, in the preview file only. False = refused.</summary>
    public bool WearProfile(string? itemId)
    {
        bool ok;
        lock (_gate)
        {
            var d = Data();
            ok = ShopPreviewRules.WearProfile(d, itemId).Ok;
            if (ok) Save(d);
        }
        if (ok) ProfileChanged?.Invoke();
        return ok;
    }

    /// <summary>The Customize dialog's save in preview: frame and picture as a whole, in the preview file only.</summary>
    public void SetProfile(string? frameId, string? avatarId)
    {
        lock (_gate)
        {
            var d = Data();
            ShopPreviewRules.SetProfile(d, frameId, avatarId);
            Save(d);
        }
        ProfileChanged?.Invoke();
    }

    /// <summary>The preview's frame or picture changed (any thread).</summary>
    public event Action? ProfileChanged;

    /// <summary>One page request to one body. Never throws, never leaves this machine.</summary>
    public JObject Handle(string? op, JObject? body, string? idem)
    {
        try
        {
            string[] before;
            JObject reply;
            lock (_gate)
            {
                var d = Data();
                before = Snapshot(d);
                var now = _clock();
                string? Str(string key) => body?[key]?.Type == JTokenType.String ? (string?)body[key] : null;
                PreviewOutcome outcome = op switch
                {
                    PrizeCounterRelay.OpState => PreviewOutcome.Done,
                    PrizeCounterRelay.OpBuy => ShopPreviewRules.Buy(d, _sample, Str("itemId"), idem, body?["coupon"]?.Type == JTokenType.Boolean && (bool)body["coupon"]!, now),
                    OpEquip => ShopPreviewRules.Equip(d, Str("slot"), Str("itemId")),
                    OpGift => ShopPreviewRules.OpenGift(d, now),
                    OpStamp => ShopPreviewRules.StampToday(d, now),
                    OpGoal => ShopPreviewRules.SetGoal(d, _sample, Str("itemId")),
                    _ => PreviewOutcome.No("bad_input"),
                };
                if (!outcome.Ok)
                {
                    reply = PrizeCounterRelay.Refusal(outcome.Reason ?? "bad_input");
                    if (op == PrizeCounterRelay.OpBuy) reply["sp"] = d.Sp;
                    return reply;
                }
                if (op != PrizeCounterRelay.OpState) Save(d);
                reply = ShopPreviewRules.BuildState(d, _sample, now);
                if (outcome.Receipt is { } r)
                {
                    reply["idem"] = r.Idem;
                    reply["itemId"] = r.ItemId;
                    reply["paidSp"] = r.PaidSp;
                    reply["spBefore"] = r.SpBefore;
                }
            }
            RaiseChanges(before);
            // a bought look: the Studio look rows unlock it at once (LookWardrobe listens to its slot)
            if (op == PrizeCounterRelay.OpBuy && ShopPreviewRules.Effect(body?["itemId"]?.Type == JTokenType.String ? (string?)body["itemId"] : null) is { } bought)
                Prizes.WornLooks.Raise(bought.Slot);
            return reply;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ShopPreview: {Op} failed: {E}", op ?? "(none)", ex.Message);
            return PrizeCounterRelay.Refusal("busy");
        }
    }

    /// <summary>A gain the desktop banked, mirrored into the preview wallet and its ledger.</summary>
    public void Credit(int amount, string code)
    {
        if (amount <= 0) return;
        lock (_gate)
        {
            var d = Data();
            ShopPreviewRules.Credit(d, amount, code, _clock());
            Save(d);
        }
    }

    /// <summary>The ledger code for a desktop credit (the page turns it into words).</summary>
    internal static string CreditCode(SparklePointSource source) => source switch
    {
        SparklePointSource.LevelUp => ShopPreviewRules.CodeLevel,
        SparklePointSource.BubbleMilestone => ShopPreviewRules.CodePops,
        _ => ShopPreviewRules.CodePlay,
    };

    /// <summary>Startup (App, DEBUG only): when the preview is on, WornLooks reads its equip and every
    /// credit the desktop banks also lands in the preview wallet. Off: does nothing.</summary>
    public static void Wire()
    {
        if (!Enabled) return;
        Prizes.WornLooks.Source = slot => Shared.Worn(slot);
        // The own avatar surfaces (profile hero, account chip, friends chip and drawer) show the preview's frame.
#if DEBUG
        OwnProfileLook.Preview = new PreviewProfileLook(Shared);
#endif
        SparklePointRewards.Awarded += (_, award) => Shared.Credit(award.Amount, CreditCode(award.Source));
        App.Logger?.Information("ShopPreview: ON, answering the Prize Counter from {File}", FileName);
    }

    private static readonly string[] WatchedSlots = { "bubble", "card", "behind", "fog", "fog2", "word", "mix" };

    private static string[] Snapshot(ShopPreviewData d)
    {
        var s = new string[WatchedSlots.Length];
        for (int i = 0; i < s.Length; i++)
            s[i] = WatchedSlots[i] == Prizes.WornLooks.Mix ? (d.Mix ? "on" : "") : d.Equip.TryGetValue(WatchedSlots[i], out var v) ? v : "";
        return s;
    }

    private void RaiseChanges(string[] before)
    {
        string[] after;
        lock (_gate) after = Snapshot(Data());
        for (int i = 0; i < after.Length; i++)
            if (!string.Equals(before[i], after[i], StringComparison.Ordinal)) Prizes.WornLooks.Raise(WatchedSlots[i]);
    }

    private ShopPreviewData Data()
    {
        if (_data != null) return _data;
        try
        {
            if (File.Exists(_path) && JsonConvert.DeserializeObject<ShopPreviewData>(File.ReadAllText(_path)) is { V: 1 } read)
            {
                read.Owned ??= new(StringComparer.Ordinal);
                read.Equip ??= new(StringComparer.Ordinal);
                ShopPreviewRules.Heal(read, _sample);
                return _data = read;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("ShopPreview: {File} unreadable, starting a fresh one: {E}", FileName, ex.Message); }
        _data = ShopPreviewRules.Seed(_clock(), _sample);
        Save(_data);
        return _data;
    }

    private void Save(ShopPreviewData d)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(d, Formatting.Indented));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) { App.Logger?.Warning("ShopPreview: save failed: {E}", ex.Message); }
    }
}
