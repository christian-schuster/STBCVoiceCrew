using System;
using System.Collections.Generic;
using Sector;
using StbcAiCrew.Voice;
using UnityEngine;

namespace StbcAiCrew.Captain
{
    /// <summary>
    /// First Officer ohne Sprachmodell: immer dabei, in der Lobby und auf der Brücke.
    ///   • Sitzt ein Mensch auf dem Captain-Stuhl, berät er: Warnungen bei Gefahr oder Fehlern, Hinweise, wenn der Captain
    ///     beim Missionsziel nicht weiterkommt oder nichts tut, eine Empfehlung auf Nachfrage ("Nummer Eins, Vorschlag?").
    ///   • Ist der Stuhl leer, führt er das Schiff: Missionsziele lesen, entscheiden, Befehle an Bots (direkt) und Menschen
    ///     (gesprochen und auf der Konsole, mit Bestätigung, Erinnerung, Lob).
    ///   • In ruhigen Momenten, auch in der Lobby, selten ein Kalauer.
    /// Er spricht nur, wenn sonst niemand spricht (Crew, Spiel, Mitspieler), kurz und knapp.
    ///
    /// Entscheidungsbaum (Kommando), in dieser Reihenfolge:
    ///   1. Ruf eingehend → annehmen.
    ///   2. Gefahr (Beschuss, Feind nah) → Alarm, Schilde, Ziel, Feuer; Energie auf Schilde bei Treffern.
    ///   3. Missionsziel: falsches Sternsystem → Warp; Ziel zu weit → Impuls/Kurs; dort je nach Art scannen, angreifen,
    ///      lahmlegen, beamen, beschützen.
    ///   4. Ruhe → selten ein Kalauer.
    /// Als Berater prüft er dieselben Schritte und sagt den nächsten, statt ihn zu befehlen.
    /// </summary>
    public sealed class FirstOfficer
    {
        public enum Mode { Off, Lobby, Tutorial, Advising, Commanding }

        public bool Enabled = true;
        /// <summary>Schiff führen, wenn niemand auf dem Captain-Stuhl sitzt.</summary>
        public bool Command = true;
        public bool Hints = true;
        public bool Jokes = true;
        public float JokeMinutes = 8f;
        /// <summary>"Sie haben die Brücke": an diese Station wechselt der Spieler.</summary>
        public Station PlayerStation = Station.Helm;

        /// <summary>Sprechen: Text in der Spielsprache, Station des Adressaten (Konsole zeigt ihn an) oder null für alle.</summary>
        public Action<string, Station?> Speak;
        /// <summary>Spricht gerade irgendwer (First Officer, Crew, Spiel, Mitspieler)?</summary>
        public Func<bool> Speaking = () => false;
        /// <summary>Ein- oder ausgeschaltet (per Sprache): Einstellung speichern.</summary>
        public Action<bool> Toggled;

        public Mode Current { get; private set; } = Mode.Off;
        public bool Active => Current == Mode.Commanding;
        public string Status { get; private set; } = "–";

        readonly WatsonCrew _crew;
        float _goalSince;
        float _missionStart = -1f, _next, _lastSpoke = -999f, _lastJoke, _lastThreat = -999f, _lastHail = -999f, _hailSince = -1f;
        float _lobbySince = -1f, _lobbyLastJoke, _lobbyLastSpoke = -999f, _lobbyNext, _connGivenAt = -999f;
        bool _greeted, _bridgeGreeted, _askedHint, _announceTake;
        string _trackKey, _goalAnnounced, _arrivedAt, _selected;
        int _trackCount = -1;
        readonly Dictionary<string, Order> _orders = new Dictionary<string, Order>();
        readonly Dictionary<string, float> _advised = new Dictionary<string, float>();
        static readonly System.Random Rng = new System.Random();

        sealed class Order
        {
            public string Id;
            public Station St;
            public bool Human;
            public Func<bool> Done;
            public string Line;
            public float NextRemind;
            public int Reminders;
            public bool Acked;
        }

        /// <summary>Nächster Schritt zum Missionsziel: als Befehl (Kommando) oder als Hinweis (Beratung).</summary>
        sealed class Step
        {
            public string Id, LineKey, Arg;
            public Station St;
            public Action Prep;      // nur im Kommando: Captain-Handgriff vorab (Warpziel setzen, Ziel wählen)
            public Action Bot;       // null = nichts zu befehlen (nur Hinweis)
            public Func<bool> Done;
            public float Remind = 25f;
        }

        public FirstOfficer(WatsonCrew crew)
        {
            _crew = crew;
            _crew.CaptainHears = Heard;
        }

        // ---------------- Takt ----------------

        public void Tick(ShipStatus st)
        {
            if (!Enabled) { ResetMission(); Current = Mode.Off; Status = Loc.T("aus", "off"); return; }
            if (!GameBridge.InMission) { ResetMission(); Lobby(); return; }
            _lobbySince = -1f;
            if (_missionStart < 0) { _missionStart = Time.time; _lastJoke = Time.time; }
            if (Time.time < _next) return;
            _next = Time.time + 1f;

            if (CaptainSense.InTutorial) { Current = Mode.Tutorial; Status = Loc.T("Tutorial – hält sich raus", "tutorial – staying out of it"); return; }

            try
            {
                Track();
                // Nach "Sie haben die Brücke" sitzt der Spieler noch kurz auf dem Stuhl, bis der Wechsel durch ist
                bool command = Command && (!CaptainSense.HumanAtCaptainConsole() || Time.time - _connGivenAt < 8f);
                if (command && Current != Mode.Commanding)
                {
                    _orders.Clear(); _selected = null;
                    _announceTake = true;
                    _bridgeGreeted = true;
                }
                else if (!command && Current == Mode.Commanding)
                {
                    _orders.Clear();
                    _announceTake = false;
                    Say(OfficerLines.Say("hand_back"), Station.Captain);
                }
                Current = command ? Mode.Commanding : Mode.Advising;
                if (_announceTake && Time.time - _missionStart > 8f && !Speaking())
                {
                    _announceTake = false;
                    var seat = CaptainSense.LocalSeat();
                    Say(OfficerLines.Say("take_command", OfficerLines.Title(seat ?? PlayerStation)), null);
                }

                if (!_bridgeGreeted && Time.time - _missionStart > 6f && !Speaking())
                {
                    _bridgeGreeted = true;
                    Say(OfficerLines.Say("bridge_ready"), Station.Captain);
                }

                bool threat = command ? Decide(st) : Advise(st);
                Joke(threat);
            }
            catch (Exception e) { Plugin.Log.LogWarning("First Officer: " + e.Message); }
        }

        void ResetMission()
        {
            if (_missionStart < 0) return;
            _missionStart = -1f; _bridgeGreeted = false; _hailSince = -1f; _announceTake = false; _connGivenAt = -999f; Current = Mode.Lobby;
            _orders.Clear(); _advised.Clear(); _trackKey = null; _goalAnnounced = null; _arrivedAt = null; _selected = null; _trackCount = -1;
        }

        /// <summary>Lobby: einmal melden, danach selten ein Kalauer.</summary>
        void Lobby()
        {
            Current = Mode.Lobby;
            Status = Loc.T("in Bereitschaft", "standing by");
            float now = Time.realtimeSinceStartup;
            if (now < _lobbyNext) return;
            _lobbyNext = now + 1f;
            if (_lobbySince < 0) { _lobbySince = now; _lobbyLastJoke = now; }
            if (Speaking()) return;
            if (!_greeted)
            {
                if (now - _lobbySince < 10f) return;
                _greeted = true;
                _lobbyLastSpoke = now;
                Say(OfficerLines.Say("greet"), null);
                return;
            }
            if (!Jokes || now - _lobbyLastJoke < JokeMinutes * 60f || now - _lobbyLastSpoke < 60f) return;
            _lobbyLastJoke = now;
            if (Rng.NextDouble() < 0.5) { _lobbyLastSpoke = now; Say(Rng.NextDouble() < 0.3 ? OfficerLines.Say("lobby_idle") : OfficerLines.Joke(), null); }
        }

        void Joke(bool threat)
        {
            if (!Jokes || threat || _orders.Count > 0 || Time.time - _lastJoke < JokeMinutes * 60f || Time.time - _lastSpoke < 40f || Speaking()) return;
            _lastJoke = Time.time;
            if (Rng.NextDouble() < 0.5) Say(OfficerLines.Joke(), null);
        }

        // ---------------- Kommando: Entscheidungsbaum ----------------

        /// <summary>Schiff führen. Rückgabe: Gefahr.</summary>
        bool Decide(ShipStatus st)
        {
            var contacts = _crew.ContactsNow ?? new List<Contact>();

            // 1. Rufe
            if (CaptainSense.HailPending && Time.time - _lastHail > 6f)
            {
                _lastHail = Time.time;
                Say(OfficerLines.Say("hail"), null);
                CaptainSense.AnswerHail();
                Status = Loc.T("Ruf angenommen", "hail answered");
                return false;
            }

            // 2. Gefahr
            Contact enemy = Nearest(contacts, c => c.Category == HHHDDNLEDBN.Enemy && c.DistanceKm >= 0 && c.DistanceKm < 25);
            bool threat = EventLog.UnderThreat(20f) || enemy != null;
            if (threat)
            {
                _lastThreat = Time.time;
                if (!CaptainSense.RedAlert) { CaptainSense.SetRedAlert(true); Say(OfficerLines.Say("red_alert"), null); }
                if (st != null && st.ShieldState == "PoweredOff")
                    Issue(new Step { Id = "shields", St = Station.Tactical, LineKey = "shields", Bot = () => _crew.Run(Intent.ShieldsUp),
                                     Done = () => GameBridge.PlayerShip == null || ShieldsUp() });
                if (st != null && st.Shields >= 0 && st.Shields < 40 && Time.time - EventLog.LastHitTime < 10f)
                    Issue(new Step { Id = "power_shields", St = Station.Engineering, LineKey = "power_shields", Bot = () => _crew.Run(Intent.PowerShields), Remind = 20f });
            }
            else if (CaptainSense.RedAlert && Time.time - _lastThreat > 45f && _lastThreat > 0)
            {
                CaptainSense.SetRedAlert(false);
                Say(OfficerLines.Say("all_clear"), null);
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
                    if (!string.IsNullOrEmpty(g.Text)) Say(OfficerLines.Say("new_goal", Clean(g.Text)), null);
                }
                foreach (var s in Plan(g, contacts, threat ? enemy : null)) { if (s.Prep != null) s.Prep(); if (s.Bot != null) Issue(s); }
                if (g.Kind != GoalKind.Attack && g.Kind != GoalKind.Defend && enemy != null) Issue(FightStep(enemy, true));
            }
            else if (enemy != null) Issue(FightStep(enemy, true));
            else Status = Loc.T("keine offenen Ziele", "no open objectives");
            return threat;
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

        /// <summary>Schritte zum Ziel, wie ein erfahrener Captain sie gehen würde (Missionsablauf aus Ziel, System, Entfernung).</summary>
        List<Step> Plan(Goal g, List<Contact> contacts, Contact enemy)
        {
            var steps = new List<Step>();
            var here = CaptainSense.CurrentSystem;

            // a) Anderes Sternsystem: Warp
            if (g.System != null && here != null && g.System != here)
            {
                string sys = CaptainSense.NameOf(g.System);
                var target = g.System;
                Status = Loc.T("Warp nach ", "warp to ") + sys;
                steps.Add(new Step { Id = "warp:" + sys, St = Station.Helm, LineKey = "warp", Arg = sys, Prep = () => CaptainSense.SetWarpTarget(target),
                                     Bot = () => _crew.Run(Intent.Warp), Done = () => CaptainSense.CurrentSystem == target, Remind = 60f });
                return steps;
            }

            // b) Ziel im System
            Contact c = Find(contacts, g.Target);
            if (c == null)
            {
                Status = Loc.T("Ziel noch nicht in Sensorreichweite: ", "objective not yet on sensors: ") + Clean(g.Text);
                if (g.Target != null) steps.Add(new Step { Id = "search:" + g.Key, St = Station.Helm, LineKey = "search", Arg = Clean(g.Text) });
                return steps;
            }
            string key = g.Key;
            string name = c.Name;
            double d = c.DistanceKm;
            Status = Clean(g.Text) + " – " + name + " " + d.ToString("0.0") + " km";
            Action select = () => { if (_selected != key) { _crew.Select(c); _selected = key; } };   // nur bei neuem Ziel, sonst überschreibt er die Auswahl des Spielers

            double reach = g.Reach > 0 ? g.Reach : Reach(g.Kind);
            if (d > reach)
            {
                bool far = d > 60;
                steps.Add(new Step { Id = (far ? "impulse:" : "course:") + name, St = Station.Helm, LineKey = far ? "impulse" : "course", Arg = name, Prep = select,
                                     Bot = () => _crew.Run(far ? Intent.Impulse : Intent.MoveToTarget, name),
                                     Done = () => { var x = Find(_crew.ContactsNow, g.Target); return x == null || x.DistanceKm <= reach; }, Remind = far ? 40f : 90f });
                return steps;
            }
            Done("course:" + name); Done("impulse:" + name);

            switch (g.Kind)
            {
                case GoalKind.Scan:
                    steps.Add(new Step { Id = "scan:" + name, St = Station.Tactical, LineKey = "scan", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.Scan, name), Done = GoalMoved });
                    break;
                case GoalKind.Attack:
                    steps.Add(new Step { Id = "attack:" + name, St = Station.Tactical, LineKey = "attack", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.FirePhasers, name), Done = GoalMoved, Remind = 30f });
                    break;
                case GoalKind.Disable:
                    // Kommt das Stören nicht voran (manche Schiffe können nicht hacken), mit Phasern auf den Antrieb
                    if (Time.time - _goalSince > 75f)
                        steps.Add(new Step { Id = "disable_fire:" + name, St = Station.Tactical, LineKey = "disable_fire", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.FirePhasers, name), Done = GoalMoved, Remind = 30f });
                    else
                        steps.Add(new Step { Id = "disable:" + name, St = Station.Tactical, LineKey = "disable", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.Hack, name), Done = GoalMoved, Remind = 30f });
                    break;
                case GoalKind.Rescue:
                    steps.Add(new Step { Id = "stop:" + name, St = Station.Helm, LineKey = "stop", Prep = select, Bot = () => _crew.Run(Intent.SpeedStop),
                                         Done = () => { var s = ShipStatus.Read(); return GoalMoved() || (s != null && (s.FullStop || !s.Moving)); }, Remind = 30f });
                    steps.Add(new Step { Id = "beam:" + name, St = Station.Engineering, LineKey = "beam", Arg = name, Bot = () => _crew.Run(Intent.Transport, name), Done = GoalMoved });
                    break;
                case GoalKind.Defend:
                    steps.Add(new Step { Id = "defend:" + name, St = Station.Helm, LineKey = "defend", Arg = name, Prep = select, Bot = () => _crew.Nav.Circle(name, 5f), Done = GoalMoved, Remind = 120f });
                    if (enemy != null) steps.Add(FightStep(enemy, false));
                    break;
                default:   // Anfliegen erreicht
                    if (Current == Mode.Commanding && _orders.Count == 0 && Time.time - _lastSpoke > 15f && _arrivedAt != key)
                    {
                        _arrivedAt = key;
                        Say(OfficerLines.Say("arrived", name), null);
                    }
                    break;
            }
            return steps;
        }

        Step FightStep(Contact enemy, bool select)
        {
            string n = enemy.Name;
            return new Step
            {
                Id = "attack:" + n, St = Station.Tactical, LineKey = "attack", Arg = n,
                Prep = select ? () => { if (_selected != "enemy:" + n) { _crew.Select(enemy); _selected = "enemy:" + n; } } : (Action)null,
                Bot = () => _crew.Run(Intent.FirePhasers, n),
                Done = () => Find(_crew.ContactsNow, enemy.Transform) == null, Remind = 30f
            };
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

        // ---------------- Beratung ----------------

        /// <summary>
        /// Ein Mensch ist Captain: aufpassen und nur dann etwas sagen, wenn es zählt. Gefährliches und Falsches sofort (kurze
        /// Sperre je Warnung), Hinweise zum Missionsziel erst, wenn der Captain festhängt oder nichts tut. Rückgabe: Gefahr.
        /// </summary>
        bool Advise(ShipStatus st)
        {
            var contacts = _crew.ContactsNow ?? new List<Contact>();
            Contact enemy = Nearest(contacts, c => c.Category == HHHDDNLEDBN.Enemy && c.DistanceKm >= 0 && c.DistanceKm < 25);
            bool hit = Time.time - EventLog.LastHitTime < 12f;
            bool threat = EventLog.UnderThreat(20f) || enemy != null;
            if (threat) _lastThreat = Time.time;

            var goals = CaptainSense.OpenGoals();
            Goal g = Pick(goals, contacts);
            var steps = g != null ? Plan(g, contacts, threat ? enemy : null) : new List<Step>();
            if (g == null) Status = enemy != null ? Loc.T("Feind in der Nähe", "enemy nearby") : Loc.T("keine offenen Ziele", "no open objectives");

            // Auf Nachfrage: Empfehlung sofort
            if (_askedHint)
            {
                _askedHint = false;
                Step next = steps.Find(s => s.Done == null || !s.Done());
                if (hit && st != null && st.ShieldState == "PoweredOff") Tell("warn_shields", null);
                else if (enemy != null && (g == null || (g.Kind != GoalKind.Attack && g.Kind != GoalKind.Defend))) Tell("hint_attack", enemy.Name);
                else if (next != null) Tell("hint_" + next.LineKey, next.Arg);
                else Tell("no_hint", null);
                return threat;
            }
            if (!Hints) return threat;

            // Gefahr und Fehler
            if (st != null)
            {
                if (hit && st.ShieldState == "PoweredOff" && Advice("w_shields", 25f, true, "warn_shields", null)) return threat;
                if (hit && st.Hull >= 0 && st.Hull < 25 && Advice("w_hull", 90f, true, "warn_hull", null)) return threat;
                if (hit && !CaptainSense.RedAlert && Advice("w_alert", 180f, false, "warn_red_alert", null)) return threat;
                if (hit && st.Shields >= 0 && st.Shields < 30 && st.ShieldState != "PoweredOff" && Advice("w_power", 90f, false, "hint_power_shields", null)) return threat;
            }
            if (CaptainSense.HailPending) { if (_hailSince < 0) _hailSince = Time.time; }
            else _hailSince = -1f;
            if (_hailSince > 0 && Time.time - _hailSince > 15f && Advice("w_hail", 45f, false, "hint_hail", null)) return threat;

            var tac = WatsonCrew.TargetAt(Station.Tactical);
            Contact sel = tac != null ? contacts.Find(x => ReferenceEquals(x.Detectable, tac)) : null;
            if (sel != null && sel.Category == HHHDDNLEDBN.Friendly && Advice("w_friend:" + sel.Name, 300f, true, "warn_friendly", sel.Name)) return threat;

            if (g == null) return threat;

            // Warpziel führt weg von allen Missionszielen
            var wt = CaptainSense.WarpTarget;
            if (wt != null && g.System != null && wt != CaptainSense.CurrentSystem && wt != g.System && !goals.Exists(x => x.System == wt) &&
                Advice("w_warp:" + CaptainSense.NameOf(wt), 180f, true, "warn_wrong_warp", CaptainSense.NameOf(g.System))) return threat;

            // Missionswissen: lahmlegen heißt nicht zerstören
            if (g.Kind == GoalKind.Disable && sel != null && sel.Transform == g.Target && Advice("w_disable:" + g.Key, 600f, false, "hint_disable_only", sel.Name)) return threat;
            if (g.HasTimer && Advice("w_timer:" + g.Key, 9999f, false, "hint_timer", null)) return threat;

            // Festgefahren oder untätig: nächsten Schritt vorschlagen
            Step step = steps.Find(s => s.Done == null || !s.Done());
            if (step == null) return threat;
            bool idle = st != null && !st.Moving && !threat;
            float wait = g.HasTimer ? 60f : idle ? 75f : 150f;
            if (Time.time - _goalSince > wait) Advice("h:" + step.Id, 150f, false, "hint_" + step.LineKey, step.Arg);
            return threat;
        }

        /// <summary>Hinweis an den Captain, wenn er nicht schon kürzlich kam und gerade niemand spricht.</summary>
        bool Advice(string key, float cooldown, bool urgent, string lineKey, string arg)
        {
            float t;
            if (_advised.TryGetValue(key, out t) && Time.time - t < cooldown) return false;
            if (Time.time - _lastSpoke < (urgent ? 8f : 25f) || Speaking()) return false;
            string line = OfficerLines.Say(lineKey, arg ?? "");
            if (line == null) return false;
            _advised[key] = Time.time;
            Plugin.Log.LogInfo("First Officer Hinweis [" + key + "]");
            Say(line, Station.Captain);
            return true;
        }

        void Tell(string lineKey, string arg) => Say(OfficerLines.Say(lineKey, arg ?? ""), Station.Captain);

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
            if (_trackKey != key)
            {
                // Ziel erledigt (oder ein neues kam dazu): offene Befehle sind hinfällig
                _moved = true;
                if (_trackKey != null && _trackKey.Length > 0 && Current == Mode.Commanding) Say(OfficerLines.Say("goal_done"), null);
                _orders.Clear();
                if (goals.Count > 0)
                {
                    var g = goals[0];
                    Plugin.Log.LogInfo("First Officer Ziel: [" + g.Kind + "] \"" + g.English + "\" System=" + (g.System != null ? CaptainSense.NameOf(g.System) : "?") +
                                       " Ziel=" + (g.Target != null ? GameBridge.StripLocId(g.Target.BaseName) : "-") + (g.Total > 0 ? " Zähler " + g.Count + "/" + g.Total : "") +
                                       (g.OpenSubGoals > 0 ? " Teilziele " + g.OpenSubGoals : "") + (g.HasTimer ? " Timer" : ""));
                }
            }
            else if (_trackCount >= 0 && count != _trackCount) _moved = true;
            _trackKey = key;
            _trackCount = count;
            if (_moved) _goalSince = Time.time;
            else if (goals.Count > 0 && Time.time - _goalSince > 240f && Time.time - _stallLogged > 240f)
            {
                _stallLogged = Time.time;
                Plugin.Log.LogWarning("First Officer: Ziel hängt seit " + Mathf.RoundToInt(Time.time - _goalSince) + " s (" + Current + "): [" + goals[0].Kind + "] \"" +
                                      goals[0].English + "\" | " + Status + " | Befehle offen: " + string.Join(", ", new List<string>(_orders.Keys).ToArray()) +
                                      " | System " + CaptainSense.NameOf(CaptainSense.CurrentSystem) + " | Warpziel " + CaptainSense.NameOf(CaptainSense.WarpTarget));
            }
        }
        float _stallLogged = -999f;

        // ---------------- Befehle ----------------

        /// <summary>
        /// Befehl an eine Station. Bot: sofort ausführen (und kurz ansagen). Mensch: ansagen, auf der Konsole zeigen, auf
        /// Bestätigung und Ausführung warten, nach Pausen erinnern, nach drei Erinnerungen aufgeben.
        /// </summary>
        void Issue(Step s)
        {
            Order o;
            if (_orders.TryGetValue(s.Id, out o))
            {
                if (s.Done != null && s.Done()) { Complete(o); return; }
                if (Time.time < o.NextRemind || Speaking()) return;
                if (!o.Human) { s.Bot(); o.NextRemind = Time.time + s.Remind; return; }   // Bot: einfach erneut
                if (o.Reminders >= 3) { Say(OfficerLines.Say("give_up"), o.St); o.NextRemind = Time.time + 120f; o.Reminders = 0; return; }
                o.Reminders++;
                o.NextRemind = Time.time + s.Remind;
                Say(OfficerLines.Say("remind", OfficerLines.Title(s.St)) + " " + o.Line, o.St);
                return;
            }
            if (s.Done != null && s.Done()) return;
            bool human = !GameBridge.IsStationAI(s.St) && s.St != Station.Captain;
            string line = OfficerLines.Say(s.LineKey + (human ? "_human" : "_bot"), s.Arg ?? "") ?? OfficerLines.Say(s.LineKey + "_human", s.Arg ?? "");
            o = new Order { Id = s.Id, St = s.St, Human = human, Done = s.Done, Line = line, NextRemind = Time.time + s.Remind };
            _orders[s.Id] = o;
            if (line != null) Say(line, human ? (Station?)s.St : null);
            if (!human) s.Bot();
            Plugin.Log.LogInfo("First Officer: " + (human ? "an Mensch " : "an Bot ") + s.St + ": " + s.Id);
        }

        void Complete(Order o)
        {
            _orders.Remove(o.Id);
            if (o.Human && Time.time - _lastSpoke > 4f && Rng.NextDouble() < 0.6) Say(OfficerLines.Say("praise", OfficerLines.Title(o.St)), o.St);
        }

        void Done(string id) => _orders.Remove(id);

        // ---------------- Zuhören ----------------

        /// <summary>
        /// Spieler hat etwas gesagt. Sprachbefehle an den First Officer ("Nummer Eins, Vorschlag?", "Sie haben die Brücke",
        /// "wegtreten") und Bestätigungen offener Befehle. true = an den First Officer gerichtet (kein Crew-Befehl).
        /// </summary>
        bool Heard(Station speaker, string text)
        {
            string s = " " + (text ?? "").ToLowerInvariant() + " ";
            bool addressed = OfficerLines.IsAddressed(s);
            var cmd = OfficerLines.Command(s, addressed);
            if (cmd != null && cmd != "off" && !Enabled) cmd = addressed ? "on" : null;   // aus: nur die Anrede weckt ihn
            if (cmd != null)
            {
                Plugin.Log.LogInfo("First Officer: Sprachbefehl " + cmd);
                switch (cmd)
                {
                    case "off": SetEnabled(false, true); break;
                    case "on": SetEnabled(true, true); break;
                    case "give_conn": GiveConn(); break;
                    case "take_conn": TakeConn(); break;
                    case "hint":
                        if (!Enabled) break;
                        if (Current == Mode.Commanding) Say(OfficerLines.Say("status", Status), speaker);
                        else _askedHint = true;
                        break;
                }
                return true;
            }
            if (Current != Mode.Commanding || !OfficerLines.IsAcknowledgement(text)) return false;
            foreach (var o in _orders.Values)
                if (o.Human && o.St == speaker && !o.Acked)
                {
                    o.Acked = true;
                    o.NextRemind = Time.time + 40f;   // bestätigt: länger Zeit lassen
                    if (Rng.NextDouble() < 0.35) Say(OfficerLines.Say("ack"), speaker);
                }
            return false;
        }

        /// <summary>Ein-/ausschalten (Overlay, Taste, Sprache). announce = mit kurzer Antwort.</summary>
        public void SetEnabled(bool on, bool announce)
        {
            if (on == Enabled) { if (announce && on) Say(OfficerLines.Say("on"), null); return; }
            if (!on && announce) Say(OfficerLines.Say("off"), null);
            Enabled = on;
            if (Toggled != null) Toggled(on);   // Einstellung speichern, Stimme bereitstellen
            if (on) { _greeted = true; if (announce) Say(OfficerLines.Say("on"), null); }
        }

        /// <summary>"Sie haben die Brücke": der Spieler wechselt an seine Station, der First Officer übernimmt.</summary>
        public void GiveConn()
        {
            if (!Enabled || !GameBridge.InMission) return;
            if (CaptainSense.LocalSeat() != Station.Captain) return;
            Say(OfficerLines.Say("conn_ack"), null);
            _bridgeGreeted = true;
            _connGivenAt = Time.time;
            Current = Mode.Commanding;   // keine zweite Ansage beim nächsten Takt
            _orders.Clear(); _selected = null;
            CaptainSense.MoveLocalPlayerTo(PlayerStation);
        }

        /// <summary>"Ich übernehme": der Spieler kehrt auf den Captain-Stuhl zurück.</summary>
        public void TakeConn()
        {
            if (!GameBridge.InMission || CaptainSense.LocalSeat() == Station.Captain) return;
            _connGivenAt = -999f;
            CaptainSense.MoveLocalPlayerTo(Station.Captain);
        }

        // ---------------- Hilfen ----------------

        void Say(string text, Station? to)
        {
            if (string.IsNullOrEmpty(text)) return;
            _lastSpoke = Time.time;
            Plugin.Log.LogInfo("First Officer sagt: " + text);
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
