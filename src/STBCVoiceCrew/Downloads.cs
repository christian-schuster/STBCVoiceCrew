using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Lädt große Dateien erst bei Bedarf (Sprachmodelle für Vosk, Piper und seine Stimmen), damit das Paket klein bleibt.
    /// Das Spiel-Mono kann kein aktuelles HTTPS; deshalb übernimmt das Windows-eigene curl.exe den Download und tar.exe
    /// das Entpacken (beide seit Windows 10 dabei). Läuft im Hintergrund; das Ergebnis meldet Poll() im Hauptthread.
    /// </summary>
    public static class Downloads
    {
        public sealed class Item
        {
            public string Name;          // für Anzeige und Log
            public string Url;
            public string TargetDir;     // hierhin entpacken bzw. speichern
            public string FileName;      // ohne Archiv: Dateiname im Ziel
            public bool Zip;
            public Action<bool> Done;    // im Hauptthread
            internal volatile int State; // 0 = läuft, 1 = fertig, 2 = Fehler
            internal string Error;
        }

        static readonly List<Item> _running = new List<Item>();
        static readonly HashSet<string> _started = new HashSet<string>();

        /// <summary>Läuft gerade ein Download? (für den Status im Overlay)</summary>
        public static string Status
        {
            get { lock (_running) return _running.Count == 0 ? null : Loc.T("Lade ", "Downloading ") + _running[0].Name + (_running.Count > 1 ? " (+" + (_running.Count - 1) + ")" : "") + " …"; }
        }

        /// <summary>Download starten (jede URL nur einmal je Spielstart). false = lief schon.</summary>
        public static bool Start(Item it)
        {
            lock (_running)
            {
                if (!_started.Add(it.Url)) return false;
                _running.Add(it);
            }
            Plugin.Log.LogInfo("Download: " + it.Name + " (" + it.Url + ")");
            var th = new Thread(() => Run(it)) { IsBackground = true, Name = "Download " + it.Name };
            th.Start();
            return true;
        }

        static void Run(Item it)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "StbcVoiceCrew_" + Guid.NewGuid().ToString("N") + (it.Zip ? ".zip" : ".part"));
            try
            {
                Directory.CreateDirectory(it.TargetDir);
                if (Exec("curl.exe", "-L --fail --silent --show-error --retry 2 -o \"" + tmp + "\" \"" + it.Url + "\"", 1800) != 0 || !File.Exists(tmp))
                    throw new Exception("curl konnte nicht laden");
                if (it.Zip)
                {
                    if (Exec("tar.exe", "-xf \"" + tmp + "\" -C \"" + it.TargetDir + "\"", 600) != 0) throw new Exception("tar konnte nicht entpacken");
                }
                else
                {
                    string dst = Path.Combine(it.TargetDir, it.FileName);
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(tmp, dst);
                }
                it.State = 1;
            }
            catch (Exception e) { it.Error = e.Message; it.State = 2; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        static int Exec(string exe, string args, int timeoutSec)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } return -1; }
                if (p.ExitCode != 0) Plugin.Log.LogWarning(exe + ": " + err.Trim());
                return p.ExitCode;
            }
        }

        /// <summary>Aus Update() (Hauptthread): fertige Downloads melden.</summary>
        public static void Poll()
        {
            List<Item> done = null;
            lock (_running)
                for (int i = _running.Count - 1; i >= 0; i--)
                    if (_running[i].State != 0) { (done = done ?? new List<Item>()).Add(_running[i]); _running.RemoveAt(i); }
            if (done == null) return;
            foreach (var it in done)
            {
                if (it.State == 1) Plugin.Log.LogInfo("Download fertig: " + it.Name);
                else Plugin.Log.LogWarning("Download fehlgeschlagen: " + it.Name + " – " + it.Error);
                try { if (it.Done != null) it.Done(it.State == 1); } catch (Exception e) { Plugin.Log.LogWarning("Nach Download: " + e.Message); }
            }
        }

        // ---------------- Bekannte Quellen ----------------

        /// <summary>Vosk-Sprachmodell (Ordnername wie im Archiv, z. B. vosk-model-small-de-0.15).</summary>
        public static Item VoskModel(string voskDir, string modelName, Action<bool> done) => new Item
        {
            Name = Loc.T("Sprachmodell ", "speech model ") + modelName, Zip = true, TargetDir = voskDir, Done = done,
            Url = "https://alphacephei.com/vosk/models/" + modelName + ".zip"
        };

        /// <summary>Piper (Windows, 64 Bit): entpackt den Ordner "piper" in piperDir.</summary>
        public static Item PiperRuntime(string piperDir, Action<bool> done) => new Item
        {
            Name = "Piper", Zip = true, TargetDir = piperDir, Done = done,
            Url = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip"
        };

        /// <summary>Piper-Stimme (z. B. de_DE-thorsten-medium): .onnx bzw. .onnx.json von huggingface.</summary>
        public static Item PiperVoiceFile(string voiceDir, string voice, string ext, Action<bool> done)
        {
            // de_DE-thorsten-medium → de/de_DE/thorsten/medium/de_DE-thorsten-medium.onnx
            var parts = voice.Split('-');
            string lang = parts[0], speaker = parts.Length > 1 ? parts[1] : "", quality = parts.Length > 2 ? parts[2] : "medium";
            return new Item
            {
                Name = Loc.T("Stimme ", "voice ") + voice + ext, Zip = false, TargetDir = voiceDir, FileName = voice + ext, Done = done,
                Url = "https://huggingface.co/rhasspy/piper-voices/resolve/main/" + lang.Substring(0, 2) + "/" + lang + "/" + speaker + "/" + quality + "/" + voice + ext
            };
        }
    }
}
