using System;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Sprache der Anzeige (Overlay, Meldungen): Deutsch, wenn das Spiel auf Deutsch eingestellt ist, sonst Englisch.
    /// Maßgeblich ist die Sprachausgabe des Spiels (Wwise: "German", "English(US)", …); solange sie noch nicht bekannt
    /// ist, die Windows-Sprache. Abgefragt wird nur im Hauptthread (<see cref="Refresh"/>), gelesen überall.
    /// </summary>
    public static class Loc
    {
        static volatile bool _german = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        static volatile string _lang = Code(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        static DateTime _next;

        public static bool German => _german;

        /// <summary>Sprache des Spiels als Kürzel: "de", "en" oder "fr" (Sprachausgabe des First Officers).</summary>
        public static string Lang => _lang;

        static string Code(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            return s.StartsWith("de") || s.StartsWith("german") ? "de" : s.StartsWith("fr") ? "fr" : "en";
        }

        /// <summary>Aus Update() (Hauptthread): Spielsprache alle paar Sekunden neu lesen.</summary>
        public static void Refresh()
        {
            if (DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddSeconds(5);
            try
            {
                string game = GameBridge.GameLanguage() ?? GameBridge.VoiceLanguage();
                if (!string.IsNullOrEmpty(game)) { _german = game.StartsWith("German", StringComparison.OrdinalIgnoreCase); _lang = Code(game); }
            }
            catch { }
        }

        /// <summary>Text in der Anzeigesprache.</summary>
        public static string T(string de, string en) => _german ? de : en;

        public static string Station(Station s)
        {
            switch (s)
            {
                case STBCVoiceCrew.Station.Helm: return T("Steuer", "Helm");
                case STBCVoiceCrew.Station.Tactical: return T("Taktik", "Tactical");
                case STBCVoiceCrew.Station.Engineering: return T("Technik", "Engineering");
                default: return "Captain";
            }
        }
    }
}
