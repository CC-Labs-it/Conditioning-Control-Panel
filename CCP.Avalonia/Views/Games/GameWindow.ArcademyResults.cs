// WPF ArcademyHostService.cs:3735-3918, 4148-4177, 4250-4273.
using System;
using System.Globalization;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Sparkles.Claims;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private string _arcademyGame = "", _arcademyDay = "", _arcademyLever = "standard";
    private bool _arcademyEndless;
    private DateTime _arcademyStartedUtc; // the Sparkles claim's class length (WPF ArcademyHostService.cs:85)

    private static string ArcademyText(JToken? value) => value?.Type == JTokenType.String ? ((string?)value ?? "").Trim() : "";
    private static string ArcademyDay(JToken? value)
    {
        string day = ArcademyText(value);
        return DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? day : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    private static string ArcademyDate() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string ArcademyKey(JToken? value) { var key = ArcademyText(value); return key.Length > 64 ? key[..64] : key; }

    private void ArcademyClassStarted(JObject frame)
    {
        _arcademyStartedUtc = DateTime.UtcNow;
        _arcademyGame = ArcademyKey(frame["gameKey"]);
        _arcademyDay = ArcademyDay(frame["dayUtc"]);
        _arcademyEndless = frame["endless"]?.Type == JTokenType.Boolean && (bool)frame["endless"]!;
        var unlocks = _arcademyMeta?.LeverUnlocks() ?? (false, false);
        _arcademyLever = ArcademyEconomy.ClampLever(ArcademyText(frame["lever"]), unlocks.Item1, unlocks.Item2);
    }

    private void ArcademyClassLeft()
    {
        // WPF ArcademyOneTryRule.cs:21-35: leaving a timed Daily Trigger spends its XP day.
        if (!_arcademyEndless && _arcademyGame == "daily_trigger")
            _arcademyMeta?.TryClaimXpDay(_arcademyGame, _arcademyDay);
        _arcademyGame = "";
        _arcademyLever = "standard";
    }

    private void ArcademyPunch(string key, string reason, ArcademyPunchCards.PunchMint? mint)
    {
        if (mint is not { } m) return;
        Post(new { type = "punchcard-result", gameKey = key, reason, minted = m.Punches,
            justUnlocked = m.JustUnlocked, holes = ArcademyPunchCards.Holes, card = m.Card });
        if (m.Minted) ArcademySyncService.NotifyMutation();
    }

    private void ArcademyEnroll(JObject frame)
    {
        var key = ArcademyKey(frame["gameKey"]);
        if (key.Length == 0 || _arcademyMeta == null) return;
        var mint = _arcademyMeta.EnrollPunchCard(key, ArcademyDate());
        if (mint is { Minted: true }) Post(_arcademyMeta.SnapshotMessage());
        ArcademyPunch(key, "enrollment", mint);
    }

    private void ArcademyWalletResult(string sku, bool ok, string? reason)
    {
        var wallet = ArcademyEconomy.EnsureShape(_arcademyMeta?.WalletSnapshot());
        Post(new { type = "wallet-result", ok, sku, reason, wallet = ArcademyEconomy.BalanceJson(wallet),
            inv = ArcademyEconomy.InvJson(wallet), unlocks = ArcademyEconomy.UnlocksJson(wallet) });
    }

    private void ArcademyBuy(JObject frame)
    {
        var sku = ArcademyKey(frame["sku"]);
        if (_arcademyMeta == null) { ArcademyWalletResult(sku, false, "unknown"); return; }
        if (ArcademyWalletSyncService.DoorOpen)
        {
            ArcademyWalletSyncService.Buy(sku, result => Dispatcher.UIThread.Post(() =>
            {
                if (_closing || _arcademyMeta == null) return;
                if (result.Ok) Post(_arcademyMeta.SnapshotMessage());
                ArcademyWalletResult(sku, result.Ok, result.Answered ? result.Reason : "offline");
            }));
            return;
        }
        var purchase = _arcademyMeta.Buy(sku, ArcademyDate());
        if (purchase.Ok) Post(_arcademyMeta.SnapshotMessage());
        ArcademyWalletResult(sku, purchase.Ok, purchase.Reason);
    }

    // WPF TillFromEconomy (ArcademyHostService.cs:4052-4064) and SettleMint's server late slip (:4023).
    internal static (int Tickets, int Base, double Mult, int Payday, bool Token, string Lever, bool Slip, JObject Wallet)
        ArcademyTill(JObject economy, string lever, bool slip)
    {
        double mult = (double?)economy["ticketMult"] ?? 1;
        var serverLever = (string?)economy["lever"];
        return (Math.Max(0, (int?)economy["tickets"] ?? 0), Math.Max(0, (int?)economy["ticketBase"] ?? 0), mult > 0 ? mult : 1,
            Math.Max(1, (int?)economy["payday"]?["mult"] ?? 1), (bool?)economy["tokenMinted"] ?? false,
            string.IsNullOrWhiteSpace(serverLever) ? lever : serverLever, (bool?)economy["lateSlipUsed"] ?? slip,
            ArcademyEconomy.EnsureShape(economy["wallet"] as JObject));
    }

    private void ArcademyClassEnded(JObject frame)
    {
        var store = _arcademyMeta;
        if (store == null) return;
        var key = ArcademyKey(frame["gameKey"]);
        if (key.Length == 0) return;
        var lever = key == _arcademyGame ? _arcademyLever : "standard";
        _arcademyGame = ""; _arcademyLever = "standard";
        int tier;
        try
        {
            var t = frame["gradeTier"];
            var raw = t is JValue { Type: JTokenType.Integer or JTokenType.Float } number
                ? Convert.ToInt32(number.Value, CultureInfo.InvariantCulture)
                : int.TryParse(ArcademyText(t), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
            tier = Math.Clamp(raw, 1, 4);
        }
        catch { tier = 1; }
        bool zen = frame["zen"]?.Type == JTokenType.Boolean && (bool)frame["zen"]!;
        string grade = ArcademyText(frame["grade"]).ToUpperInvariant();
        if (zen || grade == "PASS") grade = "pass";
        if (grade == "S+" && lever != "honors") grade = "S";
        double multiplier = grade switch { "S+" => 1.6, "S" => 1.5, "A" => 1.25, "B" or "pass" => 1, _ => .6 };
        if (multiplier == .6) grade = "C";
        // WPF ArcademyHostService.cs:3770-3778: every graded class claims; no class-started means no length.
        var length = _arcademyStartedUtc == default ? TimeSpan.Zero : DateTime.UtcNow - _arcademyStartedUtc;
        _arcademyStartedUtc = default;
        DeliverSparkles(SparklesGameEnd.Claim(Platform.SparklesHead.Service, "arcademy", "solo",
            SparklesResult.ForMedal(SparklesGameEnd.ArcademyMedal(grade)), length, new JObject { ["grade"] = grade }));
        var token = frame["flavorXp"];
        double flavor;
        try
        {
            flavor = token is JValue { Type: JTokenType.Integer or JTokenType.Float } number
                ? Convert.ToDouble(number.Value, CultureInfo.InvariantCulture)
                : double.TryParse(ArcademyText(token), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }
        catch { flavor = 0; }
        if (!double.IsFinite(flavor)) flavor = 0;
        string day = ArcademyDay(frame["dayUtc"]), local = ArcademyDate();
        bool first = store.TryClaimXpDay(key, day);
        double xp = first ? new[] { 40d, 60d, 85d, 110d }[tier - 1] * multiplier + Math.Clamp(flavor, 0, 15) : 0;
        int before = CoreSettings.Current.PlayerLevel;
        if (xp > 0) CoreProgression.AddXP(xp, "Other");
        var dare = ArcademyText(frame["dareWon"]);
        bool darePaid = first && dare is "S" or "streak" or "fast";
        if (darePaid) CoreProgression.AddXP(15, "Quest");
        bool levelUp = CoreSettings.Current.PlayerLevel > before;
        var (streak, perfect, classes, slips) = store.RecordAttendance(local, key);
        var punch = store.StampPunchCard(key, local, ArcademyEconomy.IsTokenGrade(grade, false));
        ArcademyPresenceService.NoteClassEnd(key, grade);   // WPF ArcademyHostService.cs:3926, the host's clamped grade
        Post(store.SnapshotMessage());
        ArcademyPunch(key, "daily", punch);

        void Payout(JObject? economy)
        {
            try
            {
                int tickets, ticketBase, payday; double mult; bool tokenMinted, slip = slips > 0; JObject wallet; string paidLever = lever;
                if (economy != null)
                    (tickets, ticketBase, mult, payday, tokenMinted, paidLever, slip, wallet) = ArcademyTill(economy, lever, slip);
                else
                {
                    store.TryUnlockExtraCredit(grade);
                    var prior = store.NoteWalletPlay(local, key);
                    payday = ArcademyEconomy.PaydayMultFor(ArcademyEconomy.PickPayday(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), store.EnrolledGameKeys()), key);
                    var sum = ArcademyEconomy.ComputeTickets(grade, prior, streak, payday, lever);
                    tickets = sum.Tickets; ticketBase = sum.Base; mult = sum.Mult;
                    store.EarnTickets(tickets);
                    tokenMinted = ArcademyEconomy.IsTokenGrade(grade, zen) && store.TryClaimTokenDay(local);
                    wallet = store.WalletSnapshot();
                }
                Post(store.SnapshotMessage());
                Post(new { type = "payout-result", gameKey = key, xp, levelUp, streak, perfectAttendance = perfect,
                    classesToday = classes, retake = !first, dareXp = darePaid ? 15 : 0, grade,
                    tickets, ticketBase, ticketMult = mult, tokenMinted, payday, lever = paidLever, lateSlipUsed = slip,
                    wallet = ArcademyEconomy.BalanceJson(wallet) });
            }
            catch (Exception ex) { Log.Warning("[Arcademy] payout: {E}", ex.Message); }
        }
        if (!ArcademyWalletSyncService.DoorOpen) { Payout(null); return; }
        var request = ArcademyWalletSyncService.BuildMintFrame(key, grade, zen, streak, local, lever, slips, day);
        ArcademyWalletSyncService.Bank(request, outcome => Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _arcademyMeta != store) return;
            if (outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Queue) ArcademyWalletSyncService.Park(request);
            Payout(outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Banked ? outcome.Economy : null);
        }));
    }
}
