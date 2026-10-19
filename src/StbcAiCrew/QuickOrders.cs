using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StbcAiCrew
{
    /// <summary>
    /// Sofort-Befehle (wie früher mit Watson): Eindeutige Kommandos des Captains werden ohne Sprachmodell
    /// erkannt und sofort ausgeführt. Der Offizier bestätigt danach per LLM nur noch im Charakter.
    /// Verneinungen ("nicht feuern", "keine Schilde") und alles Unklare gehen den normalen Weg über das LLM.
    /// </summary>
    public static class QuickOrders
    {
        sealed class Rule
        {
            public Regex Pattern;
            public Station Station;
            public string[] Commands;
        }

        static readonly List<Rule> Rules = new List<Rule>();

        static void R(Station st, string pattern, params string[] commands) =>
            Rules.Add(new Rule { Station = st, Commands = commands, Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled) });

        static QuickOrders()
        {
            // Taktik (Reihenfolge: "Feuer einstellen" vor "Feuer")
            R(Station.Tactical, @"\bschilde?r?\s+(hoch|rauf|aktivieren|an)\b|\broter alarm\b", "RaiseShields");
            R(Station.Tactical, @"\bschilde?r?\s+(runter|senken|deaktivieren|aus)\b", "LowerShields");
            R(Station.Tactical, @"\bfeuer\s+einstellen\b|\bwaffen\s+ruhig\b", "CeaseFire");
            R(Station.Tactical, @"\btorpedos?\b.*\b(feuer|feuern|los|abfeuern|raus)\b|\b(feuer|feuern)\b.*\btorpedos?\b", "ArmTorpedoes", "FireTorpedoes");
            R(Station.Tactical, @"\bphaser\b.*\b(feuer|feuern|los)\b|\bfeuer frei\b|^\s*feuer\s*[!.]*\s*$", "FirePhasers");
            R(Station.Tactical, @"\b(scann?e?n?|scan)\b", "ScanTarget");
            // Steuer
            R(Station.Helm, @"\bvolle?\s+(fahrt|kraft|geschwindigkeit|impuls\w*)\b", "SetSpeedFull");
            R(Station.Helm, @"\bhalbe?\s+(fahrt|kraft|geschwindigkeit|impuls\w*)\b", "SetSpeedHalf");
            R(Station.Helm, @"\bviertel\s*(fahrt|kraft|geschwindigkeit|impuls\w*)?\b", "SetSpeedQuarter");
            R(Station.Helm, @"\b(alle maschinen stopp?|stopp?\b|anhalten)", "SetSpeedStop");
            R(Station.Helm, @"\brückwärts\b", "SetSpeedReverse");
            R(Station.Helm, @"\b(ziel anfliegen|flieg\w* (uns )?zum ziel|kurs auf (das )?ziel)\b", "MoveTo");
            R(Station.Helm, @"\b(ausweichen|abdrehen|bring\w* uns hier raus|rückzug)\b", "MoveAwayFrom");
            R(Station.Helm, @"\bimpuls\w*\b.*\b(jetzt|los|springen|ausführen)\b|\b(jetzt|los)\b.*\bimpuls\w*\b|\b(jetzt )?springen\b", "EngageImpulse");
            R(Station.Helm, @"^(?!.*\b(jetzt|los)\b).*\bimpuls(sprung)?\b(?!.*\b(jetzt|los|springen|ausführen)\b)", "PrepareImpulse");
            R(Station.Helm, @"^\s*energie\s*[!.]*\s*$|\bwarp\b.*\b(jetzt|los|energie)\b", "EngageWarp");
            // Technik
            R(Station.Engineering, @"\bwarp\b|\bwarpkern laden\b|\bspulen laden\b", "PrepareWarp");
            R(Station.Engineering, @"\benergie auf (die )?schilde?\b", "PowerToShields");
            R(Station.Engineering, @"\benergie auf (die )?(waffen|phaser)\b", "PowerToPhasers");
            R(Station.Engineering, @"\benergie auf (den )?(antrieb|triebwerke)\b", "PowerToEngines");
            R(Station.Engineering, @"\benergie (ausgleichen|gleichmäßig|ausgeglichen)\b", "PowerBalanced");
            R(Station.Engineering, @"\b(beamen|beam\w* (sie|die)|an bord holen)\b", "TransportFromTarget");
        }

        static readonly Regex Negation = new Regex(@"\b(nicht|kein\w*|nie|niemals|bloß nicht|warte)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Eindeutige Befehle in den Worten des Captains: (Station, Befehlsname) in Ausführungsreihenfolge.</summary>
        public static List<KeyValuePair<Station, string>> Parse(string text)
        {
            var result = new List<KeyValuePair<Station, string>>();
            if (string.IsNullOrEmpty(text)) return result;
            // Satzweise: "Schilde hoch, volle Fahrt!" → zwei Befehle; Verneinung gilt nur für ihren Satzteil
            foreach (var part in Regex.Split(text, @"[,.;!?]+|\bund\b|\bdann\b", RegexOptions.IgnoreCase))
            {
                string p = part.Trim();
                if (p.Length == 0 || Negation.IsMatch(p)) continue;
                foreach (var r in Rules)
                {
                    if (!r.Pattern.IsMatch(p)) continue;
                    foreach (var c in r.Commands) result.Add(new KeyValuePair<Station, string>(r.Station, c));
                    if (r.Commands[0] == "CeaseFire") break;   // "Feuer einstellen" ist kein "Feuer"
                }
            }
            // "Energie!" allein am Satzende ist ein eigener Teil; Warp-Befehle im ganzen Text prüfen
            return result;
        }
    }
}
