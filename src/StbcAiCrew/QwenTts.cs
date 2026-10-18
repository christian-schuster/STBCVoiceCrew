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
        void Speak(Character c, string text, Action<short[], int> done);
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
        public ServerState State => _server.State;

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

        /// <summary>Stimme im Hintergrund entwerfen (falls nötig). force = auch wenn schon eine existiert.</summary>
        public void Prepare(Character c, bool force)
        {
            if (string.IsNullOrEmpty(c.VoiceDesign)) return;
            lock (_designing)
            {
                if (_designing.Contains(c.Id)) return;
                if (!force && HasVoice(c)) return;
                _designing.Add(c.Id);
            }
            // Eigener Thread, damit ein Entwurf (einige Sekunden) die Sprachausgabe der anderen nicht aufhält.
            // Immer nur ein Entwurf gleichzeitig (VoiceDesign-Modell belegt kurz ca. 2 GB VRAM).
            var t = new Thread(() =>
            {
                lock (_designLock)
                {
                    try { Design(c); }
                    catch (Exception e) { Error = e.Message; _log.LogWarning("Stimmentwurf für " + c.Name + " fehlgeschlagen: " + e.Message); }
                    finally { lock (_designing) _designing.Remove(c.Id); }
                }
            }) { IsBackground = true, Name = "StbcAiCrew.VoiceDesign" };
            t.Start();
        }

        readonly object _designLock = new object();

        void Design(Character c)
        {
            Status = "entwirft Stimme für " + c.Name + " …";
            string station = c.Stations.Count > 0 ? OfficerAgent.StationDe(c.Stations[0]) : "Brücke";
            // Referenzsatz: natürlich, ruhig, mit typischen Lauten, damit der Klon später gut klingt
            string refText = "Hier spricht " + c.Name + " von der " + station + ". Alle Systeme arbeiten im normalen Bereich, Captain. " +
                             "Ich melde mich sofort, wenn sich daran etwas ändert.";
            string wav = RefWav(c), tmp = wav + ".tmp.wav";
            var started = DateTime.UtcNow;

            var psi = new ProcessStartInfo(_designExe,
                "--model \"" + _designModel + "\" --codec \"" + _codec + "\" --instruct \"" + c.VoiceDesign.Replace("\"", "'") +
                "\" --lang " + _language + " -o \"" + tmp + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_designExe)
            };
            var p = Process.Start(psi);
            JobObject.Add(p.Id);
            byte[] text = Encoding.UTF8.GetBytes(refText);
            p.StandardInput.BaseStream.Write(text, 0, text.Length);
            p.StandardInput.Close();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(180000);
            if (p.ExitCode != 0 || !File.Exists(tmp)) throw new Exception("qwen-tts Exit " + p.ExitCode + ": " + Tail(err));

            if (File.Exists(wav)) File.Delete(wav);
            File.Move(tmp, wav);
            File.WriteAllText(RefTxt(c), refText, new UTF8Encoding(false));
            File.WriteAllText(RefDesign(c), DesignKey(c), new UTF8Encoding(false));
            lock (_registered) _registered.Remove(c.Id);
            _log.LogInfo("Stimme für " + c.Name + " entworfen (" + (DateTime.UtcNow - started).TotalSeconds.ToString("0") + " s).");
            Status = "bereit";
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

        public void Speak(Character c, string text, Action<short[], int> done)
        {
            bool ready = _server.State == ServerState.Ready && HasVoice(c);
            if (!ready)
            {
                // Stimme entsteht noch oder Server lädt: diesmal Piper, die Stimme wird nebenbei vorbereitet
                Prepare(c, false);
                if (_fallback != null) _fallback.Speak(c, text, done);
                else MainThread.Post(() => done(null, 0));
                return;
            }
            Enqueue(() =>
            {
                short[] pcm = null;
                int rate = 0;
                try
                {
                    bool known;
                    lock (_registered) known = _registered.Contains(c.Id);
                    if (!known) Register(c);
                    string body = Json.Serialize(new Dictionary<string, object>
                    {
                        { "input", PiperTts.CleanForSpeech(text) },
                        { "voice", c.Id },
                        { "language", _language },
                        { "response_format", "wav" }
                    });
                    byte[] wav = PostBytes("/v1/audio/speech", body);
                    pcm = PiperTts.ParseWav(wav, out rate);
                    Error = null;
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    _log.LogWarning("Qwen-Sprachausgabe fehlgeschlagen (" + c.Name + "): " + e.Message);
                }
                if (pcm == null && _fallback != null) { _fallback.Speak(c, text, done); return; }
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
