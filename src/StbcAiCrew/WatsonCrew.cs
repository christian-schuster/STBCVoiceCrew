using System;
using System.Collections.Generic;
using Sector;
using Sector.Spacecraft;
using StbcAiCrew.Voice;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Watson-Ersatz ohne LLM: Sprachbefehle aller Menschen → Befehlsparser → sofortige Ausführung über die Bot-Crew
    /// des Spiels (CommandManager), Ziele per Name, Abläufe (Warp, Impuls), Daueranweisungen, Manöver (Navigator)
    /// und Reflexe. Die Original-Crew (Rao, Hoyle, T'nar) bestätigt mit ihren Spielstimmen.
    /// </summary>
    public sealed class WatsonCrew
    {
        public enum ListenMode { Both, Address, PushToTalk, Always }

        readonly Plugin _cfg;
        public readonly StandingOrders Orders = new StandingOrders();
        public readonly Navigator Nav;
        public ListenMode Listen = ListenMode.Both;
        public WavePlayer Player;
        public bool Reflexes = true;

        public string LastHeard = "–", LastResult = "–";
        public readonly List<string> Log = new List<string>();

        List<Contact> _contacts = new List<Contact>();
        ShipStatus _status;
        readonly List<Step> _steps = new List<Step>();
        readonly Dictionary<string, float> _lastRun = new Dictionary<string, float>();

        sealed class Step
        {
            public float At;
            public Func<bool> Do;   // true = erledigt; false = später erneut (bis Deadline)
            public float Deadline;
            public float Retry;
            public string What;
        }

        public WatsonCrew(Plugin cfg)
        {
            _cfg = cfg;
            Nav = new Navigator((st, cmd) => Exec(st, cmd, "Manöver", force: true));
        }

        // ---------------- Hören ----------------

        /// <summary>Erkanntes von Vosk: "mit Grammatik ‖ frei". ptt = Push-to-talk war gedrückt.</summary>
        Station _speaker = Station.Captain;

        public void Heard(Station speaker, string speakerName, string recognized, bool ptt)
        {
            _speaker = speaker;
            if (!GameBridge.InMission) { string g0, f0; Split(recognized, out g0, out f0); LastHeard = f0.Length > 0 ? f0 : g0; LastResult = "kein Einsatz"; return; }
            string grammar, free;
            Split(recognized, out grammar, out free);
            LastHeard = grammar.Length > 0 ? grammar : free;

            var names = Names();
            var rG = CommandParser.Parse(grammar, names);
            var rF = CommandParser.Parse(free, names);
            var r = rG.Understood ? rG : rF;
            // Anzeige: ergab die Befehlsgrammatik einen Befehl, ihre Fassung (bei Befehlen meist die genauere), sonst der frei erkannte Satz
            string matched = r == rG ? grammar : free;
            LastHeard = rG.Understood || free.Length == 0 ? grammar : free;
            // Namen fehlen in der Grammatik: aus dem freien Text auflösen
            foreach (var c in r.Commands)
            {
                bool fire = c.Intent == Intent.FirePhasers || c.Intent == Intent.FireTorpedoes || c.Intent == Intent.Analyze;
                // Feuerbefehle nur mit ausdrücklichem Ziel ("Feuer auf …"), sonst bleibt das gewählte Ziel
                string tail = fire ? After(free, " auf ") : free;
                string said = After(matched, " auf ") ?? "";
                if (fire && System.Text.RegularExpressions.Regex.IsMatch(said, @"^(das\s+|dem\s+|den\s+)?(markierte\w*\s+|aktuelle\w*\s+)?ziel\w*$")) continue;
                if (c.Resolved != null || (!NeedsName(c.Intent) && !(fire && (c.Name != null || tail != null)))) continue;
                float s;
                c.Resolved = (tail != null ? Phonetics.BestMatch(tail, names, out s) : null) ??
                             (c.Name != null ? Phonetics.BestMatch(c.Name, names, out s) : null) ??
                             Partial(c.Name ?? After(grammar, " auf ")) ?? (tail != null ? Partial(tail) : null);
            }

            bool addressed = r.Addressed.HasValue || r.AddressedAll;
            // Die Befehlsgrammatik presst Ungehörtes in bekannte Wörter ("abschalten" → "abstand halten"). Folgenreiche
            // Befehle ohne Anrede/Taste nur, wenn auch die freie Erkennung sie verstanden hat.
            if (!addressed && !ptt && r == rG)
            {
                // Gerede, das die Grammatik zu Befehlen verbiegt ("ganz genau so" → "dann scanne phaser"), klingt anders als
                // die freie Fassung: gemessen an echten Spielsätzen liegt verbogenes Gerede bei 0,25–0,35, Befehle ab 0,45.
                if (!rF.Understood && free.Length > 0 && Phonetics.Similarity(grammar, free) < 0.40f)
                {
                    Note("Unsicher erkannt, nicht ausgeführt: „" + grammar + "“ / „" + free + "“");
                    return;
                }
                int removed = r.Commands.RemoveAll(c => Risky(c.Intent) && !rF.Commands.Exists(f => f.Intent == c.Intent));
                if (removed > 0) Note("Unsicher erkannt, nicht ausgeführt: " + grammar + " / " + free);
                if (r.Commands.Count == 0)
                {
                    if (!rF.Understood) return;
                    r = rF;   // die freie Fassung ergab einen Befehl ("feuer frei" statt "feuer schleichen")
                    matched = free;
                }
            }
            // Ohne Anrede: ein Satz, der vollständig als Befehl verstanden wurde (Gespräche ergeben keine oder nur Teilbefehle)
            bool shortOrder = r.Understood && r.NotUnderstood.Count == 0 && WordCount(matched) <= 8;
            bool accept = ptt || Listen == ListenMode.Always ||
                          (Listen == ListenMode.Both && (addressed || shortOrder)) ||
                          (Listen == ListenMode.Address && addressed);
            if (!accept)
            {
                if (r.Understood) Note(speakerName + " (ohne Ansprache, ignoriert): " + LastHeard);
                return;
            }
            if (!r.Understood)
            {
                if (addressed || ptt || r.NotUnderstood.Count > 0)
                {
                    LastResult = "nicht verstanden";
                    Note(speakerName + ": „" + LastHeard + "“ – nicht verstanden");
                    Beep(false);
                }
                return;
            }
            if (r.Commands.TrueForAll(c => c.Intent == Intent.Confirm || c.Intent == Intent.Deny)) return;   // "Ja"/"Nein" im Gespräch
            LastResult = r.ToString();
            Note(speakerName + ": „" + LastHeard + "“ → " + LastResult + (free != LastHeard && free.Length > 0 ? "  (frei: „" + free + "“)" : ""));
            Beep(true);
            Execute(r);
        }

        static void Split(string s, out string grammar, out string free)
        {
            int k = (s ?? "").IndexOf('‖');
            grammar = k >= 0 ? s.Substring(0, k).Trim() : (s ?? "").Trim();
            free = k >= 0 ? s.Substring(k + 1).Trim() : grammar;
        }

        static string Plain(string s) { string g, f; Split(s, out g, out f); return g.Length > 0 ? g : f; }
        static int WordCount(string s) => (s ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>Manöver und Daueranweisungen: wirken lange nach, daher nur bei sicherer Erkennung.</summary>
        static bool Risky(Intent i) =>
            i == Intent.AvoidHazard || i == Intent.KeepDistance || i == Intent.CircleTarget || i == Intent.Flee ||
            i == Intent.StealthOn || i == Intent.StayOutOfSensors || i == Intent.HoldShieldsDown || i == Intent.HoldFire ||
            i == Intent.LimitSpeed || i == Intent.ShieldsDown || i == Intent.ReleaseOrders;

        static bool NeedsName(Intent i) =>
            i == Intent.MoveToTarget || i == Intent.SelectTarget || i == Intent.Scan || i == Intent.Attack || i == Intent.Impulse ||
            i == Intent.CircleTarget || i == Intent.KeepDistance;

        static string After(string s, string marker)
        {
            int k = (" " + s + " ").LastIndexOf(marker, StringComparison.Ordinal);
            if (k < 0) return null;
            string rest = (" " + s + " ").Substring(k + marker.Length).Trim();
            return rest.Length > 0 ? rest : null;
        }

        /// <summary>
        /// Teil eines Kontaktnamens ("Mine" → "Gravitationsmine", "Frachter" → "Frachter Alpha 2"): nächster passender Kontakt.
        /// Zusammengesetzte Namen kennt Vosk oft nicht, ein Wortteil kommt aber an.
        /// </summary>
        string Partial(string heard)
        {
            if (string.IsNullOrEmpty(heard)) return null;
            Contact best = null;
            foreach (var word in heard.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = Phonetics.Normalize(word);
                if (w.Length < 4 || w == "schiff" || w == "ziel") continue;
                foreach (var c in _contacts)
                    if (Phonetics.Normalize(c.Name).Contains(w) && (best == null || c.DistanceKm < best.DistanceKm)) best = c;
            }
            return best != null ? best.Name : null;
        }

        List<string> Names()
        {
            var l = new List<string>();
            foreach (var c in _contacts) if (!l.Contains(c.Name)) l.Add(c.Name);
            return l;
        }

        // ---------------- Ausführen ----------------

        void Execute(ParseResult r)
        {
            bool approach = r.Commands.Exists(c => c.Intent == Intent.MoveToTarget);
            foreach (var c in r.Commands)
            {
                switch (c.Intent)
                {
                    // Fahrt
                    case Intent.SpeedFull: Exec(Station.Helm, "SetSpeedFull"); break;
                    case Intent.SpeedHalf: Exec(Station.Helm, "SetSpeedHalf"); break;
                    case Intent.SpeedQuarter: Exec(Station.Helm, "SetSpeedQuarter"); break;
                    case Intent.SpeedStop: Nav.Stop("Stopp"); Exec(Station.Helm, "SetSpeedStop"); break;
                    case Intent.SpeedReverse: Exec(Station.Helm, "SetSpeedReverse"); break;
                    // Navigation
                    case Intent.MoveToTarget:
                        {
                            string t = Target(c);
                            if (t == null && !IsCurrentTarget(c)) { Missing(c); break; }
                            if (Nav.StayOutOfSensors || Nav.AvoidKinds.Count > 0) Nav.Approach(t ?? Contacts.TargetOf(Station.Helm));
                            else { Nav.Stop("neuer Kurs"); Exec(Station.Helm, "MoveTo"); }
                            break;
                        }
                    case Intent.MoveAway: Nav.Stop("ausweichen"); Exec(Station.Helm, "MoveAwayFrom"); break;
                    case Intent.Impulse: if (c.Name != null) Target(c); ImpulseSequence(); break;
                    case Intent.ImpulseEngage: Exec(Station.Helm, "EngageImpulse", force: true); break;
                    case Intent.Warp: WarpSequence(); break;
                    case Intent.WarpEngage: Exec(Station.Helm, "SetSpeedFull"); Exec(Station.Helm, "EngageWarp", force: true); break;
                    case Intent.PrepareWarp: Exec(Station.Engineering, "PrepareWarp"); break;
                    // Taktik
                    case Intent.ShieldsUp: Exec(Station.Tactical, "RaiseShields", force: true); break;
                    case Intent.ShieldsDown: Exec(Station.Tactical, "LowerShields", force: true); break;
                    case Intent.ModulateShields: Exec(Station.Tactical, "ModulateShields"); break;
                    case Intent.FirePhasers: if (c.Resolved != null) Target(c); else { EnsureEnemyTarget(); if (FriendlyTarget()) break; } Exec(Station.Tactical, "FirePhasers", force: true); break;
                    case Intent.FireTorpedoes: if (c.Resolved != null) Target(c); else { EnsureEnemyTarget(); if (FriendlyTarget()) break; } Exec(Station.Tactical, "ArmTorpedoes", force: true); Later(1.5f, "Torpedos", () => Exec(Station.Tactical, "FireTorpedoes", force: true)); break;
                    case Intent.CeaseFire: Exec(Station.Tactical, "CeaseFire"); break;
                    case Intent.ArmTorpedoes: Exec(Station.Tactical, "ArmTorpedoes", force: true); break;
                    case Intent.DisarmTorpedoes: Exec(Station.Tactical, "DisarmTorpedoes"); break;
                    case Intent.TargetEngines: EnsureEnemyTarget(); Exec(Station.Tactical, "TargetEngines", force: true); break;
                    case Intent.TargetShields: EnsureEnemyTarget(); Exec(Station.Tactical, "TargetShields", force: true); break;
                    case Intent.TargetWeapons: EnsureEnemyTarget(); Exec(Station.Tactical, "TargetWeapons", force: true); break;
                    case Intent.CeaseTargeting: Exec(Station.Tactical, "CeaseTargeting"); break;
                    case Intent.Scan: if (c.Name != null && Target(c) == null && !IsCurrentTarget(c)) { Missing(c); break; } Exec(Station.Tactical, "ScanTarget"); break;
                    case Intent.Analyze: if (c.Name != null) Target(c); Exec(Station.Tactical, "AnalyseTarget", force: true); break;
                    case Intent.Attack: if (c.Name != null && Target(c) == null) { Missing(c); break; } if (c.Name == null) { EnsureEnemyTarget(); if (FriendlyTarget()) break; } Exec(Station.Tactical, "EngageTarget", force: true); break;
                    case Intent.SelectTarget:   // ohne gefundenen Namen neben anderen Befehlen meist ein Hörfehler: still übergehen
                        if (Target(c) == null && r.Commands.Count == 1) Missing(c); break;
                    case Intent.RedAlert: Exec(Station.Tactical, "RaiseShields", force: true); break;
                    case Intent.CancelRedAlert: Note("Alarm aufheben: nur an der Captain-Konsole möglich"); break;
                    case Intent.CloakHunt: Exec(Station.Tactical, "CloakHunt", force: true); break;
                    // Technik
                    case Intent.PowerWeapons: Exec(Station.Engineering, "PowerToPhasers", force: true); break;
                    case Intent.PowerShields: Exec(Station.Engineering, "PowerToShields", force: true); break;
                    case Intent.PowerEngines: Exec(Station.Engineering, "PowerToEngines", force: true); break;
                    case Intent.PowerBalanced: Exec(Station.Engineering, "PowerBalanced", force: true); break;
                    case Intent.Repair: Exec(Station.Engineering, RepairCommand(c.Subsystem)); break;
                    case Intent.RepairAll: Exec(Station.Engineering, "RepairBalanced"); break;
                    case Intent.Transport: Exec(Station.Engineering, "TransportFromTarget", force: true); break;
                    case Intent.ReduceSignature: Exec(Station.Engineering, "ReduceSignature", force: true); break;
                    case Intent.Hack:   // Systemübernahme der Taktik ("Antrieb übernehmen")
                        {
                            string s = c.Subsystem ?? "";
                            EnsureEnemyTarget();
                            Exec(Station.Tactical, s.StartsWith("waffe") ? "DisruptWeapons" : s.StartsWith("schild") ? "DisruptShields" :
                                                   s.StartsWith("komm") ? "DisruptComms" : "DisruptEngines", force: true);
                            break;
                        }
                    case Intent.AnswerHail:
                    case Intent.LaunchProbe: Note(c.Intent + ": nur an der Captain-Konsole möglich"); break;
                    // Daueranweisungen
                    case Intent.HoldShieldsDown: SetOrder("shields", "unten halten"); break;
                    case Intent.HoldShieldsUp: SetOrder("shields", "oben halten"); break;
                    case Intent.LimitSpeed: SetOrder("speed", c.Number >= 0 && c.Number <= 35 ? "viertel" : c.Number >= 0 && c.Number <= 60 ? "halb" : c.Clause.Contains("viertel") ? "viertel" : "halb"); break;
                    case Intent.HoldFire: SetOrder("fire", "halten"); break;
                    case Intent.FireOnOrder: SetOrder("fire", "nur auf befehl"); break;
                    case Intent.StealthOn:
                        SetOrder("stealth", "an"); SetOrder("shields", "unten halten"); SetOrder("speed", "viertel"); SetOrder("fire", "halten");
                        Exec(Station.Tactical, "DisarmTorpedoes");
                        Exec(Station.Engineering, "ReduceSignature", force: true);
                        break;
                    case Intent.StealthOff: Orders.Reset(); Note("Schleichfahrt aufgehoben"); break;
                    case Intent.StayOutOfSensors: Nav.StayOutOfSensors = true; Note("Daueranweisung: außerhalb gegnerischer Ortung bleiben"); if (!approach) Nav.Avoid(null, Contacts.TargetOf(Station.Helm)); break;
                    case Intent.ReleaseOrders: Orders.Reset(); Nav.StayOutOfSensors = false; Nav.AvoidKinds.Clear(); Note("Daueranweisungen aufgehoben"); break;
                    // Manöver
                    case Intent.AvoidHazard: Nav.Avoid(HazardKind(c), Contacts.TargetOf(Station.Helm)); Exec(Station.Helm, "SetSpeedHalf"); break;
                    case Intent.KeepDistance: { string t = c.Name != null ? Target(c) : null; Nav.KeepDistance(t ?? NearestEnemyOrTarget(), c.Number); break; }
                    case Intent.CircleTarget: { string t = c.Name != null ? Target(c) : null; Nav.Circle(t ?? Contacts.TargetOf(Station.Helm), c.Number); Exec(Station.Helm, "SetSpeedHalf"); break; }
                    case Intent.HoldPosition: Nav.Stop("Position halten"); Exec(Station.Helm, "SetSpeedStop"); break;
                    case Intent.Flee: Nav.Flee(); break;
                    case Intent.Confirm:
                    case Intent.Deny: break;
                }
            }
        }

        static string RepairCommand(string sub)
        {
            string s = sub ?? "";
            if (s.StartsWith("schild")) return "RepairShields";
            if (s.StartsWith("antrieb") || s.StartsWith("triebwerk")) return "RepairEngines";
            if (s.StartsWith("phaser") || s.StartsWith("waffe")) return "RepairPhasers";
            if (s.StartsWith("torpedo")) return "RepairTorpedoes";
            if (s.StartsWith("warp")) return "RepairWarpcore";
            if (s.StartsWith("sensor")) return "RepairSensors";
            if (s.StartsWith("transport")) return "RepairTransporter";
            return "RepairBalanced";
        }

        static string HazardKind(ParsedCommand c)
        {
            string s = ((c.Name ?? "") + " " + c.Clause).ToLowerInvariant();
            if (s.Contains("mine")) return "mine";
            if (s.Contains("anomal")) return "anomal";
            if (s.Contains("asteroid")) return "asteroid";
            if (s.Contains("nebel")) return "nebel";
            if (s.Contains("feind") || s.Contains("gegner") || s.Contains("klingon") || s.Contains("borg") || s.Contains("bird")) return "feind";
            return c.Name ?? "mine";
        }

        string NearestEnemyOrTarget()
        {
            Contact best = null;
            foreach (var c in _contacts) if (c.Category == HHHDDNLEDBN.Enemy && (best == null || c.DistanceKm < best.DistanceKm)) best = c;
            return best != null ? best.Name : Contacts.TargetOf(Station.Helm);
        }

        static bool IsCurrentTarget(ParsedCommand c)
        {
            string n = (c.Name ?? "").ToLowerInvariant();
            return n.Length == 0 || n.Contains("ziel") || n == "es" || n == "das" || n == "dem markierten ziel" || n.Contains("markiert");
        }

        void Missing(ParsedCommand c)
        {
            Note("Ziel „" + (c.Name ?? "?") + "“ nicht auf den Konsolen gefunden");
            Beep(false);
        }

        /// <summary>Genanntes Ziel auf allen Konsolen auswählen (wie ein Klick des Captains). Name oder null.</summary>
        string Target(ParsedCommand c)
        {
            if (IsCurrentTarget(c) && c.Resolved == null) return Contacts.TargetOf(Station.Helm) ?? Contacts.TargetOf(Station.Tactical);
            return Select(c.Resolved);
        }

        /// <summary>
        /// Eigene oder verbündete Schiffe nur auf ausdrücklichen Befehl mit Namen beschießen ("Feuer auf die Nicholson"),
        /// nie mit einem allgemeinen "Feuer frei", nur weil sie gerade als Ziel gewählt sind.
        /// </summary>
        bool FriendlyTarget()
        {
            var t = ConsoleTarget(Station.Tactical);
            Contact cur = t != null ? _contacts.Find(x => ReferenceEquals(x.Detectable, t)) : null;
            if (cur == null || (cur.Category != HHHDDNLEDBN.Friendly && cur.Category != HHHDDNLEDBN.Self)) return false;
            Note("Nicht gefeuert: " + cur.Name + " ist verbündet (zum Beschuss ausdrücklich nennen)");
            Beep(false);
            return true;
        }

        /// <summary>
        /// Waffen und Subsysteme brauchen ein Ziel auf der Taktik-Konsole ("Brauche einen Zielpunkt"). Das gewählte bleibt
        /// (auch zivile Schiffe: Antrieb lahmlegen); fehlt es, wird genau das Objekt von Captain oder Steuer übernommen
        /// (nicht über den Namen: mehrere Minen heißen gleich), sonst der nächste Feind.
        /// </summary>
        void EnsureEnemyTarget()
        {
            // Das Ziel, das der Befehlende an seiner Konsole markiert hat, gilt; sonst das eigene der Taktik, sonst Captain/Steuer
            var mine = _speaker != Station.Tactical ? ConsoleTarget(_speaker) : null;
            var tac = ConsoleTarget(Station.Tactical);
            if (mine != null) { if (mine != tac) SetAll(mine, Contacts.TargetOf(_speaker)); else Note("Taktik-Ziel: " + Contacts.TargetOf(Station.Tactical)); return; }
            if (tac != null) { Note("Taktik-Ziel: " + Contacts.TargetOf(Station.Tactical)); return; }
            var other = ConsoleTarget(Station.Captain) ?? ConsoleTarget(Station.Helm);
            if (other != null) { SetAll(other, Contacts.TargetOf(Station.Captain) ?? Contacts.TargetOf(Station.Helm)); return; }
            Contact best = null;
            foreach (var c in _contacts) if (c.Category == HHHDDNLEDBN.Enemy && (best == null || c.DistanceKm < best.DistanceKm)) best = c;
            if (best == null) { Note("Kein Gegner in Sicht"); return; }
            Select(best.Name);
        }

        /// <summary>Kontakt per Name auf allen Konsolen wählen; bei gleichen Namen den nächsten.</summary>
        string Select(string name)
        {
            if (name == null) return null;
            Contact contact = null;
            foreach (var c in _contacts)
                if (c.Name == name && c.Detectable is DIENHALJFIO && (contact == null || c.DistanceKm < contact.DistanceKm)) contact = c;
            if (contact == null) return null;
            return SetAll((DIENHALJFIO)contact.Detectable, name) ? name : null;
        }

        static ConsoleShipInterface[] Consoles()
        {
            var ship = GameBridge.PlayerShip;
            if (ship == null) return new ConsoleShipInterface[0];
            return new ConsoleShipInterface[] {
                ship.GetComponent<Crew.ConsoleCaptainShipInterface>(), ship.GetComponent<Crew.ConsoleHelmShipInterface>(),
                ship.GetComponent<Crew.ConsoleTacticalShipInterface>(), ship.GetComponent<Crew.ConsoleEngineeringShipInterface>() };
        }

        static DIENHALJFIO ConsoleTarget(Station st)
        {
            try
            {
                var c = Consoles();
                if (c.Length == 0) return null;
                var console = st == Station.Captain ? c[0] : st == Station.Helm ? c[1] : st == Station.Tactical ? c[2] : c[3];
                return console != null ? console.GetTarget() : null;
            }
            catch { return null; }
        }

        bool SetAll(DIENHALJFIO t, string name)
        {
            try
            {
                foreach (var console in Consoles())
                    if (console != null && console.GetTarget() != t) console.SetTarget(t);
                Note("Ziel: " + name);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Ziel nicht gesetzt: " + e.Message); return false; }
        }
        void SetOrder(string field, string value)
        {
            switch (field)
            {
                case "shields": Orders.Shields = value; break;
                case "speed": Orders.Speed = value; break;
                case "power": Orders.Power = value; break;
                case "fire": Orders.Fire = value; break;
                case "stealth": Orders.Stealth = value; break;
            }
            Note("Daueranweisung: " + (Orders.Describe() ?? "keine"));
            foreach (var q in StandingOrders.ApplyNow(field, value)) Exec(q.Key, q.Value, "Daueranweisung", force: true);
        }

        // ---------------- Abläufe ----------------

        void WarpSequence()
        {
            if (Orders.Blocks("SetSpeedFull") == null) Exec(Station.Helm, "SetSpeedFull");
            Exec(Station.Engineering, "PrepareWarp", force: true);
            JumpSequence("Warp", AI.IAHDEGHOPAK.PrepareWarp, "EngageWarp", 10f, "Kein Warp – ist ein Warpziel auf der Karte gewählt?");
        }

        void ImpulseSequence()
        {
            if (Orders.Blocks("SetSpeedFull") == null) Exec(Station.Helm, "SetSpeedFull");
            Exec(Station.Helm, "PrepareImpulse", force: true);
            JumpSequence("Impuls", AI.IAHDEGHOPAK.PrepareImpulse, "EngageImpulse", 4f, "Kein Impulssprung – ist ein Impulsziel auf der Karte markiert?");
        }

        /// <summary>
        /// Sprung auslösen, sobald die Vorbereitung fertig ist (das Spiel setzt ihr Flag auf dem Schwarzen Brett zurück),
        /// spätestens nach fallback Sekunden. Höchstens zwei Versuche; ein neuer Befehl ersetzt den laufenden Ablauf.
        /// </summary>
        void JumpSequence(string what, AI.IAHDEGHOPAK prepareFlag, string engage, float fallback, string failHint)
        {
            _steps.RemoveAll(x => x.What == what);
            Vector3d start = Contacts.OwnPosition;
            float t0 = Time.time, lastEngage = 0f;
            bool seenPreparing = false;
            int engages = 0;
            Repeat(0.5f, 40f, 0.5f, what, () =>
            {
                if (Jumped(start)) { Note(what + ": Sprung ausgeführt"); return true; }
                bool preparing = Flag(prepareFlag);
                if (preparing) seenPreparing = true;
                bool ready = (seenPreparing && !preparing) || Time.time - t0 > fallback + (seenPreparing ? 8f : 0f);
                if (!ready) return false;
                if (engages < 2 && (engages == 0 || Time.time - lastEngage > 8f))
                {
                    Exec(Station.Helm, engage, force: true);
                    engages++;
                    lastEngage = Time.time;
                    return false;
                }
                if (Time.time - lastEngage > 8f) { Note(failHint); Beep(false); return true; }
                return false;
            });
        }

        static bool Flag(AI.IAHDEGHOPAK key)
        {
            try
            {
                var bb = AI.CommandManager.JOMJCMPJGEK != null ? AI.CommandManager.JOMJCMPJGEK.AACGBEMCAGH : null;
                AI.OJADMPIBCFG v;
                return bb != null && bb.TryGetValue(key, out v) && v.bVal;
            }
            catch { return false; }
        }
        static bool Jumped(Vector3d start)
        {
            var d = Contacts.OwnPosition - start;
            return d.magnitude > 5000.0;
        }

        void Later(float delay, string what, Action a) => _steps.Add(new Step { At = Time.time + delay, Deadline = Time.time + delay, What = what, Do = () => { a(); return true; } });
        void Repeat(float delay, float duration, float every, string what, Func<bool> f) =>
            _steps.Add(new Step { At = Time.time + delay, Deadline = Time.time + delay + duration, Retry = every, What = what, Do = f });

        // ---------------- Befehle ----------------

        /// <summary>Befehl an eine Bot-Station. force = ausdrücklicher Befehl (geht vor Daueranweisungen).</summary>
        bool Exec(Station st, string name, string why = "Befehl", bool force = false)
        {
            var cmd = CrewCommands.Find(name);
            if (cmd == null) { Note("Unbekannter Befehl " + name); return false; }
            if (cmd.Owner != Station.Captain && !GameBridge.IsStationAI(cmd.Owner))
            {
                Note(OfficerAgent.StationDe(cmd.Owner) + " ist von einem Menschen besetzt: " + cmd.Description);
                return false;
            }
            string block = force ? null : Orders.Blocks(name);
            if (block != null) { Note(name + " unterbleibt (" + block + ")"); return false; }
            float last;
            if (_lastRun.TryGetValue(name, out last) && Time.time - last < 0.8f) return false;   // doppelt im selben Satz
            try
            {
                GameBridge.Trigger(cmd);
                _lastRun[name] = Time.time;
                Plugin.Log.LogInfo("Watson: " + OfficerAgent.StationDe(cmd.Owner) + " führt aus: " + name + " (" + why + ")");
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Watson: " + name + " fehlgeschlagen: " + e.Message); return false; }
        }

        // ---------------- Takt ----------------

        public void Tick(ShipStatus status)
        {
            if (!GameBridge.InMission) { if (Nav.Current != Navigator.Mode.None) Nav.Stop("Mission vorbei"); _steps.Clear(); return; }
            _status = status;
            if (status != null && status.ContactList != null) _contacts = status.ContactList;

            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                var s = _steps[i];
                if (Time.time < s.At) continue;
                bool done = false;
                try { done = s.Do(); } catch (Exception e) { Plugin.Log.LogWarning("Ablauf " + s.What + ": " + e.Message); done = true; }
                if (done || Time.time >= s.Deadline) _steps.RemoveAt(i);
                else s.At = Time.time + s.Retry;
            }

            Nav.Tick(_contacts, GameBridge.PlayerShip);
            if (Reflexes) RunReflexes(status);
        }

        float _nextReflex;
        readonly Dictionary<string, float> _reflexAt = new Dictionary<string, float>();

        void RunReflexes(ShipStatus st)
        {
            if (st == null || Time.time < _nextReflex) return;
            _nextReflex = Time.time + 2f;
            bool threat = EventLog.UnderThreat(20f);
            bool recentHit = Time.time - EventLog.LastHitTime < 10f;
            if (threat && st.ShieldState == "PoweredOff") Reflex(Station.Tactical, "RaiseShields", 15f);
            if (recentHit && st.Shields >= 0f && st.Shields < 50f) Reflex(Station.Engineering, "PowerToShields", 30f);
            string worst = null; float min = 75f;
            if (st.Engines >= 0 && st.Engines < min) { min = st.Engines; worst = "RepairEngines"; }
            if (st.Phasers >= 0 && st.Phasers < min) { min = st.Phasers; worst = "RepairPhasers"; }
            if (st.WarpCore >= 0 && st.WarpCore < min) { min = st.WarpCore; worst = "RepairWarpcore"; }
            if (worst != null) Reflex(Station.Engineering, worst, 25f);
        }

        void Reflex(Station st, string name, float cooldown)
        {
            float last;
            if (_reflexAt.TryGetValue(name, out last) && Time.time - last < cooldown) return;
            if (!GameBridge.IsStationAI(st) || Orders.Blocks(name) != null) return;
            _reflexAt[name] = Time.time;
            Exec(st, name, "Reflex");
        }

        // ---------------- Rückmeldung ----------------

        void Note(string s)
        {
            Plugin.Log.LogInfo("Watson: " + s);
            Log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + s);
            while (Log.Count > 8) Log.RemoveAt(0);
        }

        /// <summary>Kurzer Ton: zwei helle für "verstanden", ein tiefer für "nicht verstanden".</summary>
        void Beep(bool ok)
        {
            if (Player == null || !_cfg.WatsonBeeps.Value) return;
            const int rate = 22050;
            var tones = ok ? new[] { 880f, 1320f } : new[] { 330f };
            int len = rate * 70 / 1000, gap = rate * 30 / 1000;
            var pcm = new short[tones.Length * (len + gap)];
            for (int t = 0; t < tones.Length; t++)
                for (int i = 0; i < len; i++)
                {
                    float env = Mathf.Min(1f, Mathf.Min(i, len - i) / (rate * 0.008f));
                    pcm[t * (len + gap) + i] = (short)(Mathf.Sin(2f * Mathf.PI * tones[t] * i / rate) * 6000f * env);
                }
            try { Player.Play(pcm, rate); } catch { }
        }
    }
}
