using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Humor auf der Brücke: In ruhigen Momenten und in der Lobby erzählt, wer Humor hat, gern einen Witz.
    /// Das LLM denkt sich jeden Witz neu aus. Bereits erzählte Witze werden über alle Sessions gespeichert
    /// und als Tabu mitgegeben. Die Kollegen reagieren nach ihren Charakterzügen: lachen, trocken kommentieren,
    /// genervt sein oder ermahnen. Der Captain kann das mit "Ruhe" o. ä. für eine Weile abstellen.
    /// </summary>
    public sealed class CrewBanter
    {
        readonly LlmClient _llm;
        readonly Plugin _cfg;
        readonly CrewBrain _bridge;
        readonly string _path;
        readonly List<string[]> _jokes = new List<string[]>();   // { Erzähler, Witz, Datum }

        bool _busy;
        float _nextCheck;
        float _lastJoke = -9999f;
        float _quietUntil = -1f;

        static float Now => Time.realtimeSinceStartup;

        public CrewBanter(LlmClient llm, Plugin cfg, CrewBrain bridge, string path)
        {
            _llm = llm;
            _cfg = cfg;
            _bridge = bridge;
            _path = path;
            Load();
        }

        // ---------------- Wann wird gescherzt? ----------------

        public void Tick(bool inMission)
        {
            if (!_cfg.HumorEnabled.Value || _busy || Now < _nextCheck) return;
            _nextCheck = Now + 15f;
            if (Now < _quietUntil || !_bridge.FloorFree) return;

            float cooldown = inMission ? _cfg.JokeIntervalMissionSec.Value : _cfg.JokeIntervalLobbySec.Value;
            if (Now - _lastJoke < cooldown) return;
            if (inMission)
            {
                // Nur wenn wirklich nichts los ist
                if (EventLog.UnderThreat(90f) || Now - _bridge.LastHumanSpeech < 30f || _bridge.AnyAgentBusy) return;
            }
            else if (!_cfg.LobbyBanter.Value || _bridge.Tts == null || Now - _bridge.LastHumanSpeech < 20f)
                return;

            var group = _bridge.BanterGroup();
            if (group.Count == 0) return;

            // Wer erzählt? Je mehr Humor, desto wahrscheinlicher; ganz Humorlose nie
            float total = 0f;
            foreach (var m in group) total += Weight(m.Key);
            if (total <= 0f || UnityEngine.Random.value > 0.35f) return;
            float pick = UnityEngine.Random.value * total;
            foreach (var m in group)
            {
                pick -= Weight(m.Key);
                if (pick <= 0f) { TellJoke(m.Key, m.Value, group, inMission); return; }
            }
        }

        static float Weight(Character c)
        {
            int h = c.Trait("Humor") - 3;
            return h > 0 ? h * h : 0f;
        }

        /// <summary>Der Captain spricht: "Ruhe!" o. ä. stellt die Witze eine Weile ab.</summary>
        public void OnCaptainSpeech(string text)
        {
            string t = text.ToLowerInvariant();
            foreach (var w in new[] { "ruhe", "keine witze", "schluss mit", "konzentrier", "genug gelacht", "nicht jetzt", "lasst das", "hört auf" })
            {
                if (!t.Contains(w)) continue;
                _quietUntil = Now + _cfg.QuietAfterReprimandMin.Value * 60f;
                _bridge.Notice("Captain wünscht Ruhe: keine Witze für " + _cfg.QuietAfterReprimandMin.Value + " Minuten.");
                return;
            }
        }

        // ---------------- Witz ----------------

        // Anstöße für Abwechslung (keine fertigen Witze): Form und Thema werden zufällig gemischt
        static readonly string[] Forms =
        {
            "eine Frage mit überraschender Antwort (\"Warum …?\" / \"Was …?\")", "\"Treffen sich ein … und ein …\"",
            "\"Wie viele … braucht man, um …?\"", "ein Wortspiel", "eine Mini-Anekdote mit Pointe",
            "ein Logbucheintrag mit absurder Pointe", "\"Was ist der Unterschied zwischen … und …?\""
        };

        static readonly string[] Topics =
        {
            "Ferengi und Profit", "Klingonen und Ehre", "Vulkanier und Logik", "Borg", "Tribbles", "Replikatoren",
            "das Holodeck", "Transporterunfälle", "Rote Hemden", "Tellariten und Streitlust", "Sternenflottenbürokratie",
            "Romulaner und Tarnung", "Warpkern-Wartung", "Sternzeit", "Andorianer und Temperament", "Betazoiden und Gedankenlesen",
            "Captains und ihre Logbücher", "die Bordkantine", "Föderations-Diplomatie", "Holodeck-Sicherheitsprotokolle"
        };

        void TellJoke(Character teller, Station station, List<KeyValuePair<Character, Station>> group, bool inMission)
        {
            _busy = true;
            _lastJoke = Now;

            // Manchmal geht es um die Spezies der Anwesenden (Selbstironie oder gutmütige Neckerei)
            string topic = Topics[UnityEngine.Random.Range(0, Topics.Length)];
            if (UnityEngine.Random.value < 0.35f)
            {
                var m = group[UnityEngine.Random.Range(0, group.Count)].Key;
                if (!string.IsNullOrEmpty(m.Species)) topic = m.Species + (m == teller ? " (Selbstironie)" : "");
            }
            string form = Forms[UnityEngine.Random.Range(0, Forms.Length)];

            var sb = new StringBuilder();
            sb.Append("Erzähle einen kurzen, neu erfundenen Witz für Star-Trek-Fans, den ein Crewmitglied ")
              .Append(inMission ? "in einer ruhigen Minute auf der Brücke" : "im Bereitschaftsraum vor der Mission").Append(" laut erzählt. ");
            sb.Append("Thema: ").Append(topic).Append(". Form: ").Append(form).Append(".\n");
            sb.Append("Höchstens drei kurze Sätze, mit klarer Pointe am Ende, kollegial und nicht verletzend, keine Witze über Geschlecht, Herkunft oder Aussehen. ");
            sb.Append("Kein bekannter Witz und keiner, der diesen schon erzählten ähnelt:\n");
            int from = Math.Max(0, _jokes.Count - _cfg.JokeMemory.Value);
            if (_jokes.Count == 0) sb.Append("- (bisher keine)\n");
            for (int i = from; i < _jokes.Count; i++) sb.Append("- ").Append(_jokes[i][1]).Append('\n');
            sb.Append("Schreib nur die Worte, die gesprochen werden: keine Erzählung (\"… sagte\"), keine Anführungszeichen, keine Einleitung oder Erklärung. Auf ").Append(_cfg.Language.Value).Append(".");
            string prompt = sb.ToString();

            // Fließtext statt JSON: Ohne Formatzwang und bei mäßiger Temperatur werden kleine Modelle deutlich witziger
            Action<int> attempt = null;
            attempt = tries => _llm.Send(new List<ChatMessage> { new ChatMessage("user", prompt) }, null, 160, (content, error, latency) =>
            {
                string joke = error == null ? CleanJoke(content) : null;
                if (joke == null && tries < 1) { attempt(tries + 1); return; }
                _busy = false;
                if (joke == null) return;
                Remember(teller.Name, joke);
                // Wer viel Humor hat, lacht gern über den eigenen Witz
                if (!string.IsNullOrEmpty(teller.Laugh) && teller.Trait("Humor") >= 8 && UnityEngine.Random.value < 0.5f)
                    joke += " " + teller.Laugh;
                // Die anderen reagieren erst, wenn der Witz zu Ende erzählt ist
                _bridge.SayAs(teller, station, joke, () => React(teller, joke, group, inMission));
            }, temperature: 0.7f);
            attempt(0);
        }

        /// <summary>Witz säubern; null, wenn unbrauchbar (fremde Schriftzeichen, zu lang, leer).</summary>
        static string CleanJoke(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Replace('\n', ' ').Trim();
            // Einleitungen wie "Hier ein Witz:" oder "Zhal nickte und sagte:" abschneiden
            int colon = s.IndexOf(':');
            if (colon > 0 && colon < 90)
            {
                string head = s.Substring(0, colon).ToLowerInvariant();
                if (head.Contains("witz") || head.Contains("sagte") || head.Contains("meinte") || head.Contains("erzählt") || head.Contains("fragte"))
                    s = s.Substring(colon + 1);
            }
            var sb = new StringBuilder();
            foreach (char c in s) if (c != '"' && c != '„' && c != '“' && c != '‚' && c != '‘' && c != '*') sb.Append(c);
            s = sb.ToString().Replace("  ", " ").Trim();
            // Erzählsätze mit Sprecherangabe ("…, sagte Dehlor, während …") entfernen
            s = System.Text.RegularExpressions.Regex.Replace(s,
                @"(?<=[.!?…])\s*[^.!?…]*,\s*(sagte|meinte|erwiderte|antwortete|rief|fragte)\s+[A-ZÄÖÜ][^.!?…]*[.!?…]", "").Trim();
            foreach (char c in s)
                if (c > 0x024F && !(c >= 0x2000 && c <= 0x206F)) return null;   // nur lateinische Schrift und Satzzeichen
            if (s.Length < 20 || s.Length > 420) return null;
            return s;
        }
        // ---------------- Reaktionen ----------------

        void React(Character teller, string joke, List<KeyValuePair<Character, Station>> group, bool inMission)
        {
            var listeners = new List<KeyValuePair<Character, Station>>();
            foreach (var m in group) if (m.Key != teller) listeners.Add(m);
            // zufällige Reihenfolge, höchstens zwei reagieren
            for (int i = listeners.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var tmp = listeners[i]; listeners[i] = listeners[j]; listeners[j] = tmp;
            }

            int reactions = 0;
            foreach (var m in listeners)
            {
                if (reactions >= 2) break;
                string attitude;
                float chance = Attitude(m.Key, teller, inMission, out attitude);
                if (UnityEngine.Random.value > chance) continue;
                reactions++;
                Reply(m.Key, m.Value, teller, joke, attitude);
            }
        }

        /// <summary>Wie reagiert jemand auf einen Witz? Gibt die Wahrscheinlichkeit einer Reaktion zurück.</summary>
        static float Attitude(Character c, Character teller, bool inMission, out string attitude)
        {
            int humor = c.Trait("Humor"), discipline = c.Trait("Disziplin"), calm = c.Trait("Gelassenheit");
            bool laughs = !string.IsNullOrEmpty(c.Laugh);
            if (humor >= 7 && laughs)
            {
                attitude = "Du findest den Witz lustig: Lach zuerst laut auf deine ganz eigene Art, so wie du eben lachst (" + c.Laugh +
                           "), gern lang gezogen, schräg oder übertrieben, als Lautmalerei geschrieben. Danach höchstens ein kurzer, freundlicher Spruch.";
                return 0.8f;
            }
            if (humor <= 3)
            {
                if (discipline >= 7 && inMission)
                    attitude = "Witze im Dienst findest du unangebracht: Ermahne " + teller.Name + " kurz und sachlich, sich auf den Dienst zu konzentrieren.";
                else if (calm <= 4)
                    attitude = "Der Witz nervt dich: Reagiere kurz und genervt, aber ohne Drohungen oder Beleidigungen.";
                else
                    attitude = "Du kannst mit dem Witz nichts anfangen: Reagiere trocken oder verständnislos, so wie es zu dir passt.";
                return 0.6f;
            }
            attitude = laughs
                ? "Reagiere kurz: ein verhaltenes Lachen auf deine Art (" + c.Laugh + ", aber leiser), ein trockener Kommentar oder höflich gequältes Lächeln, je nachdem, was zu dir passt."
                : "Reagiere kurz und trocken, ohne zu lachen, so wie es zu dir passt.";
            return 0.3f;
        }

        void Reply(Character c, Station station, Character teller, string joke, string attitude)
        {
            var sb = new StringBuilder();
            sb.Append(c.ProfileForPrompt()).Append('\n');
            sb.Append(teller.Title).Append(" hat gerade diesen Witz erzählt:\n\"").Append(joke).Append("\"\n\n");
            sb.Append(attitude).Append('\n');
            sb.Append("Antworte mit einem kurzen Satz (höchstens zwei), ganz aus deiner Persönlichkeit heraus, im Rahmen des kollegialen Umgangs an Bord, ohne Sprüche über Geschlecht, Herkunft oder Aussehen. Sprich ")
              .Append(_cfg.Language.Value).Append(". Antworte als JSON {\"say\": \"...\"}.");
            _llm.Send(new List<ChatMessage> { new ChatMessage("user", sb.ToString()) }, Obj("say", 200), 150, (content, error, latency) =>
            {
                string say = error == null ? Field(content, "say") : null;
                if (!string.IsNullOrEmpty(say)) _bridge.SayAs(c, station, say, null);
            }, temperature: 0.7f);
        }

        // ---------------- Witz-Gedächtnis ----------------

        void Remember(string teller, string joke)
        {
            _jokes.Add(new[] { teller, joke, DateTime.Now.ToString("yyyy-MM-dd") });
            while (_jokes.Count > Math.Max(1, _cfg.JokeMemory.Value)) _jokes.RemoveAt(0);
            try
            {
                var list = new List<object>();
                foreach (var j in _jokes)
                    list.Add(new Dictionary<string, object> { { "by", j[0] }, { "text", j[1] }, { "date", j[2] } });
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, Json.Serialize(list), new UTF8Encoding(false));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Witze konnten nicht gespeichert werden: " + e.Message); }
        }

        void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                var arr = Json.Arr(Json.Parse(File.ReadAllText(_path, Encoding.UTF8)));
                if (arr == null) return;
                foreach (var o in arr)
                {
                    string text = Json.Str(o, "text");
                    if (!string.IsNullOrEmpty(text)) _jokes.Add(new[] { Json.Str(o, "by") ?? "", text, Json.Str(o, "date") ?? "" });
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Witze-Datei unlesbar: " + e.Message); }
        }

        // ---------------- Hilfen ----------------

        static object Obj(string field, int maxLength) => new Dictionary<string, object>
        {
            { "type", "object" },
            { "properties", new Dictionary<string, object> { { field, new Dictionary<string, object> { { "type", "string" }, { "maxLength", maxLength } } } } },
            { "required", new List<object> { field } }
        };

        static string Field(string json, string field)
        {
            try { return (Json.Str(Json.Parse(json), field) ?? "").Trim(); }
            catch { return null; }
        }
    }
}
