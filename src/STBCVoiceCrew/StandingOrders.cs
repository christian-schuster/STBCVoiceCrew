using System;
using System.Collections.Generic;
using System.Text;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Daueranweisungen des Captains ("Schilde unten lassen", "höchstens Viertel Fahrt", "volle Energie auf die
    /// Waffen", "nur auf Befehl feuern", "unauffällig bleiben"). Das LLM ordnet die Worte des Captains nur in diese
    /// feste Liste ein (Auswahl per Grammatik, kein freier Text). Reflexe und Offiziere halten sich daran;
    /// nur ein ausdrücklicher Befehl des Captains für genau diese Aktion geht vor.
    /// </summary>
    public sealed class StandingOrders
    {
        public string Shields = "frei";      // frei | oben halten | unten halten
        public string Speed = "frei";        // frei | stopp | viertel | halb
        public string Power = "frei";        // frei | waffen | schilde | antrieb | ausgeglichen
        public string Fire = "frei";         // frei | nur auf befehl | halten
        public string Stealth = "aus";       // aus | an

        // "unverändert" zuletzt: an erster Stelle wählte das Modell es fast immer
        public static readonly string[] ShieldValues = { "unten halten", "oben halten", "frei", "unverändert" };
        public static readonly string[] SpeedValues = { "viertel", "halb", "stopp", "frei", "unverändert" };
        public static readonly string[] PowerValues = { "waffen", "schilde", "antrieb", "ausgeglichen", "frei", "unverändert" };
        public static readonly string[] FireValues = { "nur auf befehl", "halten", "frei", "unverändert" };
        public static readonly string[] StealthValues = { "an", "aus", "unverändert" };

        public void Reset() { Shields = Speed = Power = Fire = "frei"; Stealth = "aus"; }

        public bool Any => Shields != "frei" || Speed != "frei" || Power != "frei" || Fire != "frei" || Stealth != "aus";

        /// <summary>Für Prompts und Overlay; null, wenn nichts gilt.</summary>
        public string Describe()
        {
            if (!Any) return null;
            var parts = new List<string>();
            if (Shields != "frei") parts.Add("Schilde " + Shields);
            if (Speed != "frei") parts.Add("Fahrt höchstens " + Speed);
            if (Power != "frei") parts.Add("Energie: " + (Power == "ausgeglichen" ? "ausgeglichen" : "voll auf " + Power) + ", nicht umverteilen");
            if (Fire != "frei") parts.Add(Fire == "halten" ? "Feuer halten" : "Feuer nur auf Befehl");
            if (Stealth == "an") parts.Add("unauffällig bleiben (keine Waffen, kleine Signatur)");
            return string.Join("; ", parts.ToArray());
        }

        /// <summary>Verbietet die Daueranweisung diesen Befehl? Grund oder null.</summary>
        public string Blocks(string cmd)
        {
            switch (cmd)
            {
                case "RaiseShields": if (Shields == "unten halten") return "Schilde sollen unten bleiben"; break;
                case "LowerShields": if (Shields == "oben halten") return "Schilde sollen oben bleiben"; break;
                case "SetSpeedFull":
                    if (Speed != "frei") return "Fahrt höchstens " + Speed; break;
                case "SetSpeedHalf":
                    if (Speed == "viertel" || Speed == "stopp") return "Fahrt höchstens " + Speed; break;
                case "SetSpeedQuarter":
                case "SetSpeedReverse":
                    if (Speed == "stopp") return "Schiff soll stehen bleiben"; break;
                case "PowerToShields": if (Power != "frei" && Power != "schilde") return "Energie soll auf " + Power + " bleiben"; break;
                case "PowerToPhasers": if (Power != "frei" && Power != "waffen") return "Energie soll auf " + Power + " bleiben"; break;
                case "PowerToEngines": if (Power != "frei" && Power != "antrieb") return "Energie soll auf " + Power + " bleiben"; break;
                case "PowerBalanced": if (Power != "frei" && Power != "ausgeglichen") return "Energie soll auf " + Power + " bleiben"; break;
                case "FirePhasers":
                case "FireTorpedoes":
                case "EngageTarget":
                case "ArmTorpedoes":
                    if (Fire != "frei") return Fire == "halten" ? "Feuer halten" : "Feuer nur auf Befehl";
                    if (Stealth == "an") return "unauffällig bleiben"; break;
            }
            if (Stealth == "an" && (cmd == "RaiseShields" || cmd == "SetSpeedFull")) return "unauffällig bleiben";
            return null;
        }

        /// <summary>Befehle, die eine neue Daueranweisung sofort umsetzen (Station, Befehl).</summary>
        public static List<KeyValuePair<Station, string>> ApplyNow(string field, string value)
        {
            var r = new List<KeyValuePair<Station, string>>();
            Action<Station, string> add = (s, c) => r.Add(new KeyValuePair<Station, string>(s, c));
            switch (field + "=" + value)
            {
                case "shields=oben halten": add(Station.Tactical, "RaiseShields"); break;
                case "shields=unten halten": add(Station.Tactical, "LowerShields"); break;
                case "speed=stopp": add(Station.Helm, "SetSpeedStop"); break;
                case "speed=viertel": add(Station.Helm, "SetSpeedQuarter"); break;
                case "speed=halb": add(Station.Helm, "SetSpeedHalf"); break;
                case "power=waffen": add(Station.Engineering, "PowerToPhasers"); break;
                case "power=schilde": add(Station.Engineering, "PowerToShields"); break;
                case "power=antrieb": add(Station.Engineering, "PowerToEngines"); break;
                case "power=ausgeglichen": add(Station.Engineering, "PowerBalanced"); break;
                case "fire=halten": add(Station.Tactical, "CeaseFire"); break;
                case "stealth=an": add(Station.Engineering, "ReduceSignature"); add(Station.Tactical, "CeaseFire"); break;
            }
            return r;
        }

        // ---------------- Einordnung per LLM ----------------

        /// <summary>Lohnt sich die Einordnung? (Spart LLM-Aufrufe bei "Ja", "Gut" usw.)</summary>
        public static bool Worth(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            foreach (var w in new[] { "lass", "halt", "bleib", "nur", "maximal", "höchstens", "nicht", "kein", "solange", "bis auf weiteres",
                                      "frei", "normal", "aufheben", "unentdeckt", "unauffällig", "tarn", "leise", "energie", "schild",
                                      "fahrt", "schub", "geschwindigkeit", "feuer", "waffen", "immer" })
                if (t.Contains(w)) return true;
            return false;
        }

        public static string Prompt(StandingOrders current, string captainWords)
        {
            var sb = new StringBuilder();
            sb.Append("Du ordnest die Worte eines Raumschiff-Captains in DAUERANWEISUNGEN ein. Eine Daueranweisung gilt, bis der Captain sie aufhebt ");
            sb.Append("(\"Schilde unten lassen\", \"nur noch Viertel Fahrt\", \"volle Energie auf die Waffen\", \"nicht ohne Befehl feuern\", \"wir dürfen nicht entdeckt werden\"). ");
            sb.Append("Einmalige Befehle (\"Schilde hoch!\", \"Feuer!\", \"volle Fahrt!\") sind KEINE Daueranweisung: dann \"unverändert\". ");
            sb.Append("\"frei\" bzw. \"aus\" heißt: Anweisung aufgehoben (\"Schilde wieder nach Bedarf\", \"normale Fahrt\", \"Waffen frei\"). Im Zweifel \"unverändert\".\n");
            sb.Append("Beispiele:\n");
            sb.Append("\"Lass die Schilde unten.\" → shields: unten halten\n");
            sb.Append("\"Flieg nur mit 30 % Schub.\" → speed: viertel; \"nicht schneller als halbe Kraft\" → speed: halb\n");
            sb.Append("\"Wir dürfen nicht entdeckt werden.\" / \"unbemerkt durch\" → stealth: an\n");
            sb.Append("\"Feuert erst, wenn ich es sage.\" → fire: nur auf befehl; \"Nicht feuern, das sind Freunde.\" → fire: halten\n");
            sb.Append("\"Volle Energie auf die Waffen, egal was passiert.\" → power: waffen\n");
            sb.Append("\"Schilde wieder nach Bedarf.\" → shields: frei; \"normale Fahrt\" → speed: frei; \"Waffen frei\" → fire: frei; \"wir müssen nicht mehr schleichen\" → stealth: aus\n");
            sb.Append("\"Schilde hoch!\", \"Volle Fahrt!\", \"Feuer!\", \"Bring uns zur Ragusa.\" → alles unverändert (einmalige Befehle)\n");
            sb.Append("Felder, die der Captain nicht anspricht, bleiben unverändert.\n");
            sb.Append("Geltende Daueranweisungen: ").Append(current.Describe() ?? "keine").Append('\n');
            sb.Append("Felder: shields (Schilde), speed (Höchstfahrt; 30 % ≈ viertel), power (Energieverteilung), fire (Waffeneinsatz), stealth (unentdeckt bleiben).\n");
            sb.Append("Worte des Captains: \"").Append(captainWords).Append('"');
            return sb.ToString();
        }

        public static object Schema()
        {
            Func<string[], object> e = vals =>
            {
                var l = new List<object>();
                foreach (var v in vals) l.Add(v);
                return new Dictionary<string, object> { { "type", "string" }, { "enum", l } };
            };
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "shields", e(ShieldValues) }, { "speed", e(SpeedValues) }, { "power", e(PowerValues) },
                        { "fire", e(FireValues) }, { "stealth", e(StealthValues) }
                    }
                },
                { "required", new List<object> { "shields", "speed", "power", "fire", "stealth" } }
            };
        }

        /// <summary>Ergebnis übernehmen; liefert die geänderten Felder (Feld, neuer Wert).</summary>
        public List<KeyValuePair<string, string>> Merge(object root)
        {
            var changed = new List<KeyValuePair<string, string>>();
            Action<string, Func<string>, Action<string>> take = (field, get, set) =>
            {
                string v = Json.Str(root, field);
                if (string.IsNullOrEmpty(v) || v == "unverändert" || v == get()) return;
                set(v);
                changed.Add(new KeyValuePair<string, string>(field, v));
            };
            take("shields", () => Shields, v => Shields = v);
            take("speed", () => Speed, v => Speed = v);
            take("power", () => Power, v => Power = v);
            take("fire", () => Fire, v => Fire = v);
            take("stealth", () => Stealth, v => Stealth = v);
            return changed;
        }
    }
}
