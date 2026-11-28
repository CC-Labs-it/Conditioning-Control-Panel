namespace ConditioningControlPanel.Localization
{
    /// <summary>
    /// Static shorthand for accessing localized strings in code-behind.
    /// Usage: Loc.Get("key") or Loc.GetF("key", arg1, arg2)
    /// </summary>
    public static class Loc
    {
        public static string Get(string key) => LocalizationManager.Instance.Get(key);
        public static string GetF(string key, params object[] args) => LocalizationManager.Instance.GetF(key, args);

        /// <summary>
        /// The app's language as a culture, for a date the app writes into one of its own sentences
        /// ("Resets 1 Dec"). The thread culture is the OS's, which can be another language (7.4 desk
        /// run: "Resets 1 dic" in English chrome on an Italian Windows). Falls back to the OS culture.
        /// </summary>
        public static System.Globalization.CultureInfo Culture => CultureFor(LocalizationManager.Instance.CurrentLanguage);

        internal static System.Globalization.CultureInfo CultureFor(string? language)
        {
            try { return System.Globalization.CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en" : language); }
            catch (System.Globalization.CultureNotFoundException) { return System.Globalization.CultureInfo.CurrentCulture; }
        }

        /// <summary>A date in the app's language with <paramref name="format"/> (local time is the caller's).</summary>
        public static string Date(System.DateTime when, string format) => when.ToString(format, Culture);
    }
}
