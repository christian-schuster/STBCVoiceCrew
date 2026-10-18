using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StbcAiCrew
{
    public enum Autonomy
    {
        OnlyOrders = 0,   // reagiert nur auf Ansprache des Captains
        Advise = 1,       // meldet sich selbst zu Wort, handelt aber nur auf Befehl
        Act = 2           // spielt selbst mit: handelt eigenständig an den Bot-Stationen
    }

    public sealed class CrewLine
    {
        public float Time;
        public string Speaker;
        public string Text;
    }

    /// <summary>
    /// Das "Gehirn" der Bot-Crew: baut aus Gedächtnis, Ereignissen und Captain-Ansprache einen Prompt,
    /// lässt das LLM als Rao/Hoyle/T'nar antworten und führt die gewählten Befehle im Spiel aus.
    /// Die feine Steuerung (Zielen, Kurs halten) erledigen weiter die eingebauten Bots –
    /// das LLM trifft die Entscheidungen darüber.
    /// </summary>
    public sealed class CrewBrain
    {
        readonly LlmClient _llm;
        readonly CrewMemory _memory;
        readonly Plugin _cfg;

        readonly Queue<string> _captainSays = new Queue<string>();
        readonly List<ChatMessage> _history = new List<ChatMessage>();
        public readonly List<CrewLine> Transcript = new List<CrewLine>();

        float _lastDecision = -999f;
        string _pendingDebriefLog;
        public string Status = "bereit";

        static readonly string[] Officers = { "helm", "tactical", "engineering" };

        public CrewBrain(LlmClient llm, CrewMemory memory, Plugin cfg)
        {
            _llm = llm;
            _memory = memory;
            _cfg = cfg;
        }

        public void CaptainSays(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _captainSays.Enqueue(text.Trim());
            AddLine("Captain", text.Trim());
        }

        public void Tick()
        {
            if (_llm.Busy) return;

            // Nachbesprechung hat Vorrang, sobald das LLM frei ist
            if (_pendingDebriefLog != null)
            {
                RunDebrief(_pendingDebriefLog);
                _pendingDebriefLog = null;
                return;
            }

            if (!GameBridge.InMission) return;

            if (_captainSays.Count > 0)
            {
                var sb = new StringBuilder();
                while (_captainSays.Count > 0) sb.Append(_captainSays.Dequeue()).Append(' ');
                Decide(sb.ToString().Trim());
                return;
            }

            var autonomy = (Autonomy)_cfg.AutonomyLevel.Value;
            if (autonomy == Autonomy.OnlyOrders) return;

            float since = Time.time - _lastDecision;
            bool news = EventLog.ImportantSinceMark > 0 && since >= _cfg.ReactionIntervalSec.Value;
            bool heartbeat = autonomy == Autonomy.Act && since >= _cfg.HeartbeatSec.Value && EventLog.MissionEventCount > 0;
            if (news || heartbeat) Decide(null);
        }

        // ---------------- Entscheidungen im Einsatz ----------------

        void Decide(string captainWords)
        {
            _lastDecision = Time.time;
            EventLog.Mark();

            var messages = new List<ChatMessage> { new ChatMessage("system", SystemPrompt()) };
            messages.AddRange(_history);
            string user = SituationPrompt(captainWords);
            messages.Add(new ChatMessage("user", user));

            Status = captainWords != null ? "hört zu …" : "beobachtet die Lage …";
            bool sent = _llm.Send(messages, DecisionSchema(), (content, error) =>
            {
                if (error != null) { Status = "LLM-Fehler: " + error; return; }
                Status = "bereit (" + _llm.LastLatencySec.ToString("0.0") + " s)";
                Remember(user, content);
                Apply(content, captainWords != null);
            });
            if (!sent && captainWords != null) _captainSays.Enqueue(captainWords);
        }

        string SystemPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("Ihr seid die Brückencrew der U.S.S. Aegis (Star Trek Bridge Crew) und spielt gemeinsam mit einem menschlichen Captain.\n");
            sb.Append("Offiziere:\n");
            sb.Append("- helm: Lt. Rao, Steuer. Ruhig, präzise, meldet Kurs und Geschwindigkeit knapp.\n");
            sb.Append("- tactical: Lt. Hoyle, Taktik. Direkt, wachsam, denkt zuerst an Schutz von Schiff und Crew.\n");
            sb.Append("- engineering: Lt. T'nar, Technik. Nüchtern und analytisch, behält Energie und Schäden im Blick.\n");
            if (!string.IsNullOrEmpty(_cfg.ExtraPersonaNotes.Value))
                sb.Append(_cfg.ExtraPersonaNotes.Value).Append('\n');
            sb.Append("\nRegeln:\n");
            sb.Append("- Antwortet ausschließlich als JSON nach Schema. 'speech' sind gesprochene Funksprüche an den Captain, 'commands' sind Befehle, die ihr sofort ausführt.\n");
            sb.Append("- Sprecht ").Append(_cfg.Language.Value).Append(", im Ton der Sternenflotte, höchstens ein bis zwei kurze Sätze pro Offizier. Nur wer etwas Relevantes zu sagen hat, spricht.\n");
            sb.Append("- Befehle des Captains haben Vorrang und werden umgesetzt, wenn sie zur Liste passen. Bestätigt sie knapp (\"Aye, Captain\").\n");
            sb.Append("- Ist ein Befehl unklar oder nicht in der Liste, fragt kurz nach statt zu raten.\n");
            sb.Append("- Jeder Offizier bedient nur seine eigene Station. Stationen, die ein Mensch bedient, steuert ihr nicht – dort dürft ihr nur Vorschläge machen.\n");
            sb.Append("- Höchstens ").Append(_cfg.MaxCommandsPerTurn.Value).Append(" Befehle pro Antwort. Wiederholt keine Befehle, die schon ausgeführt sind.\n");
            sb.Append("- Ziele (für Angriff, Anflug, Scan) wählt der Captain auf seiner Konsole; Befehle mit Ziel wirken auf das aktuell gewählte Ziel.\n");
            sb.Append("- Gibt es nichts zu tun oder zu sagen, antwortet mit leeren Listen.\n");

            sb.Append("\nVerfügbare Befehle (Name: Wirkung, Station):\n");
            foreach (var c in CrewCommands.All)
                sb.Append("- ").Append(c.Name).Append(": ").Append(c.Description).Append(" [").Append(StationKey(c.Station)).Append("]\n");

            sb.Append('\n').Append(_memory.ForPrompt());
            return sb.ToString();
        }

        string SituationPrompt(string captainWords)
        {
            var sb = new StringBuilder();
            var autonomy = (Autonomy)_cfg.AutonomyLevel.Value;
            sb.Append("Stationen: ");
            foreach (var st in new[] { Station.Helm, Station.Tactical, Station.Engineering })
                sb.Append(StationKey(st)).Append('=').Append(GameBridge.IsStationAI(st) ? "ihr" : "Mensch").Append("  ");
            sb.Append('\n');
            sb.Append("Letzte Ereignisse:\n").Append(EventLog.Recent(_cfg.EventsInPrompt.Value));
            if (captainWords != null)
            {
                sb.Append("\nDer Captain sagt: \"").Append(captainWords).Append("\"\n");
            }
            else if (autonomy == Autonomy.Act)
            {
                sb.Append("\nDer Captain hat gerade nichts gesagt. Handelt eigenständig, wenn die Lage es erfordert, und meldet wichtige Entwicklungen.\n");
            }
            else
            {
                sb.Append("\nDer Captain hat gerade nichts gesagt. Ihr dürft nur melden und vorschlagen, nicht handeln (commands leer lassen).\n");
            }
            return sb.ToString();
        }

        object DecisionSchema()
        {
            var cmdNames = new List<object>();
            foreach (var c in CrewCommands.All) cmdNames.Add(c.Name);
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "speech", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "maxItems", 3 },
                                { "items", new Dictionary<string, object>
                                    {
                                        { "type", "object" },
                                        { "properties", new Dictionary<string, object>
                                            {
                                                { "officer", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object>(Officers) } } },
                                                { "text", new Dictionary<string, object> { { "type", "string" } } }
                                            }
                                        },
                                        { "required", new List<object> { "officer", "text" } }
                                    }
                                }
                            }
                        },
                        { "commands", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "maxItems", Math.Max(0, _cfg.MaxCommandsPerTurn.Value) },
                                { "items", new Dictionary<string, object> { { "type", "string" }, { "enum", cmdNames } } }
                            }
                        }
                    }
                },
                { "required", new List<object> { "speech", "commands" } }
            };
        }

        void Apply(string content, bool fromCaptain)
        {
            object root;
            try { root = Json.Parse(content); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Crew-Antwort kein gültiges JSON: " + e.Message + " – " + content);
                return;
            }

            var speech = Json.Arr(Json.Get(root, "speech"));
            if (speech != null)
                foreach (var s in speech)
                {
                    string officer = Json.Str(s, "officer");
                    string text = Json.Str(s, "text");
                    if (string.IsNullOrEmpty(text)) continue;
                    Speak(officer, text);
                }

            var autonomy = (Autonomy)_cfg.AutonomyLevel.Value;
            bool mayAct = fromCaptain || autonomy == Autonomy.Act;
            var commands = Json.Arr(Json.Get(root, "commands"));
            if (commands == null) return;

            int executed = 0;
            foreach (var o in commands)
            {
                if (executed >= _cfg.MaxCommandsPerTurn.Value) break;
                var cmd = CrewCommands.Find(o as string);
                if (cmd == null) continue;
                if (!mayAct)
                {
                    Plugin.Log.LogInfo("Vorschlag (nicht ausgeführt, Autonomie=Beraten): " + cmd.Name);
                    continue;
                }
                if (!GameBridge.IsStationAI(cmd.Station))
                {
                    Plugin.Log.LogInfo("Übersprungen, Station wird von einem Menschen bedient: " + cmd.Name);
                    continue;
                }
                try
                {
                    GameBridge.Trigger(cmd);
                    executed++;
                    EventLog.Add("Crew hat ausgeführt: " + cmd.Description + " (" + cmd.Name + ")", false);
                    Plugin.Log.LogInfo("Ausgeführt: " + cmd.Name);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Befehl " + cmd.Name + " fehlgeschlagen: " + e);
                }
            }
        }

        void Speak(string officer, string text)
        {
            string name = OfficerName(officer);
            AddLine(name, text);
            GameBridge.ShowConsoleMessage(Station.Captain, name + ": " + text, _cfg.MessageSeconds.Value);
            // Hier hängt später die Sprachausgabe (TTS) an.
        }

        void Remember(string user, string assistant)
        {
            // Kurzer Gesprächsverlauf für Zusammenhang ("und jetzt die Torpedos")
            _history.Add(new ChatMessage("user", user));
            _history.Add(new ChatMessage("assistant", assistant));
            int max = Math.Max(0, _cfg.HistoryTurns.Value) * 2;
            while (_history.Count > max) _history.RemoveAt(0);
        }

        void AddLine(string speaker, string text)
        {
            Transcript.Add(new CrewLine { Time = Time.time, Speaker = speaker, Text = text });
            if (Transcript.Count > 50) Transcript.RemoveAt(0);
            Plugin.Log.LogInfo(speaker + ": " + text);
        }

        // ---------------- Nachbesprechung / Lernen ----------------

        /// <summary>Am Missionsende aufrufen: Protokoll sichern, Gesprächsverlauf leeren, Lernen anstoßen.</summary>
        public void EndMission(string reason)
        {
            if (EventLog.MissionEventCount >= _cfg.MinEventsForDebrief.Value)
            {
                _pendingDebriefLog = EventLog.FullLog(_cfg.DebriefLogChars.Value);
                Plugin.Log.LogInfo("Mission beendet (" + reason + "), Nachbesprechung geplant.");
            }
            EventLog.ResetMission();
            _history.Clear();
            _captainSays.Clear();
        }

        void RunDebrief(string missionLog)
        {
            var sb = new StringBuilder();
            sb.Append("Nachbesprechung nach einer Mission der U.S.S. Aegis. Ihr seid die Brückencrew (helm = Rao, tactical = Hoyle, engineering = T'nar).\n");
            sb.Append("Lest das Missionsprotokoll und pflegt euer Langzeitgedächtnis:\n");
            sb.Append("- Gebt die vollständige, aktualisierte Liste eurer Lektionen zurück (höchstens ").Append(_cfg.MaxLessons.Value).Append(").\n");
            sb.Append("- Behaltet bewährte alte Lektionen, fasst Doppeltes zusammen, streicht Widerlegtes, ergänzt Neues.\n");
            sb.Append("- Jede Lektion ist ein konkreter, umsetzbarer Satz (z. B. welche Reaktion auf welche Lage gut oder schlecht war).\n");
            sb.Append("- 'captainProfile': kurz, wie euer Captain führt und was er von euch erwartet (aktualisiert).\n");
            sb.Append("Schreibt auf ").Append(_cfg.Language.Value).Append(".\n\n");
            sb.Append(_memory.ForPrompt()).Append('\n');
            sb.Append("Missionsprotokoll:\n").Append(missionLog);

            var schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "lessons", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "maxItems", _cfg.MaxLessons.Value },
                                { "items", new Dictionary<string, object>
                                    {
                                        { "type", "object" },
                                        { "properties", new Dictionary<string, object>
                                            {
                                                { "officer", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "helm", "tactical", "engineering", "crew" } } } },
                                                { "text", new Dictionary<string, object> { { "type", "string" } } }
                                            }
                                        },
                                        { "required", new List<object> { "officer", "text" } }
                                    }
                                }
                            }
                        },
                        { "captainProfile", new Dictionary<string, object> { { "type", "string" } } }
                    }
                },
                { "required", new List<object> { "lessons", "captainProfile" } }
            };

            Status = "Nachbesprechung …";
            int oldTokens = _llm.MaxTokens;
            _llm.MaxTokens = 1500;
            bool sent = _llm.Send(new List<ChatMessage> { new ChatMessage("user", sb.ToString()) }, schema, (content, error) =>
            {
                if (error != null) { Status = "Nachbesprechung fehlgeschlagen: " + error; return; }
                try
                {
                    var root = Json.Parse(content);
                    var lessons = Json.Arr(Json.Get(root, "lessons"));
                    if (lessons == null) throw new FormatException("lessons fehlt");
                    _memory.Lessons.Clear();
                    foreach (var l in lessons)
                    {
                        string text = Json.Str(l, "text");
                        if (!string.IsNullOrEmpty(text))
                            _memory.Lessons.Add(new CrewMemory.Lesson { Officer = Json.Str(l, "officer") ?? "crew", Text = text });
                    }
                    _memory.CaptainProfile = Json.Str(root, "captainProfile") ?? _memory.CaptainProfile;
                    _memory.MissionsPlayed++;
                    _memory.Save();
                    Status = "Nachbesprechung gespeichert (" + _memory.Lessons.Count + " Lektionen)";
                    Plugin.Log.LogInfo(Status);
                }
                catch (Exception e)
                {
                    Status = "Nachbesprechung unbrauchbar: " + e.Message;
                    Plugin.Log.LogWarning(Status + " – " + content);
                }
            });
            _llm.MaxTokens = oldTokens;
            if (!sent) _pendingDebriefLog = missionLog;
        }

        // ---------------- Hilfen ----------------

        static string StationKey(Station s)
        {
            switch (s)
            {
                case Station.Helm: return "helm";
                case Station.Tactical: return "tactical";
                case Station.Engineering: return "engineering";
                default: return "mehrere";
            }
        }

        static string OfficerName(string key)
        {
            switch (key)
            {
                case "helm": return "Rao";
                case "tactical": return "Hoyle";
                case "engineering": return "T'nar";
                default: return "Crew";
            }
        }
    }
}
