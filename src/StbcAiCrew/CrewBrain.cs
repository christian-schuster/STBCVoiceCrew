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

        sealed class SpeechItem
        {
            public Character Char;
            public Station Station;
            public string Text;
            public Action OnSpoken;
            public short[] Pcm;
            public int Rate;
            public bool Ready;
            public float QueuedAt;
        }

        readonly Queue<SpeechItem> _speech = new Queue<SpeechItem>();

        /// <summary>Sprachausgabe (null = nur Text auf den Konsolen).</summary>
        public ISpeechEngine Tts;
        public WavePlayer Player;
        /// <summary>Wird aufgerufen, wenn die Crew für n Sekunden spricht (Mikrofon so lange taub stellen).</summary>
        public Action<float> CrewSpeaking;
        float _floorFreeAt;
        float _nextSnapshot;
        public bool InMission { get; private set; }
        public readonly CrewBanter Banter;

        /// <summary>Zeitpunkt (Echtzeit), zu dem zuletzt ein Mensch gesprochen hat.</summary>
        public float LastHumanSpeech { get; private set; } = -9999f;

        /// <summary>Niemand spricht und nichts wartet aufs Sprechen.</summary>
        public bool FloorFree => _speech.Count == 0 && Time.time >= _floorFreeAt && (Player == null || !Player.Playing);

        public CrewBrain(LlmClient llm, Plugin cfg, Roster roster, string dataDir)
        {
            _cfg = cfg;
            Roster = roster;
            Debriefer = new Debriefer(llm, cfg, this);
            Banter = new CrewBanter(llm, cfg, this, System.IO.Path.Combine(dataDir, "witze.json"));
            foreach (var st in EventLog.CrewStations) Agents.Add(new OfficerAgent(st, llm, cfg, this));
        }

        /// <summary>
        /// Wer gerade zusammen ist und plaudern kann: im Einsatz die KI-Offiziere auf ihren Stationen,
        /// sonst (Lobby) die Besetzung, die für die nächste Mission eingeteilt ist.
        /// </summary>
        public List<KeyValuePair<Character, Station>> BanterGroup()
        {
            var group = new List<KeyValuePair<Character, Station>>();
            if (InMission)
            {
                foreach (var a in Agents)
                    if (a.Char != null && a.Active) group.Add(new KeyValuePair<Character, Station>(a.Char, a.Station));
                return group;
            }
            var taken = new List<Character>();
            foreach (var a in Agents)
            {
                var c = Roster.Pick(a.Key, _cfg.CrewWish(a.Station), taken);
                if (c == null) continue;
                taken.Add(c);
                group.Add(new KeyValuePair<Character, Station>(c, a.Station));
            }
            return group;
        }

        public bool AnyAgentBusy
        {
            get
            {
                foreach (var a in Agents) if (a.Busy) return true;
                return false;
            }
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
            ReleaseSpeech();
            Banter.Tick(InMission);
            if (InMission)
            {
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
            LastHumanSpeech = Time.realtimeSinceStartup;
            if (isCaptain) Banter.OnCaptainSpeech(text);
            if (!InMission) return;

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

        /// <summary>Satz vormerken; die Stimme wird sofort im Hintergrund erzeugt, damit sie bereitliegt.</summary>
        public void Say(OfficerAgent speaker, string text)
        {
            if (speaker.Char != null) SayAs(speaker.Char, speaker.Station, text, null);
        }

        /// <summary>
        /// Ein Charakter sagt etwas, auch außerhalb einer Mission (Lobby). onSpoken läuft, sobald der Satz
        /// tatsächlich gesprochen wurde (z. B. damit Kollegen erst danach auf einen Witz reagieren).
        /// </summary>
        public void SayAs(Character c, Station station, string text, Action onSpoken)
        {
            var item = new SpeechItem { Char = c, Station = station, Text = text, OnSpoken = onSpoken, QueuedAt = Time.time };
            _speech.Enqueue(item);
            if (Tts != null)
                Tts.Speak(c, text, (pcm, rate) => { item.Pcm = pcm; item.Rate = rate; item.Ready = true; });
            else item.Ready = true;
        }

        /// <summary>Stimme probehören (Crew-Quartier).</summary>
        public void Preview(Character c, string text)
        {
            if (Tts == null || Player == null) return;
            Tts.Speak(c, text, (pcm, rate) => { if (pcm != null) Player.Play(pcm, rate); });
        }

        /// <summary>Immer nur einer spricht; die Dauer richtet sich nach der Sprachausgabe bzw. Textlänge.</summary>
        void ReleaseSpeech()
        {
            if (_speech.Count == 0 || Time.time < _floorFreeAt || (Player != null && Player.Playing)) return;
            var item = _speech.Peek();
            if (!item.Ready && Time.time - item.QueuedAt < 4f) return;   // Stimme ist gleich fertig
            _speech.Dequeue();
            string text = item.Text;
            string name = item.Char.Title;
            AddLine(name + " (" + OfficerAgent.StationDe(item.Station) + ")", text);
            if (InMission) GameBridge.ShowOnAllConsoles(item.Char.Name + ": " + text, _cfg.MessageSeconds.Value);
            float seconds = 0f;
            if (item.Pcm != null && Player != null) seconds = Player.Play(item.Pcm, item.Rate);
            if (seconds > 0f)
            {
                _floorFreeAt = Time.time + seconds + 0.35f;
                if (CrewSpeaking != null) CrewSpeaking(seconds + 0.4f);
            }
            else _floorFreeAt = Time.time + 1.2f + text.Length / 15f;
            if (item.OnSpoken != null) item.OnSpoken();
            if (!InMission) return;

            foreach (var other in Agents)
            {
                var p = EventLog.Of(other.Station);
                if (other.Char == item.Char) { p.Add("Du hast gesagt: \"" + text + "\"", Priority.Background); continue; }
                bool addressed = other.Char != null && Mentions(text, other, topics: false);
                p.Add(name + " (" + OfficerAgent.StationDe(item.Station) + ") sagt: \"" + text + "\"" + (addressed ? " (an dich)" : ""),
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
