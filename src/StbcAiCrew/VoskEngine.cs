using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Logging;

namespace StbcAiCrew.Voice
{
    /// <summary>
    /// Lokale Spracherkennung mit Vosk (CPU, kein Server). Je Äußerung laufen zwei Erkenner auf derselben Aufnahme:
    /// mit Befehlsgrammatik (schnell, keine Halluzinationen) und frei (für Eigennamen, die die Grammatik nicht kennt).
    /// Gemessen: 30–115 ms je Befehl mit Grammatik.
    /// </summary>
    public sealed class VoskEngine : IDisposable
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectory(string p);
        [DllImport("libvosk")] static extern void vosk_set_log_level(int l);
        [DllImport("libvosk")] static extern IntPtr vosk_model_new(byte[] path);
        [DllImport("libvosk")] static extern void vosk_model_free(IntPtr m);
        [DllImport("libvosk")] static extern IntPtr vosk_recognizer_new(IntPtr m, float rate);
        [DllImport("libvosk")] static extern IntPtr vosk_recognizer_new_grm(IntPtr m, float rate, byte[] grammar);
        [DllImport("libvosk")] static extern int vosk_recognizer_accept_waveform(IntPtr r, byte[] data, int len);
        [DllImport("libvosk")] static extern IntPtr vosk_recognizer_final_result(IntPtr r);
        [DllImport("libvosk")] static extern void vosk_recognizer_free(IntPtr r);

        readonly ManualLogSource _log;
        IntPtr _model;
        volatile string _grammar;
        readonly object _lock = new object();

        public bool Ready => _model != IntPtr.Zero;
        public string Error { get; private set; }

        public VoskEngine(ManualLogSource log, string libDir, string modelDir)
        {
            _log = log;
            try
            {
                if (!File.Exists(Path.Combine(libDir, "libvosk.dll"))) throw new FileNotFoundException("libvosk.dll fehlt in " + libDir);
                if (!Directory.Exists(modelDir)) throw new DirectoryNotFoundException("Vosk-Modell fehlt: " + modelDir);
                SetDllDirectory(libDir);
                vosk_set_log_level(-1);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _model = vosk_model_new(U(modelDir));
                if (_model == IntPtr.Zero) throw new Exception("Modell nicht ladbar: " + modelDir);
                _log.LogInfo("Vosk bereit (" + sw.ElapsedMilliseconds + " ms): " + modelDir);
            }
            catch (Exception e) { Error = e.Message; _log.LogError("Vosk nicht verfügbar: " + e.Message); }
        }

        /// <summary>Befehlswortschatz plus aktuelle Kontaktnamen (unbekannte Wörter ignoriert Vosk).</summary>
        public void SetVocabulary(IEnumerable<string> words)
        {
            var set = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (var w in words)
                foreach (var part in (w ?? "").ToLowerInvariant().Split(new[] { ' ', '-', ',', '.', '!', '?', ':' }, StringSplitOptions.RemoveEmptyEntries))
                    if (part.Length > 0 && part.IndexOf('"') < 0) set[part] = true;
            var sb = new StringBuilder("[");
            foreach (var w in set.Keys) sb.Append('"').Append(w).Append("\",");
            sb.Append("\"[unk]\"]");
            _grammar = sb.ToString();
        }

        /// <summary>16-kHz-Mono-PCM → (mit Grammatik, frei). Leere Texte, wenn nichts erkannt.</summary>
        public KeyValuePair<string, string> Recognize(short[] pcm, int rate = 16000)
        {
            if (!Ready || pcm == null || pcm.Length == 0) return new KeyValuePair<string, string>("", "");
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            string g = _grammar;
            string withGrammar = g != null ? Run(vosk_recognizer_new_grm(_model, rate, U(g)), bytes) : "";
            string free = Run(vosk_recognizer_new(_model, rate), bytes);
            return new KeyValuePair<string, string>(withGrammar, free);
        }

        static string Run(IntPtr r, byte[] data)
        {
            if (r == IntPtr.Zero) return "";
            try
            {
                vosk_recognizer_accept_waveform(r, data, data.Length);
                return TextOf(S(vosk_recognizer_final_result(r)));
            }
            finally { vosk_recognizer_free(r); }
        }

        /// <summary>{"text" : "…"} → Text, ohne JSON-Bibliothek; "[unk]" (unbekannt) entfällt.</summary>
        static string TextOf(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            int k = json.IndexOf("\"text\"", StringComparison.Ordinal);
            if (k < 0) return "";
            int a = json.IndexOf('"', json.IndexOf(':', k) + 1);
            int b = a >= 0 ? json.IndexOf('"', a + 1) : -1;
            if (a < 0 || b < 0) return "";
            return json.Substring(a + 1, b - a - 1).Replace("[unk]", "").Trim();
        }

        static byte[] U(string s) => Encoding.UTF8.GetBytes(s + "\0");

        static string S(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public void Dispose()
        {
            if (_model != IntPtr.Zero) { vosk_model_free(_model); _model = IntPtr.Zero; }
        }

        /// <summary>Befehlswortschatz für die Grammatik (aus den Regeln des Parsers und den Sprechweisen der Tests).</summary>
        public static readonly string[] CommandWords =
        {
            "steuer", "taktik", "technik", "helm", "navigation", "rao", "hoyle", "crew", "brücke", "alle", "stationen", "computer", "fähnrich", "lieutenant",
            "bitte", "jetzt", "los", "sofort", "ja", "nein", "okay", "gut", "danke", "und", "dann", "danach", "aber", "nicht", "kein", "keine", "keiner", "nur", "noch", "mal", "doch", "wieder", "erst", "wenn", "ich", "es", "sage", "uns", "wir", "dich", "mich",
            "volle", "voller", "volles", "halbe", "halber", "viertel", "fahrt", "kraft", "geschwindigkeit", "schub", "impuls", "impulse", "impulssprung", "vollgas", "schneller", "langsamer", "langsam", "stopp", "stop", "anhalten", "maschinen", "rückwärts", "voraus",
            "warp", "energie", "warpkern", "spulen", "laden", "lade", "vorbereiten", "aktivieren", "springen", "spring", "sprung", "ausführen", "starten",
            "flieg", "fliege", "fliegt", "fliegen", "fahr", "fahre", "bring", "bringe", "nimm", "kurs", "setze", "setz", "nach", "zu", "zum", "zur", "auf", "an", "in", "richtung", "näher", "nähere", "annähern", "ran", "anfliegen", "abfangen", "fang", "ab",
            "ziel", "neues", "markierte", "markierten", "markiere", "erfasse", "anvisieren", "wähle", "das", "die", "der", "den", "dem", "des", "ein", "eine", "einen",
            "schilde", "schild", "hoch", "runter", "senken", "heben", "oben", "unten", "lass", "lasse", "bleiben", "halten", "halte", "modulieren", "frequenz",
            "feuer", "feuern", "feuert", "frei", "einstellen", "phaser", "torpedo", "torpedos", "scharf", "entschärfen", "waffen", "schießen", "schiesst", "befehl", "meinen",
            "antrieb", "triebwerke", "scan", "scanne", "scannen", "analysieren", "leben", "lebenszeichen", "greif", "greift", "angreifen", "angriff",
            "roter", "alarm", "aufheben", "entwarnung", "beamen", "beame", "transporter", "transportieren", "überlebenden", "bord", "holen",
            "reparieren", "repariere", "reparatur", "offensive", "defensive", "ausgleichen", "gleichmäßig",
            "umfliege", "umfliegen", "um", "herum", "minenfeld", "minen", "mine", "anomalie", "nebel", "asteroidenfeld", "weiche", "weich", "aus", "ausweichen", "meide", "abstand", "distanz", "kilometer",
            "kreise", "kreis", "position", "flucht", "rückzug", "raus", "hier", "weg",
            "systeme", "system", "minimum", "minimal", "damit", "entdeckt", "werden", "gesehen", "unentdeckt", "unbemerkt", "schleichen", "schleichfahrt", "leise", "tarnung", "signatur", "verringern",
            "bleib", "bleibe", "außerhalb", "gegnerischen", "feindlichen", "sensoren", "ortung", "reichweite", "feind", "gegner",
            "prozent", "null", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn", "zwanzig", "dreißig", "vierzig", "fünfzig", "hundert",
            "nach", "bedarf", "normale", "normal", "anweisung", "ruf", "kanal", "öffnen", "schirm", "sonde", "starten",
            "full", "speed", "half", "quarter", "stop", "reverse", "shields", "up", "down", "fire", "phasers", "torpedoes", "scan", "target", "engage", "red", "alert", "power", "to", "weapons", "engines"
        };
    }
}
