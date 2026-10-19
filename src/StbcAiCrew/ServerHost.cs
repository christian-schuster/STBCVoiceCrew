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
                if (!string.IsNullOrEmpty(_exePath))
                {
                    if (!File.Exists(_exePath))
                        throw new FileNotFoundException(_name + " nicht gefunden: " + _exePath);

                    // Ausgaben selbst umleiten: Unity gibt Kindprozessen sonst ungültige stdout/stderr-Kanäle mit,
                    // und die Server brechen ab, sobald sie beim Laden etwas schreiben. Die Ausgabe landet in einer Log-Datei.
                    var psi = new ProcessStartInfo(_exePath, _arguments)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        WorkingDirectory = Path.GetDirectoryName(_exePath)
                    };
                    _log.LogInfo("Starte " + _name + ": \"" + _exePath + "\" " + _arguments);
                    _process = Process.Start(psi);
                    _started = DateTime.UtcNow;
                    JobObject.Add(_process.Id);
                    OpenOutputLog();
                    Drain(_process.StandardOutput.BaseStream, "out");
                    Drain(_process.StandardError.BaseStream, "err");
                }
                else
                {
                    _log.LogInfo("Kein Pfad für " + _name + " gesetzt – nutze bereits laufenden Server unter " + _baseUrl);
                }

                var deadline = DateTime.UtcNow.AddSeconds(_startupTimeoutSec);
                while (DateTime.UtcNow < deadline)
                {
                    if (_process != null && _process.HasExited && !StillRunning(_process.Id))
                        throw new Exception(_name + " wurde nach " + (DateTime.UtcNow - _started).TotalSeconds.ToString("0.0") +
                                            " s beendet (Exit-Code " + SafeExitCode() + "). Ausgabe: " + OutputLogPath);
                    if (IsHealthy())
                    {
                        _state = ServerState.Ready;
                        _log.LogInfo(_name + " bereit: " + _baseUrl);
                        return;
                    }
                    Thread.Sleep(1000);
                }
                throw new TimeoutException(_name + " nicht innerhalb von " + _startupTimeoutSec + " s bereit.");
            }
            catch (Exception e)
            {
                _error = e.Message;
                _state = ServerState.Failed;
                _log.LogError(_name + "-Start fehlgeschlagen: " + e);
            }
        }

        DateTime _started;
        StreamWriter _outLog;
        readonly object _outLock = new object();

        string OutputLogPath => Path.Combine(Path.GetDirectoryName(_exePath), _name + ".ausgabe.log");

        void OpenOutputLog()
        {
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
