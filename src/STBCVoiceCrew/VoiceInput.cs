using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace StbcAiCrew
{
    /// <summary>
    /// Dauerhaftes Zuhören: Mikrofon → Sprachaktivitätserkennung (Pegel gegen gelernten Grundrauschpegel)
    /// → fertiger Satz als WAV → whisper-server → Text → Filter gegen Whisper-Halluzinationen.
    /// Erkannte Sätze landen über den MainThread-Dispatcher im Unity-Hauptthread.
    /// </summary>
    /// <summary>Einstellungen der Spracheingabe (aus der Config, unabhängig vom Plugin testbar).</summary>
    public sealed class VoiceSettings
    {
        /// <summary>Nur für Sonderfälle: Teil des Gerätenamens. Leer = dasselbe Mikrofon wie das Spiel.</summary>
        public string Device = "";
        /// <summary>Mikrofon des Spiels (Oculus-Endpunkt-GUID, leer = Windows-Standard).</summary>
        public Func<string> GameDeviceId = () => "";
        /// <summary>Im Spiel gewähltes Mikrofon (Unity-Name); hat Vorrang vor Oculus/Windows-Standard.</summary>
        public Func<string> GameDeviceName = () => null;
        public string Language = "de";
        public float MarginDb = 12f;
        public float MinDb = -50f;
        public int EndSilenceMs = 700;
        public int MinSpeechMs = 350;
    }

    public sealed class VoiceInput : IDisposable
    {
        const int FrameSamples = 320;   // 20 ms
        const int FrameMs = 20;

        readonly ManualLogSource _log;
        readonly VoiceSettings _cfg;
        readonly string _sttUrl;
        readonly Action<string> _onText;
        MicCapture _mic;

        // VAD-Zustand (nur auf dem Mikrofon-Thread)
        readonly short[] _frame = new short[FrameSamples];
        int _frameFill;
        float _noiseDb = -60f;
        bool _speaking;
        int _speechRun, _silenceRun, _speechFrames;
        readonly Queue<short[]> _preroll = new Queue<short[]>();
        readonly List<short[]> _utterance = new List<short[]>();

        // STT-Warteschlange
        readonly Queue<short[]> _pending = new Queue<short[]>();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        Thread _sttThread;
        volatile bool _running;

        public volatile bool Muted;
        /// <summary>Eigener Erkenner (z. B. Vosk) statt Whisper per HTTP; null = Whisper.</summary>
        public Func<short[], string> Transcriber;
        /// <summary>Push-to-talk-Taste gedrückt (vom Hauptthread gesetzt).</summary>
        public volatile bool PttDown;
        bool _pttSeen;
        readonly Queue<bool> _pendingPtt = new Queue<bool>();
        /// <summary>Wurde während der zuletzt gemeldeten Äußerung Push-to-talk gedrückt?</summary>
        public bool LastPtt { get; private set; }
        long _deafUntilTicks;

        /// <summary>Mikrofon für n Sekunden ignorieren, solange die Crew spricht (sonst hört es sie als Captain).</summary>
        public void Deafen(float seconds)
        {
            Interlocked.Exchange(ref _deafUntilTicks, DateTime.UtcNow.AddSeconds(seconds).Ticks);
        }

        bool Deaf => DateTime.UtcNow.Ticks < Interlocked.Read(ref _deafUntilTicks);
        public volatile bool SttReady;
        public float LevelDb { get; private set; } = -90f;
        public float NoiseDb => _noiseDb;
        public bool Speaking => _speaking;
        public volatile string LastHeard = "";
        public volatile string State = "aus";
        static string CrewSpeaking => Loc.T("Crew spricht …", "crew speaking …");
        public string Device => _mic != null ? _mic.DeviceName : "–";

        public VoiceInput(ManualLogSource log, VoiceSettings cfg, string sttBaseUrl, Action<string> onText)
        {
            _log = log;
            _cfg = cfg;
            _sttUrl = sttBaseUrl.TrimEnd('/') + "/inference";
            _onText = onText;
        }

        public void Start()
        {
            _running = true;
            _sttThread = new Thread(SttLoop) { IsBackground = true, Name = "StbcAiCrew.Stt" };
            _sttThread.Start();
            var watchdog = new Thread(MicWatchdog) { IsBackground = true, Name = "StbcAiCrew.MicWatchdog" };
            watchdog.Start();
        }

        /// <summary>Startet das Mikrofon und startet es neu, wenn es (wieder) auftaucht oder abreißt.</summary>
        void MicWatchdog()
        {
            string lastError = null;
            while (_running)
            {
                var want = AudioDevices.ResolveInput(_cfg.Device, _cfg.GameDeviceId(), _cfg.GameDeviceName());
                bool switchDevice = _mic != null && _mic.Running && want.Valid && want.Id != _mic.DeviceId;
                if (switchDevice)
                {
                    _log.LogInfo("Mikrofon wechselt zu " + want.Name + " (" + want.Source + ").");
                    _mic.Dispose();
                }
                if (_mic == null || !_mic.Running)
                {
                    if (_mic != null && _mic.Error != null && _mic.Error != lastError) _log.LogWarning("Mikrofon weg: " + _mic.Error);
                    var mic = new MicCapture(_log, OnSamples);
                    if (mic.Start(want))
                    {
                        lastError = null;
                        if (SttReady) State = Muted ? Loc.T("stumm", "muted") : Loc.T("hört zu", "listening");
                    }
                    else
                    {
                        if (mic.Error != lastError) _log.LogWarning("Mikrofon nicht verfügbar: " + mic.Error + " – neuer Versuch alle 5 s.");
                        lastError = mic.Error;
                        State = Loc.T("kein Mikrofon (", "no microphone (") + mic.Error + ")";
                    }
                    _mic = mic;
                }
                Thread.Sleep(5000);
            }
        }
        // ---------------- Sprachaktivität ----------------

        /// <summary>Audio einspeisen (vom Mikrofon-Thread; öffentlich für Tests).</summary>
        public void Feed(short[] buf, int count) => OnSamples(buf, count);

        void OnSamples(short[] buf, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _frame[_frameFill++] = buf[i];
                if (_frameFill == FrameSamples)
                {
                    _frameFill = 0;
                    ProcessFrame();
                }
            }
        }

        void ProcessFrame()
        {
            double sum = 0;
            for (int i = 0; i < FrameSamples; i++) sum += (double)_frame[i] * _frame[i];
            float rms = (float)Math.Sqrt(sum / FrameSamples);
            float db = rms > 0 ? 20f * (float)Math.Log10(rms / 32768f) : -90f;
            LevelDb = db;

            var copy = (short[])_frame.Clone();
            if (Muted || !SttReady || Deaf)
            {
                ResetUtterance();
                State = Muted ? Loc.T("stumm", "muted") : !SttReady ? Loc.T("Spracherkennung startet …", "speech recognition starting …") : CrewSpeaking;
                return;
            }
            if (State == CrewSpeaking) State = Loc.T("hört zu", "listening");

            bool voiced = db > Math.Max(_noiseDb + _cfg.MarginDb, _cfg.MinDb);

            if (!_speaking)
            {
                // Grundrauschen langsam nachführen, nur außerhalb von Sprache
                if (!voiced) _noiseDb += (db - _noiseDb) * (db < _noiseDb ? 0.05f : 0.005f);
                _preroll.Enqueue(copy);
                while (_preroll.Count > 15) _preroll.Dequeue();   // 300 ms Vorlauf, damit der Wortanfang nicht fehlt
                _speechRun = voiced ? _speechRun + 1 : 0;
                if (_speechRun >= 3)
                {
                    _speaking = true;
                    if (PttDown) _pttSeen = true;
                    _utterance.AddRange(_preroll);
                    _preroll.Clear();
                    _speechFrames = _speechRun;
                    _silenceRun = 0;
                    State = Loc.T("hört …", "hearing …");
                }
                return;
            }

            _utterance.Add(copy);
            if (voiced) { _speechFrames++; _silenceRun = 0; }
            else _silenceRun++;

            bool endBySilence = _silenceRun * FrameMs >= _cfg.EndSilenceMs;
            bool tooLong = _utterance.Count * FrameMs >= 15000;
            if (endBySilence || tooLong)
            {
                if (_speechFrames * FrameMs >= _cfg.MinSpeechMs)
                {
                    int total = _utterance.Count * FrameSamples;
                    var pcm = new short[total];
                    for (int i = 0; i < _utterance.Count; i++) Array.Copy(_utterance[i], 0, pcm, i * FrameSamples, FrameSamples);
                    lock (_pending) { _pending.Enqueue(pcm); _pendingPtt.Enqueue(_pttSeen || PttDown); }
                    _signal.Set();
                    State = Loc.T("erkennt …", "recognizing …");
                }
                else State = Loc.T("hört zu", "listening");
                ResetUtterance();
            }
        }

        /// <summary>Vom Hauptthread je Frame: Taste während einer laufenden Äußerung gedrückt?</summary>
        public void NotePtt(bool down) { PttDown = down; if (down && _speaking) _pttSeen = true; }

        void ResetUtterance()
        {
            _speaking = false;
            _pttSeen = false;
            _utterance.Clear();
            _speechRun = _silenceRun = _speechFrames = 0;
        }

        // ---------------- Spracherkennung ----------------

        /// <summary>Erste Anfrage nach dem Start dauert lange (CUDA-Kernel werden übersetzt), deshalb vorab aufwärmen.</summary>
        public void WarmUp()
        {
            var t = new Thread(() =>
            {
                try
                {
                    var started = DateTime.UtcNow;
                    Transcribe(_sttUrl, new short[MicCapture.SampleRate], _cfg.Language);   // 1 s Stille
                    _log.LogInfo("Spracherkennung aufgewärmt (" + (DateTime.UtcNow - started).TotalSeconds.ToString("0.0") + " s).");
                }
                catch (Exception e) { _log.LogWarning("Aufwärmen der Spracherkennung fehlgeschlagen: " + e.Message); }
                SttReady = true;
                State = Muted ? Loc.T("stumm", "muted") : Loc.T("hört zu", "listening");
            }) { IsBackground = true, Name = "StbcAiCrew.SttWarmup" };
            t.Start();
        }

        void SttLoop()
        {
            while (_running)
            {
                _signal.WaitOne(500);
                while (true)
                {
                    short[] pcm;
                    bool ptt;
                    lock (_pending)
                    {
                        if (_pending.Count == 0) break;
                        pcm = _pending.Dequeue();
                        ptt = _pendingPtt.Count > 0 && _pendingPtt.Dequeue();
                    }
                    try
                    {
                        var started = DateTime.UtcNow;
                        string raw = Transcriber != null ? Transcriber(pcm) : Transcribe(_sttUrl, pcm, _cfg.Language);
                        string text = Clean(raw);
                        double secs = (DateTime.UtcNow - started).TotalSeconds;
                        if (text == null)
                        {
                            _log.LogDebug("Verworfen (Rauschen/Halluzination): '" + raw + "'");
                            if (!_speaking) State = Muted ? Loc.T("stumm", "muted") : Loc.T("hört zu", "listening");
                            continue;
                        }
                        LastHeard = text;
                        _log.LogInfo("Gehört (" + secs.ToString("0.00") + " s): " + text);
                        if (!_speaking) State = Muted ? Loc.T("stumm", "muted") : Loc.T("hört zu", "listening");
                        MainThread.Post(() => { LastPtt = ptt; _onText(text); });
                    }
                    catch (Exception e)
                    {
                        _log.LogWarning("Spracherkennung fehlgeschlagen: " + e.Message);
                        State = Loc.T("Fehler der Spracherkennung", "speech recognition error");
                    }
                }
            }
        }

        /// <summary>Brücken-Wortschatz als Whisper-Prompt (Namen der Crew werden ergänzt).</summary>
        public static string Vocabulary = "Brücke der Aegis. Warp, Impuls, Phaser, Photonentorpedos, Schilde, Rot-Alarm, Traktorstrahl, Transporter, Kurs setzen, Ziel erfassen, scannen, rufen. Flieg los! Energie! Bitte.";

        /// <summary>Namen der Kontakte, die die Konsolen gerade zeigen (z. B. "Ragusa"), damit Whisper sie richtig schreibt.</summary>
        public static volatile string ContactNames = "";

        public static string Transcribe(string sttUrl, short[] pcm, string language)
        {
            byte[] wav = Wav(pcm);
            string boundary = "----StbcAiCrew" + DateTime.UtcNow.Ticks.ToString("x");
            var body = new MemoryStream();
            void Field(string name, string value)
            {
                var s = "--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + name + "\"\r\n\r\n" + value + "\r\n";
                var b = Encoding.UTF8.GetBytes(s);
                body.Write(b, 0, b.Length);
            }
            Field("response_format", "json");
            Field("temperature", "0.0");
            Field("language", language);
            // Fachwörter vorgeben, sonst wird aus "Warp" "Varp" oder "war"
            string names = ContactNames;
            Field("prompt", string.IsNullOrEmpty(names) ? Vocabulary : Vocabulary + " Kontakte: " + names + ".");
            var head = Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"file\"; filename=\"rede.wav\"\r\nContent-Type: audio/wav\r\n\r\n");
            body.Write(head, 0, head.Length);
            body.Write(wav, 0, wav.Length);
            var tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            body.Write(tail, 0, tail.Length);

            var req = (HttpWebRequest)WebRequest.Create(sttUrl);
            req.Method = "POST";
            req.ContentType = "multipart/form-data; boundary=" + boundary;
            req.Proxy = null;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            byte[] payload = body.ToArray();
            req.ContentLength = payload.Length;
            using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return Json.Str(Json.Parse(r.ReadToEnd()), "text") ?? "";
        }

        static byte[] Wav(short[] pcm)
        {
            var ms = new MemoryStream(44 + pcm.Length * 2);
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + pcm.Length * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);                      // PCM
            w.Write((short)1);                      // Mono
            w.Write(MicCapture.SampleRate);
            w.Write(MicCapture.SampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(pcm.Length * 2);
            foreach (var s in pcm) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }

        // Whisper erfindet bei Rauschen gern Abspanntexte aus seinen Trainingsdaten (Fernsehuntertitel)
        static readonly string[] Hallucinations =
        {
            "untertitel", "zdf", "ard", "swr", "wdr", "funk", "amara.org", "copyright",
            "vielen dank fürs zuschauen", "danke fürs zuschauen", "danke für's zuschauen", "bis zum nächsten mal",
            "abonniert", "abonnieren", "thank you for watching", "subtitles", "untertitelung",
            "tschüss", "musik", "applaus", "vielen dank.", "danke."
        };

        /// <summary>Bereinigt die Erkennung; null = verwerfen.</summary>
        public static string Clean(string raw)
        {
            if (raw == null) return null;
            var sb = new StringBuilder();
            int depth = 0;
            foreach (char c in raw)
            {
                if (c == '[' || c == '(' || c == '*') { depth++; continue; }
                if ((c == ']' || c == ')' || c == '*') && depth > 0) { depth--; continue; }
                if (depth == 0) sb.Append(c == '\n' ? ' ' : c);
            }
            string text = sb.ToString().Trim();
            int letters = 0;
            foreach (char c in text) if (char.IsLetter(c)) letters++;
            if (letters < 2) return null;

            // Angehängte Floskeln, die Whisper bei Stille am Ende erfindet ("… Vielen Dank.")
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"(\s+(Vielen Dank( fürs Zuschauen)?|Danke( schön| fürs Zuschauen)?|Tschüss|Bis zum nächsten Mal)[.!]?)+\s*$", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (text.Length == 0) return null;
            // Halluzination in fremder Sprache bei Rauschen ("Hvað er að hljóða?"): Buchstaben, die es im Deutschen nicht gibt
            foreach (char c in text)
                if ("ðþæøåáíóúýœ".IndexOf(char.ToLowerInvariant(c)) >= 0 || (c > 0x024F && !(c >= 0x2000 && c <= 0x206F))) return null;

            string lower = text.ToLowerInvariant();
            foreach (var h in Hallucinations)
            {
                if (lower == h || lower.TrimEnd('.', '!', ' ') == h.TrimEnd('.'))
                    return null;
                // Lange Abspanntexte enthalten typische Stichworte
                if (h.Length > 6 && lower.Contains(h) && (h.Contains("untertitel") || h.Contains("zuschauen") || h.Contains("amara") || h.Contains("abonn")))
                    return null;
            }
            return text;
        }

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            if (_mic != null) _mic.Dispose();
            if (_sttThread != null) _sttThread.Join(1000);
        }
    }
}
