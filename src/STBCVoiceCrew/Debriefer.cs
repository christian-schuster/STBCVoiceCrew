using System;
using System.Collections.Generic;
using System.Text;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Nachbesprechung je Charakter: Er liest nur SEIN Missionsprotokoll (was er selbst gesehen und gehört hat),
    /// schreibt Gedächtnis und Charakterentwicklung fort und bekommt Erfahrungspunkte.
    /// Läuft, sobald eine Nachbesprechung aussteht, auch erst in der nächsten Spielsession.
    /// </summary>
    public sealed class Debriefer
    {
        readonly LlmClient _llm;
        readonly Plugin _cfg;
        readonly CrewBrain _bridge;
        Character _running;

        public string Status = "";

        public Debriefer(LlmClient llm, Plugin cfg, CrewBrain bridge)
        {
            _llm = llm;
            _cfg = cfg;
            _bridge = bridge;
        }

        public void Tick(Roster roster)
        {
            if (_running != null) return;
            foreach (var c in roster.All)
                if (c.HasPendingDebrief) { Run(c); return; }
        }

        void Run(Character c)
        {
            _running = c;
            // Daten übernehmen, damit eine neue Mission parallel weiterzählen kann
            string log = c.PendingDebriefLog;
            string station = c.PendingStation;
            int minutes = c.PendingMinutes, commands = c.PendingCommands, orders = c.PendingOrdersFollowed;
            var notes = new List<string>(c.MissionNotes);

            var sb = new StringBuilder();
            sb.Append(c.ProfileForPrompt()).Append('\n');
            sb.Append("NACHBESPRECHUNG: Deine Mission an der Konsole ").Append(OfficerAgent.StationDe(station)).Append(" der U.S.S. Aegis ist vorbei.\n");
            sb.Append("Unten steht nur, was DU selbst gesehen und gehört hast. Denk darüber nach wie ").Append(c.Name).Append(" und aktualisiere dein Gedächtnis:\n");
            sb.Append("- personal: wie du dich als Person entwickelt hast (Erlebnisse, Gefühle, Wachstum; 1–3 Sätze, baut auf dem Bisherigen auf).\n");
            sb.Append("- captainImpression: dein Eindruck vom Captain und was er von dir erwartet.\n");
            sb.Append("- relationships: dein Eindruck von den Kollegen, mit denen du gesprochen hast.\n");
            sb.Append("- lessons: vollständige, aktualisierte Liste deiner Lektionen (höchstens ").Append(_cfg.MaxLessons.Value)
              .Append("). Behalte Bewährtes, fasse Doppeltes zusammen, streiche Widerlegtes, ergänze Neues. Jede Lektion ist ein konkreter, umsetzbarer Satz.\n");
            sb.Append("- missionSummary: ein Satz, was diese Mission aus deiner Sicht war.\n");
            sb.Append("- captainFeedback: hat der Captain dich gelobt (lob), getadelt (tadel) oder keins von beiden (neutral)?\n");
            sb.Append("- traitChanges: höchstens zwei Charakterzüge, die sich durch ein konkretes Erlebnis dieser Mission verändert haben (delta -1 oder +1; 0 oder leere Liste, wenn sich nichts geändert hat).\n");
            sb.Append("- anecdote: ein komischer Moment dieser Mission (Missverständnis, Versprecher, kleine Panne), so wie du ihn später gern erzählst, in einem Satz. Leer, wenn es keinen gab; nichts erfinden.\n");
            sb.Append("Schreib auf ").Append(_cfg.Language.Value).Append(", aus deiner Ich-Perspektive.\n\n");
            sb.Append(c.MemoryForPrompt()).Append('\n');
            if (notes.Count > 0)
            {
                sb.Append("Deine Notizen aus der Mission:\n");
                foreach (var n in notes) sb.Append("- ").Append(n).Append('\n');
            }
            sb.Append("\nDein Protokoll (").Append(minutes).Append(" Minuten im Dienst, ").Append(commands).Append(" Befehle ausgeführt):\n").Append(log);

            Status = "Nachbesprechung " + c.Name + " …";
            _llm.Send(new List<ChatMessage> { new ChatMessage("user", sb.ToString()) }, Schema(), 1500, (content, error, latency) =>
            {
                _running = null;
                if (error != null) { Status = "Nachbesprechung " + c.Name + " fehlgeschlagen, neuer Versuch später"; return; }
                try
                {
                    Apply(c, content, log, station, minutes, commands, orders, notes.Count);
                }
                catch (Exception e)
                {
                    Status = "Nachbesprechung " + c.Name + " unbrauchbar: " + e.Message;
                    Plugin.Log.LogWarning(Status + " – " + content);
                    // Protokoll verwerfen, damit es nicht endlos wiederholt wird; XP gibt es trotzdem
                    Finish(c, log, station, minutes, commands, orders, "neutral", null, notes.Count);
                }
            });
        }

        object Schema()
        {
            var traitNames = new List<object>();
            foreach (var t in Character.TraitNames) traitNames.Add(t[0]);
            var str = new Dictionary<string, object> { { "type", "string" }, { "maxLength", 400 } };
            var lesson = new Dictionary<string, object> { { "type", "string" }, { "maxLength", 200 } };
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "personal", str },
                        { "captainImpression", str },
                        { "relationships", str },
                        { "lessons", new Dictionary<string, object> { { "type", "array" }, { "maxItems", _cfg.MaxLessons.Value }, { "items", lesson } } },
                        { "missionSummary", str },
                        { "anecdote", new Dictionary<string, object> { { "type", "string" }, { "maxLength", 300 } } },
                        { "captainFeedback", new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "lob", "neutral", "tadel" } } } },
                        { "traitChanges", new Dictionary<string, object>
                            {
                                { "type", "array" },
                                { "maxItems", 2 },
                                { "items", new Dictionary<string, object>
                                    {
                                        { "type", "object" },
                                        { "properties", new Dictionary<string, object>
                                            {
                                                { "trait", new Dictionary<string, object> { { "type", "string" }, { "enum", traitNames } } },
                                                { "delta", new Dictionary<string, object> { { "type", "integer" }, { "enum", new List<object> { -1, 0, 1 } } } },
                                                { "reason", str }
                                            }
                                        },
                                        { "required", new List<object> { "trait", "delta", "reason" } }
                                    }
                                }
                            }
                        }
                    }
                },
                { "required", new List<object> { "personal", "captainImpression", "relationships", "lessons", "missionSummary", "captainFeedback", "traitChanges" } }
            };
        }

        void Apply(Character c, string content, string log, string station, int minutes, int commands, int orders, int noteCount)
        {
            var root = Json.Parse(content);
            var lessons = Json.Arr(Json.Get(root, "lessons"));
            if (lessons == null) throw new FormatException("lessons fehlt");

            c.Lessons.Clear();
            foreach (var l in lessons)
            {
                var s = l as string;
                if (!string.IsNullOrEmpty(s)) c.Lessons.Add(s);
            }
            c.Personal = Json.Str(root, "personal") ?? c.Personal;
            c.CaptainImpression = Json.Str(root, "captainImpression") ?? c.CaptainImpression;
            c.Relationships = Json.Str(root, "relationships") ?? c.Relationships;

            var changes = Json.Arr(Json.Get(root, "traitChanges"));
            if (changes != null)
                foreach (var ch in changes)
                {
                    string trait = Json.Str(ch, "trait");
                    var d = Json.Get(ch, "delta");
                    if (trait == null || !(d is double)) continue;
                    int delta = Math.Sign((int)(double)d);
                    int before = c.Trait(trait);
                    c.SetTrait(trait, before + delta);
                    if (c.Trait(trait) != before)
                        c.Chronicle.Add(Today() + ": " + trait + " " + before + " → " + c.Trait(trait) + " (" + Json.Str(ch, "reason") + ")");
                }

            string anecdote = (Json.Str(root, "anecdote") ?? "").Trim();
            if (anecdote.Length > 15)
            {
                c.Anecdotes.Add(anecdote);
                while (c.Anecdotes.Count > 6) c.Anecdotes.RemoveAt(0);
                Plugin.Log.LogInfo(c.Name + " merkt sich eine Anekdote: " + anecdote);
            }
            string summary = Json.Str(root, "missionSummary");
            Finish(c, log, station, minutes, commands, orders, Json.Str(root, "captainFeedback") ?? "neutral", summary, noteCount);
        }

        void Finish(Character c, string log, string station, int minutes, int commands, int orders, string feedback, string summary, int noteCount)
        {
            // ---- Erfahrungspunkte ----
            int xp = 50                                   // Teilnahme
                   + Math.Min(100, minutes * 5)           // Dienstzeit
                   + Math.Min(75, commands * 5)           // Arbeit an der Konsole
                   + Math.Min(100, orders * 10);          // Befehle des Captains umgesetzt
            if (feedback == "lob") { xp += 75; c.Commendations++; }
            else if (feedback == "tadel") xp -= 25;

            int rankBefore = Ranks.Index(c.Xp);
            c.Xp = Math.Max(0, c.Xp + xp);
            c.Missions++;

            if (!string.IsNullOrEmpty(summary))
                c.Chronicle.Add(Today() + ", " + OfficerAgent.StationDe(station) + ": " + summary + (feedback == "lob" ? " (Belobigung)" : ""));
            if (Ranks.Index(c.Xp) > rankBefore)
            {
                string news = c.Name + " wurde zum " + c.Rank + " befördert.";
                c.PromotionNews.Add(news);
                c.Chronicle.Add(Today() + ": Beförderung zum " + c.Rank);
                _bridge.Notice(news);
            }
            while (c.Chronicle.Count > _cfg.ChronicleMax.Value) c.Chronicle.RemoveAt(0);

            // Nur das abhaken, was diese Nachbesprechung verarbeitet hat (eine neue Mission kann schon laufen)
            if (c.PendingDebriefLog == log)
            {
                c.PendingDebriefLog = "";
                c.PendingMinutes = c.PendingCommands = c.PendingOrdersFollowed = 0;
            }
            else
            {
                c.PendingDebriefLog = c.PendingDebriefLog.Length > log.Length ? c.PendingDebriefLog.Substring(log.Length) : "";
                c.PendingMinutes = Math.Max(0, c.PendingMinutes - minutes);
                c.PendingCommands = Math.Max(0, c.PendingCommands - commands);
                c.PendingOrdersFollowed = Math.Max(0, c.PendingOrdersFollowed - orders);
            }
            c.MissionNotes.RemoveRange(0, Math.Min(noteCount, c.MissionNotes.Count));
            c.Save();

            Status = c.Name + ": +" + xp + " XP (" + c.Xp + ", " + c.Rank + ")";
            Plugin.Log.LogInfo("Nachbesprechung " + Status + ", " + c.Lessons.Count + " Lektionen.");
        }

        static string Today() => DateTime.Now.ToString("yyyy-MM-dd");
    }
}
