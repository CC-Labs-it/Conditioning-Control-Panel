namespace ConditioningControlPanel
{
    /// <summary>The current release notes shared by both heads. Update here when bumping the release.</summary>
    public static class ReleaseNotes
    {
        public const string Current = @"v7.1.5 - Stay Tuned

FIXES SINCE 7.1.0
- new Companion > AI page: the connection (cloud, local or your own model), how often your companion talks (idle chatter, bubble time, voice lines, triggers, tube look) and its memory, all in one place and never folded away. 7.1.0 and 7.1.1 left some of them with no way in.
- the Companion page has Show, Hide, Pop out and Voice right in its header again.
- the Home slideshow is quiet: no more notes when a card changes.
- Lockdown has a 20-minute choice and starts there, so a Lockdown counts for quests without changing anything.
- flashes switched on while the engine runs now start right away. no restart needed.
- conditioning time counts each minute once. it was counting twice, so it now grows at half the old speed. your total so far stays.
- the app no longer saves its settings every second during a session.
- the companion stops suggesting quests once they are all done.
- the Social and Lobby counts wear the Social colour instead of red, and dim once you have seen them.
- launcher: click anywhere on a tile to launch it. the window grows so the bottom row fits.
- pop-up questions have a ""Turn these off"" link on the card, and switching them off survives the end of a session.
- Flash: ""when clicked"" is greyed out while Shatter is on, since Shatter decides what a click does.
- perks you have not unlocked yet are dimmed. fixed a crash in the tube fit dialog.

NEW IN 7.1.0

GAMES
- Breakout: the Endless board name shows when you arrive, then fades.
- Breakout: on level 8, a launch pressed early is kept while the finale ring forms.
- Breakout: the pendulum hint no longer covers the ""move to play"" line.

HOME
- the Home slideshow is now the Tonight Board: one card at a time for what matters right now, such as open tables, quests waiting, picking up where you left off and events, plus one tip each round.
- news from us shows on a lit pixel board. click it and it ripples.
- hover a card to hold it. snooze the board for a week if you want it quiet.

FINDING THINGS
- a new rail: seven labelled sections (Home, Studio, Companion, Play, Social, You, Library) plus Settings. Ctrl+1 to Ctrl+7 jump between them.
- each section's pages sit in a tab bar at the top of the page, and every section has its own colour. the app remembers the last page you opened in each.
- new Social section: the Lobby, Friends, Leaderboard, Remote and Leash in one place. the rail shows how many tables are open.
- the companion has its own pages: Personality, Permissions and Links. the knowledge links editor is back.
- Settings is one scrolling page and now holds Monitors and Account & Plans.
- the Premium page is back: from the Vault tile, the spark in the header, search, or Account & Plans.
- search (Ctrl+K) finds pages by their old names, forgives typos, remembers what you picked, and lists every Studio module and game.
- a one-time ""What moved"" card shows where things went. replay it from Help.
- Favorites and Recent live in a drawer at the right edge of Home.
- Library: picking media is simpler. choose a source, then a flavour card; the fine-tuning switches sit in one fold.

COMPANION
- the tube and the Bambi, Sissy and CCP Default avatar are redrawn: 28 new animations, clear glass, and soft motes rising inside it.
- the Drone companion moves: five of its outfits are animated, and every Drone line has a new voice.

LOOK AND FEEL
- buttons, chips and panels share one light and one depth. anything switched on sits pressed in.
- the header marquee is a lit drum that rolls to each new line (Full motion).
- the window edge takes the colour of the section you are in.
- effects use less CPU and GPU.

FIXES
- clicking a friend's card no longer crashes the app.
- pasting a Hypnotube link works again.
- no more XP migration screen: older accounts are restored automatically and still get the 10% XP bonus.

EVERYTHING ELSE
every 7.0.x fix is in here too. coming from 6.11.5 or older? read the 7.0.0 notes on the release page.";
    }
}
