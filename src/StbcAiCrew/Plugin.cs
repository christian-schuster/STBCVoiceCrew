using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using StbcAiCrew.Voice;
using UnityEngine;

namespace StbcAiCrew
{
    [BepInPlugin(Guid, "STBC Voice Crew", "1.0.0")]
    [BepInProcess("stbc.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "de.schuster.stbc.aicrew";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // ---- LLM / llama.cpp ----
        public ConfigEntry<bool> UseJobObject;
        public ConfigEntry<bool> SkipLauncherInstance;
        public ConfigEntry<bool> UseExternalServer;
        public ConfigEntry<string> LlamaServerExe;
        public ConfigEntry<string> ModelPath;
        public ConfigEntry<int> Port;
        public ConfigEntry<int> Slots;
        public ConfigEntry<int> ContextPerSlot;
        public ConfigEntry<int> GpuLayers;
        public ConfigEntry<string> ExtraServerArgs;
        public ConfigEntry<int> StartupTimeoutSec;
        public ConfigEntry<float> ServerStartDelaySec;
        public ConfigEntry<int> RequestTimeoutSec;
        public ConfigEntry<float> Temperature;
        public ConfigEntry<int> MaxTokens;

        // ---- Spracheingabe ----
        public ConfigEntry<bool> VoiceEnabled;
        public ConfigEntry<string> MicDevice;
        public ConfigEntry<string> MicLanguage;
        public ConfigEntry<string> MyStation;
        public ConfigEntry<string> WhisperServerExe;
        public ConfigEntry<string> WhisperModel;
        public ConfigEntry<int> WhisperPort;
        public ConfigEntry<bool> WhisperGpu;
        public ConfigEntry<int> WhisperThreads;
        public ConfigEntry<float> MicMarginDb;
        public ConfigEntry<float> MicMinDb;
        public ConfigEntry<int> MicEndSilenceMs;
        public ConfigEntry<int> MicMinSpeechMs;

        // ---- Sprachausgabe ----
        public ConfigEntry<bool> TtsEnabled;
        public ConfigEntry<string> TtsEngine;
        public ConfigEntry<string> QwenServerExe;
        public ConfigEntry<string> QwenTtsExe;
        public ConfigEntry<string> QwenBaseModel;
        public ConfigEntry<string> QwenDesignModel;
        public ConfigEntry<string> QwenCodec;
        public ConfigEntry<int> QwenPort;
        public ConfigEntry<string> QwenLanguage;
        public ConfigEntry<float> QwenMaxPitchHz;
        public ConfigEntry<string> PiperExe;
        public ConfigEntry<string> PiperVoiceDir;
        public ConfigEntry<string> SpeakerDevice;
        public ConfigEntry<float> SpeakerVolume;
        public ConfigEntry<bool> DeafWhileCrewSpeaks;
        public ConfigEntry<float> PauseBetweenLinesSec;
        public ConfigEntry<bool> ListenToGameVoice;
        public ConfigEntry<bool> NoProfanity;
        public ConfigEntry<bool> Multiplayer;
        public ConfigEntry<bool> OwnMicrophone;
        public ConfigEntry<bool> QuickOrders;
        public ConfigEntry<bool> Reflexes;
        public ConfigEntry<string> GameMicrophone;
        public ConfigEntry<string> GameVoiceLanguage;
        public ConfigEntry<float> StaleSpeechSec;
        public ConfigEntry<float> SpeechTempo;

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

        // ---- Humor ----
        public ConfigEntry<bool> HumorEnabled;
        public ConfigEntry<bool> LobbyBanter;
        public ConfigEntry<float> JokeIntervalMissionSec;
        public ConfigEntry<float> JokeIntervalLobbySec;
        public ConfigEntry<int> QuietAfterReprimandMin;
        public ConfigEntry<int> JokeMemory;

        // ---- Lernen ----
        public ConfigEntry<int> MaxLessons;
        public ConfigEntry<int> ChronicleMax;
        public ConfigEntry<int> NotesMax;
        public ConfigEntry<int> MinActiveSecForDebrief;
        public ConfigEntry<int> DebriefLogChars;

        // ---- Tasten ----
        public ConfigEntry<KeyCode> KeyMute;
        public ConfigEntry<KeyCode> KeyCrewPanel;
        public ConfigEntry<KeyCode> KeyOverlay;
        public ConfigEntry<KeyCode> KeyTalk;
        public ConfigEntry<KeyCode> KeyEndMission;
        public ConfigEntry<KeyCode> KeyStateDump;
        public ConfigEntry<KeyCode> KeyPushToTalk;

        // ---- Watson-Ersatz ----
        public ConfigEntry<string> Mode;
        public ConfigEntry<string> ListenModeSetting;
        public ConfigEntry<bool> WatsonBeeps;
        public ConfigEntry<string> VoskModel;
        public ConfigEntry<string> VoskModelEn;
        public ConfigEntry<string> CommandLanguage;
        bool _watson;
        WatsonCrew _crew;
        readonly System.Collections.Generic.List<VoskEngine> _vosk = new System.Collections.Generic.List<VoskEngine>();
        bool VoskReady => _vosk.Exists(v => v.Ready);
        string VoskState => _vosk.Count == 0 ? "kein Modell" : string.Join(", ", _vosk.ConvertAll(v => v.Tag + (v.Ready ? " bereit" : " FEHLT: " + v.Error)).ToArray());
        string _vocabKey;
        /// <summary>Watson-Modus: Befehle ohne Sprachmodell, Original-Crew mit Spielstimmen.</summary>
        public bool WatsonMode => _watson;

        ServerHost _server;
        ServerHost _sttServer;
        PiperTts _tts;
        QwenTts _qwen;
        WavePlayer _player;
        VoiceInput _voice;
        VoListener _voListener;
        RemoteVoiceTap _remoteTap;
        CrewVoiceSender _crewSender;
        bool _wasHost = true;
        bool _voiceWarmed;
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

        /// <summary>false in der ersten Startinstanz (siehe Awake): dann tut der Mod nichts.</summary>
        bool _active;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            BindConfig();

            // Steam startet stbc.exe zunächst ohne Parameter; diese Instanz startet Ubisoft Connect und beendet sich,
            // Ubisoft Connect startet dann das eigentliche Spiel mit "-uplay_steam_mode". In der ersten Instanz bleibt
            // der Mod still, sonst würde er Server starten und den Neustart stören.
            string parent;
            if (SkipLauncherInstance.Value && LaunchInstance.IsSteamLauncherInstance(out parent))
            {
                int n = LaunchInstance.ClearDoorstopVariables();
                Log.LogInfo("Startinstanz (gestartet von " + parent + "): Mod bleibt inaktiv, " + n +
                            " DOORSTOP-Variablen entfernt, damit das über Ubisoft Connect neu gestartete Spiel BepInEx lädt.");
                return;
            }
            Log.LogInfo("Spielinstanz (gestartet von " + (LaunchInstance.ParentProcessName() ?? "?") + "): Mod wird aktiv.");
            _active = true;

            string pluginDir = Path.GetDirectoryName(Info.Location);
            _dataDir = Path.Combine(Paths.ConfigPath, "StbcAiCrew");
            JobObject.Enabled = UseJobObject.Value;
            KillLeftovers(pluginDir);
            _watson = !Mode.Value.Trim().Equals("llm", StringComparison.OrdinalIgnoreCase);
            if (_watson) { AwakeWatson(pluginDir); return; }

            string baseUrl = "http://127.0.0.1:" + Port.Value;
            string exe = UseExternalServer.Value ? null : ResolvePath(pluginDir, LlamaServerExe.Value);
            string model = ResolvePath(pluginDir, ModelPath.Value);
            int slots = Math.Max(1, Slots.Value);
            string args = "-m \"" + model + "\" --host 127.0.0.1 --port " + Port.Value +
                          " -np " + slots + " -c " + (ContextPerSlot.Value * slots) + " -ngl " + GpuLayers.Value +
                          " --jinja --log-file \"" + Path.Combine(pluginDir, "llama-server.log") + "\" " + ExtraServerArgs.Value;

            _server = new ServerHost(Log, "llama-server", exe, args, baseUrl, "/health", StartupTimeoutSec.Value) { DelaySec = ServerStartDelaySec.Value };
            _llm = new LlmClient(Log, baseUrl, RequestTimeoutSec.Value * 1000) { Temperature = Temperature.Value };
            _roster = new Roster(Path.Combine(_dataDir, "crew"));
            _roster.Load();
            var names = new System.Collections.Generic.List<string>();
            foreach (var c in _roster.All) names.Add(c.Name);
            if (names.Count > 0) VoiceInput.Vocabulary += " Mit " + string.Join(", ", names.ToArray()) + " an den Stationen.";
            _brain = new CrewBrain(_llm, this, _roster, _dataDir);
            if (TtsEnabled.Value) StartTts(pluginDir);
            _panel = new CrewPanel(this, _roster, _llm, _brain, _tts, _qwen);

            GameMicFix.Preferred = (GameMicrophone.Value ?? "").Trim();
            GamePatches.VoipHooks = Config.Bind("Diagnose", "SprachchatHooks", true, "Eingriffe in den Sprachchat des Spiels (Mitspieler hören, Crew-Stimmen senden). Aus zur Fehlersuche.").Value;
            GamePatches.MicSelectHook = Config.Bind("Diagnose", "MikrofonauswahlHook", true, "Korrektur der Mikrofonauswahl des Spiels. Aus zur Fehlersuche.").Value;
            GamePatches.Apply(new Harmony(Guid));
            _server.Start();
            if (VoiceEnabled.Value) StartVoice(pluginDir);
            Log.LogInfo("STBC AI Crew geladen. Crew-Quartier: " + KeyCrewPanel.Value + ", Sprechen: " + KeyTalk.Value);
        }

        void StartVoice(string pluginDir)
        {
            string url = "http://127.0.0.1:" + WhisperPort.Value;
            string exe = UseExternalServer.Value ? null : ResolvePath(pluginDir, WhisperServerExe.Value);
            string args = "-m \"" + ResolvePath(pluginDir, WhisperModel.Value) + "\" --host 127.0.0.1 --port " + WhisperPort.Value +
                          " -l " + MicLanguage.Value + " -t " + WhisperThreads.Value + (WhisperGpu.Value ? "" : " -ng");
            _sttServer = new ServerHost(Log, "whisper-server", exe, args, url, "/", StartupTimeoutSec.Value)
            {
                After = _server   // direkt nach dem Sprachmodell: Zuhören ist wichtiger als die schöne Stimme
            };
            _sttServer.Start();
            if (_qwen != null) _qwen.Host.After = _sttServer;   // Sprachausgabe erst nach der Spracherkennung (wartet noch in der Startverzögerung)
            var vs = new VoiceSettings
            {
                Device = MicDevice.Value, GameDeviceId = () => GameBridge.GameAudioInId, GameDeviceName = () => GameMicFix.CurrentGameMic, Language = MicLanguage.Value, MarginDb = MicMarginDb.Value,
                MinDb = MicMinDb.Value, EndSilenceMs = MicEndSilenceMs.Value, MinSpeechMs = MicMinSpeechMs.Value
            };
            _voice = new VoiceInput(Log, vs, url, OnHeard);
            if (OwnMicrophone.Value) _voice.Start();
            else Log.LogInfo("Eigenes Mikrofon aus (Konfiguration): Die Mod öffnet das Mikrofon nicht selbst.");

            if (Multiplayer.Value)
            {
                // Mitspieler hören (Sprachchat) und die Crew für alle hörbar machen
                var stt = _sttServer;
                _remoteTap = new RemoteVoiceTap(Log, url, MicLanguage.Value, () => stt.State == ServerState.Ready, OnRemoteHeard);
                _crewSender = new CrewVoiceSender();
            }

            if (ListenToGameVoice.Value)
            {
                // Mithören: Sprachausgaben des Spiels (Briefing, Funk) live abhören wie der Captain
                string lang = GameVoiceLanguage.Value;
                if (string.IsNullOrEmpty(lang) || lang == "auto") lang = GameBridge.VoiceLanguage() ?? "German";
                string banks = Path.Combine(Path.Combine(Path.Combine(Application.streamingAssetsPath, "Audio"), "GeneratedSoundBanks"), "Windows");
                var sttServer = _sttServer;
                _voListener = new VoListener(Log, banks, lang, url, () => sttServer.State == ServerState.Ready,
                                             (speaker, text) => { if (_brain != null) _brain.HeardGameVoice(speaker, text); });
                GamePatches.OnGameVoice = (ev, who) =>
                {
                    string speaker = GameBridge.SpeakerName(who);
                    if (speaker != null && _voListener != null) _voListener.OnPlay(ev, speaker);
                };
            }
        }

        void StartTts(string pluginDir)
        {
            _tts = new PiperTts(Log, ResolvePath(pluginDir, PiperExe.Value), ResolvePath(pluginDir, PiperVoiceDir.Value),
                                Path.Combine(Path.GetTempPath(), "StbcAiCrew_tts"));
            if (_tts.Available) _tts.Start();
            else
            {
                Log.LogWarning("Piper oder Stimmen nicht gefunden (" + PiperExe.Value + ", " + PiperVoiceDir.Value + ").");
                _tts = null;
            }

            // Qwen3-TTS: eigene, entworfene Stimme je Charakter in 24 kHz; Piper springt ein, solange eine Stimme fehlt
            string qServer = ResolvePath(pluginDir, QwenServerExe.Value), qCli = ResolvePath(pluginDir, QwenTtsExe.Value);
            string qBase = ResolvePath(pluginDir, QwenBaseModel.Value), qDesign = ResolvePath(pluginDir, QwenDesignModel.Value), qCodec = ResolvePath(pluginDir, QwenCodec.Value);
            if (TtsEngine.Value.Equals("qwen", StringComparison.OrdinalIgnoreCase))
            {
                if (QwenTts.FilesPresent(qServer, qCli, qBase, qDesign, qCodec))
                {
                    _qwen = new QwenTts(Log, qServer, qCli, qBase, qDesign, qCodec, QwenPort.Value,
                                        Path.Combine(_dataDir, "stimmen"), QwenLanguage.Value, _tts);
                    _qwen.MayDesignNow = () => !GameBridge.InMission;
                    _qwen.MaxPitchHz = QwenMaxPitchHz.Value;
                    _qwen.Host.After = _sttServer ?? _server;   // nacheinander laden: Sprachmodell, Spracherkennung, dann Sprachausgabe
                    _qwen.Start();
                    foreach (var c in _roster.All) _qwen.Prepare(c, false);   // fehlende Stimmen gleich entwerfen
                }
                else Log.LogWarning("Qwen3-TTS-Dateien fehlen (qwentts-Ordner) – nutze Piper.");
            }
            if (_qwen == null && _tts == null) { Log.LogWarning("Keine Sprachausgabe verfügbar – Crew spricht nur als Text."); return; }
            _player = new WavePlayer
            {
                DeviceFilter = SpeakerDevice.Value,
                Volume = SpeakerVolume.Value,
                GameDeviceId = () => GameBridge.GameAudioOutId,
                GameVolume = () => GameBridge.GameVoiceVolume
            };
            Log.LogInfo("Lautsprecher: " + string.Join(" | ", AudioDevices.List(false).ToArray()));
            Log.LogInfo("Mikrofone: " + string.Join(" | ", AudioDevices.List(true).ToArray()));
            _brain.Tts = (ISpeechEngine)_qwen ?? _tts;
            _brain.Player = _player;
            AudioTempo.GlobalTempo = SpeechTempo.Value;
            _brain.HumanSpeaking = () => (_voice != null && _voice.Speaking) ||
                                         (_remoteTap != null && Time.realtimeSinceStartup - _remoteTap.LastRemoteSpeech < 0.8f);
            _brain.CrewAudio = (pcm, rate) => { if (_crewSender != null && Net.Online) _crewSender.Enqueue(pcm, rate); };
            _brain.GameVoicePlaying = GameBridge.GameVoicePlaying;
            _brain.CrewSpeaking = secs => { if (_voice != null && DeafWhileCrewSpeaks.Value) _voice.Deafen(secs); };
        }

        /// <summary>Erkannter Satz vom Mikrofon (Hauptthread).</summary>
        void OnHeard(string text)
        {
            if (!Net.IsHost) return;
            if (!_brain.InMission) Toast("Gehört (kein Einsatz): " + text);
            // Im Mehrspieler die eigene Station aus dem Spiel, sonst aus der Konfiguration
            Station? mine = Net.Online ? Net.StationOf(Net.LocalPlayerId) : null;
            _brain.HumanSays(mine ?? ParseStation(MyStation.Value), text);   // außerhalb des Einsatzes nur für "Ruhe!" usw.
        }

        void OnRemoteHeard(int playerId, string text)
        {
            if (!Net.IsHost) return;
            Station st = Net.StationOf(playerId) ?? Station.Captain;
            Log.LogInfo("Gehört von " + (Net.NameOf(playerId) ?? "Mitspieler " + playerId) + " (" + OfficerAgent.StationDe(st) + "): " + text);
            _brain.HumanSays(st, text);
        }

        static Station ParseStation(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "steuer": case "helm": return Station.Helm;
                case "taktik": case "tactical": return Station.Tactical;
                case "technik": case "engineering": return Station.Engineering;
                default: return Station.Captain;
            }
        }

        /// <summary>Server aus einer abgestürzten Session beenden (nur Programme aus dem eigenen Plugin-Ordner).</summary>
        static void KillLeftovers(string pluginDir)
        {
            foreach (var name in new[] { "llama-server", "whisper-server", "tts-server", "qwen-tts", "piper" })
            {
                System.Diagnostics.Process[] procs;
                try { procs = System.Diagnostics.Process.GetProcessesByName(name); } catch { continue; }
                foreach (var p in procs)
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (path == null || !path.StartsWith(pluginDir, StringComparison.OrdinalIgnoreCase)) continue;
                        p.Kill();
                        Log.LogInfo("Verwaisten Server beendet: " + name + " (PID " + p.Id + ")");
                    }
                    catch { }
                }
            }
        }

        // ================= Watson-Modus =================

        void AwakeWatson(string pluginDir)
        {
            GameMicFix.Preferred = (GameMicrophone.Value ?? "").Trim();
            GamePatches.VoipHooks = Config.Bind("Diagnose", "SprachchatHooks", true, "Eingriffe in den Sprachchat des Spiels (Mitspieler hören, Crew-Stimmen senden). Aus zur Fehlersuche.").Value;
            GamePatches.MicSelectHook = Config.Bind("Diagnose", "MikrofonauswahlHook", true, "Korrektur der Mikrofonauswahl des Spiels. Aus zur Fehlersuche.").Value;
            GamePatches.Apply(new Harmony(Guid));

            _crew = new WatsonCrew(this) { Reflexes = Reflexes.Value };
            switch ((ListenModeSetting.Value ?? "").Trim().ToLowerInvariant())
            {
                case "ansprache": _crew.Listen = WatsonCrew.ListenMode.Address; break;
                case "ptt": _crew.Listen = WatsonCrew.ListenMode.PushToTalk; break;
                case "dauernd": _crew.Listen = WatsonCrew.ListenMode.Always; break;
                default: _crew.Listen = WatsonCrew.ListenMode.Both; break;
            }
            _player = new WavePlayer
            {
                DeviceFilter = SpeakerDevice.Value,
                Volume = SpeakerVolume.Value,
                GameDeviceId = () => GameBridge.GameAudioOutId,
                GameVolume = () => GameBridge.GameVoiceVolume
            };
            _crew.Player = _player;

            // Befehlssprache: spiel = Sprache des Spiels, de, en, auto = beide gleichzeitig (Sprache je Sprecher gelernt)
            string lang = (CommandLanguage.Value ?? "spiel").Trim().ToLowerInvariant();
            if (lang == "spiel" || lang == "game")
            {
                string gl = GameBridge.VoiceLanguage() ?? "German";
                lang = gl.StartsWith("German", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            }
            string libDir = Path.Combine(pluginDir, "vosk");
            if (lang == "de" || lang == "auto") _vosk.Add(new VoskEngine(Log, libDir, ResolvePath(pluginDir, VoskModel.Value), "de"));
            if (lang == "en" || lang == "auto") _vosk.Add(new VoskEngine(Log, libDir, ResolvePath(pluginDir, VoskModelEn.Value), "en"));
            if (_vosk.Exists(v => v.Ready)) _vosk.RemoveAll(v => !v.Ready);
            foreach (var v in _vosk) v.SetVocabulary(v.Tag == "en" ? VoskEngine.CommandWordsEn : VoskEngine.CommandWords);
            // Je Äußerung alle Sprachen parallel; Ergebnis "de‖grammatik‖frei‖sicherheit¦en‖…", die Auswahl trifft WatsonCrew
            Func<short[], string> transcribe = pcm =>
            {
                var res = new VoskEngine.Result[_vosk.Count];
                var threads = new System.Collections.Generic.List<System.Threading.Thread>();
                for (int i = 1; i < _vosk.Count; i++)
                {
                    int k = i;
                    var th = new System.Threading.Thread(() => res[k] = _vosk[k].Recognize(pcm)) { IsBackground = true };
                    th.Start();
                    threads.Add(th);
                }
                if (_vosk.Count > 0) res[0] = _vosk[0].Recognize(pcm);
                foreach (var th in threads) th.Join();
                var parts = new string[_vosk.Count];
                for (int i = 0; i < _vosk.Count; i++)
                    parts[i] = _vosk[i].Tag + "‖" + (res[i].Grammar ?? "") + "‖" + (res[i].Free ?? "") + "‖" + res[i].Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                return string.Join("¦", parts);
            };

            if (VoiceEnabled.Value && VoskReady)
            {
                var vs = new VoiceSettings
                {
                    Device = MicDevice.Value, GameDeviceId = () => GameBridge.GameAudioInId, GameDeviceName = () => GameMicFix.CurrentGameMic, Language = MicLanguage.Value, MarginDb = MicMarginDb.Value,
                    MinDb = MicMinDb.Value, EndSilenceMs = MicEndSilenceMs.Value, MinSpeechMs = MicMinSpeechMs.Value
                };
                _voice = new VoiceInput(Log, vs, "http://127.0.0.1:0", OnHeardWatson) { Transcriber = transcribe, SttReady = true };
                if (OwnMicrophone.Value) _voice.Start();
                if (Multiplayer.Value)
                    _remoteTap = new RemoteVoiceTap(Log, "http://127.0.0.1:0", MicLanguage.Value, () => true, OnRemoteHeardWatson) { Transcriber = transcribe };
            }
            Log.LogInfo("STBC AI Crew im Watson-Modus geladen (Vosk " + VoskState +
                        ", Zuhören: " + _crew.Listen + ", Push-to-talk: " + KeyPushToTalk.Value + ").");
        }

        void OnHeardWatson(string text)
        {
            if (!Net.IsHost) return;
            Station? mine = Net.Online ? Net.StationOf(Net.LocalPlayerId) : null;
            bool ptt = _voice != null && _voice.LastPtt;
            _crew.Heard(mine ?? ParseStation(MyStation.Value), Net.Online ? (Net.NameOf(Net.LocalPlayerId) ?? "Ich") : "Captain", text, ptt);
        }

        void OnRemoteHeardWatson(int playerId, string text)
        {
            if (!Net.IsHost) return;
            Station st = Net.StationOf(playerId) ?? Station.Captain;
            _crew.Heard(st, Net.NameOf(playerId) ?? "Mitspieler " + playerId, text, false);
        }

        void UpdateWatson()
        {
            bool host = Net.IsHost;
            if (host != _wasHost) { _wasHost = host; Log.LogInfo(host ? "Host: Sprachbefehle aktiv." : "Nicht Host: Sprachbefehle laufen beim Host."); }
            bool inMission = GameBridge.InMission && host;
            if (inMission != _wasInMission) _status = null;
            _wasInMission = inMission;

            if (inMission && Time.time >= _nextStatusPoll)
            {
                _nextStatusPoll = Time.time + 1f;
                var now = ShipStatus.Read();
                if (now != null) _status = now;
                if (now != null && now.ContactList != null && _vosk.Count > 0)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var c in now.ContactList) names.Add(c.Name);
                    string key = string.Join("|", names.ToArray());
                    if (key != _vocabKey)
                    {
                        _vocabKey = key;
                        foreach (var v in _vosk)
                        {
                            var words = new System.Collections.Generic.List<string>(v.Tag == "en" ? VoskEngine.CommandWordsEn : VoskEngine.CommandWords);
                            words.AddRange(names);
                            v.SetVocabulary(words);
                        }
                    }
                }
            }
            if (host) _crew.Tick(inMission ? _status : null);

            GameMicFix.Probe();
            if (_remoteTap != null) _remoteTap.Tick();
            if (_voice != null) _voice.PttDown = Input.GetKey(KeyPushToTalk.Value);

            if (Input.GetKeyDown(KeyMute.Value) && _voice != null)
            {
                _voice.Muted = !_voice.Muted;
                Toast(_voice.Muted ? "Mikrofon stumm." : "Mikrofon hört wieder zu.");
            }
            if (Input.GetKeyDown(KeyOverlay.Value)) _showOverlay = !_showOverlay;
            if (Input.GetKeyDown(KeyStateDump.Value))
            {
                try { Toast("Zustand: " + StateProbe.Dump(Path.Combine(_dataDir, "dumps"))); }
                catch (Exception e) { Toast("Dump fehlgeschlagen: " + e.Message); Log.LogError(e); }
            }
        }

        void DrawWatson()
        {
            if (!_showOverlay) return;
            GUILayout.BeginArea(new Rect(10f, 10f, 520f, Screen.height - 20f));
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Brückencrew (Watson-Ersatz)</b>  –  Vosk: " + VoskState +
                            "  |  " + (GameBridge.InMission ? "im Einsatz" : "kein Einsatz"));
            if (_voice != null)
            {
                string bar = new string('|', Mathf.Clamp(Mathf.RoundToInt((_voice.LevelDb + 70f) / 3f), 0, 20));
                GUILayout.Label("Mikro (" + _voice.Device + "): " + _voice.State + "  " + bar + (_voice.Speaking ? " ●" : "") +
                                (_voice.PttDown ? "  [Push-to-talk]" : ""));
            }
            GUILayout.Label("Gehört: " + _crew.LastHeard);
            GUILayout.Label("Befehl: " + _crew.LastResult);
            string orders = _crew.Orders.Describe();
            if (!string.IsNullOrEmpty(orders) || _crew.Nav.StayOutOfSensors)
                GUILayout.Label("Daueranweisung: " + (orders ?? "") + (_crew.Nav.StayOutOfSensors ? " · außerhalb der Ortung" : ""));
            if (_crew.Nav.Current != Navigator.Mode.None) GUILayout.Label("Manöver: " + _crew.Nav.Status);
            foreach (var l in _crew.Log) GUILayout.Label("<i>" + l + "</i>");
            GUILayout.Label(KeyPushToTalk.Value + " halten = Befehl · " + KeyMute.Value + " Mikro stumm · " + KeyOverlay.Value + " Overlay");
            if (_toast != null && Time.realtimeSinceStartup < _toastUntil) GUILayout.Label(_toast);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void BindConfig()
        {
            SkipLauncherInstance = Config.Bind("Diagnose", "StartinstanzUeberspringen", true, "In der von Steam gestarteten Startinstanz (die nur Ubisoft Connect startet) inaktiv bleiben und die DOORSTOP-Variablen entfernen. Aktiv wird der Mod im von Ubisoft Connect gestarteten Spiel.");
            UseJobObject = Config.Bind("Diagnose", "JobObjekt", false, "Kindprozesse per Windows-Job-Objekt an das Spiel binden (im Spiel-Mono fehlerhaft, daher aus).");
            UseExternalServer = Config.Bind("LLM", "ExternenServerNutzen", false, "true = llama-server nicht selbst starten, sondern einen bereits laufenden auf dem Port nutzen.");
            LlamaServerExe = Config.Bind("LLM", "LlamaServerExe", "llama/llama-server.exe", "Pfad zu llama-server.exe (relativ zum Plugin-Ordner oder absolut).");
            ModelPath = Config.Bind("LLM", "Modell", "models/crew.gguf", "Pfad zur GGUF-Modelldatei (relativ zum Plugin-Ordner oder absolut).");
            Port = Config.Bind("LLM", "Port", 8089, "Lokaler Port des llama-server.");
            Slots = Config.Bind("LLM", "ParalleleSlots", 3, "Gleichzeitige Anfragen (ein Slot je KI-Offizier).");
            ContextPerSlot = Config.Bind("LLM", "KontextProSlot", 6144, "Kontextfenster je Slot in Tokens. Mehr = mehr VRAM.");
            GpuLayers = Config.Bind("LLM", "GpuLayers", 99, "Wie viele Modellschichten auf die GPU (99 = alle). Weniger, falls das Spiel ruckelt.");
            ExtraServerArgs = Config.Bind("LLM", "ZusatzArgumente", "-fa on --cache-type-k q8_0 --cache-type-v q8_0", "Weitere Argumente für llama-server.");
            ServerStartDelaySec = Config.Bind("LLM", "StartVerzoegerungSek", 20f, "So lange nach dem Spielstart warten, bevor die KI-Server ihre Modelle auf die GPU laden (das Spiel fährt erst Grafik/VR hoch). Danach laden sie nacheinander.");
            StartupTimeoutSec = Config.Bind("LLM", "StartTimeoutSek", 180, "So lange wird auf das Laden des Modells gewartet.");
            RequestTimeoutSec = Config.Bind("LLM", "AnfrageTimeoutSek", 60, "Zeitlimit pro LLM-Anfrage.");
            Temperature = Config.Bind("LLM", "Temperatur", 0.7f, "Kreativität der Antworten.");
            MaxTokens = Config.Bind("LLM", "MaxTokens", 300, "Maximale Antwortlänge im Einsatz.");

            VoiceEnabled = Config.Bind("Sprache", "Aktiv", true, "Dauerhaftes Zuhören über das Mikrofon (whisper.cpp).");
            MicDevice = Config.Bind("Sprache", "Mikrofon", "", "Leer lassen = dasselbe Mikrofon wie das Spiel (Oculus-Einstellung bzw. Windows-Standard). Nur für Sonderfälle: Teil eines Gerätenamens aus dem Log.");
            MicLanguage = Config.Bind("Sprache", "Sprachcode", "de", "Sprache für die Erkennung (de, en, …).");
            MyStation = Config.Bind("Sprache", "MeineStation", "Captain", "An welcher Station du sitzt: Captain, Steuer, Taktik oder Technik. Nur Worte des Captains gelten als Befehl.");
            WhisperServerExe = Config.Bind("Sprache", "WhisperServerExe", "whisper/Release/whisper-server.exe", "Pfad zu whisper-server.exe (relativ zum Plugin-Ordner oder absolut).");
            WhisperModel = Config.Bind("Sprache", "WhisperModell", "whisper/ggml-large-v3-turbo-q5_0.bin", "Whisper-Modell (relativ zum Plugin-Ordner oder absolut).");
            WhisperPort = Config.Bind("Sprache", "WhisperPort", 8090, "Lokaler Port des whisper-server.");
            WhisperGpu = Config.Bind("Sprache", "WhisperGpu", true, "Spracherkennung auf der GPU (false = CPU).");
            WhisperThreads = Config.Bind("Sprache", "WhisperThreads", 4, "CPU-Threads für whisper-server.");
            MicMarginDb = Config.Bind("Sprache", "SchwelleDb", 12f, "So viel lauter als das Grundrauschen muss Sprache sein. Höher = weniger Fehlauslösungen.");
            MicMinDb = Config.Bind("Sprache", "MinPegelDb", -50f, "Absoluter Mindestpegel für Sprache (dBFS).");
            MicEndSilenceMs = Config.Bind("Sprache", "SatzendeStilleMs", 700, "So lange Stille beendet einen Satz.");
            MicMinSpeechMs = Config.Bind("Sprache", "MinSprechdauerMs", 350, "Kürzere Geräusche werden ignoriert.");

            CrewHelm = Config.Bind("Besetzung", "Steuer", "auto", "Charakter-Id für die Steuer oder 'auto' (wird im Crew-Quartier gesetzt).");
            CrewTactical = Config.Bind("Besetzung", "Taktik", "auto", "Charakter-Id für Taktik oder 'auto'.");
            CrewEngineering = Config.Bind("Besetzung", "Technik", "auto", "Charakter-Id für Technik oder 'auto'.");

            AutonomyLevel = Config.Bind("Crew", "Autonomie", 2, new ConfigDescription(
                "0 = nur auf Ansprache/Befehl, 1 = meldet sich selbst, handelt aber nur auf Befehl, 2 = spielt selbst mit.",
                new AcceptableValueRange<int>(0, 2)));
            ReactionIntervalSec = Config.Bind("Crew", "ReaktionsabstandSek", 12f, "Mindestabstand zwischen eigenständigen Reaktionen eines Offiziers.");
            IdleMinSec = Config.Bind("Crew", "RuhephaseMinSek", 45f, "Frühestens so oft schaut ein Offizier in ruhigen Phasen von sich aus auf die Lage.");
            IdleMaxSec = Config.Bind("Crew", "RuhephaseMaxSek", 90f, "Spätestens so oft.");
            ChatterChance = Config.Bind("Crew", "Plauderlaune", 0.3f, "Wahrscheinlichkeit für persönliche Bemerkungen in ruhigen Phasen (0–1, wirkt mit Beredsamkeit).");
            AllowMajorWithoutOrder = Config.Bind("Crew", "WeitreichendOhneBefehl", false, "true = auch Fähnriche dürfen ohne Befehl feuern, warpen usw. (sonst abhängig von Rang und Gefahr).");
            MaxCommandsPerTurn = Config.Bind("Crew", "MaxBefehleProAntwort", 2, "Obergrenze ausgeführter Befehle pro Antwort eines Offiziers.");
            EventsInPrompt = Config.Bind("Crew", "EreignisseImPrompt", 20, "Wie viele letzte Wahrnehmungen ein Offizier bei jeder Entscheidung sieht.");
            HistoryTurns = Config.Bind("Crew", "Gespraechsverlauf", 4, "Wie viele eigene letzte Wortwechsel im Kontext bleiben.");
            Language = Config.Bind("Crew", "Sprache", "Deutsch", "Sprache der Crew.");
            MessageSeconds = Config.Bind("Crew", "AnzeigedauerSek", 7f, "Wie lange Gesprochenes auf den Konsolen steht.");
            MuteBotVoiceLines = Config.Bind("Crew", "BotSprachzeilenStumm", true, "Vorgefertigte Sprachzeilen von Rao/Hoyle/T'nar unterdrücken (die KI-Charaktere sprechen selbst).");

            HumorEnabled = Config.Bind("Humor", "Aktiv", true, "Charaktere mit Humor erzählen in ruhigen Momenten Witze, die anderen reagieren nach ihren Charakterzügen.");
            LobbyBanter = Config.Bind("Humor", "InDerLobby", true, "Auch außerhalb von Missionen plaudern und scherzen (nur mit Sprachausgabe).");
            JokeIntervalMissionSec = Config.Bind("Humor", "WitzAbstandMissionSek", 240f, "Mindestabstand zwischen Witzen im Einsatz.");
            JokeIntervalLobbySec = Config.Bind("Humor", "WitzAbstandLobbySek", 120f, "Mindestabstand zwischen Witzen in der Lobby.");
            JokeMemory = Config.Bind("Humor", "WitzGedaechtnis", 5, "So viele zuletzt erzählte Witze werden gemerkt und nicht wiederholt.");
            QuietAfterReprimandMin = Config.Bind("Humor", "RuheNachErmahnungMin", 15, "So lange keine Witze, nachdem der Captain um Ruhe gebeten hat.");

            MaxLessons = Config.Bind("Lernen", "MaxLektionen", 12, "Lektionen je Charakter.");
            ChronicleMax = Config.Bind("Lernen", "ChronikEintraege", 15, "Einträge der Dienstchronik je Charakter.");
            NotesMax = Config.Bind("Lernen", "NotizenProMission", 10, "Notizen, die sich ein Charakter während einer Mission machen kann.");
            MinActiveSecForDebrief = Config.Bind("Lernen", "MinDienstzeitSek", 60, "Nachbesprechung und XP erst ab so viel Dienstzeit an einer Bot-Station.");
            DebriefLogChars = Config.Bind("Lernen", "ProtokollZeichen", 10000, "Maximale Länge des Missionsprotokolls in der Nachbesprechung.");

            TtsEnabled = Config.Bind("Stimme", "Aktiv", true, "Crew spricht mit eigenen Stimmen. false = nur Text auf den Konsolen.");
            TtsEngine = Config.Bind("Stimme", "Engine", "qwen", "qwen = Qwen3-TTS (hochwertig, entworfene Stimme je Charakter, GPU), piper = Piper (schnell, CPU, einfacher Klang).");
            QwenServerExe = Config.Bind("Stimme", "QwenServerExe", "qwentts/tts-server.exe", "qwentts.cpp-Server.");
            QwenTtsExe = Config.Bind("Stimme", "QwenTtsExe", "qwentts/qwen-tts.exe", "qwentts.cpp-Kommandozeile (für den Stimmentwurf).");
            QwenBaseModel = Config.Bind("Stimme", "QwenBaseModell", "qwentts/models/qwen-talker-1.7b-base-Q4_K_M.gguf", "Qwen3-TTS-Base-Modell (Sprachausgabe mit geklonter Stimme). Q4_K_M spart gegenüber Q8_0 ca. 0,8 GB VRAM bei gleicher Verständlichkeit.");
            QwenDesignModel = Config.Bind("Stimme", "QwenDesignModell", "qwentts/models/qwen-talker-1.7b-voicedesign-Q8_0.gguf", "Qwen3-TTS-VoiceDesign-Modell (Stimmentwurf aus Beschreibung).");
            QwenCodec = Config.Bind("Stimme", "QwenCodec", "qwentts/models/qwen-tokenizer-12hz-Q8_0.gguf", "Qwen3-TTS-Tokenizer.");
            QwenPort = Config.Bind("Stimme", "QwenPort", 8091, "Lokaler Port des tts-server.");
            QwenMaxPitchHz = Config.Bind("Stimme", "MaxTonhoeheHz", 260f, "Piepskontrolle: Entworfene Stimmen über dieser Grundfrequenz werden tiefer neu entworfen (0 = aus).");
            QwenLanguage = Config.Bind("Stimme", "QwenSprache", "german", "Sprache für Qwen3-TTS (german, english, …).");
            PiperExe = Config.Bind("Stimme", "PiperExe", "piper/piper/piper.exe", "Pfad zu piper.exe (relativ zum Plugin-Ordner oder absolut).");
            PiperVoiceDir = Config.Bind("Stimme", "StimmenOrdner", "piper/voices", "Ordner mit Piper-Stimmen (*.onnx + *.onnx.json).");
            SpeakerDevice = Config.Bind("Stimme", "Lautsprecher", "", "Leer lassen = dasselbe Ausgabegerät wie das Spiel (Oculus-Einstellung bzw. Windows-Standard). Nur für Sonderfälle: Teil eines Gerätenamens aus dem Log.");
            SpeakerVolume = Config.Bind("Stimme", "LautstaerkeFaktor", 1f, "Faktor auf die Lautstärke aus dem Optionsmenü des Spiels (Master × Sprachchat). 1 = genau wie Mitspieler.");
            GameMicrophone = Config.Bind("Sprache", "SpielMikrofon", "", "Mikrofon für Sprachchat und Mikrofontest des Spiels (Teil des Gerätenamens, z. B. Plantronics). Leer = automatisch: ohne Rift nie das stumme Oculus-Mikrofon.");
            Reflexes = Config.Bind("Crew", "Reflexe", true, "Routine im Code (Schilde hoch unter Beschuss, Energie auf Schilde bei Treffern, Reparaturen), immer gebunden an die Daueranweisungen des Captains.");
            QuickOrders = Config.Bind("Crew", "SofortBefehle", true, "Eindeutige Befehle des Captains (\"Schilde hoch\", \"volle Fahrt\", \"Warp\", \"Energie!\") ohne Sprachmodell sofort ausführen.");
            OwnMicrophone = Config.Bind("Sprache", "EigenesMikrofon", true, "Die Mod öffnet das Mikrofon selbst (winmm). Aus, falls das Spiel sonst kein Mikrofon bekommt (Mikrofontest im Mehrspieler).");
            Multiplayer = Config.Bind("Sprache", "Mehrspieler", true, "Mehrspieler (nur beim Host nötig): Crew hört die Mitspieler über den Sprachchat und spricht für alle hörbar über den Sprachchat.");
            NoProfanity = Config.Bind("Crew", "KeineKraftausdruecke", false, "Crew vermeidet Flüche und vulgäre Wörter (auch beim Zitieren).");
            ListenToGameVoice = Config.Bind("Sprache", "SpielstimmenMithoeren", true, "Die Crew hört Briefing, Funksprüche und Logbuch des Spiels mit (Whisper hört die abgespielten Aufnahmen ab).");
            GameVoiceLanguage = Config.Bind("Sprache", "SpielstimmenSprache", "auto", "Sprache der Sprachausgabe des Spiels (Ordnername in GeneratedSoundBanks: German, English(US), French(France)); auto = vom Spiel.");
            PauseBetweenLinesSec = Config.Bind("Stimme", "PauseZwischenSaetzenSek", 2.5f, "Pause nach jedem Satz der Crew, in der der Captain zu Wort kommt. Notfälle unterbrechen sofort.");
            StaleSpeechSec = Config.Bind("Stimme", "VerfallNachSek", 20f, "Sätze, die so lange auf ihren Einsatz warten mussten, werden verworfen (außer Notfällen).");
            SpeechTempo = Config.Bind("Stimme", "Sprechtempo", 1.25f, "Grundtempo der Qwen-Stimmen (1 = unverändert, 1.25 = 25 % schneller, Tonhöhe bleibt).");
            DeafWhileCrewSpeaks = Config.Bind("Stimme", "MikroTaubWennCrewSpricht", true, "Mikrofon ignorieren, solange die Crew spricht (verhindert, dass die Crew sich selbst als Captain hört).");

            KeyMute = Config.Bind("Tasten", "MikrofonStumm", KeyCode.F5, "Dauerhaftes Zuhören stumm schalten / wieder an.");
            KeyCrewPanel = Config.Bind("Tasten", "CrewQuartier", KeyCode.F6, "Crew-Quartier: Charaktere auswählen, anwerben, bearbeiten, entlassen.");
            KeyOverlay = Config.Bind("Tasten", "Overlay", KeyCode.F7, "Overlay ein/aus.");
            KeyTalk = Config.Bind("Tasten", "Sprechen", KeyCode.F8, "Texteingabe an die Crew öffnen.");
            KeyEndMission = Config.Bind("Tasten", "MissionBeenden", KeyCode.F9, "Mission für beendet erklären (Nachbesprechung).");
            KeyPushToTalk = Config.Bind("Tasten", "PushToTalk", KeyCode.F4, "Watson-Modus: gedrückt halten = Befehl an die Crew, ohne sie anzusprechen.");
            Mode = Config.Bind("Allgemein", "Modus", "watson", "watson = schnelle Sprachbefehle ohne Sprachmodell (Vosk, Original-Crew mit Spielstimmen), llm = KI-Crew mit Sprachmodell (Stand Tag LLM-Version).");
            ListenModeSetting = Config.Bind("Sprache", "ZuhoerModus", "beides", "Watson-Modus: beides = Ansprache (Steuer/Rao, Taktik/Hoyle, Technik/T'nar, Crew), Push-to-talk oder kurzer Befehl; ansprache = nur mit Anrede; ptt = nur mit Taste; dauernd = alles Erkannte.");
            WatsonBeeps = Config.Bind("Watson", "Bestaetigungstoene", true, "Kurzer Ton: verstanden (zwei hohe) / nicht verstanden (ein tiefer).");
            VoskModel = Config.Bind("Watson", "VoskModell", "vosk/vosk-model-small-de-0.15", "Deutsches Vosk-Modell (relativ zum Plugin-Ordner oder absolut). Die Vosk-DLLs liegen im Ordner vosk.");
            VoskModelEn = Config.Bind("Watson", "VoskModellEnglisch", "vosk/vosk-model-small-en-us-0.15", "Englisches Vosk-Modell (relativ zum Plugin-Ordner oder absolut).");
            CommandLanguage = Config.Bind("Sprache", "BefehlsSprache", "spiel", "Sprache der Sprachbefehle: spiel = wie das Spiel eingestellt ist, de, en, auto = Deutsch und Englisch gleichzeitig (doppelte CPU-Last, Sprache wird je Sprecher gelernt).");
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
            // BepInEx liest "\t", "\n", "\r" in Config-Texten als Steuerzeichen ("qwentts\tts-server" wird zu einem Tabulator).
            // In Pfaden kommen solche Zeichen nie vor, also zurückverwandeln.
            p = p.Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r").Trim();
            return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(baseDir, p));
        }

        void Update()
        {
            if (!_active) return;
            MainThread.Drain(e => Log.LogError(e));
            EventLog.Update();
            if (_watson) { UpdateWatson(); return; }

            // Mehrspieler: Die Crew gehört dem Host (Master Client); bei allen anderen bleibt sie aus
            bool host = Net.IsHost;
            if (host != _wasHost) { _wasHost = host; Log.LogInfo(host ? "Host: KI-Crew aktiv." : "Nicht Host: KI-Crew bleibt aus (die Crew läuft beim Host)."); }
            bool inMission = GameBridge.InMission && host;
            if (inMission && !_wasInMission) { _status = null; _brain.StartMission(); }
            else if (!inMission && _wasInMission) _brain.EndMission("Mission verlassen");
            _wasInMission = inMission;

            if (inMission && Time.time >= _nextStatusPoll)
            {
                _nextStatusPoll = Time.time + 1f;
                var now = ShipStatus.Read();
                ShipStatus.ReportChanges(_status, now);
                if (now != null) _status = now;
                if (now != null && now.ContactList != null) VoiceInput.ContactNames = Contacts.Names(now.ContactList);
            }

            if (_server.State == ServerState.Ready && host) _brain.Tick(_status);
            GameMicFix.Probe();
            if (_remoteTap != null) _remoteTap.Tick();
            if (_crewSender != null) _crewSender.Tick();
            if (_voice != null && !_voiceWarmed && _sttServer.State == ServerState.Ready)
            {
                _voiceWarmed = true;
                _voice.WarmUp();
            }

            if (!_typing && !_panel.CapturesKeyboard)
            {
                if (Input.GetKeyDown(KeyMute.Value) && _voice != null)
                {
                    _voice.Muted = !_voice.Muted;
                    Toast(_voice.Muted ? "Mikrofon stumm." : "Mikrofon hört wieder zu.");
                }
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
            if (!_active) return;
            if (_watson) { DrawWatson(); return; }
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
            if (_voice != null)
            {
                string stt = _sttServer.State == ServerState.Failed ? "Fehler: " + _sttServer.Error : _voice.State;
                string bar = new string('|', Mathf.Clamp(Mathf.RoundToInt((_voice.LevelDb + 70f) / 3f), 0, 20));
                GUILayout.Label("Mikro (" + _voice.Device + "): " + stt + "  " + bar + (_voice.Speaking ? " ●" : ""));
            }
            if (_player != null)
            {
                string engine = _qwen != null ? "Qwen3-TTS (" + (_qwen.State == ServerState.Ready ? _qwen.Status : _qwen.State.ToString()) + ")" : "Piper";
                string err = _qwen != null && _qwen.Error != null ? _qwen.Error : _tts != null && _tts.Error != null ? _tts.Error : _player.Error;
                GUILayout.Label("Stimme: " + engine + " → " + _player.DeviceName + (_player.Playing ? "  ♪" : "") + (err != null ? "  Fehler: " + err : ""));
            }

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
                GUILayout.Label(KeyMute.Value + " Mikro stumm · " + KeyTalk.Value + " tippen · " + KeyCrewPanel.Value + " Crew-Quartier · " + KeyOverlay.Value + " Overlay · " +
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
            if (_shutDown || !_active) return;
            _shutDown = true;
            if (_brain != null && _brain.InMission) _brain.SaveForShutdown();
            if (_voice != null) _voice.Dispose();
            if (_remoteTap != null) _remoteTap.Dispose();
            foreach (var v in _vosk) v.Dispose();
            if (_player != null) _player.Dispose();
            if (_qwen != null) _qwen.Dispose();
            if (_tts != null) _tts.Dispose();
            if (_voListener != null) _voListener.Dispose();
            if (_sttServer != null) _sttServer.Dispose();
            if (_server != null) _server.Dispose();
        }
    }
}
