using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using BepInEx.Logging;

namespace StbcAiCrew
{
    public enum ServerState { Stopped, Starting, Ready, Failed }

    /// <summary>
    /// Startet einen lokalen KI-Server (llama-server.exe, whisper-server.exe) als Kindprozess des Spiels
    /// und beendet ihn wieder. Bewusst ein eigener Prozess statt In-Process: Ein CUDA-/Treiberfehler reißt
    /// so nicht das Spiel mit, und das Spiel (Mono 2.x) muss keine nativen Strukturen marshallen.
    /// Über ein Windows-Job-Objekt stirbt der Server automatisch mit, falls das Spiel abstürzt.
    /// </summary>
    public sealed class ServerHost : IDisposable
    {
        readonly ManualLogSource _log;
        readonly string _name;
        readonly string _healthPath;
        readonly string _exePath;
        readonly string _arguments;
        readonly string _baseUrl;
        readonly int _startupTimeoutSec;
        Process _process;
        volatile ServerState _state = ServerState.Stopped;
        volatile string _error;

        public ServerState State => _state;

        /// <summary>Wartezeit vor dem Start (das Spiel soll erst Grafik/VR hochfahren, bevor die GPU geladen wird).</summary>
        public float DelaySec;
        /// <summary>Erst starten, wenn dieser Server fertig ist (nacheinander statt alle gleichzeitig).</summary>
        public ServerHost After;
        /// <summary>Direkte Startversuche, bevor über WMI gestartet wird.</summary>
        public int Attempts = 3;
        public string Error => _error;
        public string BaseUrl => _baseUrl;

        public ServerHost(ManualLogSource log, string name, string exePath, string arguments, string baseUrl, string healthPath, int startupTimeoutSec)
        {
            _log = log;
            _name = name;
            _healthPath = healthPath;
            _exePath = exePath;
            _arguments = arguments;
            _baseUrl = baseUrl.TrimEnd('/');
            _startupTimeoutSec = startupTimeoutSec;
        }

        /// <summary>Startet den Server (falls exePath gesetzt) und wartet im Hintergrund auf /health.</summary>
        public void Start()
        {
            _state = ServerState.Starting;
            var t = new Thread(StartWorker) { IsBackground = true, Name = "StbcAiCrew.Start." + _name };
            t.Start();
        }

        void StartWorker()
        {
            try
            {
                if (string.IsNullOrEmpty(_exePath))
                {
                    _log.LogInfo("Kein Pfad für " + _name + " gesetzt – nutze bereits laufenden Server unter " + _baseUrl);
                    WaitUntilReady(null);
                    return;
                }
                if (!File.Exists(_exePath)) throw new FileNotFoundException(_name + " nicht gefunden: " + _exePath);

                // Nicht gleichzeitig mit dem Hochfahren des Spiels und nicht alle Server auf einmal laden:
                // Beim ersten Spieltest starben alle drei Server beim ersten GPU-Speicher, während das Spiel startete.
                if (DelaySec > 0f) Thread.Sleep((int)(DelaySec * 1000));
                var waitUntil = DateTime.UtcNow.AddSeconds(DelaySec + 300);
                while (After != null && (After.State == ServerState.Starting || After.State == ServerState.Stopped) && DateTime.UtcNow < waitUntil)
                    Thread.Sleep(500);

                // Stufe 1: direkter Kindprozess mit der Standard-Umgebung des Benutzers, mehrere Versuche
                string err = null;
                for (int attempt = 1; attempt <= Math.Max(1, Attempts); attempt++)
                {
                    StartDirect();
                    err = WaitUntilReady(_process);
                    if (err == null) return;
                    _log.LogWarning(_name + " Versuch " + attempt + ": " + err);
                    try { if (!_process.HasExited) _process.Kill(); } catch { }
                    Thread.Sleep(5000);
                }
                _log.LogWarning(_name + ": starte unabhängig vom Spiel über WMI neu.");

                // Stufe 2: über WMI starten. Der Server ist dann kein Kindprozess des Spiels mehr.
                StartDetached();
                err = WaitUntilReady(null);
                if (err == null) return;
                throw new Exception(err);
            }
            catch (Exception e)
            {
                _error = e.Message;
                _state = ServerState.Failed;
                _log.LogError(_name + "-Start fehlgeschlagen: " + e.Message);
            }
        }

        /// <summary>Wartet auf /health. null = bereit, sonst Fehlertext.</summary>
        string WaitUntilReady(Process p)
        {
            var deadline = DateTime.UtcNow.AddSeconds(_startupTimeoutSec);
            while (DateTime.UtcNow < deadline)
            {
                if (p != null && p.HasExited && !StillRunning(p.Id))
                    return _name + " wurde nach " + (DateTime.UtcNow - _started).TotalSeconds.ToString("0.0") +
                           " s beendet (Exit-Code " + SafeExitCode() + "). Ausgabe: " + OutputLogPath;
                if (IsHealthy())
                {
                    _state = ServerState.Ready;
                    _log.LogInfo(_name + " bereit: " + _baseUrl + (_detached ? " (über WMI gestartet)" : ""));
                    return null;
                }
                Thread.Sleep(1000);
            }
            return _name + " nicht innerhalb von " + _startupTimeoutSec + " s bereit. Ausgabe: " + OutputLogPath;
        }

        void StartDirect()
        {
            // Ausgaben selbst umleiten und in eine Log-Datei schreiben
            var psi = new ProcessStartInfo(_exePath, _arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(_exePath)
            };
            UserEnvironment.Apply(psi, _log);
            _log.LogInfo("Starte " + _name + ": \"" + _exePath + "\" " + _arguments);
            _process = Process.Start(psi);
            _started = DateTime.UtcNow;
            JobObject.Add(_process.Id);
            OpenOutputLog();
            Drain(_process.StandardOutput.BaseStream, "out");
            Drain(_process.StandardError.BaseStream, "err");
        }

        bool _detached;

        /// <summary>
        /// Start über WMI (Win32_Process.Create): Der Prozess hängt dann nicht am Spiel, erbt nichts von ihm und
        /// bekommt die normale Umgebung des Benutzers. Ausgabe per cmd-Umleitung in die Log-Datei.
        /// </summary>
        void StartDetached()
        {
            if (_outLog != null) { try { _outLog.Close(); } catch { } _outLog = null; }
            string cmdLine = "cmd.exe /c \"\"" + _exePath + "\" " + _arguments + " > \"" + OutputLogPath + "\" 2>&1\"";
            string ps = "$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = '" +
                        cmdLine.Replace("'", "''") + "'; CurrentDirectory = '" + Path.GetDirectoryName(_exePath).Replace("'", "''") +
                        "' }; [Console]::Out.Write([string]$r.ReturnValue + ' ' + [string]$r.ProcessId)";
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            var p = Process.Start(psi);
            string outText = p.StandardOutput.ReadToEnd();
            string errText = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            if (errText.Trim().Length > 0) _log.LogWarning(_name + ": WMI-Start meldet: " + errText.Trim());
            _detached = true;
            _started = DateTime.UtcNow;
            _log.LogInfo(_name + " über WMI gestartet (Rückgabe/PID: " + outText.Trim() + ").");
        }
        DateTime _started;
        StreamWriter _outLog;
        readonly object _outLock = new object();

        string OutputLogPath => Path.Combine(Path.GetDirectoryName(_exePath), _name + ".ausgabe.log");

        void OpenOutputLog()
        {
            if (_outLog != null) { try { _outLog.Close(); } catch { } _outLog = null; }
            try { _outLog = new StreamWriter(OutputLogPath, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true }; }
            catch (Exception e) { _log.LogWarning(_name + ": Ausgabe-Log nicht schreibbar: " + e.Message); }
        }

        /// <summary>Ausgabe auf eigenem Thread lesen (Mono 2.x: die asynchronen Process-Ereignisse sind unzuverlässig).</summary>
        void Drain(Stream s, string tag)
        {
            var t = new Thread(() =>
            {
                var buf = new byte[4096];
                try
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (_outLog == null) continue;
                        string text = System.Text.Encoding.UTF8.GetString(buf, 0, n);
                        lock (_outLock) _outLog.Write(text);
                    }
                }
                catch { }
            }) { IsBackground = true, Name = "StbcAiCrew." + _name + "." + tag };
            t.Start();
        }

        /// <summary>Über WMI gestartete Server haben keinen Process-Bezug: anhand des Programmpfads beenden.</summary>
        void KillByPath()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_exePath)))
                {
                    try
                    {
                        if (string.Equals(p.MainModule.FileName, _exePath, StringComparison.OrdinalIgnoreCase)) { p.Kill(); _log.LogInfo("Beende " + _name + " (PID " + p.Id + ")."); }
                    }
                    catch { }
                }
            }
            catch (Exception e) { _log.LogWarning(_name + " ließ sich nicht beenden: " + e.Message); }
        }

        static bool StillRunning(int pid)
        {
            try { var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch { return false; }
        }

        string SafeExitCode()
        {
            try { return _process.ExitCode.ToString(); } catch { return "?"; }
        }

        bool IsHealthy()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(_baseUrl + _healthPath);
                req.Timeout = 2000;
                req.Proxy = null;
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return resp.StatusCode == HttpStatusCode.OK;
            }
            catch (WebException)
            {
                // 503 = Modell lädt noch, Verbindungsfehler = Server noch nicht oben
                return false;
            }
        }

        public void Dispose()
        {
            if (_detached) KillByPath();
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _log.LogInfo("Beende " + _name + ".");
                    _process.Kill();
                    _process.WaitForExit(3000);
                }
            }
            catch (Exception e)
            {
                _log.LogWarning(_name + " ließ sich nicht sauber beenden: " + e.Message);
            }
            _state = ServerState.Stopped;
        }
    }
}
