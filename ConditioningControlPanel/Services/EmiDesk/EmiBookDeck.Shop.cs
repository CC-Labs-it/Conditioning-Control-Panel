namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: the 7.2 pair, the Prize Counter and Rain. Rules as every batch
/// (<see cref="EmiBookCards"/>): four bullets at most, the catch on its own strip, key words in
/// <c>*asterisks*</c>, every claim checked against the code.
///
/// <para><b>THE COUNTER.</b> Sparkles come from games by finishing place (Sparkles by place, B1);
/// the stock is per tab and per ISO week, the same for everybody, and an owned row is always shown
/// stamped (CCP-Server <c>proxy/shop.js</c> header). The doors are the wallet chips on the launcher
/// and the panel. The catch is the money rule: no refunds, no gifts, and a Prime row needs Prime to
/// BUY but stays usable after (owner, 2026-10-30).</para>
///
/// <para><b>RAIN.</b> The V1 | V2 pill under the tube is one global choice, played on the mods that
/// carry a v2 row (Bambi, Sissy, CCP Default). Her 3D files install once (the pinned release asset,
/// about 250 MB); Apply bakes her clips on this PC before the tube changes (RainApplyController).
/// Choker, ribbon and collar are level unlocks in items.json; the rest of the paid pieces sell at
/// the counter.</para>
/// </summary>
internal static class EmiBookDeckShop
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        new EmiBookCard(
            "prize-counter", 2, "emi_book_prize_counter",
            "THE PRIZE COUNTER",
            "a 3D *counter* that trades *Sparkles* for looks.",
            new[] { "games pay *Sparkles* by your finishing place",
                   "looks for *{companion}*, the *kart*, *chess*, your *profile* and my desk",
                   "the *stock* changes every week. what you own stays *stamped*",
                   "open it from the *wallet* chip on the launcher or the panel" },
            "no refunds, no gifts. Prime items need Prime to buy, and stay yours after.",
            null, null,
            "i'd buy the top hat. for me, obviously.", "(◕‿◕)"),

        new EmiBookCard(
            "rain", 2, "emi_book_rain",
            "V2 AVATAR",
            "a second *avatar* for the tube, in 3D, and you dress her.",
            new[] { "the *V1 | V2* pill under the tube switches to her",
                   "her files install once, about *250 MB*",
                   "dress her in the *wardrobe*, then press *Apply*",
                   "*levels* unlock the choker, the ribbon and the collar" },
            "Bambi, Sissy and CCP Default only. Apply renders her clips on this PC first.",
            null, null,
            "she gets a wardrobe. i get a desk. fine.", "(｡♥‿♥｡)"),
    };
}
