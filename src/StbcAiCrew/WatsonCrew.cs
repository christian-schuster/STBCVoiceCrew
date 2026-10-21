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
        public void Heard(Station speaker, string speakerName, string recognized, bool ptt)
        {
            if (!GameBridge.InMission) { LastHeard = Plain(recognized); LastResult = "kein Einsatz"; return; }
            string grammar, free;
            Split(recognized, out grammar, out free);
            LastHeard = grammar.Length > 0 ? grammar : free;

            var names = Names();
            var rG = CommandParser.Parse(grammar, names);
            var rF = CommandParser.Parse(free, names);
            var r = rG.Understood ? rG : rF;
            // Namen fehlen in der Grammatik: aus dem freien Text auflösen
            foreach (var c in r.Commands)
                if (c.Resolved == null && NeedsName(c.Intent))
                {
                    float s;
                    c.Resolved = Phonetics.BestMatch(free, names, out s) ?? (c.Name != null ? Phonetics.BestMatch(c.Name, names, out s) : null);
                }

            bool addressed = r.Addressed.HasValue || r.AddressedAll;
            bool shortOrder = r.Understood && WordCount(LastHeard) <= 4;
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
            Note(speakerName + ": „" + LastHeard + "“ → " + LastResult);
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

        static bool NeedsName(Intent i) =>
            i == Intent.MoveToTarget || i == Intent.SelectTarget || i == Intent.Scan || i == Intent.Attack || i == Intent.Impulse ||
            i == Intent.CircleTarget || i == Intent.KeepDistance;

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
                    case Intent.FirePhasers: if (c.Name != null) Target(c); Exec(Station.Tactical, "FirePhasers", force: true); break;
                    case Intent.FireTorpedoes: if (c.Name != null) Target(c); Exec(Station.Tactical, "ArmTorpedoes", force: true); Later(1.5f, "Torpedos", () => Exec(Station.Tactical, "FireTorpedoes", force: true)); break;
                    case Intent.CeaseFire: Exec(Station.Tactical, "CeaseFire"); break;
                    case Intent.ArmTorpedoes: Exec(Station.Tactical, "ArmTorpedoes", force: true); break;
                    case Intent.DisarmTorpedoes: Exec(Station.Tactical, "DisarmTorpedoes"); break;
                    case Intent.TargetEngines: Exec(Station.Tactical, "TargetEngines", force: true); break;
                    case Intent.TargetShields: Exec(Station.Tactical, "TargetShields", force: true); break;
                    case Intent.TargetWeapons: Exec(Station.Tactical, "TargetWeapons", force: true); break;
                    case Intent.CeaseTargeting: Exec(Station.Tactical, "CeaseTargeting"); break;
                    case Intent.Scan: if (c.Name != null && Target(c) == null && !IsCurrentTarget(c)) { Missing(c); break; } Exec(Station.Tactical, "ScanTarget"); break;
                    case Intent.Analyze: if (c.Name != null) Target(c); Exec(Station.Tactical, "AnalyseTarget", force: true); break;
                    case Intent.Attack: if (c.Name != null && Target(c) == null) { Missing(c); break; } Exec(Station.Tactical, "EngageTarget", force: true); break;
                    case Intent.SelectTarget: if (Target(c) == null) Missing(c); break;
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
                    case Intent.Hack: Exec(Station.Tactical, "DisruptShields", force: true); break;
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
            string name = c.Resolved;
            if (name == null) return null;
            Contact contact = _contacts.Find(x => x.Name == name);
            var t = contact != null ? contact.Detectable as DIENHALJFIO : null;
            if (t == null) return null;
            var ship = GameBridge.PlayerShip;
            if (ship == null) return null;
            try
            {
                foreach (ConsoleShipInterface console in new ConsoleShipInterface[] {
                    ship.GetComponent<Crew.ConsoleCaptainShipInterface>(), ship.GetComponent<Crew.ConsoleHelmShipInterface>(),
                    ship.GetComponent<Crew.ConsoleTacticalShipInterface>(), ship.GetComponent<Crew.ConsoleEngineeringShipInterface>() })
                    if (console != null && console.GetTarget() != t) console.SetTarget(t);
                Note("Ziel: " + name);
                return name;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Ziel nicht gesetzt: " + e.Message); return null; }
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
            Exec(Station.Engineering, "PrepareWarp", force: true);
            if (Orders.Blocks("SetSpeedFull") == null) Exec(Station.Helm, "SetSpeedFull");
            Vector3d start = Contacts.OwnPosition;
            Repeat(10f, 25f, 3f, "Warp", () =>
            {
                if (Jumped(start)) return true;
                Exec(Station.Helm, "EngageWarp", force: true);
                return false;
            });
        }

        void ImpulseSequence()
        {
            if (Orders.Blocks("SetSpeedFull") == null) Exec(Station.Helm, "SetSpeedFull");
            Exec(Station.Helm, "PrepareImpulse", force: true);
            Vector3d start = Contacts.OwnPosition;
            Repeat(6f, 20f, 3f, "Impuls", () =>
            {
                if (Jumped(start)) return true;
                Exec(Station.Helm, "EngageImpulse", force: true);
                return false;
            });
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
