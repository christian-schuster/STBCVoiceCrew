using System.Collections.Generic;
using System.Text;

namespace StbcAiCrew
{
    /// <summary>
    /// Übersetzt typische Worte des Captains in Befehlsvorschläge je Station ("Warp!" → Technik: PrepareWarp,
    /// Steuer: EngageWarp nach der Meldung der Technik). Das kleine Sprachmodell verwechselt sonst leicht
    /// Warp und Impulssprung oder vergisst den ersten Schritt. Die Entscheidung bleibt beim Offizier.
    /// </summary>
    public static class OrderHints
    {
        sealed class Hint
        {
            public string[] Words;
            public Station Station;
            public string Text;
        }

        static readonly List<Hint> Hints = new List<Hint>
        {
            H(Station.Engineering, "PrepareWarp (Warpkern laden), danach \"Warpkern geladen\" melden", "warp"),
            H(Station.Helm, "jetzt noch NICHTS auslösen, sondern \"Warte auf den Warpkern\" sagen; erst wenn die Technik \"Warpkern geladen\" gemeldet hat, EngageWarp", "warp"),
            H(Station.Helm, "PrepareImpulse, dann EngageImpulse (Impulssprung zum gewählten Ziel)", "impulssprung", "sprung", "springen"),
            H(Station.Helm, "SetSpeedFull", "volle kraft", "volle impuls", "volle geschwindigkeit", "voller impuls"),
            H(Station.Helm, "SetSpeedHalf", "halbe kraft", "halbe impuls", "halbe geschwindigkeit"),
            H(Station.Helm, "SetSpeedQuarter", "viertel"),
            H(Station.Helm, "SetSpeedStop", "stopp", "alle maschinen stop", "anhalten", "full stop"),
            H(Station.Helm, "SetSpeedReverse", "rückwärts", "zurücksetzen"),
            H(Station.Helm, "MoveAwayFrom (Abstand zum Ziel)", "ausweichen", "abstand", "weg von", "rückzug", "abdrehen", "bring uns hier raus", "bringen sie uns hier raus"),
            H(Station.Helm, "MoveTo (Ziel anfliegen)", "anfliegen", "näher ran", "kurs auf das ziel", "fliegen sie zu", "flieg zu", "abfangen"),
            H(Station.Tactical, "RaiseShields", "schilde hoch", "schilder hoch", "schilde aktivieren", "schutzschilde hoch", "roter alarm"),
            H(Station.Tactical, "LowerShields", "schilde runter", "schilder runter", "schilde senken", "schilde deaktivieren"),
            H(Station.Tactical, "FirePhasers", "phaser", "feuer frei", "feuern", "angriff"),
            H(Station.Tactical, "FireTorpedoes (vorher ArmTorpedoes, falls nicht scharf)", "torpedo"),
            H(Station.Tactical, "ScanTarget", "scan", "analysier"),
            H(Station.Tactical, "TargetWeapons", "waffen des ziels", "auf die waffen", "waffen anvisieren"),
            H(Station.Tactical, "TargetEngines", "antrieb des ziels", "auf den antrieb", "antrieb anvisieren"),
            H(Station.Tactical, "TargetShields", "schilde des ziels", "auf die schilde zielen"),
            H(Station.Tactical, "CeaseFire", "feuer einstellen", "nicht feuern", "waffen ruhig"),
            H(Station.Engineering, "PowerToShields", "energie auf die schilde", "energie auf schilde", "mehr energie für die schilde"),
            H(Station.Engineering, "PowerToPhasers", "energie auf die phaser", "energie auf die waffen", "energie auf phaser"),
            H(Station.Engineering, "PowerToEngines", "energie auf den antrieb", "energie auf antrieb", "energie auf die triebwerke"),
            H(Station.Engineering, "RepairBalanced (oder gezielt RepairShields/RepairEngines/…)", "reparier", "schaden beheben", "schadensbericht"),
            H(Station.Engineering, "TransportFromTarget", "beamen", "transporter", "an bord holen"),
        };

        static Hint H(Station st, string text, params string[] words) => new Hint { Station = st, Text = text, Words = words };

        /// <summary>Vorschläge für diese Station zu den Worten des Captains; null, wenn nichts passt.</summary>
        public static string For(Station st, string captainWords)
        {
            if (string.IsNullOrEmpty(captainWords)) return null;
            string t = captainWords.ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var h in Hints)
            {
                if (h.Station != st) continue;
                foreach (var w in h.Words)
                {
                    if (!t.Contains(w)) continue;
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(h.Text);
                    break;
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }
}
