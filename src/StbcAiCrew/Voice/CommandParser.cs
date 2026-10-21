using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StbcAiCrew.Voice
{
    /// <summary>Was der Sprecher will (Watson-Befehlssatz + Daueranweisungen + Manöver).</summary>
    public enum Intent
    {
        // Fahrt
        SpeedFull, SpeedHalf, SpeedQuarter, SpeedStop, SpeedReverse,
        // Navigation
        MoveToTarget, MoveAway, Impulse, ImpulseEngage, Warp, WarpEngage, PrepareWarp,
        // Taktik
        ShieldsUp, ShieldsDown, ModulateShields, FirePhasers, FireTorpedoes, CeaseFire, ArmTorpedoes, DisarmTorpedoes,
        TargetEngines, TargetShields, TargetWeapons, CeaseTargeting, Scan, Analyze, Attack, SelectTarget,
        RedAlert, CancelRedAlert, CloakHunt,
        // Technik
        PowerWeapons, PowerShields, PowerEngines, PowerBalanced, Repair, RepairAll, Transport, ReduceSignature, Hack,
        // Captain
        AnswerHail, LaunchProbe,
        // Daueranweisungen
        HoldShieldsUp, HoldShieldsDown, LimitSpeed, HoldFire, FireOnOrder, StealthOn, StealthOff, StayOutOfSensors, ReleaseOrders,
        // Manöver
        AvoidHazard, KeepDistance, CircleTarget, HoldPosition, Flee,
        // Gespräch
        Confirm, Deny,
    }

    public sealed class ParsedCommand
    {
        public Intent Intent;
        public Station Station;
        /// <summary>Genannter Name (Ziel, Reiseziel, Gefahr), wie gehört; aufgelöst über <see cref="Resolved"/>.</summary>
        public string Name;
        public string Resolved;
        public string Subsystem;
        public float Number = -1f;
        public string Clause;
        public override string ToString() =>
            Intent + (Name != null ? "(" + (Resolved ?? Name) + ")" : "") + (Subsystem != null ? "[" + Subsystem + "]" : "") + (Number >= 0 ? "{" + Number + "}" : "");
    }

    public sealed class ParseResult
    {
        public readonly List<ParsedCommand> Commands = new List<ParsedCommand>();
        /// <summary>Gezielt angesprochene Station ("Steuer, …", "Rao, …"); null = keine Ansprache.</summary>
        public Station? Addressed;
        /// <summary>An alle ("Crew", "Brücke", "alle Stationen").</summary>
        public bool AddressedAll;
        /// <summary>Satzteile, die nach Befehl klangen, aber nicht verstanden wurden.</summary>
        public readonly List<string> NotUnderstood = new List<string>();
        public bool Understood => Commands.Count > 0;
        public override string ToString() => string.Join(", ", Commands.ConvertAll(c => c.ToString()).ToArray());
    }

    /// <summary>
    /// Regelbasierter Befehlsparser ohne Sprachmodell: Satzteile, Synonyme (Deutsch/Englisch), Platzhalter
    /// (Name, Subsystem, Zahl), Verneinung, Ansprache. Namen werden per Klangabgleich gegen die Kontaktliste aufgelöst.
    /// </summary>
    public static class CommandParser
    {
        sealed class Rule
        {
            public Intent Intent;
            public Station Station;
            public Regex Pattern;
            public bool AllowsNegation;   // Regel beschreibt selbst eine Verneinung ("nicht feuern", "nicht entdeckt")
        }

        static readonly List<Rule> Rules = new List<Rule>();
        const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

        static void R(Intent i, Station st, string pattern, bool negation = false) =>
            Rules.Add(new Rule { Intent = i, Station = st, Pattern = new Regex(pattern, O), AllowsNegation = negation });

        // Wortbausteine
        const string ART = @"(?:(?:de[mnrs]|die|das|zu[mr]|eine?[nmrs]?|unse?re?[mnrs]?)\s+)?";
        const string NAME = @"(?<name>[\p{L}\p{N}][\p{L}\p{N}\s\-]*)";   // gierig bis zum Satzteilende, Füllwörter entfernt CleanName
        const string BITTE = @"(?:bitte\s+)?";

        static CommandParser()
        {
            var H = Station.Helm; var T = Station.Tactical; var E = Station.Engineering; var C = Station.Captain;

            // ---------- Daueranweisungen und Manöver zuerst (sie enthalten oft Wörter einfacher Befehle) ----------
            R(Intent.StealthOn, E, @"(alle\s+systeme\s+(auf|runter|herunter)\w*\s*(minimum|minimal)|systeme\s+auf\s+minimum|schleich\w*|unentdeckt|unbemerkt|nicht\s+entdeckt|nicht\s+gesehen|nicht\s+bemerkt|keiner\s+(darf|soll)\s+uns\s+sehen|leise\s+bleiben|tarn(en|ung)|stealth|silent\s+running|run\s+silent)", negation: true);
            R(Intent.StealthOff, E, @"(nicht\s+mehr\s+schleichen|schleichfahrt\s+(beenden|aus|aufheben)|tarnung\s+(aus|aufheben|beenden)|end\s+stealth)", negation: true);
            R(Intent.StayOutOfSensors, H, @"(au(ß|ss)erhalb\s+(der\s+)?(gegnerischen\s+|feindlichen\s+)?(sensor|ortung|scanner|reichweite)\w*|(sensor|ortungs)\w*reichweite\s+meiden|nicht\s+in\s+(die\s+)?(sensor|ortungs)\w*|out\s+of\s+(their\s+)?sensor\s+range)", negation: true);
            R(Intent.AvoidHazard, H, @"(um\s*flieg\w*|umfahr\w*|umschiff\w*|um\s+\S+\s+herum|weich\w*\s+(den|der|dem|die)?\s*\S+\s+aus|meide\w*|avoid|steer\s+clear)\s*" + ART + NAME + @"?\s*(herum|aus)?$");
            R(Intent.KeepDistance, H, @"(halte?\w*|bleib\w*)\s+(?<num>[\w,.]+)\s*(km|kilometer)\s+(abstand|entfernt|weg)|abstand\s+(halten|wahren)|auf\s+distanz\s+bleiben|keep\s+(your\s+)?distance");
            R(Intent.CircleTarget, H, @"(kreis\w*\s+um|umkreis\w*|orbit\w*)\s*" + ART + NAME + "?");
            R(Intent.HoldPosition, H, @"(position\s+halten|halte\s+(die\s+)?position|hold\s+position)");
            R(Intent.Flee, H, @"(bring\w*\s+uns\s+(hier\s+)?raus|nichts\s+wie\s+weg|flucht|rückzug|zieh\w*\s+uns\s+zurück|get\s+us\s+out|retreat)");
            R(Intent.HoldShieldsDown, T, @"(schilde?r?\s+(bleiben|lassen|halten)\s+\w*\s*(unten|aus)|lass\w*\s+(die\s+)?schilde?r?\s+(unten|aus)|schilde?r?\s+unten\s+lassen|keep\s+shields\s+down)", negation: true);
            R(Intent.HoldShieldsUp, T, @"(schilde?r?\s+(bleiben|lassen|halten)\s+\w*\s*(oben|an)|lass\w*\s+(die\s+)?schilde?r?\s+(oben|an)|keep\s+shields\s+up)");
            R(Intent.LimitSpeed, H, @"(nur\s+(noch\s+)?(mit\s+)?(?<num>[\w,.]+)\s*(%|prozent)|(höchstens|maximal|nicht\s+schneller\s+als)\s+(?<num2>[\w,.]+\s*(%|prozent)|halbe|viertel)|langsam\s+(fliegen|bleiben))", negation: true);
            R(Intent.HoldFire, T, @"(nicht\s+(mehr\s+)?(feuern|schie(ß|ss)en)|feuer\s+halten|haltet\s+das\s+feuer|keine\s+waffen|hold\s+fire|don'?t\s+fire)", negation: true);
            R(Intent.FireOnOrder, T, @"(nur\s+auf\s+(meinen\s+)?befehl\s+(feuern|schie(ß|ss)en)|(feuer|schie(ß|ss))\w*\s+(erst|nur)(\s+(wenn|auf|nach))?\b|niemand\s+schie(ß|ss)t\s+ohne|ohne\s+(meinen\s+)?befehl)", negation: true);
            R(Intent.ReleaseOrders, C, @"(wieder\s+(nach\s+bedarf|frei|normal)|normale?\s+(fahrt|betrieb)|anweisung\w*\s+aufheben|waffen\s+frei|as\s+you\s+were)");

            // ---------- Fahrt ----------
            R(Intent.SpeedFull, H, @"(volle?r?\s+(fahrt|kraft|geschwindigkeit|schub|impuls\w*)|vollgas|maximum\s+(speed|impulse)|full\s+(speed|impulse)|flieg\w*\s+schneller|schneller)");
            R(Intent.SpeedHalf, H, @"(halbe?r?\s+(fahrt|kraft|geschwindigkeit|schub|impuls\w*)|half\s+(speed|impulse))");
            R(Intent.SpeedQuarter, H, @"(viertel\w*\s*(fahrt|kraft|geschwindigkeit|schub|impuls\w*)?|quarter\s+(speed|impulse))");
            R(Intent.SpeedStop, H, @"(alle\s+maschinen\s+stopp?|volle?r?\s+stopp?|^\s*stopp?\b|\bstopp?\s*$|anhalten|all\s+stop|full\s+stop)");
            R(Intent.SpeedReverse, H, @"(rückwärts|zurücksetzen|reverse)");

            // ---------- Warp / Impuls ----------
            R(Intent.WarpEngage, H, @"(^\s*energie\s*$|warp\w*\s+(jetzt|los|aktivieren|ausführen|starten)|jetzt\s+(springen|warp)|engage\s+warp)");
            R(Intent.PrepareWarp, E, @"(warp\w*\s*(kern|spulen)\s*(laden|vorbereiten)|(lade|lad)\w*\s+(den\s+|die\s+)?warp\w*|warp\s+vorbereiten|prepare\s+warp)");
            R(Intent.Warp, H, @"((geh|gehe|gehen|flieg\w*|spring\w*|bring\w*\s+uns)?\s*(auf|mit)?\s*warp\w*(\s+(nach|zu[mr]?|in)\s+" + ART + NAME + ")?|\bwarp\b)");
            R(Intent.ImpulseEngage, H, @"((jetzt|los)\s+\w*\s*impuls\w*|impuls\w*\s+(jetzt|los|ausführen|starten|aktivieren)|impulssprung\s+ausführen|engage\s+impulse)");
            R(Intent.Impulse, H, @"((geh|gehe|gehen|flieg\w*|spring\w*)?\s*(auf|mit|zu)?\s*impuls\w*(\s+(nach|zu[mr]?|in)\s+" + ART + NAME + ")?|impulse)");

            // ---------- Anflug / Kurs ----------
            R(Intent.MoveAway, H, @"(weg\s+von|abstand\s+(zu|zum|zur)|abdreh\w*|ausweich\w*|evasive|move\s+away)");
            R(Intent.MoveToTarget, H, @"((flieg\w*|fliegt|fahr\w*|bring\w*|steuer\w*|klick\w*)\s+(uns\s+)?(nach|zu[mr]?|an|in\s+richtung|richtung)\s+" + ART + NAME + @"|kurs\s+(nehm\w*\s+|setz\w*\s+)?(auf|zu[mr]?|nach)(\s+" + ART + NAME + @")?|(nimm|nehm\w*|setz\w*)\s+kurs\s+(auf|zu[mr]?|nach)(\s+" + ART + NAME + @")?|(nähere?\w*|annäher\w*|näher\s+an|näher\s+ran|ran\s+an)\s*(dich|uns)?\s*" + ART + NAME + @"?|(fang\w*|abfang\w*)\s+" + ART + NAME + @"?\s*(ab)?|ziel\s+anfliegen|anfliegen|richte\w*\s+(das\s+schiff|uns)\s+(aus\s+)?(zu[mr]?|auf)\s+" + ART + NAME + @"|(das\s+schiff|uns)\s+(aus)?richten\s+(zu[mr]?|auf|nach)\s+" + ART + NAME + @"|(move|fly|head)\s+to(ward)?s?\s+" + NAME + ")");

            // ---------- Taktik ----------
            R(Intent.ShieldsUp, T, @"(schilde?r?\s+(hoch|rauf|aktivieren|an|heben)|roter\s+alarm|red\s+alert|raise\s+shields|shields\s+up)");
            R(Intent.ShieldsDown, T, @"(schilde?r?\s+(runter|senken|deaktivieren|aus)|lower\s+shields|shields\s+down)");
            R(Intent.ModulateShields, T, @"(schild\w*\s*(frequenz\w*)?\s+modul\w*|modul\w*\s+(die\s+)?schild\w*|modulate)");
            R(Intent.CeaseFire, T, @"(feuer\s+einstellen|feuer\s+stopp|cease\s+fire)");
            R(Intent.FireTorpedoes, T, @"(torpedo\w*\s*(feuer\w*|los|ab\w*|raus)?|(feuer\w*|fire)\s+\w*\s*torpedo\w*)");
            R(Intent.FirePhasers, T, @"(phaser\w*|feuer\s+frei|^\s*feuer\s*$|^\s*feuer\s+(auf|aufs|mein\w*|das|die|den|dem)\b|feuern|schie(ß|ss)\w*|open\s+fire|fire)");
            R(Intent.DisarmTorpedoes, T, @"(torpedo\w*\s+entschärf\w*|entschärf\w*)");
            R(Intent.TargetEngines, T, @"((ziel\w*|anvisier\w*|zielt?|stör\w*)\s+\w*\s*(auf\s+)?(den\s+|die\s+)?(antrieb|triebwerk)\w*|(antrieb|triebwerk)\w*\s+(des\s+ziels\s+)?(sub\s*system\w*\s+)?(anvisier\w*|auswähl\w*|wähl\w*|stör\w*)|target\s+(their\s+)?engines)");
            R(Intent.TargetWeapons, T, @"((ziel\w*|anvisier\w*|zielt?|stör\w*)\s+\w*\s*(auf\s+)?(die\s+)?waffen\w*|waffen\w*\s+(des\s+ziels\s+)?(sub\s*system\w*\s+)?(anvisier\w*|auswähl\w*|wähl\w*|stör\w*)|target\s+(their\s+)?weapons)");
            R(Intent.TargetShields, T, @"((ziel\w*|anvisier\w*|zielt?|stör\w*)\s+\w*\s*(auf\s+)?(die\s+)?schild\w*|schilde\s+des\s+ziels|schild\w*\s+(des\s+ziels\s+)?(sub\s*system\w*\s+)?(anvisier\w*|auswähl\w*|wähl\w*|stör\w*)|target\s+(their\s+)?shields)");
            R(Intent.CeaseTargeting, T, @"(zielerfassung\s+(aufheben|beenden|aus)|cease\s+target)");
            R(Intent.Analyze, T, @"(analys\w*|analyze)");
            R(Intent.Scan, T, @"(scan\w*|sca\w*\s+(die|das|den)\s+|abtast\w*|(nach\s+)?leben\w*zeichen)(\s+" + ART + NAME + ")?");
            R(Intent.Attack, T, @"(greif\w*\s+(" + ART + @"(?<name>.+?)\s+)?an\b|angriff|angreifen|attack\w*|engage)");
            R(Intent.SelectTarget, T, @"((neues\s+)?ziel\s*[:,]?\s*(ist\s+)?" + ART + NAME + @"|(markier\w*|erfass\w*|anvisier\w*|wähl\w*)\s+" + ART + NAME + @"|target\s+" + NAME + ")");
            R(Intent.CancelRedAlert, T, @"(alarm\s+(aufheben|beenden|aus)|entwarnung|cancel\s+red\s+alert)");
            R(Intent.CloakHunt, T, @"(getarnt\w*\s+(schiff\w*\s+)?(jagen|suchen)|tarn\w*\s+jagen|cloak\s*hunt)");

            // ---------- Technik ----------
            R(Intent.PowerWeapons, E, @"(energie\s+(auf|an|in|zu)\s+(die\s+)?(waffen|phaser)|(volle\s+)?offensiv\w*|power\s+to\s+weapons)");
            R(Intent.PowerShields, E, @"(energie\s+(auf|an|in|zu)\s+(die\s+)?schild\w*|defensiv\w*|power\s+to\s+shields)");
            R(Intent.PowerEngines, E, @"(energie\s+(auf|an|in|zu)\s+(den\s+)?(antrieb|triebwerk)\w*|alles\s+in\s+den\s+antrieb|power\s+to\s+engines)");
            R(Intent.PowerBalanced, E, @"(energie\s+(gleichmäßig|ausgleich\w*|ausgeglichen|verteilen)|balance\s+power)");
            R(Intent.RepairAll, E, @"(alles\s+reparier\w*|alle\s+systeme\s+reparier\w*|repair\s+all)");
            R(Intent.Repair, E, @"(reparier\w*|repair)(\s+" + ART + @"(?<sub>\p{L}+))?");
            R(Intent.Transport, E, @"(beam\w*|transport\w*|an\s+bord\s+(holen|nehmen)|anborg|energize)");
            R(Intent.ReduceSignature, E, @"(signatur\s+(verringern|reduzieren|senken)|reduce\s+signature)");
            R(Intent.Hack, E, @"(hack\w*|eindring\w*|system\s+intrusion)");

            // ---------- Captain / Gespräch ----------
            R(Intent.AnswerHail, C, @"(ruf\s+(annehmen|beantworten)|kanal\s+öffnen|auf\s+den\s+schirm|answer\s+hail|on\s+screen)");
            R(Intent.LaunchProbe, C, @"(sonde\s+(starten|abschießen|losschicken)|launch\s+probe)");
            R(Intent.Confirm, C, @"^\s*(ja|jawohl|bestätigt|richtig|genau|yes|affirmative)\s*$");
            R(Intent.Deny, C, @"^\s*(nein|abbrechen|stopp\s+das|negativ|no|cancel)\s*$");
        }

        // ---------------- Ansprache ----------------

        static readonly Regex AddressRx = new Regex(@"^\s*(?<who>steuer\w*|helm|navigation|navigator|rao|fähnrich\s+rao|lieutenant\s+rao|taktik|tactical|waffen\w*|hoyle|lieutenant\s+hoyle|technik|engineering|maschinenraum|t'?nar|tenar|crew|brücke|alle(\s+stationen)?)\s*[,:!]?\s+", O);

        static Station? StationOf(string who, out bool all)
        {
            all = false;
            string w = who.ToLowerInvariant();
            if (w.StartsWith("steuer") || w == "helm" || w.StartsWith("navigat") || w.Contains("rao")) return Station.Helm;
            if (w.StartsWith("taktik") || w == "tactical" || w.StartsWith("waffen") || w.Contains("hoyle")) return Station.Tactical;
            if (w.StartsWith("technik") || w == "engineering" || w.StartsWith("maschinen") || w.Contains("nar") || w == "tenar") return Station.Engineering;
            all = true;
            return null;
        }

        // ---------------- Zerlegen ----------------

        static readonly Regex Splitter = new Regex(@"[,.;!?]+|\s+und\s+(dann\s+)?|\s+dann\s+|\s+danach\s+|\s+anschließend\s+|\s+and\s+(then\s+)?|\s+then\s+", O);
        static readonly Regex Negation = new Regex(@"\b(nicht|kein\w*|nie|niemals|bloß\s+nicht|don'?t|never)\b", O);
        static readonly Regex Filler = new Regex(@"^(bitte|so|okay|ok|gut|also|ja|äh|ähm|hm|und|doch|mal)\s+", O);
        static readonly Regex Purpose = new Regex(@"\s+(damit|so\s+dass|sodass|weil)\s+.*$", O);

        public static ParseResult Parse(string text, IList<string> knownNames = null)
        {
            var result = new ParseResult();
            if (string.IsNullOrEmpty(text)) return result;
            string t = " " + text.Trim() + " ";
            t = t.Replace("ß", "ss").Replace('-', ' ');   // Schreibweisen vereinheitlichen ("Warp-Spulen" = "Warp Spulen")
            t = t.Trim();

            // Ansprache am Anfang ("Steuer, …", "Rao bitte …", "Crew: …")
            var am = AddressRx.Match(t);
            if (am.Success)
            {
                bool all;
                result.Addressed = StationOf(am.Groups["who"].Value, out all);
                result.AddressedAll = all;
                t = t.Substring(am.Length);
            }

            // Zweck-Nebensatz gehört zum Befehl ("…, damit wir nicht entdeckt werden"): als eigener Teil prüfen
            foreach (var raw in Splitter.Split(t))
            {
                if (raw == null) continue;
                string clause = raw.Trim();
                while (true) { var f = Filler.Match(clause); if (!f.Success) break; clause = clause.Substring(f.Length).Trim(); }
                if (clause.Length == 0) continue;
                ParseClause(clause, result, knownNames);
            }

            // Doppelte (gleiche Absicht über mehrere Satzteile, z. B. "…Minimum, damit wir nicht entdeckt werden") entfernen
            var unique = new List<ParsedCommand>();
            foreach (var c in result.Commands)
                if (!unique.Exists(u => u.Intent == c.Intent && (u.Name ?? "") == (c.Name ?? ""))) unique.Add(c);
            result.Commands.Clear();
            result.Commands.AddRange(unique);
            // "Ja"/"Nein" sind nur allein eine Antwort ("Ja, flieg zur Ragusa" ist ein Befehl)
            if (result.Commands.Exists(c => c.Intent != Intent.Confirm && c.Intent != Intent.Deny))
                result.Commands.RemoveAll(c => c.Intent == Intent.Confirm || c.Intent == Intent.Deny);

            // Station aus Ansprache übernehmen, wenn der Befehl zu mehreren Stationen passt (z. B. "Scan" an Technik)
            if (result.Addressed.HasValue)
                foreach (var c in result.Commands)
                    if (c.Station == Station.Captain) c.Station = result.Addressed.Value;
            return result;
        }

        static void ParseClause(string clause, ParseResult result, IList<string> knownNames)
        {
            bool negated = Negation.IsMatch(clause);
            bool matched = false;
            var seen = new HashSet<Intent>();
            foreach (var r in Rules)
            {
                var m = r.Pattern.Match(clause);
                if (!m.Success) continue;
                if (negated && !r.AllowsNegation) continue;
                // Überdeckung: eine Daueranweisung schließt den gleichnamigen Einzelbefehl im selben Satzteil aus
                if (Shadowed(r.Intent, seen)) continue;
                var cmd = new ParsedCommand { Intent = r.Intent, Station = r.Station, Clause = clause };
                var name = m.Groups["name"];
                if (name.Success && name.Value.Trim().Length > 0) cmd.Name = CleanName(name.Value);
                var sub = m.Groups["sub"];
                if (sub.Success) cmd.Subsystem = sub.Value.ToLowerInvariant();
                cmd.Number = Number(m.Groups["num"].Success ? m.Groups["num"].Value : m.Groups["num2"].Success ? m.Groups["num2"].Value : null);
                if (cmd.Name != null && knownNames != null)
                {
                    float score;
                    cmd.Resolved = Phonetics.BestMatch(cmd.Name, knownNames, out score);
                }
                result.Commands.Add(cmd);
                seen.Add(r.Intent);
                matched = true;
            }
            // "Schilde anvisieren" meint die gegnerischen (die Schilde-hoch-Regel steht davor)
            if (seen.Contains(Intent.TargetShields))
                result.Commands.RemoveAll(c => c.Clause == clause && c.Intent == Intent.ShieldsUp);
            if (matched) return;
            // Nur ein Name, der zum vorigen Befehl gehört ("Nimm Kurs auf, Frachter Alpha 2")
            var prev = result.Commands.Count > 0 ? result.Commands[result.Commands.Count - 1] : null;
            if (prev != null && prev.Name == null && TakesName(prev.Intent))
            {
                prev.Name = CleanName(clause);
                if (prev.Name != null && knownNames != null) { float sc; prev.Resolved = Phonetics.BestMatch(prev.Name, knownNames, out sc); }
                if (prev.Name != null) return;
            }
            if (LooksLikeOrder(clause)) result.NotUnderstood.Add(clause);
        }

        static bool TakesName(Intent i) =>
            i == Intent.MoveToTarget || i == Intent.Warp || i == Intent.Impulse || i == Intent.Scan || i == Intent.SelectTarget ||
            i == Intent.Attack || i == Intent.AvoidHazard || i == Intent.CircleTarget;

        /// <summary>Spezifischere Absicht im selben Satzteil schließt allgemeinere aus.</summary>
        static bool Shadowed(Intent i, HashSet<Intent> seen)
        {
            if (seen.Count == 0) return false;
            switch (i)
            {
                case Intent.ShieldsUp: return seen.Contains(Intent.HoldShieldsUp) || seen.Contains(Intent.TargetShields);
                case Intent.ShieldsDown: return seen.Contains(Intent.HoldShieldsDown) || seen.Contains(Intent.StealthOn);
                case Intent.SpeedQuarter:
                case Intent.SpeedHalf:
                case Intent.SpeedFull: return seen.Contains(Intent.LimitSpeed) || seen.Contains(Intent.SpeedQuarter) || seen.Contains(Intent.SpeedHalf) || seen.Contains(Intent.SpeedStop);
                case Intent.SpeedStop: return seen.Contains(Intent.CeaseFire);
                case Intent.FirePhasers: return seen.Contains(Intent.HoldFire) || seen.Contains(Intent.FireOnOrder) || seen.Contains(Intent.CeaseFire) || seen.Contains(Intent.FireTorpedoes);
                case Intent.FireTorpedoes: return seen.Contains(Intent.HoldFire) || seen.Contains(Intent.FireOnOrder) || seen.Contains(Intent.DisarmTorpedoes);
                case Intent.Warp: return seen.Contains(Intent.WarpEngage) || seen.Contains(Intent.PrepareWarp);
                case Intent.Impulse: return seen.Contains(Intent.ImpulseEngage) || seen.Contains(Intent.SpeedFull) || seen.Contains(Intent.SpeedHalf) || seen.Contains(Intent.SpeedQuarter);
                case Intent.MoveToTarget: return seen.Contains(Intent.AvoidHazard) || seen.Contains(Intent.CircleTarget) || seen.Contains(Intent.Warp) || seen.Contains(Intent.Impulse);
                case Intent.MoveAway: return seen.Contains(Intent.KeepDistance) || seen.Contains(Intent.Flee) || seen.Contains(Intent.AvoidHazard);
                case Intent.Scan: return seen.Contains(Intent.Analyze);
                case Intent.SelectTarget: return seen.Count > 0;   // "Ziel" nur, wenn sonst nichts erkannt wurde
                case Intent.Attack: return seen.Contains(Intent.FirePhasers) || seen.Contains(Intent.FireTorpedoes);
                case Intent.ReduceSignature: return seen.Contains(Intent.StealthOn);
                case Intent.PowerEngines: return seen.Contains(Intent.StealthOn);
                case Intent.ReleaseOrders: return false;
                default: return false;
            }
        }

        static readonly Regex OrderWords = new Regex(@"\b(flieg|kurs|ziel|schild|feuer|phaser|torpedo|warp|impuls|scan|beam|energie|reparier|fahrt|stopp|angriff|ausweich|minen|abstand)\w*", O);
        static bool LooksLikeOrder(string clause) => OrderWords.IsMatch(clause);

        static readonly Regex NameNoise = new Regex(@"^(uns\s+)?(zu[mr]?|nach|an|auf|die|der|das|den|dem|ein\w*|schiff|station|bitte)\s+|\s+(und\s+.*|scanne?n?.*|auf\s+leben.*|an\s+bord.*|herum|aus|ab|bitte|jetzt)$", O);
        static string CleanName(string s)
        {
            string n = s.Trim();
            for (int i = 0; i < 3; i++) { string m = NameNoise.Replace(n, "").Trim(); if (m == n) break; n = m; }
            if (Regex.IsMatch(n, @"^(es|sie|ihn|ihm|das|die|der|den|dem|uns|euch|da|dort|hin|leben)$", O)) return null;   // Fürwörter sind keine Namen
            return n.Length > 0 ? n : null;
        }

        // ---------------- Zahlen ----------------

        static readonly Dictionary<string, int> Words = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "null", 0 }, { "eins", 1 }, { "ein", 1 }, { "einen", 1 }, { "zwei", 2 }, { "drei", 3 }, { "vier", 4 }, { "fünf", 5 },
            { "sechs", 6 }, { "sieben", 7 }, { "acht", 8 }, { "neun", 9 }, { "zehn", 10 }, { "elf", 11 }, { "zwölf", 12 },
            { "fünfzehn", 15 }, { "zwanzig", 20 }, { "dreißig", 30 }, { "dreissig", 30 }, { "vierzig", 40 }, { "fünfzig", 50 },
            { "sechzig", 60 }, { "siebzig", 70 }, { "achtzig", 80 }, { "neunzig", 90 }, { "hundert", 100 },
            { "halbe", 50 }, { "viertel", 25 }
        };

        public static float Number(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1f;
            string t = s.Trim().TrimEnd('%').Replace("prozent", "").Trim().Replace(',', '.');
            float f;
            if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) return f;
            int v;
            return Words.TryGetValue(t, out v) ? v : -1f;
        }
    }
}
