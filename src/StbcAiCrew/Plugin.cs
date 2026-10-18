using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace StbcAiCrew
{
    [BepInPlugin(Guid, "STBC AI Crew", "0.1.0")]
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
        public ConfigEntry<int> ContextSize;
        public ConfigEntry<int> GpuLayers;
        public ConfigEntry<string> ExtraServerArgs;
        public ConfigEntry<int> StartupTimeoutSec;
        public ConfigEntry<int> RequestTimeoutSec;
        public ConfigEntry<float> Temperature;
        public ConfigEntry<int> MaxTokens;

        // ---- Crew-Verhalten ----
        public ConfigEntry<int> AutonomyLevel;
        public ConfigEntry<float> ReactionIntervalSec;
        public ConfigEntry<float> HeartbeatSec;
        public ConfigEntry<int> MaxCommandsPerTurn;
        public ConfigEntry<int> EventsInPrompt;
        public ConfigEntry<int> HistoryTurns;
        public ConfigEntry<string> Language;
        public ConfigEntry<string> ExtraPersonaNotes;
        public ConfigEntry<float> MessageSeconds;
        public ConfigEntry<bool> MuteBotVoiceLines;
        public ConfigEntry<bool> LogBotVoiceLines;

        // ---- Lernen ----
        public ConfigEntry<int> MaxLessons;
        public ConfigEntry<int> MinEventsForDebrief;
        public ConfigEntry<int> DebriefLogChars;

        // ---- Tasten ----
        public ConfigEntry<KeyCode> KeyOverlay;
        public ConfigEntry<KeyCode> KeyTalk;
        public ConfigEntry<KeyCode> KeyDebrief;
        public ConfigEntry<KeyCode> KeyStateDump;

        LlamaServerHost _server;
        LlmClient _llm;
        CrewMemory _memory;
        CrewBrain _brain;
        string _dataDir;
        bool _wasInMission;

        bool _showOverlay = true;
        bool _typing;
        string _input = "";
        string _toast;
        float _toastUntil;

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
            string args = "-m \"" + model + "\" --host 127.0.0.1 --port " + Port.Value +
                          " -c " + ContextSize.Value + " -ngl " + GpuLayers.Value +
                          " --jinja --log-file \"" + Path.Combine(pluginDir, "llama-server.log") + "\" " + ExtraServerArgs.Value;

            _server = new LlamaServerHost(Log, exe, args, baseUrl, StartupTimeoutSec.Value);
            _llm = new LlmClient(Log, baseUrl, RequestTimeoutSec.Value * 1000)
            {
                Temperature = Temperature.Value,
                MaxTokens = MaxTokens.Value
            };
            _memory = new CrewMemory(Path.Combine(_dataDir, "gedaechtnis.json"));
            _memory.Load();
            _brain = new CrewBrain(_llm, _memory, this);

            GamePatches.Apply(new Harmony(Guid));
            _server.Start();
            Log.LogInfo("STBC AI Crew geladen. Overlay: " + KeyOverlay.Value + ", Sprechen: " + KeyTalk.Value);
        }

        void BindConfig()
        {
            UseExternalServer = Config.Bind("LLM", "ExternenServerNutzen", false, "true = llama-server nicht selbst starten, sondern einen bereits laufenden auf dem Port nutzen.");
            LlamaServerExe = Config.Bind("LLM", "LlamaServerExe", @"llama\llama-server.exe", "Pfad zu llama-server.exe (relativ zum Plugin-Ordner oder absolut).");
            ModelPath = Config.Bind("LLM", "Modell", @"models\crew.gguf", "Pfad zur GGUF-Modelldatei (relativ zum Plugin-Ordner oder absolut).");
            Port = Config.Bind("LLM", "Port", 8089, "Lokaler Port des llama-server.");
            ContextSize = Config.Bind("LLM", "Kontextgroesse", 8192, "Kontextfenster in Tokens. Mehr = mehr VRAM.");
            GpuLayers = Config.Bind("LLM", "GpuLayers", 99, "Wie viele Modellschichten auf die GPU (99 = alle). Weniger, falls das Spiel ruckelt.");
            ExtraServerArgs = Config.Bind("LLM", "ZusatzArgumente", "-fa on --cache-type-k q8_0 --cache-type-v q8_0", "Weitere Argumente für llama-server.");
            StartupTimeoutSec = Config.Bind("LLM", "StartTimeoutSek", 180, "So lange wird auf das Laden des Modells gewartet.");
            RequestTimeoutSec = Config.Bind("LLM", "AnfrageTimeoutSek", 60, "Zeitlimit pro LLM-Anfrage.");
            Temperature = Config.Bind("LLM", "Temperatur", 0.6f, "Kreativität der Antworten.");
            MaxTokens = Config.Bind("LLM", "MaxTokens", 400, "Maximale Antwortlänge im Einsatz.");

            AutonomyLevel = Config.Bind("Crew", "Autonomie", 2, new ConfigDescription(
                "0 = nur auf Befehl, 1 = meldet sich selbst, handelt aber nur auf Befehl, 2 = spielt selbst mit und handelt eigenständig.",
                new AcceptableValueRange<int>(0, 2)));
            ReactionIntervalSec = Config.Bind("Crew", "ReaktionsabstandSek", 6f, "Mindestabstand zwischen eigenständigen Reaktionen auf neue Ereignisse.");
            HeartbeatSec = Config.Bind("Crew", "LagepruefungSek", 30f, "Bei Autonomie 2: so oft prüft die Crew die Lage auch ohne neue Ereignisse.");
            MaxCommandsPerTurn = Config.Bind("Crew", "MaxBefehleProAntwort", 3, "Obergrenze ausgeführter Befehle pro LLM-Antwort.");
            EventsInPrompt = Config.Bind("Crew", "EreignisseImPrompt", 20, "Wie viele letzte Ereignisse die Crew bei jeder Entscheidung sieht.");
            HistoryTurns = Config.Bind("Crew", "Gespraechsverlauf", 4, "Wie viele letzte Wortwechsel im Kontext bleiben.");
            Language = Config.Bind("Crew", "Sprache", "Deutsch", "Sprache der Crew.");
            ExtraPersonaNotes = Config.Bind("Crew", "PersonaZusatz", "", "Zusätzliche Anweisungen zur Persönlichkeit der Crew.");
            MessageSeconds = Config.Bind("Crew", "AnzeigedauerSek", 7f, "Wie lange Funksprüche auf der Captain-Konsole stehen.");
            MuteBotVoiceLines = Config.Bind("Crew", "BotSprachzeilenStumm", false, "Vorgefertigte Sprachzeilen der Bots unterdrücken (sinnvoll, sobald TTS da ist).");
            LogBotVoiceLines = Config.Bind("Crew", "BotSprachzeilenProtokollieren", false, "Vorgefertigte Sprachzeilen der Bots als Ereignis aufnehmen.");

            MaxLessons = Config.Bind("Lernen", "MaxLektionen", 30, "Obergrenze der Lektionen im Langzeitgedächtnis.");
            MinEventsForDebrief = Config.Bind("Lernen", "MinEreignisse", 8, "Nachbesprechung erst ab so vielen Ereignissen in der Mission.");
            DebriefLogChars = Config.Bind("Lernen", "ProtokollZeichen", 12000, "Maximale Länge des Missionsprotokolls in der Nachbesprechung.");

            KeyOverlay = Config.Bind("Tasten", "Overlay", KeyCode.F7, "Overlay ein/aus.");
            KeyTalk = Config.Bind("Tasten", "Sprechen", KeyCode.F8, "Texteingabe an die Crew öffnen.");
            KeyDebrief = Config.Bind("Tasten", "Nachbesprechung", KeyCode.F9, "Mission für beendet erklären und Nachbesprechung starten.");
            KeyStateDump = Config.Bind("Tasten", "Zustandsdump", KeyCode.F10, "Felder des Spielerschiffs in eine Datei schreiben (Reverse Engineering).");
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
            if (inMission && !_wasInMission)
            {
                EventLog.ResetMission();
                EventLog.Add("Mission beginnt, die Crew ist auf der Brücke.", false);
            }
            else if (!inMission && _wasInMission)
            {
                _brain.EndMission("Mission verlassen");
            }
            _wasInMission = inMission;

            if (_server.State == ServerState.Ready) _brain.Tick();

            if (!_typing)
            {
                if (Input.GetKeyDown(KeyOverlay.Value)) _showOverlay = !_showOverlay;
                if (Input.GetKeyDown(KeyTalk.Value)) { _typing = true; _input = ""; _showOverlay = true; }
                if (Input.GetKeyDown(KeyDebrief.Value)) { _brain.EndMission("manuell"); Toast("Nachbesprechung geplant."); }
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
            if (!_showOverlay) return;

            const float w = 520f;
            GUILayout.BeginArea(new Rect(10f, 10f, w, Screen.height - 20f));
            GUILayout.BeginVertical("box");

            string server = _server.State == ServerState.Failed ? "Fehler: " + _server.Error : _server.State.ToString();
            GUILayout.Label("<b>AI Crew</b>  –  LLM: " + server + "  |  Crew: " + _brain.Status +
                            "  |  Autonomie: " + AutonomyLevel.Value + "  |  " + (GameBridge.InMission ? "im Einsatz" : "kein Einsatz"));

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
                GUILayout.Label("Captain an Crew (Enter senden, Esc abbrechen):");
                GUI.SetNextControlName("StbcAiCrewInput");
                _input = GUILayout.TextField(_input ?? "", 300);
                GUI.FocusControl("StbcAiCrewInput");
            }
            else
            {
                GUILayout.Label(KeyTalk.Value + " = mit der Crew sprechen, " + KeyOverlay.Value + " = Overlay, " +
                                KeyDebrief.Value + " = Nachbesprechung, " + KeyStateDump.Value + " = Zustandsdump");
            }

            if (_toast != null && Time.realtimeSinceStartup < _toastUntil) GUILayout.Label(_toast);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void OnDestroy()
        {
            if (_memory != null) _memory.Save();
            if (_server != null) _server.Dispose();
        }

        void OnApplicationQuit()
        {
            OnDestroy();
        }
    }
}
