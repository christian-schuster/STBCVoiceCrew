using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StbcAiCrew
{
    public sealed class CrewLine
    {
        public float Time;
        public string Speaker;
        public string Text;
    }

    /// <summary>
    /// Die Brücke: besetzt die Stationen mit Charakteren, verteilt Gesprochenes an alle, die es hören,
    /// sorgt dafür, dass nicht alle gleichzeitig reden, und stößt die Nachbesprechungen an.
    /// </summary>
    public sealed class CrewBrain
    {
        readonly Plugin _cfg;
        public readonly Roster Roster;
        public readonly Debriefer Debriefer;
        public readonly List<OfficerAgent> Agents = new List<OfficerAgent>();
        public readonly List<CrewLine> Transcript = new List<CrewLine>();
        public readonly List<string> Notices = new List<string>();

        readonly Queue<KeyValuePair<OfficerAgent, string>> _speech = new Queue<KeyValuePair<OfficerAgent, string>>();
        float _floorFreeAt;
        float _nextSnapshot;
        public bool InMission { get; private set; }

        public CrewBrain(LlmClient llm, Plugin cfg, Roster roster)
        {
            _cfg = cfg;
            Roster = roster;
            Debriefer = new Debriefer(llm, cfg, this);
            foreach (var st in EventLog.CrewStations) Agents.Add(new OfficerAgent(st, llm, cfg, this));
        }

        // ---------------- Missionsablauf ----------------

        public void StartMission()
        {
            InMission = true;
            EventLog.ResetMission();
            _speech.Clear();
            var taken = new List<Character>();
            foreach (var a in Agents)
            {
                var c = Roster.Pick(a.Key, _cfg.CrewWish(a.Station), taken);
                if (c == null) continue;
                taken.Add(c);
                c.LastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                c.Save();
                a.Assign(c);
            }
            string lineup = Lineup(null);
            Plugin.Log.LogInfo("Mission beginnt. " + lineup);
            Notice("Besatzung: " + lineup);
            EventLog.ToAll("Mission beginnt. Besatzung: " + lineup, Priority.Background);

            // Beförderungen werden vor der Crew verkündet
            foreach (var a in Agents)
            {
                if (a.Char == null || a.Char.PromotionNews.Count == 0) continue;
                foreach (var news in a.Char.PromotionNews)
                {
                    EventLog.ToAll("Bekanntmachung: " + news, Priority.Important);
                    GameBridge.ShowOnAllConsoles("Bekanntmachung: " + news, 8f);
                }
                a.Char.PromotionNews.Clear();
                a.Char.Save();
            }
        }

        public void EndMission(string reason)
        {
            if (!InMission) return;
            InMission = false;
            Plugin.Log.LogInfo("Mission beendet (" + reason + ").");
            foreach (var a in Agents) a.EndMission();
            EventLog.ResetMission();
            _speech.Clear();
        }

        /// <summary>Beim Beenden des Spiels: laufenden Einsatz sichern, Nachbesprechung beim nächsten Start.</summary>
        public void SaveForShutdown()
        {
            foreach (var a in Agents) a.Snapshot();
        }

        public void Tick(ShipStatus status)
        {
            if (InMission)
            {
                ReleaseSpeech();
                foreach (var a in Agents) a.Tick(status);
                if (Time.time >= _nextSnapshot)
                {
                    _nextSnapshot = Time.time + 60f;
                    foreach (var a in Agents) a.Snapshot();
                }
            }
            Debriefer.Tick(Roster);
        }

        // ---------------- Sprechen und Hören ----------------

        /// <summary>Der Captain spricht (Spracheingabe oder F8): alle auf der Brücke hören es.</summary>
        public void CaptainSays(string text) => HumanSays(Station.Captain, text);

        /// <summary>
        /// Ein Mensch am lokalen Rechner spricht. Wer gemeint ist, ergibt sich aus Namen, Stationen und
        /// Fachbegriffen ("Schilde hoch" an Taktik). Die anderen hören mit, ohne sich angesprochen zu fühlen.
        /// Nur Worte des Captains gelten als Befehl.
        /// </summary>
        public void HumanSays(Station speaker, string text)
        {
            text = text.Trim();
            if (text.Length == 0) return;
            bool isCaptain = speaker == Station.Captain;
            string who = isCaptain ? "Captain" : "Mensch an " + OfficerAgent.StationDe(speaker);
            AddLine(who, text);

            var addressed = new List<OfficerAgent>();
            foreach (var a in Agents) if (a.Char != null && a.Station != speaker && Mentions(text, a, topics: true)) addressed.Add(a);
            foreach (var a in Agents)
            {
                if (a.Station == speaker) continue;
                var p = EventLog.Of(a.Station);
                string line = who + " sagt: \"" + text + "\"";
                if (addressed.Count == 0) p.Add(line + " (an alle)", isCaptain ? Priority.Urgent : Priority.Important, fromCaptain: isCaptain);
                else if (addressed.Contains(a)) p.Add(line + " (an dich)", Priority.Urgent, fromCaptain: isCaptain, addressed: true);
                else p.Add(line + " (an andere)", Priority.Background);
            }
        }

        public void Say(OfficerAgent speaker, string text)
        {
            _speech.Enqueue(new KeyValuePair<OfficerAgent, string>(speaker, text));
        }

        /// <summary>Immer nur einer spricht; die Dauer richtet sich nach der Textlänge.</summary>
        void ReleaseSpeech()
        {
            if (_speech.Count == 0 || Time.time < _floorFreeAt) return;
            var item = _speech.Dequeue();
            var speaker = item.Key;
            string text = item.Value;
            if (speaker.Char == null) return;

            string name = speaker.Char.Title;
            AddLine(name + " (" + OfficerAgent.StationDe(speaker.Station) + ")", text);
            GameBridge.ShowOnAllConsoles(speaker.Char.Name + ": " + text, _cfg.MessageSeconds.Value);
            _floorFreeAt = Time.time + 1.2f + text.Length / 15f;

            foreach (var other in Agents)
            {
                var p = EventLog.Of(other.Station);
                if (other == speaker) { p.Add("Du hast gesagt: \"" + text + "\"", Priority.Background); continue; }
                bool addressed = other.Char != null && Mentions(text, other, topics: false);
                p.Add(name + " (" + OfficerAgent.StationDe(speaker.Station) + ") sagt: \"" + text + "\"" + (addressed ? " (an dich)" : ""),
                      addressed ? Priority.Urgent : Priority.Background, addressed: addressed);
            }
        }

        static readonly Dictionary<Station, string[]> StationWords = new Dictionary<Station, string[]>
        {
            { Station.Helm, new[] { "steuer", "helm", "navigation", "navigator" } },
            { Station.Tactical, new[] { "taktik", "tactical", "waffenoffizier" } },
            { Station.Engineering, new[] { "technik", "maschinenraum", "engineering", "ingenieur", "chefingenieur" } },
        };

        // Fachbegriffe, an denen man auch ohne Namen erkennt, wer gemeint ist (nur bei Worten von Menschen)
        static readonly Dictionary<Station, string[]> TopicWords = new Dictionary<Station, string[]>
        {
            { Station.Helm, new[] { "kurs", "geschwindigkeit", "impuls", "volle kraft", "halbe kraft", "viertel", "stopp", "rückwärts",
                                    "ausweich", "abdrehen", "beidrehen", "anfliegen", "flieg", "warp ausführen", "warp aktivieren", "beschleunig" } },
            { Station.Tactical, new[] { "schilde hoch", "schilder hoch", "schilde runter", "schilder runter", "schilde aktivieren", "schilde senken", "schutzschild", "phaser", "torpedo", "feuer",
                                        "ziel erfassen", "zielen", "anvisieren", "scannen", "scan", "modulier", "stören" } },
            { Station.Engineering, new[] { "energie", "reparier", "reparatur", "schaden", "schäden", "warpkern", "optimier", "warp vorbereiten",
                                           "beamen", "transporter", "signatur" } },
        };

        static bool Mentions(string text, OfficerAgent a, bool topics)
        {
            string t = text.ToLowerInvariant();
            foreach (var part in a.Char.Name.ToLowerInvariant().Split(' ', '\''))
                if (part.Length >= 3 && t.Contains(part)) return true;
            foreach (var w in StationWords[a.Station]) if (t.Contains(w)) return true;
            if (topics)
                foreach (var w in TopicWords[a.Station]) if (t.Contains(w)) return true;
            return false;
        }

        /// <summary>Wer sitzt wo: für Prompts und Overlay. "du" markiert den Fragenden.</summary>
        public string Lineup(OfficerAgent me)
        {
            var sb = new StringBuilder("Captain (Mensch)");
            foreach (var a in Agents)
            {
                sb.Append(" | ").Append(OfficerAgent.StationDe(a.Station)).Append(": ");
                if (a.Char != null && a.Active) sb.Append(a.Char.Title).Append(a == me ? " (du)" : "");
                else sb.Append("ein menschlicher Offizier");
            }
            return sb.ToString();
        }

        void AddLine(string speaker, string text)
        {
            Transcript.Add(new CrewLine { Time = Time.time, Speaker = speaker, Text = text });
            if (Transcript.Count > 60) Transcript.RemoveAt(0);
            Plugin.Log.LogInfo(speaker + ": " + text);
        }

        public void Notice(string text)
        {
            Notices.Add(text);
            if (Notices.Count > 8) Notices.RemoveAt(0);
            Plugin.Log.LogInfo(text);
        }
    }
}
