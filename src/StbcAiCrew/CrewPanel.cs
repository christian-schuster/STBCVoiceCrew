using System;
using System.Collections.Generic;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Crew-Quartier (F6, Desktop-Fenster): Stationen besetzen, Charaktere bearbeiten,
    /// neue per LLM anwerben, schlechte entlassen und Entlassene zurückholen.
    /// </summary>
    public sealed class CrewPanel
    {
        readonly Plugin _cfg;
        readonly Roster _roster;
        readonly LlmClient _llm;
        readonly CrewBrain _brain;
        readonly PiperTts _tts;
        List<KeyValuePair<string, int>> _voices;

        public bool Visible;
        Rect _rect = new Rect(0, 0, 820, 600);
        bool _centered;
        int _tab;
        static readonly string[] Tabs = { "Besetzung", "Crew", "Anwerben", "Entlassene" };

        Character _sel;
        Vector2 _listScroll, _editScroll, _dismissedScroll;
        bool _confirmDismiss;
        string _message = "";

        string _recruitText = "";
        readonly Dictionary<string, bool> _recruitStations = new Dictionary<string, bool> { { "helm", true }, { "tactical", false }, { "engineering", false } };
        bool _recruiting;

        List<Character> _dismissed;

        public CrewPanel(Plugin cfg, Roster roster, LlmClient llm, CrewBrain brain, PiperTts tts)
        {
            _cfg = cfg;
            _roster = roster;
            _llm = llm;
            _brain = brain;
            _tts = tts;
        }

        /// <summary>Solange im Fenster getippt wird, sollen keine Mod-Tasten auslösen.</summary>
        public bool CapturesKeyboard => Visible && GUIUtility.keyboardControl != 0;

        public void Draw(bool inMission)
        {
            if (!Visible) return;
            if (!_centered)
            {
                _rect.x = (Screen.width - _rect.width) / 2f;
                _rect.y = (Screen.height - _rect.height) / 2f;
                _centered = true;
            }
            _rect = GUI.Window(0x5AC12E, _rect, id => Window(inMission), "Crew-Quartier der U.S.S. Aegis");
        }

        void Window(bool inMission)
        {
            GUILayout.BeginHorizontal();
            int tab = GUILayout.Toolbar(_tab, Tabs);
            if (tab != _tab) { _tab = tab; _confirmDismiss = false; if (tab == 3) _dismissed = null; }
            if (GUILayout.Button("Schließen", GUILayout.Width(90))) { Visible = false; GUIUtility.keyboardControl = 0; }
            GUILayout.EndHorizontal();

            switch (_tab)
            {
                case 0: Lineup(inMission); break;
                case 1: CrewTab(); break;
                case 2: RecruitTab(); break;
                case 3: DismissedTab(); break;
            }

            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        // ---------------- Besetzung ----------------

        void Lineup(bool inMission)
        {
            GUILayout.Label("Wer soll in der nächsten Mission an den Bot-Stationen sitzen? 'auto' wechselt durch, damit alle Erfahrung sammeln.");
            if (inMission) GUILayout.Label("<i>Änderungen gelten ab der nächsten Mission.</i>");
            GUILayout.Space(8);
            foreach (var st in EventLog.CrewStations)
            {
                string key = OfficerAgent.StationKey(st);
                var options = new List<string> { "auto" };
                foreach (var c in _roster.All) if (c.CanServe(key)) options.Add(c.Id);
                foreach (var c in _roster.All) if (!c.CanServe(key)) options.Add(c.Id);   // fachfremd, aber möglich

                string current = _cfg.CrewWish(st);
                int idx = Math.Max(0, options.IndexOf(current));

                GUILayout.BeginHorizontal("box");
                GUILayout.Label("<b>" + OfficerAgent.StationDe(st) + "</b>", GUILayout.Width(90));
                if (GUILayout.Button("◄", GUILayout.Width(30))) idx = (idx + options.Count - 1) % options.Count;
                var ch = _roster.Find(options[idx]);
                string label = ch == null ? "auto" : ch.Title + "  (" + ch.Species + ", " + ch.Xp + " XP, " + ch.Missions + " Missionen" +
                                                     (ch.CanServe(key) ? "" : ", fachfremd") + ")";
                GUILayout.Label(label, GUILayout.Width(560));
                if (GUILayout.Button("►", GUILayout.Width(30))) idx = (idx + 1) % options.Count;
                GUILayout.EndHorizontal();

                if (options[idx] != current) _cfg.SetCrewWish(st, options[idx]);
            }
            GUILayout.Space(8);
            GUILayout.Label("Ist eine Person an zwei Stationen eingetragen, wird die zweite automatisch besetzt.");
        }

        // ---------------- Crew bearbeiten ----------------

        void CrewTab()
        {
            GUILayout.BeginHorizontal();

            _listScroll = GUILayout.BeginScrollView(_listScroll, "box", GUILayout.Width(250));
            foreach (var c in _roster.All)
            {
                string label = c.Title + "\n" + c.Species + " · " + c.Xp + " XP · " + c.Missions + " M.";
                if (GUILayout.Toggle(_sel == c, label, "button")) { if (_sel != c) { _sel = c; _confirmDismiss = false; } }
            }
            GUILayout.EndScrollView();

            _editScroll = GUILayout.BeginScrollView(_editScroll, "box");
            if (_sel == null) GUILayout.Label("Links einen Charakter wählen.");
            else Editor(_sel);
            GUILayout.EndScrollView();

            GUILayout.EndHorizontal();
        }

        void Editor(Character c)
        {
            int next = Ranks.NextAt(c.Xp);
            GUILayout.Label("<b>" + c.Rank + " " + c.Name + "</b>   " + c.Xp + " XP" + (next > 0 ? " (nächster Rang ab " + next + ")" : " (höchster Rang)") +
                            "   ·   " + c.Missions + " Missionen   ·   " + c.Commendations + " Belobigungen");

            c.Name = Field("Name", c.Name);
            c.Species = Field("Spezies", c.Species);
            c.Pronouns = Field("Pronomen", c.Pronouns);
            c.Personality = Area("Persönlichkeit", c.Personality);
            c.SpeechStyle = Area("Sprechweise", c.SpeechStyle);
            c.Background = Area("Hintergrund", c.Background);
            c.Appearance = Area("Aussehen", c.Appearance);
            c.Laugh = Field("Lachen", c.Laugh);
            GUILayout.Label("<i>Lachen lautmalerisch, z. B. \"Har har har!\" oder \"Hihihi!\"; leer = lacht nie.</i>");
            GUILayout.Label("<i>Das Aussehen wird gespeichert, aber im Spiel noch nicht angewendet.</i>");
            VoiceEditor(c);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Stationen", GUILayout.Width(110));
            foreach (var key in new[] { "helm", "tactical", "engineering" })
            {
                bool has = c.Stations.Contains(key);
                bool now = GUILayout.Toggle(has, OfficerAgent.StationDe(key), GUILayout.Width(90));
                if (now && !has) c.Stations.Add(key);
                if (!now && has) c.Stations.Remove(key);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            foreach (var t in Character.TraitNames)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(t[0], GUILayout.Width(110));
                int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(c.Trait(t[0]), 0f, 10f, GUILayout.Width(200)));
                c.SetTrait(t[0], v);
                GUILayout.Label(v + "  " + (v <= 2 ? t[1] : v >= 8 ? t[2] : ""));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Gedächtnis</b> (wird durch Nachbesprechungen fortgeschrieben)");
            if (!string.IsNullOrEmpty(c.Personal)) GUILayout.Label("Entwicklung: " + c.Personal);
            if (!string.IsNullOrEmpty(c.CaptainImpression)) GUILayout.Label("Über den Captain: " + c.CaptainImpression);
            if (!string.IsNullOrEmpty(c.Relationships)) GUILayout.Label("Kollegen: " + c.Relationships);
            foreach (var l in c.Lessons) GUILayout.Label("• " + l);
            int from = Math.Max(0, c.Chronicle.Count - 5);
            for (int i = from; i < c.Chronicle.Count; i++) GUILayout.Label("◦ " + c.Chronicle[i]);
            if (c.HasPendingDebrief) GUILayout.Label("<i>Nachbesprechung steht noch aus.</i>");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Speichern", GUILayout.Width(120))) { c.Save(); _message = c.Name + " gespeichert."; }
            GUILayout.FlexibleSpace();
            if (!_confirmDismiss)
            {
                if (GUILayout.Button("Entlassen …", GUILayout.Width(120))) _confirmDismiss = true;
            }
            else
            {
                GUILayout.Label("Wirklich entlassen?");
                if (GUILayout.Button("Ja, entlassen", GUILayout.Width(120)))
                {
                    try
                    {
                        c.Save();
                        _roster.Dismiss(c);
                        _message = c.Name + " wurde entlassen (zurückholbar unter 'Entlassene').";
                        _sel = null;
                        _dismissed = null;
                    }
                    catch (Exception e) { _message = "Entlassen fehlgeschlagen: " + e.Message; }
                    _confirmDismiss = false;
                }
                if (GUILayout.Button("Nein", GUILayout.Width(60))) _confirmDismiss = false;
            }
            GUILayout.EndHorizontal();
        }

        void VoiceEditor(Character c)
        {
            if (_tts == null) { GUILayout.Label("Stimme: Sprachausgabe nicht verfügbar (Piper fehlt oder ist abgeschaltet)."); return; }
            if (_voices == null) _voices = new List<KeyValuePair<string, int>>(_tts.Voices());
            if (_voices.Count == 0) { GUILayout.Label("Stimme: keine Piper-Stimmen installiert."); return; }

            var spec = c.VoiceSpec;
            int idx = Math.Max(0, _voices.FindIndex(v => v.Key == spec.Model));
            int speakers = _voices[idx].Value;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Stimme", GUILayout.Width(110));
            if (GUILayout.Button("◄", GUILayout.Width(30))) { idx = (idx + _voices.Count - 1) % _voices.Count; spec.Speaker = 0; }
            GUILayout.Label(_voices[idx].Key.Replace("de_DE-", ""), GUILayout.Width(220));
            if (GUILayout.Button("►", GUILayout.Width(30))) { idx = (idx + 1) % _voices.Count; spec.Speaker = 0; }
            speakers = _voices[idx].Value;
            if (speakers > 1)
            {
                GUILayout.Label("Sprecher", GUILayout.Width(60));
                if (GUILayout.Button("–", GUILayout.Width(24))) spec.Speaker = (spec.Speaker + speakers - 1) % speakers;
                GUILayout.Label(spec.Speaker + " / " + (speakers - 1), GUILayout.Width(60));
                if (GUILayout.Button("+", GUILayout.Width(24))) spec.Speaker = (spec.Speaker + 1) % speakers;
            }
            GUILayout.EndHorizontal();
            c.Voice = _voices[idx].Key + (speakers > 1 ? "#" + Math.Min(spec.Speaker, speakers - 1) : "");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Tempo", GUILayout.Width(110));
            c.VoiceSpeed = (float)Math.Round(GUILayout.HorizontalSlider(c.VoiceSpeed, 0.7f, 1.4f, GUILayout.Width(150)), 2);
            GUILayout.Label(c.VoiceSpeed.ToString("0.00"), GUILayout.Width(40));
            GUILayout.Label("Tonhöhe", GUILayout.Width(60));
            c.VoicePitch = (float)Math.Round(GUILayout.HorizontalSlider(c.VoicePitch, 0.8f, 1.25f, GUILayout.Width(150)), 2);
            GUILayout.Label(c.VoicePitch.ToString("0.00"), GUILayout.Width(40));
            if (GUILayout.Button("Probehören", GUILayout.Width(100)))
            {
                string station = c.Stations.Count > 0 ? OfficerAgent.StationDe(c.Stations[0]) : "Brücke";
                _brain.Preview(c, "Hier spricht " + c.Rank + " " + c.Name + ", " + station + ". Bereit für Ihre Befehle, Captain.");
            }
            GUILayout.EndHorizontal();
        }

        static string Field(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            value = GUILayout.TextField(value ?? "", 80);
            GUILayout.EndHorizontal();
            return value;
        }

        static string Area(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            value = GUILayout.TextArea(value ?? "", 500, GUILayout.MinHeight(38));
            GUILayout.EndHorizontal();
            return value;
        }

        // ---------------- Anwerben ----------------

        void RecruitTab()
        {
            GUILayout.Label("Beschreibe kurz, wen du an Bord haben möchtest. Das LLM entwirft daraus einen vollständigen Charakter, der als Fähnrich anfängt.");
            GUILayout.Label("Beispiel: \"nervöser Bolianer, Witzbold, will unbedingt Steuermann werden\"");
            _recruitText = GUILayout.TextArea(_recruitText, 400, GUILayout.MinHeight(60));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Station(en)", GUILayout.Width(110));
            foreach (var key in new[] { "helm", "tactical", "engineering" })
                _recruitStations[key] = GUILayout.Toggle(_recruitStations[key], OfficerAgent.StationDe(key), GUILayout.Width(90));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            bool ready = _cfg.ServerState == ServerState.Ready;
            GUI.enabled = ready && !_recruiting && _recruitText.Trim().Length > 0;
            if (GUILayout.Button(_recruiting ? "Personalakte wird erstellt …" : "Anwerben", GUILayout.Width(220))) Recruit();
            GUI.enabled = true;
            if (!ready) GUILayout.Label("(LLM noch nicht bereit)");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Leeren Charakter anlegen", GUILayout.Width(200)))
            {
                var c = new Character { Name = "Neuer Offizier", Species = "Mensch" };
                foreach (var kv in _recruitStations) if (kv.Value) c.Stations.Add(kv.Key);
                if (c.Stations.Count == 0) c.Stations.Add("helm");
                _sel = _roster.Add(c);
                _tab = 1;
                _message = "Angelegt, jetzt im Tab 'Crew' ausfüllen.";
            }
            GUILayout.EndHorizontal();
        }

        void Recruit()
        {
            _recruiting = true;
            var stations = new List<object>();
            foreach (var kv in _recruitStations) if (kv.Value) stations.Add(kv.Key);
            if (stations.Count == 0) stations.Add("helm");

            var traitProps = new Dictionary<string, object>();
            var traitReq = new List<object>();
            foreach (var t in Character.TraitNames)
            {
                traitProps[t[0]] = new Dictionary<string, object> { { "type", "integer" }, { "minimum", 0 }, { "maximum", 10 } };
                traitReq.Add(t[0]);
            }
            var str = new Dictionary<string, object> { { "type", "string" }, { "maxLength", 280 } };
            var shortStr = new Dictionary<string, object> { { "type", "string" }, { "maxLength", 40 } };
            var pronouns = new Dictionary<string, object> { { "type", "string" }, { "enum", new List<object> { "sie/ihr", "er/ihm", "" } } };
            var schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "name", shortStr }, { "species", shortStr }, { "pronouns", pronouns },
                        { "personality", str }, { "speechStyle", str }, { "background", str }, { "appearance", str }, { "laugh", shortStr },
                        { "traits", new Dictionary<string, object> { { "type", "object" }, { "properties", traitProps }, { "required", traitReq } } }
                    }
                },
                { "required", new List<object> { "name", "species", "pronouns", "personality", "speechStyle", "background", "appearance", "laugh", "traits" } }
            };

            var names = new List<string>();
            foreach (var c in _roster.All) names.Add(c.Name);
            string traitList = "";
            foreach (var t in Character.TraitNames) traitList += t[0] + " (0 = " + t[1] + ", 10 = " + t[2] + "); ";

            string prompt =
                "Entwirf ein neues Crewmitglied der Sternenflotte für die U.S.S. Aegis (Star Trek, 24. Jahrhundert) nach diesem Wunsch des Captains:\n\"" +
                _recruitText.Trim() + "\"\nEinsatzbereich: " + string.Join(", ", ConvertAll(stations)) + ".\n" +
                "Erfinde einen eigenständigen Charakter (keine bekannte Serienfigur), mit glaubwürdiger Spezies aus dem Star-Trek-Universum. " +
                "Felder: name (Vor- und/oder Nachname passend zur Spezies, nicht einer von: " + string.Join(", ", names.ToArray()) + "), species, " +
                "pronouns (sie/ihr, er/ihm oder leer), personality (2 kurze Sätze), speechStyle (1 Satz), background (1–2 Sätze, frisch von der Akademie oder Quereinsteiger), " +
                "appearance (1–2 Sätze, passend zur Spezies laut Star Trek), laugh (wie die Person lacht, lautmalerisch und ganz eigen, z. B. Har har har! oder Hihihi! oder Bwahaha!; leer, wenn sie nie lacht), traits als ganze Zahlen 0–10: " + traitList +
                "Die Werte sollen zur Beschreibung passen und ruhig Ecken und Kanten haben. Schreib auf " + _cfg.Language.Value + ".";

            _message = "Sternenflottenkommando prüft die Bewerbung …";
            _llm.Send(new List<ChatMessage> { new ChatMessage("user", prompt) }, schema, 900, (content, error, latency) =>
            {
                _recruiting = false;
                if (error != null) { _message = "Anwerben fehlgeschlagen: " + error; return; }
                try
                {
                    var o = Json.Parse(content);
                    var c = new Character
                    {
                        Name = Json.Str(o, "name") ?? "Unbekannt",
                        Species = Json.Str(o, "species") ?? "",
                        Pronouns = Json.Str(o, "pronouns") ?? "",
                        Personality = Json.Str(o, "personality") ?? "",
                        SpeechStyle = Json.Str(o, "speechStyle") ?? "",
                        Background = Json.Str(o, "background") ?? "",
                        Appearance = Json.Str(o, "appearance") ?? "",
                        Laugh = Json.Str(o, "laugh"),
                        Xp = 0
                    };
                    foreach (var s in stations) c.Stations.Add((string)s);
                    var traits = Json.Obj(Json.Get(o, "traits"));
                    if (traits != null)
                        foreach (var kv in traits)
                            if (kv.Value is double) c.SetTrait(kv.Key, (int)Math.Round((double)kv.Value));
                    _sel = _roster.Add(c);
                    _tab = 1;
                    _recruitText = "";
                    _message = "Willkommen an Bord, Fähnrich " + c.Name + "! (" + latency.ToString("0.0") + " s)";
                }
                catch (Exception e)
                {
                    _message = "Bewerbung unleserlich: " + e.Message;
                    Plugin.Log.LogWarning(_message + " – " + content);
                }
            });
        }

        static string[] ConvertAll(List<object> keys)
        {
            var a = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++) a[i] = OfficerAgent.StationDe((string)keys[i]);
            return a;
        }

        // ---------------- Entlassene ----------------

        void DismissedTab()
        {
            if (_dismissed == null) _dismissed = _roster.LoadDismissed();
            if (_dismissed.Count == 0) { GUILayout.Label("Niemand entlassen."); return; }
            _dismissedScroll = GUILayout.BeginScrollView(_dismissedScroll);
            foreach (var c in _dismissed.ToArray())
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(c.Title + " (" + c.Species + ", " + c.Xp + " XP, " + c.Missions + " Missionen)", GUILayout.Width(560));
                if (GUILayout.Button("Zurückholen", GUILayout.Width(120)))
                {
                    try
                    {
                        _roster.Restore(c);
                        _dismissed.Remove(c);
                        _message = c.Name + " ist zurück im Dienst.";
                    }
                    catch (Exception e) { _message = "Zurückholen fehlgeschlagen: " + e.Message; }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }
}
