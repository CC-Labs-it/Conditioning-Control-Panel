namespace ConditioningControlPanel
{
    /// <summary>Canonical release notes for every head. Update with Version.props at release.</summary>
    public static class ReleaseNotes
    {
        public const string Current =
            "v7.4.2 - Terms & Conditioning\n" +
            "\n" +
            "a round of fixes on top of 7.4.1.\n" +
            "\n" +
            "TERMS & CONDITIONING\n" +
            "- playing it in the app now pays Sparkles, for reaching an end and for stickers. it never did before, which was not in the contract either way.\n" +
            "- a new sticker shows up as a small card in the corner instead of a pile of them at once.\n" +
            "- saving your card as a GIF or a video is about three times faster and no longer hangs at the end, and the card says where the file went.\n" +
            "- saying yes to Scrolller pictures inside the game now gives you pictures, even when your Library is set to My own assets. your Library setting stays as it was.\n" +
            "\n" +
            "GETTING STARTED\n" +
            "- three short cards show where pictures come from, how to add your own and how to switch things on. they are in the Inbox and in Help.\n" +
            "\n" +
            "FIXES\n" +
            "- Presets and sessions: with many presets the sessions list was squeezed down to one row. the presets now scroll in their own box and the list keeps its room.\n" +
            "- Mandatory Video: the length values lost their first digit in the new font. they have it back.\n" +
            "- other apps stay quiet for a whole floating video. the quiet used to lift after five minutes while the video was still playing.\n" +
            "- opening the Blink Trainer or Settings > Devices no longer freezes the app while it looks for cameras.\n" +
            "- Companion links: a pool you emptied stays empty, and a pool with one video may play it again.\n" +
            "- a fresh install starts the Library on Both, so there is something to look at before you add your own files. it still asks first.\n" +
            "- the question about online pictures says Scrolller, where the pictures come from, and no longer says Reddit.\n" +
            "- six option rows lost a stray colon, and Bubble Pop's Speed row speaks your language.\n" +
            "- chess: in a cup replay the losing king waits for the last move to land before it falls.\n" +
            "- the installer closes a CCP that is running hidden instead of failing on it.\n" +
            "\n" +
            "UNDER THE HOOD\n" +
            "- when stopping takes longer than a second, the log now says which step took the time. if the app froze on you after Stop, a bug report from this version tells us far more than before.\n" +
            "\n" +
            "this is a pre-release for testers. tell us what breaks.";
    }
}
