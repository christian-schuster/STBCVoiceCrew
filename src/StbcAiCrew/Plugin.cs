using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace StbcAiCrew
{
    [BepInPlugin(Guid, "STBC AI Crew", "0.2.0")]
    [BepInProcess("stbc.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "de.schuster.stbc.aicrew";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // ---- LLM / llama.cpp ----
        public ConfigEntry<bool> UseExternalServer;
        public ConfigEntry<string> LlamaServerExe;
        public ConfigEntry<string> ModelPath;
        public ConfigEntry<int> Port;
        public ConfigEntry<int> Slots;
        public ConfigEntry<int> ContextPerSlot;
        public ConfigEntry<int> GpuLayers;
        public ConfigEntry<string> ExtraServerArgs;
        public ConfigEntry<int> StartupTimeoutSec;
        public ConfigEntry<int> RequestTimeoutSec;
        public ConfigEntry<float> Temperature;
        public ConfigEntry<int> MaxTokens;

        // ---- Besetzung ----
        public ConfigEntry<string> CrewHelm;
        public ConfigEntry<string> CrewTactical;
        public ConfigEntry<string> CrewEngineering;

        // ---- Crew-Verhalten ----
        public ConfigEntry<int> AutonomyLevel;
        public ConfigEntry<float> ReactionIntervalSec;
        public ConfigEntry<float> IdleMinSec;
        public ConfigEntry<float> IdleMaxSec;
        public ConfigEntry<float> ChatterChance;
        public ConfigEntry<bool> AllowMajorWithoutOrder;
        public ConfigEntry<int> MaxCommandsPerTurn;
        public ConfigEntry<int> EventsInPrompt;
        public ConfigEntry<int> HistoryTurns;
        public ConfigEntry<string> Language;
        public ConfigEntry<float> MessageSeconds;
        public ConfigEntry<bool> MuteBotVoiceLines;

        // ---- Lernen ----
        public ConfigEntry<int> MaxLessons;
        public ConfigEntry<int> ChronicleMax;
        public ConfigEntry<int> NotesMax;
        public ConfigEntry<int> MinActiveSecForDebrief;
        public ConfigEntry<int> DebriefLogChars;

        // ---- Tasten ----
        public ConfigEntry<KeyCode> KeyCrewPanel;
        public ConfigEntry<KeyCode> KeyOverlay;
        public ConfigEntry<KeyCode> KeyTalk;
        public ConfigEntry<KeyCode> KeyEndMission;
        public ConfigEntry<KeyCode> KeyStateDump;

        LlamaServerHost _server;
        LlmClient _llm;
        Roster _roster;
        CrewBrain _brain;
        CrewPanel _panel;
        string _dataDir;
        bool _wasInMission;

        ShipStatus _status;
        float _nextStatusPoll;

        bool _showOverlay = true;
        bool _typing;
        string _input = "";
        string _toast;
        float _toastUntil;

        public ServerState ServerState => _server.State;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            BindConfig();

            string pluginDir = Path.GetDirectoryName(Info.Location);
            _dataDir = Path.Combine(Paths.ConfigPath, "StbcAiCrew");

            string baseUrl = "http://127.0.0.1:" + Port.Value;
            string exe = UseExternalServer.Value ? null : ResolvePath(pluginDir, LlamaServerExe.Value);
            string model = ResolvePath(pluginDir, ModelPath.Value);
            int slots = Math.Max(1, Slots.Value);
            string args = "-m \"" + model + "\" --host 127.0.0.1 --port " + Port.Value +
                          " -np " + slots + " -c " + (ContextPerSlot.Value * slots) + " -ngl " + GpuLayers.Value +
                          " --jinja --log-file \"" + Path.Combine(pluginDir, "llama-server.log") + "\" " + ExtraServerArgs.Value;

            _server = new LlamaServerHost(Log, exe, args, baseUrl, StartupTimeoutSec.Value);
            _llm = new LlmClient(Log, baseUrl, RequestTimeoutSec.Value * 1000) { Temperature = Temperature.Value };
            _roster = new Roster(Path.Combine(_dataDir, "crew"));
            _roster.Load();
            _brain = new CrewBrain(_llm, this, _roster);
            _panel = new CrewPanel(this, _roster, _llm);

            GamePatches.Apply(new Harmony(Guid));
            _server.Start();
            Log.LogInfo("STBC AI Crew geladen. Crew-Quartier: " + KeyCrewPanel.Value + ", Sprechen: " + KeyTalk.Value);
        }

        void BindConfig()
        {
            UseExternalServer = Config.Bind("LLM", "ExternenServerNutzen", false, "true = llama-server nicht selbst starten, sondern einen bereits laufenden auf dem Port nutzen.");
            LlamaServerExe = Config.Bind("LLM", "LlamaServerExe", @"llama\llama-server.exe", "Pfad zu llama-server.exe (relativ zum Plugin-Ordner oder absolut).");
            ModelPath = Config.Bind("LLM", "Modell", @"models\crew.gguf", "Pfad zur GGUF-Modelldatei (relativ zum Plugin-Ordner oder absolut).");
            Port = Config.Bind("LLM", "Port", 8089, "Lokaler Port des llama-server.");
            Slots = Config.Bind("LLM", "ParalleleSlots", 3, "Gleichzeitige Anfragen (ein Slot je KI-Offizier).");
            ContextPerSlot = Config.Bind("LLM", "KontextProSlot", 6144, "Kontextfenster je Slot in Tokens. Mehr = mehr VRAM.");
            GpuLayers = Config.Bind("LLM", "GpuLayers", 99, "Wie viele Modellschichten auf die GPU (99 = alle). Weniger, falls das Spiel ruckelt.");
            ExtraServerArgs = Config.Bind("LLM", "ZusatzArgumente", "-fa on --cache-type-k q8_0 --cache-type-v q8_0", "Weitere Argumente für llama-server.");
            StartupTimeoutSec = Config.Bind("LLM", "StartTimeoutSek", 180, "So lange wird auf das Laden des Modells gewartet.");
            RequestTimeoutSec = Config.Bind("LLM", "AnfrageTimeoutSek", 60, "Zeitlimit pro LLM-Anfrage.");
            Temperature = Config.Bind("LLM", "Temperatur", 0.7f, "Kreativität der Antworten.");
            MaxTokens = Config.Bind("LLM", "MaxTokens", 300, "Maximale Antwortlänge im Einsatz.");

            CrewHelm = Config.Bind("Besetzung", "Steuer", "auto", "Charakter-Id für die Steuer oder 'auto' (wird im Crew-Quartier gesetzt).");
            CrewTactical = Config.Bind("Besetzung", "Taktik", "auto", "Charakter-Id für Taktik oder 'auto'.");
            CrewEngineering = Config.Bind("Besetzung", "Technik", "auto", "Charakter-Id für Technik oder 'auto'.");

            AutonomyLevel = Config.Bind("Crew", "Autonomie", 2, new ConfigDescription(
                "0 = nur auf Ansprache/Befehl, 1 = meldet sich selbst, handelt aber nur auf Befehl, 2 = spielt selbst mit.",
                new AcceptableValueRange<int>(0, 2)));
            ReactionIntervalSec = Config.Bind("Crew", "ReaktionsabstandSek", 5f, "Mindestabstand zwischen eigenständigen Reaktionen eines Offiziers.");
            IdleMinSec = Config.Bind("Crew", "RuhephaseMinSek", 25f, "Frühestens so oft schaut ein Offizier in ruhigen Phasen von sich aus auf die Lage.");
            IdleMaxSec = Config.Bind("Crew", "RuhephaseMaxSek", 50f, "Spätestens so oft.");
            ChatterChance = Config.Bind("Crew", "Plauderlaune", 0.3f, "Wahrscheinlichkeit für persönliche Bemerkungen in ruhigen Phasen (0–1, wirkt mit Beredsamkeit).");
            AllowMajorWithoutOrder = Config.Bind("Crew", "WeitreichendOhneBefehl", false, "true = auch Fähnriche dürfen ohne Befehl feuern, warpen usw. (sonst abhängig von Rang und Gefahr).");
            MaxCommandsPerTurn = Config.Bind("Crew", "MaxBefehleProAntwort", 2, "Obergrenze ausgeführter Befehle pro Antwort eines Offiziers.");
            EventsInPrompt = Config.Bind("Crew", "EreignisseImPrompt", 20, "Wie viele letzte Wahrnehmungen ein Offizier bei jeder Entscheidung sieht.");
            HistoryTurns = Config.Bind("Crew", "Gespraechsverlauf", 4, "Wie viele eigene letzte Wortwechsel im Kontext bleiben.");
            Language = Config.Bind("Crew", "Sprache", "Deutsch", "Sprache der Crew.");
            MessageSeconds = Config.Bind("Crew", "AnzeigedauerSek", 7f, "Wie lange Gesprochenes auf den Konsolen steht.");
            MuteBotVoiceLines = Config.Bind("Crew", "BotSprachzeilenStumm", true, "Vorgefertigte Sprachzeilen von Rao/Hoyle/T'nar unterdrücken (die KI-Charaktere sprechen selbst).");

            MaxLessons = Config.Bind("Lernen", "MaxLektionen", 12, "Lektionen je Charakter.");
            ChronicleMax = Config.Bind("Lernen", "ChronikEintraege", 15, "Einträge der Dienstchronik je Charakter.");
            NotesMax = Config.Bind("Lernen", "NotizenProMission", 10, "Notizen, die sich ein Charakter während einer Mission machen kann.");
            MinActiveSecForDebrief = Config.Bind("Lernen", "MinDienstzeitSek", 60, "Nachbesprechung und XP erst ab so viel Dienstzeit an einer Bot-Station.");
            DebriefLogChars = Config.Bind("Lernen", "ProtokollZeichen", 10000, "Maximale Länge des Missionsprotokolls in der Nachbesprechung.");

            KeyCrewPanel = Config.Bind("Tasten", "CrewQuartier", KeyCode.F6, "Crew-Quartier: Charaktere auswählen, anwerben, bearbeiten, entlassen.");
            KeyOverlay = Config.Bind("Tasten", "Overlay", KeyCode.F7, "Overlay ein/aus.");
            KeyTalk = Config.Bind("Tasten", "Sprechen", KeyCode.F8, "Texteingabe an die Crew öffnen.");
            KeyEndMission = Config.Bind("Tasten", "MissionBeenden", KeyCode.F9, "Mission für beendet erklären (Nachbesprechung).");
            KeyStateDump = Config.Bind("Tasten", "Zustandsdump", KeyCode.F10, "Felder des Spielerschiffs in eine Datei schreiben (Reverse Engineering).");
        }

        public string CrewWish(Station s)
        {
            switch (s)
            {
                case Station.Helm: return CrewHelm.Value;
                case Station.Tactical: return CrewTactical.Value;
                case Station.Engineering: return CrewEngineering.Value;
            }
            return "auto";
        }

        public void SetCrewWish(Station s, string id)
        {
            switch (s)
            {
                case Station.Helm: CrewHelm.Value = id; break;
                case Station.Tactical: CrewTactical.Value = id; break;
                case Station.Engineering: CrewEngineering.Value = id; break;
            }
        }

        static string ResolvePath(string baseDir, string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(baseDir, p));
        }

        void Update()
        {
            MainThread.Drain(e => Log.LogError(e));
            EventLog.Update();

            bool inMission = GameBridge.InMission;
            if (inMission && !_wasInMission) { _status = null; _brain.StartMission(); }
            else if (!inMission && _wasInMission) _brain.EndMission("Mission verlassen");
            _wasInMission = inMission;

            if (inMission && Time.time >= _nextStatusPoll)
            {
                _nextStatusPoll = Time.time + 1f;
                var now = ShipStatus.Read();
                ShipStatus.ReportChanges(_status, now);
                if (now != null) _status = now;
            }

            if (_server.State == ServerState.Ready) _brain.Tick(_status);

            if (!_typing && !_panel.CapturesKeyboard)
            {
                if (Input.GetKeyDown(KeyCrewPanel.Value)) _panel.Visible = !_panel.Visible;
                if (Input.GetKeyDown(KeyOverlay.Value)) _showOverlay = !_showOverlay;
                if (Input.GetKeyDown(KeyTalk.Value)) { _typing = true; _input = ""; _showOverlay = true; }
                if (Input.GetKeyDown(KeyEndMission.Value)) { _brain.EndMission("manuell"); Toast("Mission beendet, Nachbesprechungen laufen."); }
                if (Input.GetKeyDown(KeyStateDump.Value))
                {
                    try { Toast("Zustand: " + StateProbe.Dump(Path.Combine(_dataDir, "dumps"))); }
                    catch (Exception e) { Toast("Dump fehlgeschlagen: " + e.Message); Log.LogError(e); }
                }
            }
        }

        void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.realtimeSinceStartup + 6f;
            Log.LogInfo(msg);
        }

        void OnGUI()
        {
            _panel.Draw(_brain.InMission);
            if (!_showOverlay) return;

            GUILayout.BeginArea(new Rect(10f, 10f, 560f, Screen.height - 20f));
            GUILayout.BeginVertical("box");

            string server = _server.State == ServerState.Failed ? "Fehler: " + _server.Error : _server.State.ToString();
            GUILayout.Label("<b>AI Crew</b>  –  LLM: " + server + "  |  Autonomie " + AutonomyLevel.Value +
                            "  |  " + (_brain.InMission ? "im Einsatz" : "kein Einsatz"));
            foreach (var a in _brain.Agents)
            {
                string who = a.Char != null ? a.Char.Title + " (" + a.Char.Xp + " XP)" : "–";
                GUILayout.Label(OfficerAgent.StationDe(a.Station) + ": " + who + "  " + a.Status);
            }
            if (!string.IsNullOrEmpty(_brain.Debriefer.Status)) GUILayout.Label(_brain.Debriefer.Status);

            // Nur Gesprochenes: das hört jeder auf der Brücke
            int from = Math.Max(0, _brain.Transcript.Count - 8);
            for (int i = from; i < _brain.Transcript.Count; i++)
            {
                var l = _brain.Transcript[i];
                GUILayout.Label("<b>" + l.Speaker + ":</b> " + l.Text);
            }

            if (_typing)
            {
                var e = Event.current;
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (_input.Trim().Length > 0) _brain.CaptainSays(_input);
                    _typing = false;
                    e.Use();
                }
                else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    _typing = false;
                    e.Use();
                }
                GUILayout.Label("Captain an Brücke (Enter senden, Esc abbrechen):");
                GUI.SetNextControlName("StbcAiCrewInput");
                _input = GUILayout.TextField(_input ?? "", 300);
                GUI.FocusControl("StbcAiCrewInput");
            }
            else
            {
                GUILayout.Label(KeyTalk.Value + " sprechen · " + KeyCrewPanel.Value + " Crew-Quartier · " + KeyOverlay.Value + " Overlay · " +
                                KeyEndMission.Value + " Mission beenden · " + KeyStateDump.Value + " Dump");
            }

            foreach (var n in _brain.Notices) GUILayout.Label("<i>" + n + "</i>");
            if (_toast != null && Time.realtimeSinceStartup < _toastUntil) GUILayout.Label(_toast);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        bool _shutDown;

        void OnDestroy() => Shutdown();
        void OnApplicationQuit() => Shutdown();

        void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            if (_brain != null && _brain.InMission) _brain.SaveForShutdown();
            if (_server != null) _server.Dispose();
        }
    }
}
