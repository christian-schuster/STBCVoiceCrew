using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace StbcAiCrew
{
    /// <summary>Sprachausgabe für Charaktere (Qwen3-TTS oder Piper).</summary>
    public interface ISpeechEngine : IDisposable
    {
        /// <summary>done(pcm, sampleRate) im Unity-Hauptthread; pcm = null bei Fehler.</summary>
        void Speak(Character c, string text, string mood, Action<short[], int> done);
        string Error { get; }
    }

    /// <summary>
    /// Hochwertige Sprachausgabe mit Qwen3-TTS (qwentts.cpp, 24 kHz, GPU).
    /// Jeder Charakter bekommt einmalig eine Stimme aus seiner Stimmbeschreibung (VoiceDesign-Modell).
    /// Diese Referenzaufnahme wird gespeichert und beim dauerhaft laufenden tts-server registriert,
    /// der damit jede Zeile in derselben Stimme spricht (Stimmklon mit dem Base-Modell).
    /// Solange eine Stimme noch entsteht oder der Server nicht bereit ist, spricht Piper.
    /// </summary>
    public sealed class QwenTts : ISpeechEngine
    {
        readonly ManualLogSource _log;
        readonly ServerHost _server;
        readonly string _baseUrl;
        readonly string _designExe;
        readonly string _designModel;
        readonly string _codec;
        readonly string _voiceDir;
        readonly string _language;
        readonly ISpeechEngine _fallback;

        readonly Queue<Action> _jobs = new Queue<Action>();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        readonly HashSet<string> _registered = new HashSet<string>();
        readonly HashSet<string> _designing = new HashSet<string>();
        Thread _thread;
        volatile bool _running;

        public string Error { get; private set; }
        public string Status = "startet …";
        /// <summary>Stimmentwürfe nur, wenn das erlaubt ist (z. B. nicht im Einsatz: der Entwurf belegt kurz zusätzliches VRAM).</summary>
        public Func<bool> MayDesignNow = () => true;
        public ServerState State => _server.State;
        public ServerHost Host => _server;

        public QwenTts(ManualLogSource log, string serverExe, string designExe, string baseModel, string designModel,
                       string codec, int port, string voiceDir, string language, ISpeechEngine fallback)
        {
            _log = log;
            _baseUrl = "http://127.0.0.1:" + port;
            _designExe = designExe;
            _designModel = designModel;
            _codec = codec;
            _voiceDir = voiceDir;
            _language = language;
            _fallback = fallback;
            string args = "--model \"" + baseModel + "\" --codec \"" + codec + "\" --host 127.0.0.1 --port " + port + " --lang " + language;
            _server = new ServerHost(log, "tts-server", serverExe, args, _baseUrl, "/health", 180);
        }

        public static bool FilesPresent(string serverExe, string designExe, string baseModel, string designModel, string codec) =>
            File.Exists(serverExe) && File.Exists(designExe) && File.Exists(baseModel) && File.Exists(designModel) && File.Exists(codec);

        public void Start()
        {
            Directory.CreateDirectory(_voiceDir);
            _running = true;
            _server.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "StbcAiCrew.Qwen" };
            _thread.Start();
        }

        // ---------------- Stimmen vorbereiten ----------------

        string RefWav(Character c) => Path.Combine(_voiceDir, c.Id + ".wav");
        string RefTxt(Character c) => Path.Combine(_voiceDir, c.Id + ".txt");
        string RefDesign(Character c) => Path.Combine(_voiceDir, c.Id + ".design");

        /// <summary>Hat der Charakter eine fertige Stimme, die zur aktuellen Beschreibung passt?</summary>
        public bool HasVoice(Character c)
        {
            try
            {
                return File.Exists(RefWav(c)) && File.Exists(RefTxt(c)) && File.Exists(RefDesign(c)) &&
                       File.ReadAllText(RefDesign(c), Encoding.UTF8) == DesignKey(c);
            }
            catch { return false; }
        }

        static string DesignKey(Character c) => (c.VoiceDesign ?? "").Trim();

        string MoodWav(Character c, string m) => Path.Combine(_voiceDir, c.Id + "." + m + ".wav");
        string MoodTxt(Character c, string m) => Path.Combine(_voiceDir, c.Id + "." + m + ".txt");
        string MoodKeyFile(Character c, string m) => Path.Combine(_voiceDir, c.Id + "." + m + ".design");
        static string MoodKey(Character c, string m) => DesignKey(c) + "|" + Moods.ReferenceText(m, c);

        /// <summary>Gibt es eine Stimmungs-Referenz, die zur aktuellen Stimme und zum aktuellen Lachen passt?</summary>
        public bool HasMood(Character c, string m)
        {
            if (m == Moods.Neutral) return HasVoice(c);
            try
            {
                return File.Exists(MoodWav(c, m)) && File.Exists(MoodTxt(c, m)) && File.Exists(MoodKeyFile(c, m)) &&
                       File.ReadAllText(MoodKeyFile(c, m), Encoding.UTF8) == MoodKey(c, m);
            }
            catch { return false; }
        }

        bool HasAllMoods(Character c)
        {
            foreach (var m in Moods.All) if (!HasMood(c, m)) return false;
            return true;
        }

        /// <summary>
        /// Stimmungs-Referenzen erzeugen: die geklonte Stimme spricht einen typischen Satz der Stimmung.
        /// So bleibt die Klangfarbe gleich (Stimmentwurf mit Emotions-Zusatz würde die Stimme verfälschen).
        /// </summary>
        void EnsureMoods(Character c)
        {
            if (!HasVoice(c)) return;
            while (_running && _server.State != ServerState.Ready) Thread.Sleep(1000);
            foreach (var m in Moods.All)
            {
                if (m == Moods.Neutral || HasMood(c, m)) continue;
                Status = "Stimmung " + m + " für " + c.Name + " …";
                bool known;
                lock (_registered) known = _registered.Contains(c.Id);
                if (!known) Register(c);
                string text = Moods.ReferenceText(m, c);
                byte[] wav = PostBytes("/v1/audio/speech", Json.Serialize(new Dictionary<string, object>
                {
                    { "input", PiperTts.CleanForSpeech(text) }, { "voice", c.Id }, { "language", _language }, { "response_format", "wav" }
                }));
                File.WriteAllBytes(MoodWav(c, m), wav);
                File.WriteAllText(MoodTxt(c, m), PiperTts.CleanForSpeech(text), new UTF8Encoding(false));
                File.WriteAllText(MoodKeyFile(c, m), MoodKey(c, m), new UTF8Encoding(false));
                lock (_registered) _registered.Remove(c.Id + "~" + m);
            }
            Status = "bereit";
            _log.LogInfo("Stimmungen für " + c.Name + " bereit.");
        }

        void RegisterMood(Character c, string m)
        {
            string body = Json.Serialize(new Dictionary<string, object>
            {
                { "name", c.Id + "~" + m },
                { "ref_text", File.ReadAllText(MoodTxt(c, m), Encoding.UTF8) },
                { "wav_b64", Convert.ToBase64String(File.ReadAllBytes(MoodWav(c, m))) }
            });
            Post("/v1/audio/voices", body);
            lock (_registered) _registered.Add(c.Id + "~" + m);
        }

        /// <summary>Stimme im Hintergrund entwerfen (falls nötig). force = auch wenn schon eine existiert.</summary>
        public void Prepare(Character c, bool force)
        {
            if (string.IsNullOrEmpty(c.VoiceDesign)) return;
            lock (_designing)
            {
                if (_designing.Contains(c.Id)) return;
                if (!force && HasVoice(c) && HasAllMoods(c)) return;
                _designing.Add(c.Id);
            }
            // Eigener Thread, damit ein Entwurf (einige Sekunden) die Sprachausgabe der anderen nicht aufhält.
            // Immer nur ein Entwurf gleichzeitig (VoiceDesign-Modell belegt kurz ca. 2 GB VRAM).
            var t = new Thread(() =>
            {
                while (_running && !MayDesignNow()) Thread.Sleep(2000);   // erst nach der Mission
                if (!_running) return;
                lock (_designLock)
                {
                    try { if (force || !HasVoice(c)) Design(c); EnsureMoods(c); }
                    catch (Exception e) { Error = e.Message; _log.LogWarning("Stimmentwurf für " + c.Name + " fehlgeschlagen: " + e.Message); }
                    finally { lock (_designing) _designing.Remove(c.Id); }
                }
            }) { IsBackground = true, Name = "StbcAiCrew.VoiceDesign" };
            t.Start();
        }

        readonly object _designLock = new object();

        /// <summary>Höchste erlaubte Grundfrequenz eines Entwurfs; darüber wird neu entworfen (0 = aus).</summary>
        public float MaxPitchHz = 260f;

        void Design(Character c)
        {
            Status = "entwirft Stimme für " + c.Name + " …";
            string station = c.Stations.Count > 0 ? OfficerAgent.StationDe(c.Stations[0]) : "Brücke";
            // Referenzsatz: natürlich und ruhig, damit der Klon später gut klingt
            string refText = "Hier spricht " + c.Name.Replace("'", "") + " von der " + station + ". Alle Systeme arbeiten im normalen Bereich, Captain. " +
                             "Ich melde mich sofort, wenn sich daran etwas ändert.";
            string wav = RefWav(c);
            var started = DateTime.UtcNow;

            // Piepskontrolle: Tonhöhe messen; zu hohe Entwürfe mit dem Zusatz "nicht hoch" wiederholen, tiefsten behalten
            string best = null;
            float bestF0 = float.MaxValue;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string instruct = c.VoiceDesign.Trim();
                if (attempt > 0) instruct = instruct.TrimEnd('.') + ", not high-pitched, " + (attempt > 1 ? "clearly lower and fuller voice." : "lower voice.");
                string tmp = wav + ".try" + attempt + ".wav";
                RunDesign(instruct, refText, tmp);
                int rate;
                float f0 = VoiceAnalysis.MedianPitch(PiperTts.ParseWav(File.ReadAllBytes(tmp), out rate), rate);
                _log.LogInfo("Stimmentwurf " + c.Name + " Versuch " + (attempt + 1) + ": " + Mathf0(f0) + " Hz");
                if (f0 < bestF0)
                {
                    if (best != null) File.Delete(best);
                    best = tmp;
                    bestF0 = f0;
                }
                else File.Delete(tmp);
                if (MaxPitchHz <= 0f || f0 <= MaxPitchHz) break;
            }

            if (File.Exists(wav)) File.Delete(wav);
            File.Move(best, wav);
            File.WriteAllText(RefTxt(c), refText, new UTF8Encoding(false));
            File.WriteAllText(RefDesign(c), DesignKey(c), new UTF8Encoding(false));
            lock (_registered) _registered.Remove(c.Id);
            _log.LogInfo("Stimme für " + c.Name + " entworfen (" + Mathf0(bestF0) + " Hz, " + (DateTime.UtcNow - started).TotalSeconds.ToString("0") + " s).");
            Status = "bereit";
        }

        static string Mathf0(float f) => f >= float.MaxValue ? "?" : f.ToString("0");

        void RunDesign(string instruct, string refText, string outFile)
        {
            var psi = new ProcessStartInfo(_designExe,
                "--model \"" + _designModel + "\" --codec \"" + _codec + "\" --instruct \"" + instruct.Replace("\"", "'") +
                "\" --lang " + _language + " -o \"" + outFile + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(_designExe)
            };
            var p = Process.Start(psi);
            JobObject.Add(p.Id);
            var outStream = p.StandardOutput.BaseStream;   // stdout leeren, sonst kann eine volle Pipe blockieren
            new Thread(() => { try { var b = new byte[4096]; while (outStream.Read(b, 0, b.Length) > 0) { } } catch { } }) { IsBackground = true }.Start();
            byte[] text = Encoding.UTF8.GetBytes(refText);
            p.StandardInput.BaseStream.Write(text, 0, text.Length);
            p.StandardInput.Close();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(180000);
            if (p.ExitCode != 0 || !File.Exists(outFile)) throw new Exception("qwen-tts Exit " + p.ExitCode + ": " + Tail(err));
        }
        void Register(Character c)
        {
            string body = Json.Serialize(new Dictionary<string, object>
            {
                { "name", c.Id },
                { "ref_text", File.ReadAllText(RefTxt(c), Encoding.UTF8) },
                { "wav_b64", Convert.ToBase64String(File.ReadAllBytes(RefWav(c))) }
            });
            Post("/v1/audio/voices", body);
            lock (_registered) _registered.Add(c.Id);
            _log.LogInfo("Stimme registriert: " + c.Name);
        }

        // ---------------- Sprechen ----------------

        public void Speak(Character c, string text, string mood, Action<short[], int> done)
        {
            mood = Moods.Normalize(mood);
            bool ready = _server.State == ServerState.Ready && HasVoice(c);
            if (!ready)
            {
                // Stimme entsteht noch oder Server lädt: diesmal Piper, die Stimme wird nebenbei vorbereitet
                Prepare(c, false);
                if (_fallback != null) _fallback.Speak(c, text, mood, done);
                else MainThread.Post(() => done(null, 0));
                return;
            }
            Enqueue(() =>
            {
                short[] pcm = null;
                int rate = 0;
                try
                {
                    // Stimmung, wenn ihre Referenz fertig ist, sonst die neutrale Stimme
                    string voice = c.Id;
                    if (mood != Moods.Neutral && HasMood(c, mood))
                    {
                        bool knownMood;
                        lock (_registered) knownMood = _registered.Contains(c.Id + "~" + mood);
                        if (!knownMood) RegisterMood(c, mood);
                        voice = c.Id + "~" + mood;
                    }
                    else
                    {
                        bool known;
                        lock (_registered) known = _registered.Contains(c.Id);
                        if (!known) Register(c);
                    }
                    string body = Json.Serialize(new Dictionary<string, object>
                    {
                        { "input", PiperTts.CleanForSpeech(text) },
                        { "voice", voice },
                        { "language", _language },
                        { "response_format", "wav" }
                    });
                    byte[] wav = PostBytes("/v1/audio/speech", body);
                    pcm = PiperTts.ParseWav(wav, out rate);
                    // Qwen spricht recht gemächlich: Tempo anheben (ohne Tonhöhe) und Stille kappen
                    pcm = AudioTempo.Process(pcm, rate, AudioTempo.GlobalTempo * c.VoiceSpeed);
                    Error = null;
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    _log.LogWarning("Qwen-Sprachausgabe fehlgeschlagen (" + c.Name + "): " + e.Message);
                }
                if (pcm == null && _fallback != null) { _fallback.Speak(c, text, mood, done); return; }
                var p = pcm; var r = rate;
                MainThread.Post(() => done(p, r));
            });
        }

        // ---------------- Hilfen ----------------

        void Enqueue(Action a)
        {
            lock (_jobs) _jobs.Enqueue(a);
            _signal.Set();
        }

        void Loop()
        {
            while (_running)
            {
                _signal.WaitOne(1000);
                if (_server.State == ServerState.Ready && Status == "startet …") Status = "bereit";
                while (true)
                {
                    Action a;
                    lock (_jobs)
                    {
                        if (_jobs.Count == 0) break;
                        a = _jobs.Dequeue();
                    }
                    try { a(); } catch (Exception e) { _log.LogError(e); }
                }
            }
        }

        void Post(string path, string json) => PostBytes(path, json);

        byte[] PostBytes(string path, string json)
        {
            var req = (HttpWebRequest)WebRequest.Create(_baseUrl + path);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Proxy = null;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            byte[] b = Encoding.UTF8.GetBytes(json);
            req.ContentLength = b.Length;
            using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rs = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[65536];
                    int n;
                    while ((n = rs.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
            catch (WebException we)
            {
                string detail = "";
                try { if (we.Response != null) using (var r = new StreamReader(we.Response.GetResponseStream())) detail = r.ReadToEnd(); } catch { }
                throw new Exception(we.Message + " " + Tail(detail));
            }
        }

        static string Tail(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(s.Length - 300) : s).Trim();

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            if (_thread != null) _thread.Join(1000);
            _server.Dispose();
        }
    }
}
