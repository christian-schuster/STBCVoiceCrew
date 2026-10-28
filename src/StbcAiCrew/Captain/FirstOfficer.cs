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
        /// <summary>Autopilot: führt das Schiff, obwohl der Spieler auf dem Captain-Stuhl sitzt (der schaut zu).</summary>
        public bool Autopilot;
        /// <summary>Zu jedem Missionsbeginn selbst übernehmen (unbeaufsichtigtes Testen).</summary>
        public bool AutoCommand;
        public bool Jokes = true;
        public float JokeMinutes = 8f;
        /// <summary>"Sie haben die Brücke": an diese Station wechselt der Spieler.</summary>
        public Station PlayerStation = Station.Helm;

        /// <summary>Sprechen: Text in der Spielsprache, Station des Adressaten (Konsole zeigt ihn an) oder null für alle.</summary>
        /// <summary>Sprechen: Text, Station des Adressaten (null = alle), Rückruf, sobald der Satz zu Ende gesprochen ist.</summary>
        public Action<string, Station?, Action> Speak;
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
        string _stealthFor;
        float _redAlertAt = -999f, _warpBlockedUntil = -999f;
        int _warpTries;
        bool _alertAnnounced;
        float _breakawayAt = -1f;
        bool _leaving;   // Warp oder Flucht steht an (letzter Takt)   // Freifliegen vor dem Warp: höchstens 8 s, sonst ist man ein leichtes Ziel
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
                if (AutoCommand && !Autopilot && Settled) { Plugin.Log.LogInfo("First Officer: AutoKommando – übernimmt die Brücke"); TakeBridge(); }
                // Autopilot: der Spieler bleibt auf dem Stuhl und schaut zu, der First Officer führt alle Stationen
                bool command = Command && (Autopilot || !CaptainSense.HumanAtCaptainConsole() || Time.time - _connGivenAt < 8f);
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

                if (!_bridgeGreeted && Settled && !Speaking())
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
            _missionStart = -1f; _firstGoalAt = -1f; _bridgeGreeted = false; Autopilot = false; _hailSince = -1f; _announceTake = false; _connGivenAt = -999f; Current = Mode.Lobby;
            _orders.Clear(); _advised.Clear(); _scanned.Clear(); _visitedPoints.Clear(); _trackKey = null; _goalAnnounced = null; _arrivedAt = null; _selected = null; _trackCount = -1;
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
            if (!Jokes || threat || !Settled || _orders.Count > 0 || Time.time - _lastJoke < JokeMinutes * 60f || Time.time - _lastSpoke < 40f || Speaking()) return;
            _lastJoke = Time.time;
            if (Rng.NextDouble() < 0.5) Say(OfficerLines.Joke(), null);
        }

        // ---------------- Kommando: Entscheidungsbaum ----------------

        /// <summary>Schiff führen. Rückgabe: Gefahr.</summary>
        bool Decide(ShipStatus st)
        {
            var contacts = _crew.ContactsNow ?? new List<Contact>();
            // Ein Befehl nach dem anderen: erst aussprechen, dann ausführen, dann der Crew Zeit für ihre Antwort lassen
            if (Busy) return EventLog.UnderThreat(20f);

            // 1. Rufe
            if (CaptainSense.HailPending && Time.time - _lastHail > 6f)
            {
                _lastHail = Time.time;
                Act(OfficerLines.Say("hail"), null, () => CaptainSense.AnswerHail());
                Status = Loc.T("Ruf angenommen", "hail answered");
                return false;
            }

            // 2. Gefahr
            Contact enemy = Nearest(contacts, c => c.Category == HHHDDNLEDBN.Enemy && !Hazard(c.Name) && c.DistanceKm >= 0 && c.DistanceKm < 25);
            bool threat = EventLog.UnderThreat(20f) || enemy != null;
            if (threat)
            {
                _lastThreat = Time.time;
                // Alarmstatus vom Spiel selbst (CommandManager.SetRedAlert), einmal je Gefecht ansagen
                if (!_alertAnnounced && !EventLog.RedAlert && !CaptainSense.RedAlert)
                {
                    _redAlertAt = Time.time;
                    _alertAnnounced = true;   // bis zur Entwarnung nicht noch einmal
                    Act(OfficerLines.Say("red_alert"), null, () => CaptainSense.SetRedAlert(true));
                    return true;
                }
                if (st != null && st.ShieldState == "PoweredOff")
                    Issue(new Step { Id = "shields", St = Station.Tactical, LineKey = "shields", Bot = () => _crew.Run(Intent.ShieldsUp),
                                     Done = () => GameBridge.PlayerShip == null || ShieldsUp() });
                if (st != null && st.Shields >= 0 && st.Shields < 40 && Time.time - EventLog.LastHitTime < 10f && !_leaving)   // beim Warp braucht der Antrieb die Energie
                    Issue(new Step { Id = "power_shields", St = Station.Engineering, LineKey = "power_shields", Bot = () => _crew.Run(Intent.PowerShields), Remind = 20f });
            }
            else if ((EventLog.RedAlert || CaptainSense.RedAlert || _alertAnnounced) && Time.time - _lastThreat > 45f && _lastThreat > 0)
            {
                _lastThreat = -999f;
                _alertAnnounced = false;
                Act(OfficerLines.Say("all_clear"), null, () => CaptainSense.SetRedAlert(false));
                return false;
            }

            // 3. Missionsziel
            var goals = CaptainSense.OpenGoals();
            Goal g = Pick(goals, contacts);
            if (g != null)
            {
                if (g.Key != _goalAnnounced)
                {
                    _goalAnnounced = g.Key;
                    if (!string.IsNullOrEmpty(g.Text)) { Act(OfficerLines.Say("new_goal", Clean(g.Text)), null, null, 1f); return threat; }
                }
                var plan = Plan(g, contacts, threat ? enemy : null);
                foreach (var s in plan) { if (s.Prep != null) s.Prep(); if (s.Bot != null) Issue(s); }
                // Feinde in der Nähe bekämpfen – außer der Auftrag verbietet es (Schleichfahrt, nur beobachten), es heißt fliehen,
                // oder wir gehen gerade auf Warp (ein neues Ziel auf allen Konsolen würde die Steuer vom Warpkurs abbringen)
                bool leaving = plan.Exists(s => s.Id.StartsWith("warp:") || s.Id.StartsWith("escape:") || s.Id.StartsWith("breakaway:") || s.Id.StartsWith("flee:"));
                bool peaceful = g.Note == "note_stealth" || g.Note == "note_scan_only" || g.Kind == GoalKind.Escape || leaving;
                _leaving = leaving;
                if (g.Kind != GoalKind.Attack && g.Kind != GoalKind.Defend && enemy != null && !peaceful) Issue(FightStep(enemy, true));
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
            // Pflicht vor Kür: optionale Ziele erst, wenn sonst nichts ansteht; Warteziele laufen von selbst
            for (int pass = 0; pass < 2; pass++)
            {
                bool optional = pass == 1;
                foreach (var g in goals) if (g.Optional == optional && g.Kind == GoalKind.Escape) return g;   // Flucht geht vor
                foreach (var g in goals) if (g.Optional == optional && g.Kind != GoalKind.Wait && (g.System == null || g.System == here) && Find(contacts, g.Target) != null) return g;
                foreach (var g in goals) if (g.Optional == optional && g.Kind != GoalKind.Wait) return g;
            }
            return goals[0];
        }

        /// <summary>Schritte zum Ziel, wie ein erfahrener Captain sie gehen würde (Missionsablauf aus Ziel, System, Entfernung).</summary>
        List<Step> Plan(Goal g, List<Contact> contacts, Contact enemy)
        {
            var steps = new List<Step>();
            var here = CaptainSense.CurrentSystem;
            GoalKind kind = g.Kind;

            // Flucht ohne Ziel im System: raus (Warp in irgendein anderes System) oder per Impuls weg von den Verfolgern
            if (kind == GoalKind.Escape && g.Target == null)
            {
                Status = Loc.T("Flucht: ", "escape: ") + Clean(g.Text);
                if (g.Note == "note_impulse_escape")
                {
                    steps.Add(new Step { Id = "flee:" + g.Key, St = Station.Helm, LineKey = "flee", Bot = () => _crew.Run(Intent.Flee), Done = GoalMoved, Remind = 40f });
                    return steps;
                }
                var from = here;
                var dest = g.System != null && g.System != here ? g.System : CaptainSense.OtherSystem(here);
                if (dest != null)
                {
                    string sys = CaptainSense.NameOf(dest);
                    steps.Add(new Step { Id = "escape:" + sys, St = Station.Helm, LineKey = "escape", Arg = sys, Prep = () => CaptainSense.SetWarpTarget(dest),
                                         Bot = () => _crew.Run(Intent.Warp), Done = () => CaptainSense.CurrentSystem != from, Remind = 30f });
                }
                return steps;
            }
            if (kind == GoalKind.Escape) kind = GoalKind.Travel;   // Fluchtpunkt im System (Nebelrand): hinfliegen, schnell
            if (kind == GoalKind.Gather && g.Reach > 0) kind = GoalKind.Travel;   // "Fly within 10 KM of …": dort bleiben
            if (kind == GoalKind.Wait || kind == GoalKind.Hail)
            {
                Status = Clean(g.Text);
                return steps;
            }

            // Missionswissen: Schilde für die Energiewelle, Schleichfahrt
            if (g.Note == "note_raise_shields")
                steps.Add(new Step { Id = "wave_shields", St = Station.Tactical, LineKey = "shields", Bot = () => _crew.Run(Intent.ShieldsUp), Done = ShieldsUp });
            if (g.Note == "note_lower_shields")
                steps.Add(new Step { Id = "wave_shields_down", St = Station.Tactical, LineKey = "shields_down", Bot = () => _crew.Run(Intent.ShieldsDown), Done = () => !ShieldsUp() });
            if (g.Note == "note_stealth")
            {
                string sk = g.Key;
                steps.Add(new Step { Id = "stealth:" + sk, St = Station.Engineering, LineKey = "stealth", Bot = () => { _crew.Run(Intent.StealthOn); _stealthFor = sk; },
                                     Done = () => _stealthFor == sk, Remind = 120f });
            }

            // a) Anderes Sternsystem: Warp
            if (g.System != null && here != null && g.System != here)
            {
                string sys = CaptainSense.NameOf(g.System);
                var target = g.System;
                // Warp gesperrt (Mission verbietet es gerade, System nicht freigegeben) oder schon zweimal vergeblich: später wieder
                if (Time.time < _warpBlockedUntil)
                {
                    Status = Loc.T("Warp derzeit nicht möglich: ", "warp unavailable: ") + sys;
                    return steps;
                }
                // Feind dicht dran: erst Schub und Abstand gewinnen, sonst richtet sich die Steuer nie sauber auf den Warpkurs aus
                if (enemy != null && enemy.DistanceKm < 20 && (_breakawayAt < 0 || Time.time - _breakawayAt <= 8f || Time.time - _breakawayAt > 90f))
                {
                    Status = Loc.T("Abstand gewinnen vor dem Warp", "gaining distance before warp");
                    var foe = enemy.Transform;
                    steps.Add(new Step { Id = "breakaway:" + sys, St = Station.Helm, LineKey = "breakaway", Bot = () => { _breakawayAt = Time.time; _crew.Run(Intent.Flee); },
                                         Done = () => { var x = Find(_crew.ContactsNow, foe); return x == null || x.DistanceKm >= 20 || (_breakawayAt > 0 && Time.time - _breakawayAt > 8f); }, Remind = 20f });
                    return steps;
                }
                Done("breakaway:" + sys);   // nach höchstens 8 s Schub: springen, auch wenn der Feind noch nah ist
                Status = Loc.T("Warp nach ", "warp to ") + sys;
                steps.Add(new Step
                {
                    Id = "warp:" + sys, St = Station.Helm, LineKey = "warp", Arg = sys, Prep = () => CaptainSense.SetWarpTarget(target),
                    Bot = () =>
                    {
                        _warpTries++;
                        var ws = ShipStatus.Read();
                        Plugin.Log.LogInfo("First Officer Warp-Diagnose: Versuch " + _warpTries + ", möglich " + CaptainSense.CanWarp + ", Ausrichtung " + CaptainSense.WarpAlignment + ", Warpkern " + (ws != null ? ws.WarpCore.ToString("0") : "?") + " %, Ziel " + CaptainSense.NameOf(CaptainSense.WarpTarget));
                        // Zweiter Versuch: erst Schub, weg von dem, was im Weg ist (Würfel, Planet, Trümmer), dann erneut Warp
                        if (_warpTries == 2 && CaptainSense.CanWarp != false)
                        {
                            Plugin.Log.LogInfo("First Officer: Warp nach " + sys + " klappt nicht – erst Schub, dann neuer Versuch");
                            Say(OfficerLines.Say("warp_clear"), null);
                            _crew.Run(Intent.Flee);
                            return;
                        }
                        if (CaptainSense.CanWarp == false || _warpTries > 3)
                        {
                            _warpTries = 0;
                            _warpBlockedUntil = Time.time + 180f;
                            _orders.Remove("warp:" + sys);
                            Plugin.Log.LogInfo("First Officer: Warp nach " + sys + " nicht möglich, neuer Versuch in 3 min");
                            Say(OfficerLines.Say("warp_blocked"), null);
                            return;
                        }
                        _crew.Nav.Stop("Warp");   // Ausweichmanöver beenden, die Steuer richtet sich selbst auf den Warpkurs aus
                        _crew.Run(Intent.Warp);
                    },
                    Done = () => { bool there = CaptainSense.CurrentSystem == target; if (there) _warpTries = 0; return there; }, Remind = 60f
                });
                return steps;
            }

            // b) Ziel im System. Scan-Ziele ohne festes Objekt ("Scan Anomalies in this System", "Scan ships in the system"):
            //    das nächste passende, noch nicht gescannte Objekt auf den Sensoren nehmen
            if (g.Target == null && kind == GoalKind.Scan)
            {
                var next = NextUnscanned(contacts, g);
                if (next != null) g.Target = next.Transform;
            }
            Contact c = Find(contacts, g.Target);
            if (c == null)
            {
                Status = Loc.T("Ziel noch nicht in Sensorreichweite: ", "objective not yet on sensors: ") + Clean(g.Text);
                // Die markierten Punkte der Impulskarte nacheinander anfliegen (Signalquelle, Suchgebiet, Treffpunkt)
                var poi = NextMapPoint();
                if (poi != null)
                {
                    string pn = GameBridge.StripLocId(poi.AKDKEDEMFDJ) ?? "?";
                    var p = poi;
                    Status = Loc.T("Impuls zum Kartenpunkt ", "impulse to map point ") + pn;
                    steps.Add(new Step { Id = "poi:" + p.GetInstanceID(), St = Station.Helm, LineKey = "impulse", Arg = pn, Prep = () => CaptainSense.SetImpulseTarget(p),
                                         Bot = () => { _visitedPoints.Add(p.GetInstanceID()); _crew.Run(Intent.Impulse); },
                                         Done = () => GoalMoved() || DistanceKm(p) < 8, Remind = 45f });
                    return steps;
                }
                if (g.Target != null) steps.Add(new Step { Id = "search:" + g.Key, St = Station.Helm, LineKey = "search", Arg = Clean(g.Text) });
                return steps;
            }
            string key = g.Key;
            string name = c.Name;
            double d = c.DistanceKm;
            Status = Clean(g.Text) + " – " + name + " " + d.ToString("0.0") + " km";
            Action select = () => { if (_selected != key) { _crew.Select(c); _selected = key; } };   // nur bei neuem Ziel, sonst überschreibt er die Auswahl des Spielers

            double reach = g.Reach > 0 ? g.Reach : Reach(kind);
            // Mindestabstand (Protomaterie): nicht näher heran, aus der Ferne mit Torpedos
            if (g.MinDistance > 0)
            {
                reach = Math.Max(reach, g.MinDistance + 3);
                double min = g.MinDistance;
                if (d < min)
                    steps.Add(new Step { Id = "keep_distance:" + name, St = Station.Helm, LineKey = "keep_distance", Arg = min.ToString("0"), Prep = select,
                                         Bot = () => _crew.Run(Intent.MoveAway, name), Done = () => { var x = Find(_crew.ContactsNow, g.Target); return x == null || x.DistanceKm >= min; }, Remind = 20f });
            }
            // Teilchen sammeln: mitten hindurch, volle Fahrt
            if (kind == GoalKind.Gather)
            {
                steps.Add(new Step { Id = "gather:" + name, St = Station.Helm, LineKey = "gather", Arg = name, Prep = select,
                                     Bot = () => { _crew.Run(Intent.MoveToTarget, name); _crew.Run(Intent.SpeedFull); }, Done = GoalMoved, Remind = 30f });
                return steps;
            }
            if (d > reach)
            {
                bool far = d > 60;
                steps.Add(new Step { Id = (far ? "impulse:" : "course:") + name, St = Station.Helm, LineKey = far ? "impulse" : "course", Arg = name, Prep = select,
                                     Bot = () => _crew.Run(far ? Intent.Impulse : Intent.MoveToTarget, name),
                                     Done = () => { var x = Find(_crew.ContactsNow, g.Target); return x == null || x.DistanceKm <= reach; }, Remind = far ? 40f : 90f });
                return steps;
            }
            Done("course:" + name); Done("impulse:" + name);

            switch (kind)
            {
                case GoalKind.Scan:
                    {
                        // Gescannt ist es, wenn das Ziel weiterkommt (Zähler, Teilziel) oder nach 30 s Scan; dann das nächste Objekt
                        var scanned = c.Transform;
                        float since = Time.time;
                        steps.Add(new Step { Id = "scan:" + name, St = Station.Tactical, LineKey = "scan", Arg = name, Prep = select, Bot = () => { since = Time.time; _crew.Run(Intent.Scan, name); },
                                             Done = () => { bool done = GoalMoved() || Time.time - since > 30f; if (done && scanned != null) _scanned.Add(scanned); return done; } });
                    }
                    break;
                case GoalKind.Attack:
                    // Mit Mindestabstand: Torpedos (Phaser reichen nicht so weit)
                    if (g.MinDistance > 0)
                        steps.Add(new Step { Id = "torpedo:" + name, St = Station.Tactical, LineKey = "torpedo", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.FireTorpedoes, name), Done = GoalMoved, Remind = 30f });
                    else
                        steps.Add(new Step { Id = "attack:" + name, St = Station.Tactical, LineKey = "attack", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.FirePhasers, name), Done = GoalMoved, Remind = 30f });
                    break;
                case GoalKind.Disable:
                    // Kommt das Stören nicht voran (manche Schiffe können nicht hacken), mit Phasern auf den Antrieb
                    string sub = g.Subsystem ?? "engines";
                    if (sub == "engines" && Time.time - _goalSince > 75f)
                        steps.Add(new Step { Id = "disable_fire:" + name, St = Station.Tactical, LineKey = "disable_fire", Arg = name, Prep = select, Bot = () => _crew.Run(Intent.FirePhasers, name), Done = GoalMoved, Remind = 30f });
                    else
                        steps.Add(new Step { Id = "disable:" + name + ":" + sub, St = Station.Tactical, LineKey = sub == "engines" ? "disable" : "disable_" + sub, Arg = name, Prep = select,
                                             Bot = () => _crew.Run(Intent.Hack, name, sub), Done = GoalMoved, Remind = 30f });
                    break;
                case GoalKind.Rescue:
                    // Schiffe, die ihre Schilde nicht senken: erst die Schilde runterschießen
                    if (g.Note == "note_shields_then_beam")
                        steps.Add(new Step { Id = "shields_off:" + name, St = Station.Tactical, LineKey = "attack_shields", Arg = name, Prep = select,
                                             Bot = () => { _crew.Run(Intent.TargetShields); _crew.Run(Intent.FirePhasers, name); }, Done = GoalMoved, Remind = 30f });
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
            Contact enemy = Nearest(contacts, c => c.Category == HHHDDNLEDBN.Enemy && !Hazard(c.Name) && c.DistanceKm >= 0 && c.DistanceKm < 25);
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

            // Missionswissen: was bei diesem Ziel zu beachten ist, einmal je Ziel
            Contact gc = Find(contacts, g.Target);
            if (g.MinDistance > 0 && gc != null && gc.DistanceKm < g.MinDistance &&
                Advice("w_min:" + g.Key, 45f, true, "warn_min_distance", g.MinDistance.ToString("0"))) return threat;
            if (g.Kind == GoalKind.Scan && st != null && (st.ImpulsePreparing || st.WarpPreparing) && gc != null && gc.DistanceKm < 30 &&
                Advice("w_unscanned:" + g.Key, 60f, true, "warn_leave_unscanned", gc.Name)) return threat;
            if (g.Note != null && Advice("note:" + g.Key + ":" + g.Note, 9999f, false, g.Note, gc != null ? gc.Name : "")) return threat;

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
            if (!Settled && !urgent) return false;   // Briefing läuft noch
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
                                       (g.OpenSubGoals > 0 ? " Teilziele " + g.OpenSubGoals : "") + (g.SubTexts.Count > 0 ? " \"" + g.SubTexts[0] + "\"" : "") +
                                       (g.Note != null ? " Hinweis " + g.Note : "") + (g.Optional ? " optional" : "") + (g.HasTimer ? " Timer" : ""));
                }
            }
            else if (_trackCount >= 0 && count != _trackCount) _moved = true;
            _trackKey = key;
            _trackCount = count;
            if (goals.Count > 0 && _firstGoalAt < 0) _firstGoalAt = Time.time;
            if (_moved) _goalSince = Time.time;
            else if (goals.Count > 0 && Time.time - _goalSince > 240f && Time.time - _stallLogged > 240f)
            {
                _stallLogged = Time.time;
                Plugin.Log.LogWarning("First Officer: Ziel hängt seit " + Mathf.RoundToInt(Time.time - _goalSince) + " s (" + Current + "): [" + goals[0].Kind + "] \"" +
                                      goals[0].English + "\" | " + Status + " | Befehle offen: " + string.Join(", ", new List<string>(_orders.Keys).ToArray()) +
                                      " | System " + CaptainSense.NameOf(CaptainSense.CurrentSystem) + " | Warpziel " + CaptainSense.NameOf(CaptainSense.WarpTarget));
            }
        }
        float _stallLogged = -999f, _firstGoalAt = -1f;

        /// <summary>Briefing vorbei: das erste Missionsziel steht seit ein paar Sekunden auf der Konsole.</summary>
        bool Settled => _firstGoalAt >= 0 && Time.time - _firstGoalAt > 8f;

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
                // Die Station hat das befohlene Ziel erfasst: Befehl angekommen, nicht nachbohren
                if (o.Human && !string.IsNullOrEmpty(s.Arg) && Contacts.TargetOf(o.St) == s.Arg)
                {
                    o.Acked = true;
                    o.NextRemind = Math.Max(o.NextRemind, Time.time + 60f);
                    return;
                }
                if (Time.time < o.NextRemind || Speaking()) return;
                if (Busy) return;
                if (!o.Human) { o.NextRemind = Time.time + s.Remind; Act(null, null, s.Bot); return; }   // Bot: einfach erneut
                if (o.Reminders >= 3) { Say(OfficerLines.Say("give_up"), o.St); o.NextRemind = Time.time + 120f; o.Reminders = 0; return; }
                o.Reminders++;
                o.NextRemind = Time.time + s.Remind;
                Act(OfficerLines.Say("remind", OfficerLines.Title(s.St)) + " " + o.Line, o.St, null, 3f);
                return;
            }
            if (Busy) return;
            if (s.Done != null && s.Done()) return;
            bool human = !GameBridge.IsStationAI(s.St) && s.St != Station.Captain;
            string line = OfficerLines.Say(s.LineKey + (human ? "_human" : "_bot"), s.Arg ?? "") ?? OfficerLines.Say(s.LineKey + "_human", s.Arg ?? "");
            o = new Order { Id = s.Id, St = s.St, Human = human, Done = s.Done, Line = line, NextRemind = Time.time + s.Remind };
            _orders[s.Id] = o;
            // Erst aussprechen, dann führt der Bot aus; dem Menschen Zeit zum Reagieren lassen
            Act(line, human ? (Station?)s.St : null, human ? null : s.Bot, human ? 3f : 1.5f);   // 1,5 s, bis die Antwort des Bots einsetzt
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
            // "Number One, acknowledged": Bestätigung, keine Frage
            if (cmd == "hint" && Current == Mode.Commanding && OfficerLines.IsAcknowledgement(text)) cmd = null;
            if (cmd != null && cmd != "off" && !Enabled) cmd = addressed ? "on" : null;   // aus: nur die Anrede weckt ihn
            if (cmd != null)
            {
                Plugin.Log.LogInfo("First Officer: Sprachbefehl " + cmd);
                switch (cmd)
                {
                    case "off": SetEnabled(false, true); break;
                    case "on": SetEnabled(true, true); break;
                    case "joke":   // auf Zuruf, außer im Gefecht
                        if (Enabled) Say(EventLog.UnderThreat(20f) && GameBridge.InMission ? OfficerLines.Say("joke_later") : OfficerLines.Joke(), null);
                        break;
                    case "give_conn": TakeBridge(); break;
                    case "take_conn": TakeConn(); break;
                    case "hint":
                        if (!Enabled) break;
                        if (!GameBridge.InMission) Say(OfficerLines.Say(Rng.NextDouble() < 0.5 ? "lobby_idle" : "greet"), null);   // Lobby: kurz antworten
                        else if (Current == Mode.Commanding) Say(OfficerLines.Say("status", Status), speaker);
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
            return addressed;   // "Number One, aye" ist an ihn gerichtet, kein Crew-Befehl
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
        /// <summary>
        /// "Number One, take the bridge" / "Sie haben die Brücke": der Spieler bleibt auf dem Captain-Stuhl und schaut zu,
        /// der First Officer führt alle Stationen (auch zum unbeaufsichtigten Testen, siehe AutoKommando).
        /// </summary>
        public void TakeBridge()
        {
            if (!Enabled || !GameBridge.InMission || Autopilot) return;
            Autopilot = true;
            Say(OfficerLines.Say("conn_ack"), null);
            _bridgeGreeted = true;
            Current = Mode.Commanding;   // keine zweite Ansage beim nächsten Takt
            _orders.Clear(); _selected = null;
        }

        /// <summary>"Ich übernehme": der Spieler kehrt auf den Captain-Stuhl zurück.</summary>
        public void TakeConn()
        {
            if (!GameBridge.InMission) return;
            if (Autopilot)
            {
                Autopilot = false;
                AutoCommand = false;   // wer übernimmt, will selbst spielen
                Current = Mode.Advising;
                _orders.Clear();
                Say(OfficerLines.Say("hand_back"), Station.Captain);
            }
            // Den Platz wechselt der Spieler selbst (Stationswahl des Spiels)
        }

        // ---------------- Hilfen ----------------

        // ---------------- Reihenfolge: sprechen → ausführen → warten ----------------

        bool _acting;
        float _actingSince, _readyAt;

        /// <summary>Wartet er gerade darauf, dass ein Befehl gesprochen, ausgeführt und beantwortet ist?</summary>
        bool Busy
        {
            get
            {
                if (_acting && Time.time - _actingSince > 20f) _acting = false;   // Sicherung, falls der Rückruf ausbleibt
                if (_acting || Time.time < _readyAt) return true;
                // Die Crew antwortet noch ("Aye, Captain"): ausreden lassen, höchstens 10 s nach dem Befehl
                return Time.time < _readyAt + 10f && GameBridge.CrewVoicePlaying();
            }
        }

        /// <summary>Satz sprechen, danach die Handlung ausführen, dann warten, bis die Crew geantwortet hat (mindestens pause Sekunden).</summary>
        void Act(string line, Station? to, Action action, float pause = 1.5f)
        {
            _acting = true;
            _actingSince = Time.time;
            Action finish = () =>
            {
                try { if (action != null) action(); }
                catch (Exception e) { Plugin.Log.LogWarning("First Officer: " + e.Message); }
                _readyAt = Time.time + pause;
                _acting = false;
            };
            if (string.IsNullOrEmpty(line)) finish();
            else Say(line, to, finish);
        }

        void Say(string text, Station? to, Action spoken = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            _lastSpoke = Time.time;
            Plugin.Log.LogInfo("First Officer sagt: " + text);
            if (Speak != null) Speak(text, to, spoken); else if (spoken != null) spoken();
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

        /// <summary>Feindlich markiert, aber kein Schiff: explosive Objekte, Minen, Anomalien. Darauf feuert man nicht nebenbei.</summary>
        /// <summary>Schon angeflogene Kartenpunkte (je Mission).</summary>
        readonly HashSet<int> _visitedPoints = new HashSet<int>();

        /// <summary>Nächster nicht besuchter Kartenpunkt: Missionsziele zuerst, sonst der nächstgelegene.</summary>
        Sector.SectorBody NextMapPoint()
        {
            Sector.SectorBody best = null;
            bool bestObjective = false;
            double bestDist = double.MaxValue;
            foreach (var b in CaptainSense.MapPoints())
            {
                if (_visitedPoints.Contains(b.GetInstanceID())) continue;
                bool obj = CaptainSense.IsObjectivePoint(b);
                double d = DistanceKm(b);
                if (d >= 0 && d < 8) { _visitedPoints.Add(b.GetInstanceID()); continue; }   // schon da
                if (best == null || (obj && !bestObjective) || (obj == bestObjective && d < bestDist)) { best = b; bestObjective = obj; bestDist = d; }
            }
            return best;
        }

        static double DistanceKm(Sector.SectorBody b)
        {
            try { return b != null && b.ALPAOOKFIEE != null && Contacts.HasOwn ? (b.ALPAOOKFIEE.Position - Contacts.OwnPosition).magnitude / 1000.0 : -1; }
            catch { return -1; }
        }

        /// <summary>Schon gescannte Objekte (je Mission), damit Scan-Ziele ohne festes Objekt weiterwandern.</summary>
        readonly HashSet<SystemTransform> _scanned = new HashSet<SystemTransform>();

        static readonly HashSet<string> ScanStopWords = new HashSet<string>
        {
            "scan", "scans", "scanned", "perform", "tactical", "system", "systems", "gather", "data", "this", "these", "status",
            "impulse", "different", "locations", "location", "find", "waiting", "complete", "compiling", "investigate", "nearby",
            "every", "their", "there", "where", "which", "while", "region", "smuggled", "cargo", "single", "life", "sign", "signs"
        };

        /// <summary>
        /// Nächstes passendes, noch nicht gescanntes Objekt für ein Scan-Ziel ohne festes Objekt. Passend heißt: Name enthält
        /// ein Stichwort aus Ziel oder Teilziel ("Anomalies" → "anoma", "freighters" → "freig"); ohne Stichwort-Treffer nichts.
        /// </summary>
        Contact NextUnscanned(List<Contact> contacts, Goal g)
        {
            var stems = new List<string>();
            var words = new List<string>((g.English ?? "").ToLowerInvariant().Split(new[] { ' ', ',', '.', ':', '-' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var s in g.SubTexts) words.AddRange(s.ToLowerInvariant().Split(new[] { ' ', ',', '.', ':', '-' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var w in words) if (w.Length >= 5 && !ScanStopWords.Contains(w)) stems.Add(w.Substring(0, 5));
            Contact best = null;
            foreach (var c in contacts)
            {
                if (c.Transform == null || _scanned.Contains(c.Transform) || c.Category == HHHDDNLEDBN.Self) continue;
                string n = (c.Name ?? "").ToLowerInvariant();
                if (!stems.Exists(s => n.Contains(s))) continue;
                if (best == null || c.DistanceKm < best.DistanceKm) best = c;
            }
            return best;
        }

        static bool Hazard(string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(name ?? "", @"volatile|mine\b|mines|anomal|debris|barrel|asteroid|explosive|core", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static Contact Nearest(List<Contact> contacts, Func<Contact, bool> pred)
        {
            Contact best = null;
            foreach (var c in contacts) if (pred(c) && (best == null || c.DistanceKm < best.DistanceKm)) best = c;
            return best;
        }
    }
}
