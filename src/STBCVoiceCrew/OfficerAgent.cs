using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace STBCVoiceCrew
{
    public enum Autonomy
    {
        OnlyOrders = 0,   // spricht nur, wenn angesprochen; handelt nur auf Befehl
        Advise = 1,       // meldet sich selbst zu Wort, handelt aber nur auf Befehl
        Act = 2           // spielt selbst mit: handelt eigenständig an der eigenen Station
    }

    /// <summary>
    /// Ein KI-Offizier an einer Station. Er hat ein eigenes LLM-Gespräch und sieht nur seine
    /// eigene Wahrnehmung (Konsole + Gesprochenes). Wer an der Station sitzt, bestimmt der zugewiesene
    /// Charakter mit Persönlichkeit, Rang und Gedächtnis.
    /// </summary>
    public sealed class OfficerAgent
    {
        public readonly Station Station;
        public readonly string Key;
        public Character Char { get; private set; }
        public string Status = "frei";

        readonly LlmClient _llm;
        readonly Plugin _cfg;
        readonly CrewBrain _bridge;
        readonly List<ChatMessage> _history = new List<ChatMessage>();

        bool _busy;
        public bool Busy => _busy;
        float _lastDecision = -999f;
        float _nextIdle;
        float _activeSeconds;
        int _commands, _ordersFollowed;

        Perception P => EventLog.Of(Station);

        public OfficerAgent(Station station, LlmClient llm, Plugin cfg, CrewBrain bridge)
        {
            Station = station;
            Key = StationKey(station);
            _llm = llm;
            _cfg = cfg;
            _bridge = bridge;
        }

        /// <summary>Bot sitzt an der Station (kein Mensch) und ein Charakter ist zugewiesen.</summary>
        public bool Active => Char != null && GameBridge.IsStationAI(Station);

        public void Assign(Character c)
        {
            Char = c;
            _history.Clear();
            _commands = _ordersFollowed = 0;
            _activeSeconds = 0f;
            _lastDecision = -999f;
            _nextIdle = Time.time + UnityEngine.Random.Range(8f, 20f);
            Status = c != null ? "auf Posten" : "frei";
        }

        public void Tick(ShipStatus status)
        {
            if (Char == null) return;
            if (!Active) { Status = "Station von Mensch besetzt"; return; }
            _activeSeconds += Time.deltaTime;
            if (_busy) return;

            var autonomy = (Autonomy)_cfg.AutonomyLevel.Value;
            float since = Time.time - _lastDecision;
            bool urgent = P.HasPending && P.Pending == Priority.Urgent;
            bool news = autonomy != Autonomy.OnlyOrders && P.HasPending && P.Pending >= Priority.Important
                        && since >= _cfg.ReactionIntervalSec.Value;
            bool idle = autonomy != Autonomy.OnlyOrders && Time.time >= _nextIdle && !EventLog.UnderThreat(30f);
            if (urgent) Decide("urgent", status);
            else if (news) Decide("news", status);
            else if (idle) Decide("idle", status);
        }

        // ---------------- Entscheidung ----------------

        void Decide(string reason, ShipStatus status)
        {
            _busy = true;
            _lastDecision = Time.time;
            _nextIdle = Time.time + UnityEngine.Random.Range(_cfg.IdleMinSec.Value, _cfg.IdleMaxSec.Value);
            bool captainSpoke = P.CaptainSinceMark;
            AnsweringCaptain = captainSpoke || P.AddressedSinceMark;
            string user = SituationPrompt(reason, status);
            P.Mark();

            string system = SystemPrompt();
            FitHistory(system.Length + user.Length);
            var messages = new List<ChatMessage> { new ChatMessage("system", system) };
            messages.AddRange(_history);
            messages.Add(new ChatMessage("user", user));

            Status = reason == "urgent" ? "hört zu …" : reason == "news" ? "prüft die Konsole …" : "beobachtet …";
            var who = Char;
            _llm.Send(messages, Schema(), _cfg.MaxTokens.Value, (content, error, latency) =>
            {
                _busy = false;
                if (Char != who) return;   // Mission inzwischen vorbei / neu besetzt
                if (error != null) { Status = "LLM-Fehler"; if (error.Contains("context")) _history.Clear(); return; }
                Status = "auf Posten (" + latency.ToString("0.0") + " s)";
                Remember(user, content);
                Apply(content, captainSpoke);
            });
        }

        string SystemPrompt()
        {
            var sb = new StringBuilder();
            sb.Append(Char.ProfileForPrompt());
            sb.Append("\nDu dienst auf der Brücke der U.S.S. Aegis (Star Trek Bridge Crew) an der Konsole ").Append(StationDe(Station))
              .Append(". Der Captain ist ein Mensch.\n");

            sb.Append("\nWAS DU WEISST\n");
            sb.Append("Du weißt nur, was deine eigene Konsole anzeigt, was du an Bord spürst (Alarm, Erschütterungen) und was auf der Brücke gesagt wird. ");
            sb.Append("Andere Konsolen siehst du nicht. Was dort steht, erfährst du nur, wenn es jemand sagt. Erfinde keine Werte, Sichtungen oder Missionsziele. Frag nach, wenn dir etwas fehlt.\n");
            sb.Append("Was der Captain sagt, kommt über eine Spracherkennung, Namen können verhört sein. Klingt ein Name ähnlich wie ein Kontakt auf deiner Konsole (\"Rekusa\" statt \"Ragusa\"), meint er diesen Kontakt. Verwende dann den Namen von deiner Konsole. Ergibt ein Befehl keinen Sinn oder klingt verstümmelt, wiederhole ihn nicht wörtlich: Führe aus, was offensichtlich gemeint ist, oder frag kurz nach (\"Wie bitte, Captain?\").\n");
            if (_cfg.NoProfanity.Value) sb.Append("Keine Flüche und keine vulgären Wörter, auch nicht im Zitat.\n");
            else sb.Append("Deine Ausdrucksweise richtet sich nach deinem Benehmen: wer rüpelhaft ist, flucht auch mal, wer gute Manieren hat, nie.\n");
            sb.Append("Nenne nur Zahlen, die unter 'Deine Konsole zeigt' stehen. Über Systeme anderer Stationen sagst du nur, was du gehört hast. ");
            sb.Append("Behaupte nur, etwas getan zu haben, wenn du es in 'commands' ausführst. Die Aufgaben anderer Stationen erledigst du nicht, darum bittest du höchstens.\n");

            sb.Append("\nWIE DU DICH VERHÄLTST (wie ein Offizier in den Star-Trek-Serien)\n");
            sb.Append("- Melde Veränderungen auf deiner Konsole unaufgefordert, kurz und mit Zahlen (\"Schilde bei 40 Prozent, Captain\").\n");
            sb.Append("- Bestätige Befehle knapp und melde, wenn sie ausgeführt sind.\n");
            sb.Append("- Routine in deinem Bereich erledigst du selbst und sagst kurz, was du getan hast.\n");
            sb.Append("- Sprich Kollegen direkt mit Namen an, wenn ihr euch abstimmen müsst (z. B. Taktik bittet Technik um Energie für die Schilde).\n");
            sb.Append("- Hältst du einen Befehl für falsch oder gefährlich, sag es respektvoll. Besteht der Captain darauf, führst du ihn aus.\n");
            sb.Append("- In ruhigen Momenten darfst du gelegentlich etwas Persönliches sagen, passend zu deiner Figur, aber nicht dauernd.\n");
            sb.Append("- Sprich nur, wenn du etwas beizutragen hast. Spricht der Captain gezielt eine andere Station an, schweigst du.\n");
            sb.Append("- Sprich immer ").Append(_cfg.Language.Value).Append(", auch wenn Meldungen der Konsole englisch sind (übersetze sie sinngemäß). Fass dich kurz wie im Funkverkehr: meist ein knapper Satz, höchstens zwei, auch wenn du beredsam bist.\n");
            sb.Append("- Wiederhole keine Bitte oder Meldung, die du oder ein Kollege schon gesagt hat. Ist alles gesagt, schweig (\"say\" leer). Der Captain braucht Raum zum Reden.\n");
            sb.Append("- Kollegen sind gleichrangig: Bitte sie höflich oder mach Vorschläge, aber erteil ihnen keine Befehle und droh ihnen nicht.\n");
            sb.Append("- Schreib so, wie man wirklich spricht: Füllwörter und Laute (äh, hm, puh, pff), Pausen mit …, Betonung mit ! oder ?!. Sparsam und passend zu deiner Figur.\n");
            sb.Append("- Lachen (lautmalerisch) nur, wenn etwas wirklich lustig ist, nie bei Gefahr. Keine Regieanweisungen in Klammern oder Sternchen: Alles in \"say\" wird laut gesprochen.\n");

            sb.Append('\n').Append(BridgeManual.Text);
            sb.Append("\nDEINE BEFEHLE (nur deine Station; Befehle mit Ziel wirken auf das Ziel, das der Captain gewählt hat)\n");
            foreach (var c in CrewCommands.ForOfficer(Station))
                sb.Append("- ").Append(c.Name).Append(": ").Append(c.Description).Append(c.Major ? " [weitreichend]" : "").Append('\n');

            sb.Append("\nANTWORT als JSON: \"commands\" = Befehle, die du jetzt ausführst (höchstens ")
              .Append(_cfg.MaxCommandsPerTurn.Value).Append("), ").Append(Moods.PromptHelp).Append(" \"say\" = was du laut sagst, ein knapper Satz (leer = schweigen, das ist oft richtig).\n\n");

            string heard = _bridge.HeardGameContext();
            if (heard != null)
                sb.Append("\nGEHÖRT ÜBER LAUTSPRECHER UND FUNK (Briefing, Funksprüche, Logbuch, Schiffscomputer; Hintergrundwissen, kein Auftrag an dich, nicht wiederholen):\n")
                  .Append(heard).Append('\n');
            sb.Append(Char.MemoryForPrompt());
            return sb.ToString();
        }

        string SituationPrompt(string reason, ShipStatus status)
        {
            var sb = new StringBuilder();
            sb.Append("Besatzung: ").Append(_bridge.Lineup(this)).Append('\n');
            if (status != null)
            {
                // Für alle sichtbar: Alarmlicht auf der ganzen Brücke
            sb.Append("Brücke: ").Append(EventLog.RedAlert ? "ROTER ALARM (Sirene, rotes Licht)" : "kein Alarm").Append('\n');
            sb.Append("Deine Konsole zeigt: ").Append(status.ForStation(Station)).Append('\n');
                string pending = status.PendingFor(Station);
                if (pending != null) sb.Append("Offene Befehle an deine Station (noch nicht ausgeführt): ").Append(pending).Append('\n');
            }
            if (reason == "urgent" && UnityEngine.Time.realtimeSinceStartup - _bridge.LastCaptainTime < 20f)
            {
                string hint = OrderHints.For(Station, _bridge.LastCaptainWords);
                if (hint != null) sb.Append("Passend zu den Worten des Captains wäre für deine Station: ").Append(hint).Append(" (prüfe selbst, ob es zur Lage passt)\n");
            }
            string standing = _bridge.Orders.Describe();
            if (standing != null) sb.Append("DAUERANWEISUNGEN DES CAPTAINS (halte dich daran): ").Append(standing).Append('\n');
            sb.Append("Zuletzt wahrgenommen:\n").Append(P.Recent(_cfg.EventsInPrompt.Value));
            if (Char.MissionNotes.Count > 0)
            {
                sb.Append("Deine Notizen aus diesem Einsatz:\n");
                foreach (var n in Char.MissionNotes) sb.Append("- ").Append(n).Append('\n');
            }

            var autonomy = (Autonomy)_cfg.AutonomyLevel.Value;
            sb.Append('\n');
            switch (reason)
            {
                case "urgent":
                    if (P.AddressedSinceMark) sb.Append("Du wurdest direkt angesprochen. Antworte und handle.");
                    else if (P.CaptainSinceMark) sb.Append("Der Captain hat zu allen gesprochen oder dir einen Befehl auf die Konsole geschickt. Reagiere, wenn es deine Station betrifft; sonst schweig.");
                    else sb.Append("Auf deiner Konsole ist etwas Wichtiges eingegangen. Reagiere.");
                    break;
                case "news":
                    sb.Append("Es hat sich etwas getan. Melde es, falls es wichtig ist, und reagiere an deiner Station.");
                    break;
                default:
                    bool chat = !EventLog.UnderThreat(30f) && UnityEngine.Random.value < _cfg.ChatterChance.Value * (0.4f + Char.Trait("Beredsamkeit") / 8f);
                    sb.Append(chat
                        ? "Gerade ist es ruhig. Wenn es passt, darfst du eine kurze persönliche Bemerkung machen oder einen Kollegen ansprechen."
                        : "Gerade ist es ruhig. Melde dich nur, wenn es etwas Wichtiges gibt; sonst schweig.");
                    break;
            }
            if (!P.AddressedSinceMark && !P.CaptainSinceMark && !_bridge.MaySpeakUnprompted(this))
                sb.Append(" Es wurde gerade genug geredet: say bleibt leer, du handelst nur.");
            if (autonomy != Autonomy.Act && reason != "urgent")
                sb.Append(" Ohne Befehl des Captains führst du nichts aus (commands leer), du darfst nur melden und vorschlagen.");
            sb.Append('\n');
            return sb.ToString();
        }

        object Schema()
        {
            var cmds = new List<object>();
            foreach (var c in CrewCommands.ForOfficer(Station)) cmds.Add(c.Name);
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "commands", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "maxItems", Math.Max(0, _cfg.MaxCommandsPerTurn.Value) },
                                { "items", new Dictionary<string, object> { { "type", "string" }, { "enum", cmds } } }
                            }
                        },
                        { "mood", new Dictionary<string, object> { { "type", "string" }, { "enum", Moods.Enum() } } },
                        { "say", new Dictionary<string, object> { { "type", "string" }, { "maxLength", 160 } } }
                    }
                },
                // Reihenfolge = Erzeugungsreihenfolge: erst entscheiden, dann (kurz) reden. Notizen macht die Nachbesprechung.
                { "required", new List<object> { "commands", "mood", "say" } }
            };
        }

        void Apply(string content, bool captainSpoke)
        {
            object root;
            try { root = Json.Parse(content); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(Char.Name + ": Antwort kein gültiges JSON: " + e.Message + " – " + content);
                return;
            }

            var remember = Json.Arr(Json.Get(root, "remember"));
            if (remember != null && remember.Count > 0)
            {
                foreach (var r in remember)
                {
                    var s = r as string;
                    if (string.IsNullOrEmpty(s)) continue;
                    if (KnownNote(s)) continue;   // im Test merkte sich Venn bei jeder Überlegung dieselben Sätze
                    Char.MissionNotes.Add(s);
                    Plugin.Log.LogInfo(Char.Name + " merkt sich: " + s);
                }
                while (Char.MissionNotes.Count > _cfg.NotesMax.Value) Char.MissionNotes.RemoveAt(0);
                Char.Save();
            }

            ExecuteCommands(Json.Arr(Json.Get(root, "commands")), captainSpoke);

            string say = Json.Str(root, "say");
            if (!string.IsNullOrEmpty(say) && say.Trim().Length > 0) _bridge.Say(this, CompleteSentences(say), Json.Str(root, "mood"));
        }

        readonly Dictionary<string, float> _lastRun = new Dictionary<string, float>();

        /// <summary>Die laufende Überlegung antwortet auf den Captain (Rede-Budget gilt dann nicht).</summary>
        public bool AnsweringCaptain { get; private set; }

        /// <summary>Reflex aus dem Code: Routine ausführen, wenn keine Daueranweisung dagegen spricht.</summary>
        public void Reflex(string name, string reason, float cooldown)
        {
            var cmd = CrewCommands.Find(name);
            if (cmd == null || cmd.Owner != Station || Char == null) return;
            float last;
            if (_lastRun.TryGetValue(cmd.Name, out last) && Time.time - last < cooldown) return;
            if (_bridge.Orders.Blocks(cmd.Name) != null) return;
            try
            {
                GameBridge.Trigger(cmd);
                _lastRun[cmd.Name] = Time.time;
                _commands++;
                Plugin.Log.LogInfo("Reflex: " + Char.Name + " führt aus: " + cmd.Name + " (" + reason + ")");
                P.Add("Routine: Du hast " + cmd.Description + " (" + reason + ").", Priority.Background);
            }
            catch (Exception e) { Plugin.Log.LogWarning(Char.Name + ": Reflex " + cmd.Name + " fehlgeschlagen: " + e.Message); }
        }

        /// <summary>
        /// Sofort-Befehl des Captains (ohne LLM erkannt): gleich ausführen, danach bestätigt der Offizier nur noch.
        /// </summary>
        public bool QuickExecute(string name)
        {
            var cmd = CrewCommands.Find(name);
            if (cmd == null || cmd.Owner != Station || Char == null) return false;
            float last;
            if (_lastRun.TryGetValue(cmd.Name, out last) && Time.time - last < 3f) return false;   // eben erst gedrückt
            try
            {
                GameBridge.Trigger(cmd);
                _lastRun[cmd.Name] = Time.time;
                _commands++;
                _ordersFollowed++;
                Plugin.Log.LogInfo("Sofortbefehl: " + Char.Name + " führt aus: " + cmd.Name);
                P.Add("Auf Befehl des Captains hast du sofort ausgeführt: " + cmd.Description +
                      ". Bestätige knapp (\"Aye, Captain\" o. ä.) und führe es nicht noch einmal aus.", Priority.Urgent, fromCaptain: true, addressed: true);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(Char.Name + ": Sofortbefehl " + cmd.Name + " fehlgeschlagen: " + e.Message);
                return false;
            }
        }

        /// <summary>Steht das (fast wortgleich) schon in den Notizen oder Lektionen?</summary>
        bool KnownNote(string s)
        {
            var w = NoteWords(s);
            foreach (var list in new[] { Char.MissionNotes, Char.Lessons })
                foreach (var old in list)
                {
                    var o = NoteWords(old);
                    int common = 0;
                    foreach (var x in w) if (o.Contains(x)) common++;
                    if (Math.Max(w.Count, o.Count) > 0 && common >= 0.7 * Math.Max(w.Count, o.Count)) return true;
                }
            return false;
        }

        static HashSet<string> NoteWords(string s)
        {
            var set = new HashSet<string>();
            foreach (var x in System.Text.RegularExpressions.Regex.Split(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+"))
                if (x.Length > 2) set.Add(x);
            return set;
        }

        /// <summary>Hat die Längengrenze den Satz abgeschnitten, bis zum letzten vollständigen Satz kürzen.</summary>
        static string CompleteSentences(string s)
        {
            s = s.Trim();
            if (s.Length == 0 || ".!?…\"".IndexOf(s[s.Length - 1]) >= 0) return s;
            int cut = s.LastIndexOfAny(new[] { '.', '!', '?', '…' });
            return cut > 10 ? s.Substring(0, cut + 1) : s + " …";
        }

        void ExecuteCommands(List<object> commands, bool captainSpoke)
        {
            if (commands == null) return;
            // Befehl des Captains: Seine letzten Worte (höchstens 60 s alt) müssen GENAU diesen Befehl anordnen.
            // Vorher galt jeder Satz als Befehl (Brannok griff mitten in der Flucht an), und ein Befehl verfiel,
            // sobald eine andere Meldung dazwischenkam (Venn durfte "Warp, Energie!" nicht ausführen).
            string orderWords = Time.realtimeSinceStartup - _bridge.LastCaptainTime < 60f ? _bridge.LastCaptainWords : null;
            var autonomy = (Autonomy)_cfg.AutonomyLevel.Value;
            bool mayAct = captainSpoke || autonomy == Autonomy.Act;
            int executed = 0;
            foreach (var o in commands)
            {
                if (executed >= _cfg.MaxCommandsPerTurn.Value) break;
                var cmd = CrewCommands.Find(o as string);
                if (cmd == null || cmd.Owner != Station) continue;
                bool ordered = OrderHints.Orders(Station, cmd.Name, orderWords);
                float last;
                if (_lastRun.TryGetValue(cmd.Name, out last) && Time.time - last < (ordered ? 8f : 20f)) continue;   // nicht dauernd dasselbe drücken (auch nicht nach einem Sofortbefehl)
                if (!mayAct && !ordered) { Plugin.Log.LogInfo(Char.Name + " schlägt vor (nicht ausgeführt): " + cmd.Name); continue; }
                string blocked = ordered ? null : _bridge.Orders.Blocks(cmd.Name);
                if (blocked != null)
                {
                    Plugin.Log.LogInfo(Char.Name + ": " + cmd.Name + " widerspricht der Daueranweisung (" + blocked + ").");
                    P.Add("Du wolltest " + cmd.Description + ", aber die Daueranweisung des Captains gilt: " + blocked + ".", Priority.Background);
                    continue;
                }
                if (!ordered && CrewCommands.OrderOnly(cmd))
                {
                    Plugin.Log.LogInfo(Char.Name + " darf " + cmd.Name + " nur auf Befehl.");
                    P.Add("Du wolltest " + cmd.Description + ", das tust du nur auf ausdrücklichen Befehl des Captains.", Priority.Background);
                    continue;
                }
                if (cmd.Major && !ordered && !_cfg.AllowMajorWithoutOrder.Value &&
                    !Ranks.MayActMajor(Char.Xp, EventLog.UnderThreat(60f)))
                {
                    Plugin.Log.LogInfo(Char.Name + " (" + Char.Rank + ") darf " + cmd.Name + " nicht eigenmächtig.");
                    P.Add("Du wolltest " + cmd.Description + ", das darfst du ohne Befehl des Captains nicht.", Priority.Background);
                    continue;
                }
                try
                {
                    GameBridge.Trigger(cmd);
                    _lastRun[cmd.Name] = Time.time;
                    executed++;
                    _commands++;
                    if (ordered || captainSpoke) _ordersFollowed++;
                    P.Add("Du hast an deiner Konsole ausgeführt: " + cmd.Description, Priority.Background);
                    Plugin.Log.LogInfo(Char.Name + " führt aus: " + cmd.Name);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError(Char.Name + ": Befehl " + cmd.Name + " fehlgeschlagen: " + e);
                }
            }
        }

        /// <summary>
        /// Älteste Wortwechsel verwerfen, bis die Anfrage ins Kontextfenster des Slots passt
        /// (grob 3 Zeichen je Token, Platz für die Antwort und etwas Reserve).
        /// </summary>
        void FitHistory(int fixedChars)
        {
            int budgetChars = (_cfg.ContextPerSlot.Value - _cfg.MaxTokens.Value - 300) * 3;
            int total = fixedChars;
            foreach (var m in _history) total += m.Content.Length;
            while (_history.Count >= 2 && total > budgetChars)
            {
                total -= _history[0].Content.Length + _history[1].Content.Length;
                _history.RemoveRange(0, 2);
            }
        }

        const int HistoryUserChars = 700;

        void Remember(string user, string assistant)
        {
            // Frühere Lagebilder nur gekürzt behalten: das Ende enthält die Wahrnehmungen, die Werte sind ohnehin veraltet
            if (user.Length > HistoryUserChars) user = "(frühere Lage, gekürzt) …" + user.Substring(user.Length - HistoryUserChars);
            _history.Add(new ChatMessage("user", user));
            _history.Add(new ChatMessage("assistant", assistant));
            int max = Math.Max(0, _cfg.HistoryTurns.Value) * 2;
            while (_history.Count > max) _history.RemoveAt(0);
        }

        // ---------------- Missionsende / Absturzsicherung ----------------

        /// <summary>Aktuellen Einsatz in die Charakterdatei sichern (falls das Spiel abstürzt).</summary>
        public void Snapshot()
        {
            if (Char == null || _activeSeconds < _cfg.MinActiveSecForDebrief.Value) return;
            Char.UnfinishedLog = P.FullLog(_cfg.DebriefLogChars.Value);
            Char.UnfinishedMinutes = Mathf.RoundToInt(_activeSeconds / 60f);
            Char.UnfinishedCommands = _commands;
            Char.UnfinishedOrdersFollowed = _ordersFollowed;
            Char.Save();
        }

        /// <summary>Mission vorbei: Nachbesprechung vormerken, Station freigeben.</summary>
        public void EndMission()
        {
            if (Char == null) return;
            if (_activeSeconds >= _cfg.MinActiveSecForDebrief.Value && P.Count > 0)
            {
                string log = P.FullLog(_cfg.DebriefLogChars.Value);
                Char.PendingDebriefLog = string.IsNullOrEmpty(Char.PendingDebriefLog) ? log : Char.PendingDebriefLog + "\n" + log;
                Char.PendingStation = Key;
                Char.PendingMinutes += Mathf.RoundToInt(_activeSeconds / 60f);
                Char.PendingCommands += _commands;
                Char.PendingOrdersFollowed += _ordersFollowed;
            }
            Char.UnfinishedLog = "";
            Char.UnfinishedMinutes = Char.UnfinishedCommands = Char.UnfinishedOrdersFollowed = 0;
            Char.Save();
            Assign(null);
        }


        // ---------------- Hilfen ----------------

        public static string StationKey(Station s)
        {
            switch (s)
            {
                case Station.Helm: return "helm";
                case Station.Tactical: return "tactical";
                case Station.Engineering: return "engineering";
                default: return "captain";
            }
        }

        public static string StationDe(Station s)
        {
            switch (s)
            {
                case Station.Helm: return "Steuer";
                case Station.Tactical: return "Taktik";
                case Station.Engineering: return "Technik";
                default: return "Captain";
            }
        }

        public static string StationDe(string key)
        {
            switch (key)
            {
                case "helm": return "Steuer";
                case "tactical": return "Taktik";
                case "engineering": return "Technik";
                default: return key;
            }
        }
    }
}
