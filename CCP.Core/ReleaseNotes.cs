namespace ConditioningControlPanel
{
    /// <summary>Canonical release notes for every head. Update with Version.props at release.</summary>
    public static class ReleaseNotes
    {
        public const string Current =
            "v7.3.8 - Back to the basics\n" +
            "\n" +
            "NEW IN 7.3.8\n" +
            "\n" +
            "FIXES\n" +
            "- the update installs again on PCs with Smart App Control: every part of the installer and the uninstaller is signed now.\n" +
            "- Mandatory videos: a preset made before 7.3.7, or a built-in one, no longer switches floating videos back to full screen.\n" +
            "- presets made before 7.3.1, and the built-in ones, load with the V2 effects off, the way they were saved.\n" +
            "- takeover and AI web videos go fullscreen on their own again when you are in another window.\n" +
            "- lock cards you type into take the keyboard as they show, even while another app is in front. no click first.\n" +
            "- your own AI (Ollama or another endpoint) gets longer replies, so they no longer get cut off.\n" +
            "- during Lockdown the Games pill on Play says the launcher waits, instead of offering a pop out that opens nothing.\n" +
            "- the BambiCloud menu keeps its top line in a smaller window.\n" +
            "\n" +
            "NEW\n" +
            "- the dashboard browser has + and - zoom buttons. holding Ctrl and scrolling zooms too.\n" +
            "- Just Drop in the app: Haptic Waves, Heartbeat and Breath Pacer move your toy, Flash Gallery flashes your own pictures, and with your media source on Local the feed plays your own videos.\n" +
            "\n" +
            "FROM 7.3.7\n" +
            "\n" +
            "HOTFIX\n" +
            "- presets: a preset now keeps its Steam and filter colour, fog, floating video, flash motion and V2 effect options, so loading one no longer carries the last look over. presets saved before 7.3.7 put those back to their defaults: load yours, set them again, save.\n" +
            "- Lockdown: 40 and 50 minute lengths.\n" +
            "- quests: hovering a quest door shows where it goes, and it clicks when it opens.\n" +
            "- fixed a freeze that could hit when the volume moved while a video played.\n" +
            "\n" +
            "FROM 7.3.6\n" +
            "\n" +
            "FIX DAY\n" +
            "- Sparkles: the top bar matches the shop now. after a big run it can dip for a moment, then climbs back as your Sparkles are booked.\n" +
            "- Afterglow: the words on the line keep their distance as they glow and sink.\n" +
            "- Lights Down: a new Follow eyes or mouse switch. on, it dims only while you watch the video, by your eyes when the camera is on, else by the mouse on it. off, it stays as it is.\n" +
            "- Steam: the clear spot around a resting cursor fades into the fog instead of ending at a hard edge.\n" +
            "- notifications that pile up while CCP starts come one after another, not all at once.\n" +
            "- leash stickers are bigger, and you can read them.\n" +
            "- Dares: the (?) on the deadline says how a bet settles. on its own, early once the target is met, otherwise at the deadline. give it two minutes.\n" +
            "\n" +
            "FROM 7.3.5\n" +
            "\n" +
            "DARES, ROUND TWO\n" +
            "- you can only stake Sparkles you have. amounts you can't cover are greyed out, and a friend who can't cover their side is told when they accept.\n" +
            "- the camera check passes at 60% of the time now, not 80%. a camera bet shows how much of the time the camera saw you, and says so when it can't see you.\n" +
            "- friends: the card under a friend has a new look, and hovering a friend says what a right-click holds.\n" +
            "- in our Discord, /sparkles shows your Sparkles and the stars EMI gave you.\n" +
            "\n" +
            "DECK\n" +
            "- cards no longer stall side-on mid-flip: each card gets its next picture ready while it rests.\n" +
            "\n" +
            "FROM 7.3.4\n" +
            "\n" +
            "DARES\n" +
            "bet a friend on your own CCP use. right-click a friend and pick Dare, or type /bet make in our Discord.\n" +
            "\n" +
            "- the bet is one sentence you fill in one blank at a time: pictures seen, minutes, video time, a session by difficulty, bubbles popped, or a dare of your own. \"you can't do it\", or a duel where the bigger count wins.\n" +
            "- stakes: Sparkles, a dare (you type its words in Discord with /bet write, the app shows it as a numbered sealed dare), lock minutes on your own Chaster tab, or a Lockdown. the Lockdown stake is for subscribers.\n" +
            "- your own app counts what you do. a camera check is there if you both want it.\n" +
            "- the loser gets one card: pay, renounce (no dares for 7 days, and your friend is told) or close CCP. nothing ever starts on its own. a debt left unpaid for 7 days counts as renounced.\n" +
            "\n" +
            "FROM YOUR 7.3.3 ROUND\n" +
            "- a new switch, Moving effects pop bubbles and flash images, in the Scrawl and Whirlpool boxes. it is off: the bouncing word and the pool still push and pull things, they just stop popping and breaking them on their own. switch it on for the 7.3.3 behaviour.\n" +
            "- Whirlpool with Stay centered pulls and swirls bubbles but never eats them. it was setting off Hydra cascades.\n" +
            "- Whirlpool: Drags in has a Words switch. off, the pool leaves the bouncing word alone.\n" +
            "- Steam: Reach scales the whole fog now, so 10% is a thin line at the edges. Strength under 10% really thins it.\n" +
            "- the companion no longer slips a stray CALLBACK or a bare number into what it says.\n" +
            "- the camera also stays off when you stop it from the title bar pill or Settings > Devices.\n" +
            "- AI-started takeover videos on Hypnotube go fullscreen again.\n" +
            "- chess: an Arrows on / off button top right of the board. off, a right drag turns the board instead of drawing.\n" +
            "\n" +
            "FROM 7.3.3\n" +
            "\n" +
            "a round for those who told us the panel slows the effects down, and for Scrawl with big text.\n" +
            "\n" +
            "- the panel goes still while the engine runs: its glows, sparkles and moving banners rest, so your effects get the frames. it wakes up when you stop. switch Auto performance off to keep it moving.\n" +
            "- Motion (Full, Reduced, Off) is the first row of Settings > Performance now, and it reaches every animation in the panel the moment you change it.\n" +
            "- Scrawl: with Corner flash off the gold CORNER word stays away, and big text no longer counts almost every bounce as a corner.\n" +
            "- Scrawl: bigger text holds fewer stamps, fewer again with Second text on. a new Stamp size slider sets the stamps apart from the text.\n" +
            "- Whirlpool: a new Stay centered switch. the pool sits in the middle of the screen at full size and stops following the mouse.\n" +
            "- V2 subliminals use your Subliminal font, and \"Ignore CCP's own windows\" skips them.\n" +
            "- the camera stays off after you stop it. it used to come back at every launch with Focus Gaze on.\n" +
            "\n" +
            "FROM 7.3.2\n" +
            "\n" +
            "a small round after your first night with 7.3.1.\n" +
            "\n" +
            "- bubble skins under Circe's Lock: a skin you wear shows on a plain bubble now, with no lock drawn over it. with no skin worn, the lock bubbles stay as they were.\n" +
            "- the picture that washes over the screen when a glitch bubble pops is sized for each monitor, not stretched across all of them.\n" +
            "- takeover web videos: two more cases where fullscreen was lost are fixed. one was a video that starts late, the other a video started while someone holds your remote.\n" +
            "- the shop shows Emi as the example face inside profile frames.\n" +
            "\n" +
            "FROM 7.3.1\n" +
            "\n" +
            "7.3.0 gave the basics a V2. this one gives them a wardrobe, and fixes what you told us about the V2s.\n" +
            "\n" +
            "THE SHOP\n" +
            "the Prize Counter is rebuilt. it is a sheet that drops down from your wallet now, not a room you walk through. look for the gold SHOP tag on the launcher, the panel wallet, Studio and EMI's desk.\n" +
            "\n" +
            "- tabs with their own emblem: Featured, Karts, Chess, Bubbles and effects, Accessories, Prime only, Back Room, Owned.\n" +
            "- every item has a picture, a short \"what it does\", and a try-on before you buy.\n" +
            "- after you buy or wear something, the shop tells you where it shows and takes you there.\n" +
            "- Sparkles are one gold star everywhere, and every credit flies to your wallet.\n" +
            "- a weekly gift, a stamp card for your first two weeks, and a \"saving for\" bar you can pin to one item.\n" +
            "\n" +
            "LOOKS FOR YOUR EFFECTS\n" +
            "- Bubbles: seven skins. Soap, Marble, Heart, Champagne, Spiral, Disco, Frost. each has its own movement inside, its own pop and its own sound. the old pop is still there as a switch.\n" +
            "- Deck cards: backs in Ink, Check and Lace, and Whisper, which puts a spiral with words coming at you on the back of every flip.\n" +
            "- Subliminals: word styles. Typed, Chalk, Drift, Echo, Marquee.\n" +
            "- Pink Filter: Two-tone, a second colour blended into the first.\n" +
            "- what you own is picked in the effect's own options, next to everything else.\n" +
            "\n" +
            "CHESS AND KARTS\n" +
            "- Piece by Piece: five piece finishes (Candy lacquer, Mineral strata, Liquid ribbons, Oil slick, Gold faultlines), four boards (Midnight Observatory, Glasshouse, Velvet Theatre, After Hours Arcade), and Emi as a cheerleader at your table.\n" +
            "- chess and kart items sit on their own shelves now, easy to tell apart.\n" +
            "\n" +
            "PROFILE\n" +
            "- profile frames are redrawn and they move: Lace, Ink Stamp, Gilt, Polaroid.\n" +
            "- your own picture wears the frame everywhere you see it.\n" +
            "\n" +
            "V2, AFTER YOUR FEEDBACK\n" +
            "- every settings page has a plain V2 on/off switch, and presets remember which V2s are on.\n" +
            "- the old Back Room \"Flashes v2\" and \"Bubbles v2\" are called Extra Flashes and Extra Bubbles now. the Super effects are the V2.\n" +
            "- Deck: the card takes the shape of the picture, so tall pictures no longer sit in an empty frame. the flip is quicker and steadier.\n" +
            "- Deck keeps its cards when you own Shatter.\n" +
            "- Steam: a pause between fog phrases, and a Reach slider for how far in the fog comes, from the edges only to the centre.\n" +
            "- Two-tone moved into Steam's gold box, with a colour of your own next to the palette.\n" +
            "- the V2 subliminals and the words in the fog wear your word style.\n" +
            "- Subliminals: with the V2 on, the normal cards rest. \"Also show the normal subliminals\" runs both.\n" +
            "- Lights Down is reworked. fullscreen: a soft dim around the screen that pulls back when you move the mouse or look away. floating videos: the whole screen dims from the edges and the videos stay lit, with a glow in your mod's colour.\n" +
            "- the attention target does not run with floating videos. the page says so now and switches it off.\n" +
            "- fog and the Whirlpool are much lighter on the processor.\n" +
            "- the V2 star and three other icons no longer show as empty boxes.\n" +
            "\n" +
            "STUDIO\n" +
            "- option pages are split into named sections (Basics and so on) that fold, and each one says how many things you changed.\n" +
            "\n" +
            "FIXES\n" +
            "- fixed a \"Collection was modified\" crash while popping bubbles with Froth on.\n" +
            "- a Games pill on Play opens the launcher's Games page.\n" +
            "- takeover web videos go fullscreen again.\n" +
            "\n" +
            "FROM 7.3.0\n" +
            "\n" +
            "most of you spend most of your time on the basics: flashes, bubbles, the spiral, subliminals. it has been almost a year since those arrived, so a V2 was owed. this one is their revamp.\n" +
            "\n" +
            "SUPER EFFECTS\n" +
            "eight of the basics get a second version. look for the gold corner on a dashboard tile: click it and the V2 runs. each one has its own gold box of options in the feature's panel, and a \"See V2\" button in its help card shows what it does before you turn it on.\n" +
            "\n" +
            "- Deck (Flash Images): tap a flash and it flips. keep going and it cracks, then breaks, and a break knocks its neighbours.\n" +
            "- Froth (Bubbles): bubbles merge, carry both pictures inside, and turn gold when ripe. pop one and the pop can chain. a merged bubble pays the sum of its parts.\n" +
            "- Whirlpool (Spiral): a spiral under your cursor that grows while you rest and shrinks when you move. it drags pictures, bubbles and fog to the eye and pops them there.\n" +
            "- Steam (Pink Filter): pink fog drifts in from the edges. wipe it with the mouse and it creeps back, with words in it.\n" +
            "- Afterglow (Subliminals): words pop by the mouse and leave ghosts that build a line.\n" +
            "- Scrawl (Bouncing Text): the bouncing word stamps itself on the screen and the stamps stay a while.\n" +
            "- Lights Down (Mandatory Videos): the room dims around the video.\n" +
            "- Undertow (Brain Drain): a clear lens in the blur that follows you and shrinks.\n" +
            "\n" +
            "they react to each other: the Whirlpool swallows flashes and bubbles, flings the bouncing word back out, and stirs the fog into a spiral.\n" +
            "\n" +
            "Super effects come with Basic and up. without a plan the corner and the options stay visible, with a lock. Gentle visuals calms them, and panic stops all of them.\n" +
            "\n" +
            "A QUIETER PANEL\n" +
            "- the launcher is three doors now: the Panel, Games and Tools. each door opens its own page, and Continue takes you back to the last thing you played.\n" +
            "- the games moved out of Play and into the Games door. Play keeps Eyes, Sessions and Deeper. Just Drop and the Deeper editor moved to the launcher too.\n" +
            "- Studio: every row shows on, off or selected at a glance, each group shows how many are on, and the page wears one colour.\n" +
            "- the tab strip is quieter: only the tab you are on is filled, and there is one help button instead of one per tab.\n" +
            "- Start has its own bar, and the header banner shows on Home only.\n" +
            "\n" +
            "WE TAKE BITCOIN NOW\n" +
            "for those of you who are extra careful about privacy, or just prefer it: you can pay with Bitcoin. it buys a pass for Basic or Prime, paid once, for the months you pick. it never renews and nothing gets billed again. you get a code, and Settings > Account > \"Redeem a code\" turns it on.\n" +
            "\n" +
            "the card that compares Basic and Prime is redone as two tickets, and the Bitcoin pass is linked right on it.\n" +
            "\n" +
            "NEW\n" +
            "- Mandatory videos: \"Fill the minimum with other videos\", off by default. on, a clip shorter than the minimum is followed by other clips until the minimum has played.\n" +
            "- Mod Creator: searching a skill by name shows its effect and flavour texts with it.\n" +
            "- Piece by Piece: a castling hint on the board, and you can castle by dropping the king on the rook. the board stays readable under the spiral, and a Board motion switch calms it.\n" +
            "\n" +
            "FIXES\n" +
            "- a local AI host typed without http:// no longer stops the panel from opening.\n" +
            "- the companion no longer giggles about once a minute with the engine off.\n" +
            "- cutting your own leash is never blocked by an open \"Let go\" question.\n" +
            "- Deeper: fixed a crash while a library file was being read.\n" +
            "- Piece by Piece: a press that does not move is a click, the lit square is the one a click plays, and an arrow no longer stays stuck after a lost right release.\n" +
            "\n" +
            "this is a pre-release. it carries everything in 7.3.7, 7.3.6, 7.3.5, 7.3.4, 7.3.3, 7.3.2, 7.3.1, 7.3.0 and 7.2.4.";
    }
}
