using System;

namespace StbcAiCrew
{
    /// <summary>
    /// Sprache der Anzeige (Overlay, Meldungen): Deutsch, wenn das Spiel auf Deutsch eingestellt ist, sonst Englisch.
    /// Maßgeblich ist die Sprachausgabe des Spiels (Wwise: "German", "English(US)", …); solange sie noch nicht bekannt
    /// ist, die Windows-Sprache. Abgefragt wird nur im Hauptthread (<see cref="Refresh"/>), gelesen überall.
    /// </summary>
    public static class Loc
    {
        static volatile bool _german = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        static DateTime _next;

        public static bool German => _german;

        /// <summary>Aus Update() (Hauptthread): Spielsprache alle paar Sekunden neu lesen.</summary>
        public static void Refresh()
        {
            if (DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddSeconds(5);
            try
            {
                string game = GameBridge.VoiceLanguage();
                if (!string.IsNullOrEmpty(game)) _german = game.StartsWith("German", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        /// <summary>Text in der Anzeigesprache.</summary>
        public static string T(string de, string en) => _german ? de : en;

        public static string Station(Station s)
        {
            switch (s)
            {
                case StbcAiCrew.Station.Helm: return T("Steuer", "Helm");
                case StbcAiCrew.Station.Tactical: return T("Taktik", "Tactical");
                case StbcAiCrew.Station.Engineering: return T("Technik", "Engineering");
                default: return "Captain";
            }
        }
    }
}
