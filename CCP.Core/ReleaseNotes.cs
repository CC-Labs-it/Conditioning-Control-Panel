namespace ConditioningControlPanel
{
    /// <summary>Canonical release notes for every head. Update with Version.props at release.</summary>
    public static class ReleaseNotes
    {
        public const string Current =
            "v7.1.15 - Settle Down\n\nNEW IN 7.1.15\n\nGAMES\n- Breakout: the pause card has a Menu button.\n" +
            "\nSETTINGS STAY YOURS\n- a session that crashes or gets closed mid-run no longer leaves its values behind. ducking, flash and pink opacity, Hydra and subliminal length go back to yours at the next launch.\n" +
            "- a panic, an exit, the AI overlay switching off or a Takeover pulse no longer freeze their temporary values into your settings.\n" +
            "- other apps no longer stay ducked after CCP closes.\n" +
            "\nCOMPANION\n- with the engine off, webcam and window-awareness lines show their bubble quietly, the same as idle lines. \"Speak idle lines when the engine is off\" brings the sound back.\n" +
            "- AI effect commands obey your own switches.\n" +
            "\nPANIC AND LOCKDOWN\n- the voice panic follows the panic key rules, and now also stops the Deeper player, browser videos and keyword triggers.\n" +
            "- a Stop button is never eaten by the Ctrl+K palette or an Escape pause.\n" +
            "- a running Lockdown keeps the panic key off when a remote session ends. its way out survives a sign-out, a clock change and a full-width phrase.\n" +
            "- opening the Lockdown tab no longer redraws the veil over a running lockdown.\n" +
            "\nREMOTE, LEASH AND CIRCE'S TAB\n- your own settings and strict lock come back when a remote session ends.\n" +
            "- Leash minutes count real time, so a busy PC no longer misses a minutes task.\n" +
            "- cutting the leash drops leash bookings that were not pushed yet.\n" +
            "- one failed attention check books once.\n" +
            "\nFIXES\n- a floating video no longer clears the bubbles.\n" +
            "- named flash sizes apply.\n" +
            "- Hypnotube links from friends open the video.\n" +
            "- the online media share unsticks after a clock change.\n" +
            "- factory reset and the graphics-failure exit no longer freeze the app.\n" +
            "\nNEW IN 7.1.14\n\nONLINE MEDIA\n- online media kept on your PC now clears itself 24 hours after it was downloaded. switching online media off clears it at once.\n" +
            "- the built-in browser pages clear their own copies of online media too.\n" +
            "- the \"Media from Scrolller\" line now sits on the Assets card and on the wallpaper credit.\n" +
            "- a Scrolller picture is never left behind as your Windows wallpaper. a plain one takes its place when the wallpaper switches off.\n" +
            "- bug reports no longer carry which online posts you looked at.\n" +
            "\nNEW IN 7.1.13\n\nFRIENDS\n- friend links: press Copy link in the Friends drawer and send it anywhere. whoever opens it becomes your friend, on the desktop, the phone or app.cclabs.app. a link lasts 7 days or 25 uses, and New link retires the old one.\n" +
            "- paste a friend's link into the add box to claim it, even when it sits inside a longer message.\n" +
            "- online friends show where they are: a small PC, phone or globe mark next to their name.\n" +
            "- friends on the web can invite you to Racing Thoughts and Breakout, and the invite opens the right place.\n" +
            "\nONLINE MEDIA, GOING OFFICIAL\n- we're finalising a commercial licence with Scrolller and starting the work now. every online picture, GIF and clip carries a small \"via Scrolller\" mark.\n" +
            "- each plan gets a daily share of online media. the meter in the title bar shows today's use in percent and resets at midnight, your time.\n" +
            "- anything you've seen in the last 24 hours is kept on your PC and replays free. sessions mix it with fresh picks, so the share goes further.\n" +
            "- when the share runs out, kept media and your own files keep playing. fresh online media is back at midnight.\n" +
            "- Basic gets more each day than Free, and Prime the most. a new install gets extra on its first day.\n" +
            "- the meter is counted on your PC only. a one-time card explains all this the first time you use online media.\n" +
            "\nGAMES\n- Arcademy: the Records Annex cameras show the whole picture on wide screens.\n" +
            "- Goon: a listed table says so, and reminds you that you never see your own table in the list.\n" +
            "\nVIDEOS\n- floating mandatory videos have three new switches on the Video page: Pop up together opens every window at once (up to Most at once), Keep sound stays on the video you're watching while new ones join muted, and Replay after a failed check can be turned off. strict videos and Lockdown always replay.\n" +
            "\nCOMPANION\n- personality studio: edit and delete your own personalities. Save now updates yours instead of adding a copy, and the name box shows the whole name.\n" +
            "\nACCOUNT AND HOME\n- an account with no display name is asked for one after signing in.\n" +
            "- the Discord pill on Home is now a round Discord bubble.\n" +
            "\nFIXES IN 7.1.13\n- haptics synced to audio no longer stop at 30:00.\n" +
            "- lock cards accept punctuation: PROCESS, COMPLY, REPEAT matches PROCESS. COMPLY. REPEAT.\n" +
            "- drone program: day 12 no longer gets stuck. optional tasks keep counting after the required ones finish the day.\n" +
            "- Ollama setup: Finish saves the host it checked, and the Engine Room shows it.\n" +
            "- the Strict note on the Video page says which modes keep videos full screen.\n" +
            "- new pop-ups wait until a running session or Lockdown is over.\n" +
            "\nv7.1.12 - Hold Still\n\nNEW IN 7.1.12\n- mandatory videos have a small mute button and a close X in the top right. the X is hidden on strict videos and in Lockdown, and neither shows during a Down the Rabbit Hole run.\n" +
            "- floating videos open with a short animation, and only once the picture is ready: no more black or empty frame. they no longer clear your flashes, and the \"via\" card now shows over the end of the clip instead of after it.\n" +
            "- online media meter: a pill in the title bar and a card on the Assets page show how much of today's online media you have used. it is counted per account, and a new ? button explains how the daily share and the cache work.\n" +
            "- online pictures and clips are kept on your PC once seen, so watching them again costs nothing from your daily share.\n" +
            "- companion: a new switch keeps idle lines silent while the engine is off. on by default. the bubble still shows.\n" +
            "\nFIXES IN 7.1.12\n- panic: nothing comes back on its own after a panic. a remote controller can no longer resume or restart your session, Trigger Mode stays quiet, your companion stays quiet, and Lockdown no longer restarts the engine. pressing START yourself brings everything back and resumes your session.\n" +
            "- GIFs keep animating round after round in flashes, and GIFs saved for later stay animated instead of turning into stills.\n" +
            "- Escape keeps working in every game: it pauses Arcademy, Goon, the Back Room and Intake, and pause cards in chess and Breakout resume properly.\n" +
            "- capped clips start at a random point instead of always at the beginning.\n" +
            "- floating mandatory videos no longer overlap now and then.\n" +
            "- Down the Rabbit Hole: a clip on the tube's stage now leaves with the descent.\n" +
            "- Circe's Tab: the Locktober banner and the lock calendar agree on your counted days, and they clear when you sign out or switch accounts.\n" +
            "- the Sign in button at the top right opens the sign-in dialog.\n" +
            "- the dashboard logo has rounded corners, and the patron achievements note says what is locked.\n" +
            "- smaller fixes: sparkle cleanup, remote video stop, gaze monitors, Italian wording.\n" +
            "\nv7.1.11 - Stay Tuned\n\nNEW IN 7.1.11\n- Leash: a small ? next to every command. hover it to see what lands on the other player's screen, with a little picture: Assign, Reward, Punish, Tug, every punishment and reward row, and Let go.\n" +
            "- just subscribed and still locked? press \"I just subscribed\" on the Premium page. the app also checks again on its own when you come back from the checkout page, and a small card tells you when your Basic or Prime is on.\n" +
            "- Italiano (beta) rewritten line by line, so it reads like Italian and not like a translation.\n" +
            "- a flag next to each language in the language picker.\n" +
            "- online pictures and clips now say where they come from: a small \"via Scrolller\" credit wherever they show, and the app tells Scrolller plainly who it is.\n" +
            "\nARCADEMY\n- Daily Trigger: one paid try a day. it counts as soon as the class starts, so leaving with Esc uses it; after that it runs as a retake. the day now turns over at the same time as the credits (midnight UTC).\n" +
            "\nFIXES IN 7.1.11\n- Patreon: patrons who sign in with Discord no longer lose access after 14 days while they are paying.\n" +
            "- Takeover: Mind Wipe only plays when Mind Wipe is switched on.\n" +
            "- changing the wallpaper can no longer freeze the app.\n" +
            "- flashes that stay until popped are no longer trimmed by \"Max on screen\" (up to 40, as the setting says).\n" +
            "- keyword triggers written as patterns now fire on screen text too, not only on typing.\n" +
            "- Bubble Count tells you when it has no videos to play instead of staying silent.\n" +
            "- 12 dead or VIP-only video links removed from the built-in lists.\n" +
            "- Leash: long words on the holder buttons shrink instead of being cut, row names show in full, Chaster chips read +15m / +30m / +1h.\n" +
            "- Circe's Tab: during the raffle month a day in credit still counts. the first slip-up of the day goes to your lock and the credit pays the rest.\n" +
            "- wording: \"Basic tier\" and \"Prime tier\", \"Down the Rabbit Hole run\" instead of \"Chaos run\", Goon tables say they start on Friends, and Slut Mode is no longer listed as a Patreon perk (it is for everyone).\n" +
            "\nv7.1.10 - Stay Tuned\n\nNEW IN 7.1.10\n- Italiano (beta): the whole app in Italian. pick it under Settings > Language. it is a first pass, so odd lines are expected; tell us in #bug-reports.\n" +
            "- Slut Mode has teeth: with the switch on, every persona talks dirty in the chat box, not only the ones that had a filthy variant. your companion says so when you flip it. nothing else changes: barks, voice lines and triggers stay as they were.\n" +
            "- the tube chat box closes when you send, so it no longer sits over the reply. click the avatar or press the chat key to type the next line.\n" +
            "\nFIXES IN 7.1.10\n- online clips are capped at 720p, so the engine stops pulling 4K files for a thumbnail-sized flash.\n" +
            "- Back Room: wall screens that light up together now show different pictures.\n" +
            "\nv7.1.9 - Stay Tuned\n\nPIECE BY PIECE\n- watch a game: public games have a Watch button in the Lobby. you see the real board about 15 s behind, can swap sides, use the same camera and Options, and see both players' IQ. players still never see each other's. \"Let people watch my games\" sits in Options, on by default.\n" +
            "- a featured game on the Home board, rotating every 20 s. Watch is one click.\n" +
            "- reactions: six of them, each with its own little sound, with a cooldown so nobody can flood the board. players see them float up and can turn them off (\"Play watchers' reactions\") or mute them.\n" +
            "- the stands replay the turn player's Distraction with your own pictures. games between players on older versions get calm ambient pictures instead.\n" +
            "- name tags: both players float at their end of the board, hop when they move, flinch when they lose a piece, and sweat and tremble in check.\n" +
            "- rating and ladder: your rating sits by your name, the Ladder shows the top 50 and your rank, and the end card rolls your rating up or down. unrated games say why.\n" +
            "- a short opening card when the game opens, and a clockwork board intro where the pieces rise through brass gears. both can be switched off in Options.\n" +
            "- the menus are rebuilt: a Back button on every screen, clearer buttons, sounds, and things that react when you press them.\n" +
            "\nNEW IN 7.1.9\n- Gentle visuals, for light-sensitive eyes: one switch in Settings > Performance. everything fades in and out (you pick 0.25 to 2 s), GIFs and spirals slow down (down to 0.25x), brightness eases in instead of snapping, nothing flickers, and subliminals lose their black card. videos fade but play at normal speed.\n" +
            "- local AI setup: the first page now says plainly that the AI runs on your own graphics card and memory, shows what your PC has, and warns when it is under 16 GB memory or 8 GB graphics memory.\n" +
            "- Intake: unlimited graded Intake for Basic too, not only Prime.\n" +
            "\nFIXES IN 7.1.9\n- a lock card sent by the companion or Remote with no count showed 0 / 0 and never closed. it always asks for at least one now.\n" +
            "- Flash \"Max on screen\" is now the ceiling in every mode. Stay mode could reach 40.\n" +
            "- what the avatar says in the tube chat now shows in the Companion page history too.\n" +
            "\nNEW IN 7.1.8\n- audio player: a playlist card on the Deeper page. add your own files, play, skip, seek. it has its own volume, Deeper enhancements can ride the current track, and panic or engine Stop silences it. .ogg files are not supported.\n" +
            "- Goon: toys now react during matches. a cue never goes above your own slider, and the other player can only lower it. no toy cues in practice.\n" +
            "- companion: a Voice Level slider.\n" +
            "- CCP Default: 44 more companion lines have a recorded voice.\n" +
            "- Intake: spoken answers work while the wake word is on. the card melt never lands on a timed question and scores nothing.\n" +
            "- favourites drawer on every page, and Ctrl+K leads with your pinned favourites.\n" +
            "- Mandatory Video: the \"Video length\" row now cuts web videos and floating videos at your max too. a video you asked for in chat plays whole.\n" +
            "- plans: locked features show Basic and Prime with their price, and the button opens the right checkout.\n" +
            "- invites: the app tells you when someone uses your invite code, and when a friend you invited subscribes.\n" +
            "\nDOWN THE RABBIT HOLE\n- dock toys wake on their own as you play. no key, no button. grabbing a toy again takes it a level deeper for the fall, and Overload works again.\n" +
            "- a calmer feel: a detonation dips the sound and the colour instead of flashing, and pops climb a little scale.\n" +
            "- bubbles move, every spiral is woven live in the chamber's colours, and online gifs play instead of sitting still.\n" +
            "- tube cards show pictures first, clips later. a popped flash card leaves in style.\n" +
            "- Escape pauses the run.\n" +
            "\nBREAKOUT\n- Endless: the board name fades once you have arrived.\n" +
            "- story level 8 keeps your launch held while the finale ring forms.\n" +
            "- the pendulum hint no longer sits on the \"move to play\" line.\n" +
            "\nFIXES IN 7.1.8\n- content packs: a pack video is no longer loaded whole into memory. with packs active this could eat several GB and make the whole PC crawl.\n" +
            "- a web video started by the companion could run past your max video length. it stops there now.\n" +
            "- Mind Wipe, one-shot sounds and the Mantra drone follow the master volume.\n" +
            "- Deeper and Mantra volume no longer move the app's volume in the Windows mixer.\n" +
            "- subliminals show above a playing video.\n" +
            "- the Audio action names the right folder, and falls back to your mod's recorded clips.\n" +
            "- Presets says what a preset covers.\n" +
            "- temp files that could not be deleted are retried instead of piling up.\n" +
            "\nNEW IN 7.1.7\n- lines with no recorded voice on your current mod wear a small muted-speaker mark in the phrase editor, and Audio Whispers says so when some of your lines will stay silent. nothing changed in what plays: a line with no recording was always silent.\n" +
            "- Awareness keyword triggers play their sound only while the engine or a session is running. the Test button still plays.\n" +
            "- bug reports now say which sound started while the engine was off, and name the companion speech bubble if the app hangs there. if you get stray sounds or a freeze, send a report right after.\n" +
            "\nNEW IN 7.1.6\n- mandatory videos can float. Studio > Mandatory Video > \"Shows as\" > Floating window: small bordered windows you can drag across monitors and throw around. up to four at once, with sliders for size and drift speed. the lit one has the sound; touch another to move it. floating videos have no attention checks.\n" +
            "- Bubble Count: a new option pauses the other effects while a round runs.\n" +
            "- the bottom bar shows the session name and the time left while a session runs.\n" +
            "- Leash: \"What each level allows\" at the level picker explains Soft, Standard and Strict.\n" +
            "- chess: the board explains what IQ means.\n" +
            "\nFIXES IN 7.1.6\n- Library: with an online source picked, the list no longer runs under the Start bar, so the \"add your own community\" field can be reached. the Asset Browser no longer vanishes after switching back to your own assets.\n" +
            "- red hold-to-pop bubbles no longer let the click through to the window behind. a bubble on top of a flash image takes the click.\n" +
            "- the corner GIF shows only while the engine runs.\n" +
            "- applying Her Favor no longer drops the frame rate for a few seconds.\n" +
            "- detached companion: the invisible areas around it no longer block clicks.\n" +
            "- companion personality: \"Fork and edit by hand\" opens the current prompt, the editor no longer cuts your text short, and Screen Awareness Reactions can be edited again.\n" +
            "- ending a Remote session no longer stops your own running session.\n" +
            "- a Deeper file played as a mandatory video shows all of its subliminals.\n" +
            "- haptics: a test only plays the test, and says so when haptics are switched off.\n" +
            "- updates: a stalled download times out and retries instead of sitting on the startup window.\n" +
            "\nFIXES SINCE 7.1.0\n- new Companion > AI page: the connection (cloud, local or your own model), how often your companion talks (idle chatter, bubble time, voice lines, triggers, tube look) and its memory, all in one place and never folded away. 7.1.0 and 7.1.1 left some of them with no way in.\n" +
            "- the Companion page has Show, Hide, Pop out and Voice right in its header again.\n" +
            "- the Home slideshow is quiet: no more notes when a card changes.\n" +
            "- Lockdown has a 20-minute choice and starts there, so a Lockdown counts for quests without changing anything.\n" +
            "- flashes switched on while the engine runs now start right away. no restart needed.\n" +
            "- conditioning time counts each minute once. it was counting twice, so it now grows at half the old speed. your total so far stays.\n" +
            "- the app no longer saves its settings every second during a session.\n" +
            "- the companion stops suggesting quests once they are all done.\n" +
            "- the Social and Lobby counts wear the Social colour instead of red, and dim once you have seen them.\n" +
            "- launcher: click anywhere on a tile to launch it. the window grows so the bottom row fits.\n" +
            "- pop-up questions have a \"Turn these off\" link on the card, and switching them off survives the end of a session.\n" +
            "- Flash: \"when clicked\" is greyed out while Shatter is on, since Shatter decides what a click does.\n" +
            "- perks you have not unlocked yet are dimmed. fixed a crash in the tube fit dialog.\n" +
            "\nNEW IN 7.1.0\n\nGAMES\n- Breakout: the Endless board name shows when you arrive, then fades.\n" +
            "- Breakout: on level 8, a launch pressed early is kept while the finale ring forms.\n" +
            "- Breakout: the pendulum hint no longer covers the \"move to play\" line.\n" +
            "\nHOME\n- the Home slideshow is now the Tonight Board: one card at a time for what matters right now, such as open tables, quests waiting, picking up where you left off and events, plus one tip each round.\n" +
            "- news from us shows on a lit pixel board. click it and it ripples.\n" +
            "- hover a card to hold it. snooze the board for a week if you want it quiet.\n" +
            "\nFINDING THINGS\n- a new rail: seven labelled sections (Home, Studio, Companion, Play, Social, You, Library) plus Settings. Ctrl+1 to Ctrl+7 jump between them.\n" +
            "- each section's pages sit in a tab bar at the top of the page, and every section has its own colour. the app remembers the last page you opened in each.\n" +
            "- new Social section: the Lobby, Friends, Leaderboard, Remote and Leash in one place. the rail shows how many tables are open.\n" +
            "- the companion has its own pages: Personality, Permissions and Links. the knowledge links editor is back.\n" +
            "- Settings is one scrolling page and now holds Monitors and Account & Plans.\n" +
            "- the Premium page is back: from the Vault tile, the spark in the header, search, or Account & Plans.\n" +
            "- search (Ctrl+K) finds pages by their old names, forgives typos, remembers what you picked, and lists every Studio module and game.\n" +
            "- a one-time \"What moved\" card shows where things went. replay it from Help.\n" +
            "- Favorites and Recent live in a drawer at the right edge of Home.\n" +
            "- Library: picking media is simpler. choose a source, then a flavour card; the fine-tuning switches sit in one fold.\n" +
            "\nCOMPANION\n- the tube and the Bambi, Sissy and CCP Default avatar are redrawn: 28 new animations, clear glass, and soft motes rising inside it.\n" +
            "- the Drone companion moves: five of its outfits are animated, and every Drone line has a new voice.\n" +
            "\nLOOK AND FEEL\n- buttons, chips and panels share one light and one depth. anything switched on sits pressed in.\n" +
            "- the header marquee is a lit drum that rolls to each new line (Full motion).\n" +
            "- the window edge takes the colour of the section you are in.\n" +
            "- effects use less CPU and GPU.\n" +
            "\nFIXES\n- clicking a friend's card no longer crashes the app.\n" +
            "- pasting a Hypnotube link works again.\n" +
            "- no more XP migration screen: older accounts are restored automatically and still get the 10% XP bonus.\n" +
            "\nEVERYTHING ELSE\nevery 7.0.x fix is in here too. coming from 6.11.5 or older? read the 7.0.0 notes on the release page.";
    }
}
