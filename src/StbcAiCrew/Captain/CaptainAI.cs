using System;
using System.Collections.Generic;
using Sector;
using StbcAiCrew.Voice;
using UnityEngine;

namespace StbcAiCrew.Captain
{
    /// <summary>
    /// KI-Captain ohne Sprachmodell: führt das Schiff, wenn niemand an der Captain-Konsole sitzt. Der Spieler übernimmt eine
    /// andere Station (eingebauter Stationswechsel), der Captain liest die Missionsziele, entscheidet nach einem festen
    /// Entscheidungsbaum und gibt Befehle: an Bots direkt (wie Watson), an Menschen gesprochen und auf der Konsole, wartet auf
    /// Bestätigung, prüft die Ausführung, erinnert und lobt. Captain-Handgriffe (Rufe, Alarm, Warpziel, Zielwahl) macht er selbst.
    ///
    /// Entscheidungsbaum, in dieser Reihenfolge:
    ///   1. Ruf eingehend → annehmen.
    ///   2. Gefahr (Beschuss, Feind nah) → Alarm, Schilde, Ziel, Feuer; Energie auf Schilde bei Treffern.
    ///   3. Missionsziel: falsches Sternsystem → Warp; Ziel zu weit → Impuls/Kurs; dort je nach Art scannen, angreifen,
    ///      lahmlegen, beamen, beschützen.
    ///   4. Ruhe → selten ein Kalauer.
    /// </summary>
    public sealed class CaptainAI
    {
        public bool Enabled;
        /// <summary>An diese Station setzt sich der Spieler zu Missionsbeginn (Einzelspieler).</summary>
        public Station PlayerStation = Station.Helm;
        public float JokeMinutes = 8f;
        public bool Jokes = true;

        /// <summary>Sprechen: Text in der Spielsprache, Station des Adressaten (Konsole zeigt ihn an) oder null für alle.</summary>
        public Action<string, Station?> Speak;
        public Func<bool> Speaking = () => false;

        public bool Active { get; private set; }
        public string Status { get; private set; } = "–";

        readonly WatsonCrew _crew;
        float _goalSince;
        float _missionStart = -1f, _next, _seatTry, _lastSpoke = -999f, _lastJoke, _lastThreat = -999f, _lastHail = -999f;
        int _seatTries;
        bool _announced;
        string _trackKey, _goalAnnounced, _arrivedAt, _selected;
        int _trackCount = -1;
        readonly Dictionary<string, Order> _orders = new Dictionary<string, Order>();
        readonly Dictionary<Station, float> _ackAt = new Dictionary<Station, float>();
        static readonly System.Random Rng = new System.Random();

        sealed class Order
        {
            public string Id;
            public Station St;
            public bool Human;
            public Func<bool> Done;
            public string Line;
            public float At, NextRemind;
            public int Reminders;
            public bool Acked;
        }

        public CaptainAI(WatsonCrew crew)
        {
            _crew = crew;
            _crew.CaptainHears = Heard;
        }

        // ---------------- Takt ----------------

        public void Tick(ShipStatus st)
        {
            if (!Enabled || !GameBridge.InMission) { Reset(); return; }
            if (_missionStart < 0) { _missionStart = Time.time; _lastJoke = Time.time; }
            if (Time.time < _next) return;
            _next = Time.time + 1f;

            TakeSeat();
            Active = !CaptainSense.HumanAtCaptainConsole();
            if (!Active) { Status = Loc.T("Captain-Konsole besetzt", "captain's console manned"); return; }
            if (!_announced && Time.time - _missionStart > 8f)
            {
                _announced = true;
                var seat = CaptainSense.LocalSeat();
                Say(CaptainLines.Say("take_command", CaptainLines.Title(seat ?? PlayerStation)), null);
            }

            try { Decide(st); }
            catch (Exception e) { Plugin.Log.LogWarning("KI-Captain: " + e.Message); }
        }

        void Reset()
        {
            _missionStart = -1f; _announced = false; _seatTries = 0; Active = false;
            _orders.Clear(); _trackKey = null; _goalAnnounced = null; _arrivedAt = null; _selected = null; _trackCount = -1;
        }

        /// <summary>Einzelspieler: Zu Missionsbeginn wechselt der Spieler von der Captain-Konsole an seine Station.</summary>
        void TakeSeat()
        {
            if (PlayerStation == Station.Captain || _seatTries >= 3 || Time.time - _missionStart < 5f || Time.time < _seatTry) return;
            if (CaptainSense.LocalSeat() != Station.Captain) return;
            _seatTry = Time.time + 6f;
            _seatTries++;
            Plugin.Log.LogInfo("KI-Captain: Spieler wechselt an " + PlayerStation);
            CaptainSense.MoveLocalPlayerTo(PlayerStation);
        }

        // ---------------- Entscheidungsbaum ----------------

        void Decide(ShipStatus st)
        {
            var contacts = _crew.ContactsNow ?? new List<Contact>();
            Track();

            // 1. Rufe
            if (CaptainSense.HailPending && Time.time - _lastHail > 6f)
            {
                _lastHail = Time.time;
                Say(CaptainLines.Say("hail"), null);
                CaptainSense.AnswerHail();
                Status = Loc.T("Ruf angenommen", "hail answered");
                return;
            }

            // 2. Gefahr
            Contact enemy = Nearest(contacts, c => c.Category == HHHDDNLEDBN.Enemy && c.DistanceKm >= 0 && c.DistanceKm < 25);
            bool threat = EventLog.UnderThreat(20f) || enemy != null;
            if (threat)
            {
                _lastThreat = Time.time;
                if (!CaptainSense.RedAlert) { CaptainSense.SetRedAlert(true); Say(CaptainLines.Say("red_alert"), null); }
                if (st != null && st.ShieldState == "PoweredOff")
                    Issue("shields", Station.Tactical, "shields", null, () => _crew.Run(Intent.ShieldsUp),
                          () => GameBridge.PlayerShip == null || ShieldsUp());
                if (st != null && st.Shields >= 0 && st.Shields < 40 && Time.time - EventLog.LastHitTime < 10f)
                    Issue("power_shields", Station.Engineering, "power_shields", null, () => _crew.Run(Intent.PowerShields), null, 20f);
            }
            else if (CaptainSense.RedAlert && Time.time - _lastThreat > 45f && _lastThreat > 0)
            {
                CaptainSense.SetRedAlert(false);
                Say(CaptainLines.Say("all_clear"), null);
                _lastThreat = -999f;
            }

            // 3. Missionsziel
            var goals = CaptainSense.OpenGoals();
            Goal g = Pick(goals, contacts);
            if (g != null)
            {
                if (g.Key != _goalAnnounced)
                {
                    _goalAnnounced = g.Key;
                    if (!string.IsNullOrEmpty(g.Text)) Say(CaptainLines.Say("new_goal", Clean(g.Text)), null);
                    _goalSince = Time.time;
                    Plugin.Log.LogInfo("KI-Captain Ziel: [" + g.Kind + "] \"" + g.English + "\" System=" + (g.System != null ? CaptainSense.NameOf(g.System) : "?") +
                                       " Ziel=" + (g.Target != null ? GameBridge.StripLocId(g.Target.BaseName) : "-") + (g.Total > 0 ? " Zähler " + g.Count + "/" + g.Total : "") +
                                       (g.OpenSubGoals > 0 ? " Teilziele " + g.OpenSubGoals : "") + (g.HasTimer ? " Timer" : ""));
                }
                if (_moved) _goalSince = Time.time;
                else if (Time.time - _goalSince > 240f)
                {
                    _goalSince = Time.time;
                    Plugin.Log.LogWarning("KI-Captain hängt seit 4 min: [" + g.Kind + "] \"" + g.English + "\" | " + Status + " | Befehle offen: " +
                                          string.Join(", ", new List<string>(_orders.Keys).ToArray()) + " | System " + CaptainSense.NameOf(CaptainSense.CurrentSystem) +
                                          " | Warpziel " + CaptainSense.NameOf(CaptainSense.WarpTarget));
                }
                Pursue(g, contacts, threat ? enemy : null);
                if (g.Kind != GoalKind.Attack && g.Kind != GoalKind.Defend && enemy != null) Fight(enemy);
            }
            else if (enemy != null) Fight(enemy);
            else Status = Loc.T("keine offenen Ziele", "no open objectives");

            // 4. Ruhe: selten ein Kalauer
            if (Jokes && !threat && _orders.Count == 0 && Time.time - _lastJoke > JokeMinutes * 60f && Time.time - _lastSpoke > 40f && !Speaking())
            {
                _lastJoke = Time.time;
                if (Rng.NextDouble() < 0.5) Say(CaptainLines.Joke(), null);
            }
        }

        /// <summary>Wichtigstes offenes Ziel: zuerst eins im aktuellen System mit sichtbarem Ziel, sonst das erste.</summary>
        Goal Pick(List<Goal> goals, List<Contact> contacts)
        {
            if (goals.Count == 0) return null;
            var here = CaptainSense.CurrentSystem;
            foreach (var g in goals) if (g.Kind != GoalKind.Wait && (g.System == null || g.System == here) && Find(contacts, g.Target) != null) return g;
            foreach (var g in goals) if (g.Kind != GoalKind.Wait) return g;
            return goals[0];
        }

        void Pursue(Goal g, List<Contact> contacts, Contact enemy)
        {
            var here = CaptainSense.CurrentSystem;
            // a) Anderes Sternsystem: Warp
            if (g.System != null && here != null && g.System != here)
            {
                string sys = CaptainSense.NameOf(g.System);
                CaptainSense.SetWarpTarget(g.System);
                Status = Loc.T("Warp nach ", "warp to ") + sys;
                var target = g.System;
                Issue("warp:" + sys, Station.Helm, "warp", sys, () => _crew.Run(Intent.Warp), () => CaptainSense.CurrentSystem == target, 60f);
                return;
            }

            // b) Ziel im System
            Contact c = Find(contacts, g.Target);
            if (c == null)
            {
                Status = Loc.T("Ziel noch nicht in Sensorreichweite: ", "objective not yet on sensors: ") + Clean(g.Text);
                return;
            }
            if (_selected != g.Key) { _crew.Select(c); _selected = g.Key; }   // nur bei neuem Ziel, sonst überschreibt er die Auswahl des Spielers
            string name = c.Name;
            double d = c.DistanceKm;
            Status = Clean(g.Text) + " – " + name + " " + d.ToString("0.0") + " km";

            double reach = g.Reach > 0 ? g.Reach : Reach(g.Kind);
            if (d > reach)
            {
                bool far = d > 60;
                Issue((far ? "impulse:" : "course:") + name, Station.Helm, far ? "impulse" : "course", name,
                      () => _crew.Run(far ? Intent.Impulse : Intent.MoveToTarget, name),
                      () => { var x = Find(_crew.ContactsNow, g.Target); return x == null || x.DistanceKm <= reach; }, far ? 40f : 90f);
                return;
            }
            Done("course:" + name); Done("impulse:" + name);

            switch (g.Kind)
            {
                case GoalKind.Scan:
                    Issue("scan:" + name, Station.Tactical, "scan", name, () => _crew.Run(Intent.Scan, name), GoalMoved, 25f);
                    break;
                case GoalKind.Attack:
                    Issue("attack:" + name, Station.Tactical, "attack", name, () => _crew.Run(Intent.FirePhasers, name), GoalMoved, 30f);
                    break;
                case GoalKind.Disable:
                    Issue("disable:" + name, Station.Tactical, "disable", name, () => _crew.Run(Intent.Hack, name), GoalMoved, 30f);
                    break;
                case GoalKind.Rescue:
                    Issue("stop:" + name, Station.Helm, "stop", null, () => _crew.Run(Intent.SpeedStop), () => { var s = ShipStatus.Read(); return GoalMoved() || (s != null && s.FullStop); }, 30f);
                    Issue("beam:" + name, Station.Engineering, "beam", name, () => _crew.Run(Intent.Transport, name), GoalMoved, 25f);
                    break;
                case GoalKind.Defend:
                    Issue("defend:" + name, Station.Helm, "defend", name, () => _crew.Nav.Circle(name, 5f), GoalMoved, 120f);
                    if (enemy != null) Fight(enemy);
                    break;
                default:   // Anfliegen erreicht
                    if (_orders.Count == 0 && Time.time - _lastSpoke > 15f && _arrivedAt != g.Key)
                    {
                        _arrivedAt = g.Key;
                        Say(CaptainLines.Say("arrived", name), null);
                    }
                    break;
            }
        }

        void Fight(Contact enemy)
        {
            if (enemy == null) return;
            if (_selected != "enemy:" + enemy.Name) { _crew.Select(enemy); _selected = "enemy:" + enemy.Name; }
            Issue("attack:" + enemy.Name, Station.Tactical, "attack", enemy.Name, () => _crew.Run(Intent.FirePhasers, enemy.Name),
                  () => Find(_crew.ContactsNow, enemy.Transform) == null, 30f);
        }

        static double Reach(GoalKind k)
        {
            switch (k)
            {
                case GoalKind.Rescue: return 3.0;     // Transporterreichweite
                case GoalKind.Scan: return 12.0;
                case GoalKind.Attack:
                case GoalKind.Disable: return 10.0;
                case GoalKind.Defend: return 8.0;
                default: return 5.0;
            }
        }

        // ---------------- Fortschritt ----------------

        /// <summary>Hat sich am aktuellen Ziel etwas getan (Zähler, Teilziel, anderes Ziel)?</summary>
        bool _moved;
        bool GoalMoved() => _moved;

        void Track()
        {
            var goals = CaptainSense.OpenGoals();
            string key = goals.Count > 0 ? goals[0].Key : "";
            int count = goals.Count > 0 ? goals[0].Count * 100 + goals[0].OpenSubGoals : -1;
            _moved = false;
            if (_trackKey != null && key != _trackKey)
            {
                // Ziel erledigt (oder ein neues kam dazu): offene Befehle sind hinfällig
                _moved = true;
                if (_trackKey.Length > 0) Say(CaptainLines.Say("goal_done"), null);
                _orders.Clear();
            }
            else if (_trackCount >= 0 && count != _trackCount) _moved = true;
            _trackKey = key;
            _trackCount = count;
        }

        // ---------------- Befehle ----------------

        /// <summary>
        /// Befehl an eine Station. Bot: sofort ausführen (und kurz ansagen). Mensch: ansagen, auf der Konsole zeigen, auf
        /// Bestätigung und Ausführung warten, nach Pausen erinnern, nach drei Erinnerungen aufgeben.
        /// </summary>
        void Issue(string id, Station st, string lineKey, string arg, Action bot, Func<bool> done, float remindAfter = 25f)
        {
            Order o;
            if (_orders.TryGetValue(id, out o))
            {
                if (done != null && done()) { Complete(o); return; }
                if (Time.time < o.NextRemind || Speaking()) return;
                if (!o.Human) { bot(); o.NextRemind = Time.time + remindAfter; return; }   // Bot: einfach erneut
                if (o.Reminders >= 3) { Say(CaptainLines.Say("give_up"), o.St); o.NextRemind = Time.time + 120f; o.Reminders = 0; return; }
                o.Reminders++;
                o.NextRemind = Time.time + remindAfter;
                Say(CaptainLines.Say("remind", CaptainLines.Title(st)) + " " + o.Line, o.St);
                return;
            }
            if (done != null && done()) return;
            bool human = !GameBridge.IsStationAI(st) && st != Station.Captain;
            string line = CaptainLines.Say(lineKey + (human ? "_human" : "_bot"), arg ?? "") ?? CaptainLines.Say(lineKey + "_human", arg ?? "");
            o = new Order { Id = id, St = st, Human = human, Done = done, Line = line, At = Time.time, NextRemind = Time.time + remindAfter };
            _orders[id] = o;
            if (line != null) Say(line, human ? (Station?)st : null);
            if (!human) bot();
            Plugin.Log.LogInfo("KI-Captain: " + (human ? "an Mensch " : "an Bot ") + st + ": " + id);
        }

        void Complete(Order o)
        {
            _orders.Remove(o.Id);
            if (o.Human && Time.time - _lastSpoke > 4f && Rng.NextDouble() < 0.6) Say(CaptainLines.Say("praise", CaptainLines.Title(o.St)), o.St);
        }

        void Done(string id) => _orders.Remove(id);

        /// <summary>Spieler hat etwas gesagt: Bestätigung eines offenen Befehls an seine Station?</summary>
        void Heard(Station speaker, string text)
        {
            if (!Active || !CaptainLines.IsAcknowledgement(text)) return;
            foreach (var o in _orders.Values)
                if (o.Human && o.St == speaker && !o.Acked)
                {
                    o.Acked = true;
                    o.NextRemind = Time.time + 40f;   // bestätigt: länger Zeit lassen
                    if (Rng.NextDouble() < 0.35) Say(CaptainLines.Say("ack"), speaker);
                }
            _ackAt[speaker] = Time.time;
        }

        // ---------------- Hilfen ----------------

        void Say(string text, Station? to)
        {
            if (string.IsNullOrEmpty(text)) return;
            _lastSpoke = Time.time;
            Plugin.Log.LogInfo("KI-Captain sagt: " + text);
            if (Speak != null) Speak(text, to);
        }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");   // Formatierung der Konsole
            return s.Trim().TrimEnd('.');
        }

        static bool ShieldsUp()
        {
            var s = ShipStatus.Read();
            return s != null && s.ShieldState != "PoweredOff";
        }

        static Contact Find(List<Contact> contacts, SystemTransform t)
        {
            if (contacts == null || t == null) return null;
            foreach (var c in contacts) if (c.Transform == t) return c;
            return null;
        }

        static Contact Nearest(List<Contact> contacts, Func<Contact, bool> pred)
        {
            Contact best = null;
            foreach (var c in contacts) if (pred(c) && (best == null || c.DistanceKm < best.DistanceKm)) best = c;
            return best;
        }
    }
}
