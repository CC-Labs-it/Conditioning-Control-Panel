namespace ConditioningControlPanel
{
    /// <summary>Canonical release notes for every head. Update with Version.props at release.</summary>
    public static class ReleaseNotes
    {
        public const string Current =
            "v7.4.1 - Terms & Conditioning\n" +
            "\n" +
            "everything since 7.3.8, in one place.\n" +
            "\n" +
            "TERMS & CONDITIONING\n" +
            "just your ordinary clicker game about accepting stuff - nothing sus about it :3\n" +
            "\n" +
            "- it has its own tile in the launcher now. there is a button, you press it, and sometimes it asks you to agree to something small. you can stop any time, probably.\n" +
            "- free for everyone. subscribers get the full version, which is the same harmless clicker, only with more of it.\n" +
            "- it can borrow your pictures and your toy if you say yes, and your save follows your account between the app and the website.\n" +
            "- at some point the game may ask to download something, about 135 MB. it is not a virus. it is, at most, a commitment.\n" +
            "- that is all there is to say about it. really.\n" +
            "\n" +
            "PRESETS AND SESSIONS\n" +
            "- the Presets tab is now Presets and sessions. presets are pink and sessions are blue, and a line under Sessions says what starting one does: it turns on only its own effects and hands yours back when it ends.\n" +
            "- an Import button sits beside Export, so a preset file someone sent you no longer has to be dragged onto the window to get in.\n" +
            "\n" +
            "READABLE ROWS\n" +
            "- every option row in Studio and Settings got bigger text, a small picture that says what the option is about (a stopwatch for how often, dice for random, a stack for how many) and a number you can read from across the desk. the same meaning wears the same picture everywhere.\n" +
            "- a switch that is off greys its picture, so a page shows at a glance what is on.\n" +
            "- the Studio rack and the Settings list wear the same big tinted names, and the little row pictures got a dark outline so they stop melting into the panel.\n" +
            "\n" +
            "LIVE PREVIEW\n" +
            "- each of the twelve effect pages now has a slice of a pretend desktop beside its options, where the effect plays as you change them. it wakes when you touch an option and settles again after a few seconds. it uses drawn stand-in pictures, never yours, and it changes nothing.\n" +
            "- Takeover, Haptics and Voice swapped their side picture for a live preview of their own. the Haptics one never reaches your toy and the Voice one never opens your microphone.\n" +
            "\n" +
            "SUPER EFFECTS\n" +
            "- Deck: Fewest flips and Most flips, so each card breaks after a random number of flips between the two.\n" +
            "- Whirlpool: Smallest size sets how far the pool can shrink, and Restless pool turns it around so it grows while you move and calms when you rest.\n" +
            "- the Whirlpool box shows four rows and keeps the rest behind More options.\n" +
            "- Brain Drain started from a bubble follows your Super switch, and the first picture of a bubble flash moves when flash motion is on.\n" +
            "\n" +
            "FRIENDS\n" +
            "- the drawer folds. the leash, What happened and Dares each sit under a header: folded you see the newest two lines and a bubble for what you have not read, opened you see everything. a leash offer opens its fold by itself.\n" +
            "- a settled dare can be dismissed, and corner notices come two at a time at most.\n" +
            "\n" +
            "COMFORT\n" +
            "- Settings > Performance has a pink filter limit for light-sensitive eyes. switched on, the pink filter and the Super fog never get stronger than the limit you set, whatever a preset, session or mod asks for.\n" +
            "\n" +
            "SMALL THINGS\n" +
            "- a bell with your recent notifications, newest first.\n" +
            "- Racing Thoughts 3D can play the pictures you ticked in your own library.\n" +
            "- dropping a pack shows its progress while it unpacks.\n" +
            "- the video quest says which videos count: the ones the app plays, not the browser.\n" +
            "- the Mod Creator keeps a draft on this PC and offers it back when you open it again.\n" +
            "- a video you put fullscreen by hand goes to your content screen, like Takeover does.\n" +
            "- your own AI (Ollama or another endpoint) keeps more of the chat in mind.\n" +
            "\n" +
            "FIXES\n" +
            "- videos play through a newer LibVLC, which closes a security hole in the way some video files were read.\n" +
            "- the file box in the Deeper player shows the file's name instead of a line of code.\n" +
            "- attention checks no longer appear so close to the end of a video that you cannot answer them.\n" +
            "- a lockdown of 20 minutes or more counts as a session for Daily and Weekly Devotion.\n" +
            "- the \"prompt too long\" notice only shows when the cloud AI is the one answering.\n" +
            "- the companion remembers what the avatar said out loud, so you can answer it.\n" +
            "- keyword sounds play only for words that were just highlighted.\n" +
            "- closing CCP no longer waits on a camera that is stuck starting.\n" +
            "- changing the language now reaches the page you have open: its title, its breadcrumb and the haptics status follow at once.\n" +
            "- the Haptics tab no longer wears a lock for free accounts. toys are free.\n" +
            "- a failed toy connection keeps its hint on screen until you try again.\n" +
            "- dates on the invite card follow the app's language.\n" +
            "- Terms & Conditioning: the spot where you could get stuck, look around and not move is fixed.";
    }
}
