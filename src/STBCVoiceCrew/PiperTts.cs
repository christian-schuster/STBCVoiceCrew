using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace STBCVoiceCrew
{
    /// <summary>Stimme eines Charakters: Piper-Modell, Sprecher, Tempo und Tonhöhe.</summary>
    public sealed class VoiceSpec
    {
        public string Model = "";     // Dateiname ohne .onnx, z. B. de_DE-thorsten-medium
        public int Speaker;           // bei Mehrsprecher-Modellen
        public float Speed = 1f;      // >1 schneller
        public float Pitch = 1f;      // >1 höher

        /// <summary>Format in der Charakterdatei: "modell" oder "modell#sprecher".</summary>
        public static VoiceSpec Parse(string voice, float speed, float pitch)
        {
            var v = new VoiceSpec { Speed = Clamp(speed, 0.6f, 1.6f, 1f), Pitch = Clamp(pitch, 0.75f, 1.3f, 1f) };
            if (string.IsNullOrEmpty(voice)) return v;
            int hash = voice.IndexOf('#');
            if (hash < 0) v.Model = voice.Trim();
            else
            {
                v.Model = voice.Substring(0, hash).Trim();
                int.TryParse(voice.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out v.Speaker);
            }
            return v;
        }

        static float Clamp(float v, float min, float max, float fallback) => v <= 0f ? fallback : Math.Max(min, Math.Min(max, v));
    }

    /// <summary>
    /// Sprachausgabe mit Piper (lokal, CPU). Je Stimme und Tempo läuft ein dauerhafter piper.exe-Prozess
    /// im JSON-Modus, damit das Modell nur einmal geladen wird (danach ca. 0,1 s pro Satz).
    /// Die Tonhöhe wird ohne Tempoänderung verschoben: Piper spricht um den Faktor langsamer, und die
    /// Wiedergabe wird um denselben Faktor beschleunigt.
    /// </summary>
    public sealed class PiperTts : ISpeechEngine
    {
        sealed class Job
        {
            public VoiceSpec Voice;
            public string Text;
            public Action<short[], int> Done;
        }

        sealed class Engine
        {
            public Process Proc;
            public StreamWriter In;
            public DateTime LastUse;
        }

        readonly ManualLogSource _log;
        readonly string _exe;
        readonly string _voiceDir;
        readonly string _tmpDir;
        readonly Dictionary<string, Engine> _engines = new Dictionary<string, Engine>();
        readonly Queue<Job> _jobs = new Queue<Job>();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        Thread _thread;
        volatile bool _running;

        public string Error { get; private set; }
        public bool Available => File.Exists(_exe) && Directory.Exists(_voiceDir);

        public PiperTts(ManualLogSource log, string exe, string voiceDir, string tmpDir)
        {
            _log = log;
            _exe = exe;
            _voiceDir = voiceDir;
            _tmpDir = tmpDir;
        }

        public void Start()
        {
            Directory.CreateDirectory(_tmpDir);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "STBCVoiceCrew.Tts" };
            _thread.Start();
        }

        /// <summary>Installierte Stimmen: Modellname → Anzahl Sprecher.</summary>
        public SortedDictionary<string, int> Voices()
        {
            var d = new SortedDictionary<string, int>();
            if (!Directory.Exists(_voiceDir)) return d;
            foreach (var f in Directory.GetFiles(_voiceDir, "*.onnx"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                int speakers = 1;
                try
                {
                    var cfg = Json.Parse(File.ReadAllText(f + ".json", Encoding.UTF8));
                    var n = Json.Get(cfg, "num_speakers");
                    if (n is double) speakers = Math.Max(1, (int)(double)n);
                }
                catch { }
                d[name] = speakers;
            }
            return d;
        }

        public void Speak(Character c, string text, string mood, Action<short[], int> done) => Speak(c.VoiceSpec, text, done);

        /// <summary>Erzeugt einen Satz; done(pcm, sampleRate) läuft im Unity-Hauptthread (pcm = null bei Fehler).</summary>
        public void Speak(VoiceSpec voice, string text, Action<short[], int> done)
        {
            lock (_jobs) _jobs.Enqueue(new Job { Voice = voice, Text = text, Done = done });
            _signal.Set();
        }

        void Loop()
        {
            while (_running)
            {
                _signal.WaitOne(1000);
                while (true)
                {
                    Job job;
                    lock (_jobs)
                    {
                        if (_jobs.Count == 0) break;
                        job = _jobs.Dequeue();
                    }
                    short[] pcm = null;
                    int rate = 0;
                    try { pcm = Synthesize(job.Voice, job.Text, out rate); Error = null; }
                    catch (Exception e)
                    {
                        Error = e.Message;
                        _log.LogWarning("Sprachausgabe fehlgeschlagen (" + job.Voice.Model + "): " + e.Message);
                    }
                    var j = job;
                    MainThread.Post(() => j.Done(pcm, rate));
                }
                CloseIdleEngines();
            }
        }

        short[] Synthesize(VoiceSpec v, string text, out int rate)
        {
            string model = Path.Combine(_voiceDir, v.Model + ".onnx");
            if (!File.Exists(model)) throw new FileNotFoundException("Stimme fehlt: " + v.Model);

            // Tonhöhe p: Piper spricht p-fach langsamer, Wiedergabe p-fach schneller → gleiches Tempo, höhere Stimme
            float lengthScale = v.Pitch / v.Speed;
            string key = v.Model + "|" + lengthScale.ToString("0.00", CultureInfo.InvariantCulture);
            var eng = GetEngine(key, model, lengthScale);

            string file = Path.Combine(_tmpDir, Guid.NewGuid().ToString("N") + ".wav");
            var req = new Dictionary<string, object> { { "text", CleanForSpeech(text) }, { "output_file", file } };
            if (v.Speaker > 0) req["speaker_id"] = v.Speaker;
            eng.In.WriteLine(Json.Serialize(req));
            string line = eng.Proc.StandardOutput.ReadLine();
            if (line == null)
            {
                KillEngine(key);
                throw new Exception("piper wurde beendet");
            }
            eng.LastUse = DateTime.UtcNow;

            byte[] wav = File.ReadAllBytes(file);
            try { File.Delete(file); } catch { }
            short[] pcm = ParseWav(wav, out rate);
            if (Math.Abs(v.Pitch - 1f) > 0.01f) pcm = Resample(pcm, v.Pitch);
            return pcm;
        }

        Engine GetEngine(string key, string model, float lengthScale)
        {
            Engine e;
            if (_engines.TryGetValue(key, out e) && !e.Proc.HasExited) return e;
            if (e != null) KillEngine(key);

            var psi = new ProcessStartInfo(_exe,
                "-m \"" + model + "\" --json-input -q --sentence_silence 0.15 --length_scale " +
                lengthScale.ToString("0.00", CultureInfo.InvariantCulture))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_exe)
            };
            var proc = Process.Start(psi);
            JobObject.Add(proc.Id);
            var errStream = proc.StandardError.BaseStream;   // stderr leeren (Unity gibt sonst ungültige Kanäle mit)
            new Thread(() => { try { var b = new byte[4096]; while (errStream.Read(b, 0, b.Length) > 0) { } } catch { } }) { IsBackground = true }.Start();
            e = new Engine
            {
                Proc = proc,
                In = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true },
                LastUse = DateTime.UtcNow
            };
            _engines[key] = e;
            _log.LogInfo("Stimme geladen: " + key);
            return e;
        }

        void CloseIdleEngines()
        {
            var idle = new List<string>();
            foreach (var kv in _engines)
                if ((DateTime.UtcNow - kv.Value.LastUse).TotalMinutes > 10) idle.Add(kv.Key);
            foreach (var k in idle) KillEngine(k);
        }

        void KillEngine(string key)
        {
            Engine e;
            if (!_engines.TryGetValue(key, out e)) return;
            _engines.Remove(key);
            try { e.In.Close(); } catch { }
            try { if (!e.Proc.HasExited) e.Proc.Kill(); } catch { }
        }

        public static string CleanForSpeech(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '*' || c == '_' || c == '#' || c == '"' || c == '„' || c == '“') continue;
                sb.Append(c == '\n' ? ' ' : c);
            }
            string s = " " + sb.ToString().Replace("%", " Prozent") + " ";
            // Aussprache wie in der deutschen Synchronisation (die deutsche Sprachsynthese liest sonst "Kaptein")
            foreach (var r in Pronunciations) s = ReplaceWord(s, r[0], r[1]);
            return s.Trim();
        }

        static readonly string[][] Pronunciations =
        {
            new[] { "Lt. Cmdr.", "Lutenant Kommander" }, new[] { "Lt. j.g.", "Lutenant" }, new[] { "Lt.", "Lutenant" },
            new[] { "Cmdr.", "Kommander" }, new[] { "Captain", "Käptn" }, new[] { "Captains", "Käptns" },
            new[] { "Lieutenant", "Lutenant" }, new[] { "Commander", "Kommander" }, new[] { "Junior Grade", "Dschunior Grejd" },
            new[] { "T'nar", "Tnar" }, new[] { "U.S.S.", "U S S" }, new[] { "Aye", "Ei" }, new[] { "Bird of Prey", "Börd of Prej" },
        };

        static string ReplaceWord(string s, string word, string with)
        {
            int i = 0;
            while ((i = s.IndexOf(word, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool startOk = i == 0 || !char.IsLetter(s[i - 1]);
                int end = i + word.Length;
                bool endOk = end >= s.Length || !char.IsLetter(s[end]) || word.EndsWith(".");
                if (startOk && endOk) { s = s.Substring(0, i) + with + s.Substring(end); i += with.Length; }
                else i = end;
            }
            return s;
        }

        public static short[] ParseWav(byte[] wav, out int rate)
        {
            rate = 22050;
            int pos = 12;
            int channels = 1;
            while (pos + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, pos, 4);
                int size = BitConverter.ToInt32(wav, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(wav, pos + 10);
                    rate = BitConverter.ToInt32(wav, pos + 12);
                }
                else if (id == "data")
                {
                    int bytes = Math.Min(size, wav.Length - pos - 8);
                    var pcm = new short[bytes / 2 / Math.Max(1, channels)];
                    for (int i = 0; i < pcm.Length; i++) pcm[i] = BitConverter.ToInt16(wav, pos + 8 + i * 2 * channels);
                    return pcm;
                }
                pos += 8 + size + (size & 1);
            }
            throw new FormatException("WAV ohne data-Block");
        }

        /// <summary>Lineare Neuabtastung: factor > 1 macht kürzer und höher.</summary>
        static short[] Resample(short[] src, float factor)
        {
            int n = (int)(src.Length / factor);
            var dst = new short[n];
            for (int i = 0; i < n; i++)
            {
                float p = i * factor;
                int a = (int)p;
                int b = Math.Min(a + 1, src.Length - 1);
                float t = p - a;
                dst[i] = (short)(src[a] * (1f - t) + src[b] * t);
            }
            return dst;
        }

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            if (_thread != null) _thread.Join(1000);
            foreach (var k in new List<string>(_engines.Keys)) KillEngine(k);
        }
    }
}
