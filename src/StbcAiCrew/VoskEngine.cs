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
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string p);
        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        // Unitys Mono findet libvosk per DllImport nicht (auch nicht vorgeladen): Funktionen über ihre Adressen aufrufen.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetLogLevel(int l);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ModelNew(byte[] path);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Free(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr RecognizerNew(IntPtr m, float rate);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr RecognizerNewGrm(IntPtr m, float rate, byte[] grammar);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AcceptWaveform(IntPtr r, byte[] data, int len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FinalResult(IntPtr r);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetWords(IntPtr r, int on);

        static SetLogLevel vosk_set_log_level;
        static ModelNew vosk_model_new;
        static Free vosk_model_free, vosk_recognizer_free;
        static RecognizerNew vosk_recognizer_new;
        static RecognizerNewGrm vosk_recognizer_new_grm;
        static AcceptWaveform vosk_recognizer_accept_waveform;
        static FinalResult vosk_recognizer_final_result;
        static SetWords vosk_recognizer_set_words;

        static T Fn<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = GetProcAddress(lib, name);
            if (p == IntPtr.Zero) throw new Exception("Funktion " + name + " fehlt in libvosk.dll");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        static void Bind(IntPtr lib)
        {
            vosk_set_log_level = Fn<SetLogLevel>(lib, "vosk_set_log_level");
            vosk_model_new = Fn<ModelNew>(lib, "vosk_model_new");
            vosk_model_free = Fn<Free>(lib, "vosk_model_free");
            vosk_recognizer_new = Fn<RecognizerNew>(lib, "vosk_recognizer_new");
            vosk_recognizer_new_grm = Fn<RecognizerNewGrm>(lib, "vosk_recognizer_new_grm");
            vosk_recognizer_accept_waveform = Fn<AcceptWaveform>(lib, "vosk_recognizer_accept_waveform");
            vosk_recognizer_final_result = Fn<FinalResult>(lib, "vosk_recognizer_final_result");
            vosk_recognizer_free = Fn<Free>(lib, "vosk_recognizer_free");
            vosk_recognizer_set_words = Fn<SetWords>(lib, "vosk_recognizer_set_words");
        }

        readonly ManualLogSource _log;
        IntPtr _model;
        volatile string _grammar;
        readonly object _lock = new object();

        public bool Ready => _model != IntPtr.Zero;
        public string Error { get; private set; }

        /// <summary>Sprachkürzel ("de", "en") für Log und Auswahl.</summary>
        public readonly string Tag;

        public VoskEngine(ManualLogSource log, string libDir, string modelDir, string tag = "de")
        {
            _log = log;
            Tag = tag;
            try
            {
                if (!File.Exists(Path.Combine(libDir, "libvosk.dll"))) throw new FileNotFoundException("libvosk.dll fehlt in " + libDir);
                if (!Directory.Exists(modelDir)) throw new DirectoryNotFoundException("Vosk-Modell fehlt: " + modelDir);
                SetDllDirectory(libDir);
                // Mit vollem Pfad laden (erst die Abhängigkeiten), dann die Funktionen binden
                IntPtr lib = IntPtr.Zero;
                foreach (var dll in new[] { "libwinpthread-1.dll", "libgcc_s_seh-1.dll", "libstdc++-6.dll", "libvosk.dll" })
                    if ((lib = LoadLibrary(Path.Combine(libDir, dll))) == IntPtr.Zero)
                        throw new Exception(dll + " nicht ladbar (Fehler " + Marshal.GetLastWin32Error() + ")");
                Bind(lib);
                vosk_set_log_level(-1);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _model = vosk_model_new(U(modelDir));
                if (_model == IntPtr.Zero) throw new Exception("Modell nicht ladbar: " + modelDir);
                _log.LogInfo("Vosk " + tag + " bereit (" + sw.ElapsedMilliseconds + " ms): " + modelDir);
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

        public struct Result
        {
            public string Grammar, Free;
            /// <summary>Mittlere Wortsicherheit der Grammatik-Erkennung (0–1), 0 ohne Wörter.</summary>
            public float Confidence;
        }

        /// <summary>16-kHz-Mono-PCM → mit Grammatik, frei, Sicherheit. Leere Texte, wenn nichts erkannt.</summary>
        public Result Recognize(short[] pcm, int rate = 16000)
        {
            if (!Ready || pcm == null || pcm.Length == 0) return new Result { Grammar = "", Free = "" };
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            string g = _grammar;
            string withGrammar = "";
            float conf = 0f;
            if (g != null)
            {
                IntPtr rg = vosk_recognizer_new_grm(_model, rate, U(g));
                if (rg != IntPtr.Zero) vosk_recognizer_set_words(rg, 1);
                string json = RunJson(rg, bytes);
                withGrammar = TextOf(json);
                if (withGrammar.Length > 0) _log.LogInfo("Vosk-Sicherheit " + Tag + ": " + Confidences(json, out conf));
            }
            string free = Run(vosk_recognizer_new(_model, rate), bytes);
            return new Result { Grammar = withGrammar, Free = free, Confidence = conf };
        }

        static string Run(IntPtr r, byte[] data) => TextOf(RunJson(r, data));

        static string RunJson(IntPtr r, byte[] data)
        {
            if (r == IntPtr.Zero) return "";
            try
            {
                vosk_recognizer_accept_waveform(r, data, data.Length);
                return S(vosk_recognizer_final_result(r));
            }
            finally { vosk_recognizer_free(r); }
        }

        /// <summary>Wortweise Sicherheit aus dem Ergebnis ("wort 0.93 …"), zum Einstellen der Gerede-Sperre.</summary>
        static string Confidences(string json, out float mean)
        {
            var sb = new StringBuilder();
            double sum = 0; int n = 0;
            int i = 0;
            while ((i = json.IndexOf("\"conf\"", i, StringComparison.Ordinal)) >= 0)
            {
                int c = json.IndexOf(':', i) + 1;
                int ce = json.IndexOfAny(new[] { ',', '}', '\n' }, c);
                string conf = json.Substring(c, ce - c).Trim();
                int w = json.IndexOf("\"word\"", i, StringComparison.Ordinal);
                string word = "?";
                if (w >= 0) { int a = json.IndexOf('"', json.IndexOf(':', w) + 1); int b = json.IndexOf('"', a + 1); word = json.Substring(a + 1, b - a - 1); }
                double v;
                bool okv = double.TryParse(conf, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                if (okv) { sum += v; n++; }
                sb.Append(word).Append(' ').Append(okv ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : conf).Append("  ");
                i = ce;
            }
            mean = n > 0 ? (float)(sum / n) : 0f;
            return sb.ToString().Trim() + "  (Mittel " + mean.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")";
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
            "nummer", "eins", "erster", "offizier", "vorschlag", "empfehlung", "wegtreten", "kommando", "haben", "habe", "sie",   // First Officer
            "subsystem", "subsysteme", "system", "auswählen", "wähle", "wählen", "stören", "störe", "anvisieren", "visiere",
            "maximal", "maximale", "maximalen", "maximum", "minimum", "minimal", "minimale",
            "übernehmen", "übernimm", "übernehme", "hacken", "hacke", "kommunikation", "eindringen",
            "abschalten", "ausschalten", "deaktivieren", "aktivieren", "einschalten", "lahmlegen", "lahm", "legen",
            "steuer", "steuermann", "taktik", "technik", "helm", "navigation", "rao", "hoyle", "crew", "brücke", "alle", "stationen", "computer", "fähnrich", "lieutenant",
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

        /// <summary>Englischer Befehlswortschatz (Regeln aus CommandParserEn und Star-Trek-Wendungen).</summary>
        public static readonly string[] CommandWordsEn =
        {
            "number", "first", "suggestion", "recommendation", "dismissed", "have", "you",   // First Officer
            "helm", "conn", "navigation", "navigator", "ensign", "mister", "rao", "tactical", "weapons", "officer", "lieutenant", "hoyle", "engineering", "engineer", "chief", "tnar", "crew", "bridge", "everyone", "all", "stations", "computer",
            "please", "now", "ok", "okay", "alright", "right", "so", "just", "and", "then", "yes", "yeah", "no", "aye", "affirmative", "negative", "cancel", "belay", "that", "not", "don't", "do", "never", "only", "when", "i", "say", "my", "your", "our", "their", "us", "it", "them", "we", "they", "can't", "won't",
            "full", "half", "quarter", "one", "speed", "impulse", "ahead", "throttle", "maximum", "max", "top", "faster", "slower", "slow", "speed", "up", "down", "floor", "stop", "all", "halt", "come", "reverse", "back", "astern",
            "engage", "make", "so", "punch", "warp", "factor", "prepare", "for", "charge", "spin", "core", "coils", "drive", "online", "ready", "jump", "go", "take",
            "fly", "head", "move", "steer", "proceed", "get", "to", "toward", "towards", "over", "set", "lay", "in", "plot", "a", "an", "the", "this", "course", "approach", "close", "distance", "closer", "intercept", "away", "from", "evasive", "maneuvers", "break", "off", "veer",
            "shields", "shield", "raise", "lower", "drop", "on", "activate", "deactivate", "red", "alert", "modulate", "modulation", "remodulate", "keep", "leave", "stay",
            "fire", "firing", "cease", "shooting", "shoot", "hold", "at", "will", "open", "phaser", "phasers", "photon", "torpedo", "torpedoes", "arm", "load", "loaded", "armed", "disarm", "unload", "launch",
            "target", "targeting", "aim", "their", "engines", "propulsion", "weapon", "systems", "clear", "lock", "onto", "select", "mark", "marked", "new", "is",
            "scan", "scanning", "run", "life", "signs", "analyze", "analyse", "attack", "enemy", "hostile", "hostiles", "destroy", "out",
            "power", "more", "balance", "even", "distribute", "evenly", "repair", "fix", "everything", "beam", "energize", "transport", "survivors", "aboard", "board", "bring",
            "reduce", "minimize", "signature", "disrupt", "hack", "scramble", "control", "of", "override", "comms", "communications", "intrusion",
            "avoid", "around", "minefield", "mines", "mine", "anomaly", "nebula", "asteroid", "field", "route", "evade", "kilometers", "km", "clicks", "safe", "circle", "orbit", "position", "here", "retreat", "fall", "flee", "bug",
            "stealth", "mode", "silent", "running", "dark", "detected", "detect", "seen", "see", "noticed", "notice", "spotted", "undetected", "sneak", "end", "sensor", "sensors", "range", "detection", "outside",
            "percent", "zero", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "twenty", "thirty", "forty", "fifty", "hundred",
            "as", "were", "normal", "operations", "orders", "standing", "resume", "answer", "hail", "screen", "channel", "probe"
        };
    }
}
