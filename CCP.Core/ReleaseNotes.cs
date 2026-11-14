namespace ConditioningControlPanel
{
    /// <summary>Canonical release notes for every head. Update with Version.props at release.</summary>
    public static class ReleaseNotes
    {
        public const string Current =
            "v7.2.4 - Free Toys\n" +
            "\n" +
            "NEW IN 7.2.4\n" +
            "\n" +
            "TOYS ARE FREE\n" +
            "connected toys are free for everyone now, no Basic needed. your limits still apply and panic still stops them.\n" +
            "\n" +
            "RACING THOUGHTS CLASSIC IS BACK\n" +
            "the original 2D race returns beside the 3D one, as its own door on Play and in the launcher. free for everyone, every track open. the 3D Racing Thoughts stays Prime. Classic is also on the website and, soon, on the phone.\n" +
            "\n" +
            "PIECE BY PIECE\n" +
            "- right-click arrows and circles on the board: right drag from one square to another draws an arrow, a right click circles a square, the same again removes it, a left click clears them all. only you see them. a right drag that starts off the board still turns the camera.\n" +
            "\n" +
            "NEW\n" +
            "- Mod Creator: Text Replacements lists every original text a replacement can match, with a search box. click one and it fills the original box. Export list saves the whole list as a text file.\n" +
            "- Mandatory videos: \"Loop short videos until the minimum\", off by default. on, a clip shorter than the minimum replays until the minimum has played, then ends once, with one result.\n" +
            "- Wallpaper: \"Save this wallpaper\" keeps the picture on your desktop, in Pictures > CCP Wallpapers.\n" +
            "- Wallpaper: a Fit choice (Fill, Fit, Stretch, Span, Center), so a vertical or mixed setup can span instead of stretch. your own fit comes back after.\n" +
            "- the launcher shows an \"Update ready\" chip when a new version is waiting, so you can update without opening the panel.\n" +
            "- Settings > Account: \"Redeem a code\" for a pass code.\n" +
            "- Settings > Data: Delete account now sits in the Danger zone, next to Factory reset.\n" +
            "- Perk effects: Full, No cards or Off, in Settings > Notifications. Off also turns off the Pink Rush wash and the gold and pink sparkles on flashes and bubbles. XP still pays the same.\n" +
            "- Circe's Tab: a price and the day limit can go up to 24 hours. anything over an hour shows a warning line.\n" +
            "- Awareness: a new \"On at launch\" switch. it can now be turned off during a session, and pausing a session pauses it.\n" +
            "- quests: click a daily or weekly quest to open the page that completes it. Lockdown included.\n" +
            "- Natasha's bubbles: trigger words can come up more often, in Circe's Tab costs.\n" +
            "- Brain Drain: the audio clip rate goes down to 0, for blur only.\n" +
            "- \"+Add Trigger\" jumps to the new row and puts the cursor in it.\n" +
            "- fewer popups: the online media offer, the \"share achievements on Discord?\" question and the tier celebration cards are gone. online media and Discord sharing are still one switch away.\n" +
            "\n" +
            "FIXES\n" +
            "- the app no longer freezes when a video in the in-app browser goes full screen.\n" +
            "- Escape typed into another app no longer pauses and then closes a floating video. a floating video now answers Escape only when Escape is your panic key.\n" +
            "- a settings backup taken during a session now keeps your own values, so a restore on a new PC no longer brings back the session's pink filter, ducking or Hydra.\n" +
            "- your own voice lines speak on the idle timer again.\n" +
            "- importing the same pack twice no longer makes a copy of every file.\n" +
            "- Prize Counter: a buy that needs Discord says \"Link Discord first\" instead of failing on every try.\n" +
            "- Wallpaper no longer leaves a white desktop when Windows rotates your wallpapers: Dynamic redraws the whole desktop when it stops, and Static and the session wallpaper leave a rotating, Spotlight or per-monitor desktop alone.\n" +
            "- other apps such as Discord no longer stay turned down after a session.\n" +
            "- the spiral clears when a session ends.\n" +
            "- the companion's speech bubble no longer covers the chat box, and the tube no longer covers its own menu.\n" +
            "- the Prize Counter plays its sounds as soon as it opens.\n" +
            "- Bubble Count time counts for the \"watch videos\" quests, including a clip you leave early.\n" +
            "- a strict lock card with panic turned off says that Escape closes it.\n" +
            "- the Social badge checks the Lobby every 2 minutes instead of every 10 seconds. the Lobby itself stays live while it is open.\n" +
            "\n" +
            "HUNTING A STRAY SOUND\n" +
            "thanks to your reports, most \"random\" sounds now have a name. the giggle while idle was the companion asking you something on its own: with nothing running it now shows the question quietly, unless you let it speak while idle. it also waits while a Deeper session plays. the sounds right at launch are the launcher's own chimes and hover notes (its speaker button mutes them). still hearing something? send an in-app bug report right away, it now names more sources.\n" +
            "\n" +
            "ALSO LOGGED FOR THE NEXT REPORT\n" +
            "- a crash when the in-app browser goes full screen now saves your graphics card and window sizes.\n" +
            "- corner GIFs log how long they take to load and show.\n" +
            "\n" +
            "this replaces the 7.2.3 pre-release and carries all of it. coming from 7.2.1 or older? read the 7.2.2 notes on the release page.";
    }
}
