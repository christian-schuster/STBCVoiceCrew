using System.Collections.Generic;

namespace StbcAiCrew
{
    /// <summary>
    /// Stimmungen, in denen ein Charakter sprechen kann. Qwen3-TTS kennt keine Befehle wie [lacht],
    /// übernimmt beim Stimmklon aber den Sprechstil der Referenz. Deshalb bekommt jeder Charakter je Stimmung
    /// eine eigene Referenzaufnahme, gesprochen von seiner eigenen (geklonten) Stimme: gleiche Klangfarbe,
    /// andere Färbung. Das LLM wählt pro Satz die Stimmung.
    /// </summary>
    public static class Moods
    {
        public const string Neutral = "neutral";
        public const string Amused = "belustigt";
        public const string Urgent = "dringend";
        public const string Angry = "wuetend";
        public const string Afraid = "aengstlich";

        public static readonly string[] All = { Neutral, Amused, Urgent, Angry, Afraid };

        /// <summary>Für den Prompt: was die Stimmungen bedeuten.</summary>
        public const string PromptHelp =
            "\"mood\" = wie du es sagst: neutral (sachlich), belustigt (lachend, gut gelaunt), dringend (Gefahr, laute Gefechtsrufe), " +
            "wuetend (verärgert, genervt), aengstlich (verunsichert, nervös).";

        /// <summary>Referenztext, den die geklonte Stimme in dieser Stimmung spricht (deutscher Text bestimmt die Färbung).</summary>
        public static string ReferenceText(string mood, Character c)
        {
            string laugh = string.IsNullOrEmpty(c.Laugh) ? "" : c.Laugh + " ";
            switch (mood)
            {
                case Amused:
                    return string.IsNullOrEmpty(c.Laugh)
                        ? "Das ist… durchaus amüsant. Wirklich, das hätte ich nicht erwartet. Sehr gut."
                        : laugh + "Oh, das ist ja großartig! " + laugh + "Ich kann nicht mehr, wirklich!";
                case Urgent:
                    return "Achtung! Feindliche Schiffe direkt voraus! Schilde hoch, sofort! Wir haben keine Zeit mehr!";
                case Angry:
                    return "Verdammt noch mal! Wer hat das schon wieder kaputt gemacht?! Das ist doch nicht zu fassen!";
                case Afraid:
                    return "Oh nein… oh nein, das sieht gar nicht gut aus… Captain, ich… ich weiß nicht, ob wir das schaffen…";
                default:
                    return null;
            }
        }

        public static string Normalize(string mood)
        {
            if (string.IsNullOrEmpty(mood)) return Neutral;
            string m = mood.Trim().ToLowerInvariant().Replace("ü", "ue").Replace("ä", "ae");
            foreach (var x in All) if (x == m) return x;
            return Neutral;
        }

        public static List<object> Enum()
        {
            var l = new List<object>();
            foreach (var m in All) l.Add(m);
            return l;
        }
    }
}
