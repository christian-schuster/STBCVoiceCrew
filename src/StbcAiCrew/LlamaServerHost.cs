using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Logging;

namespace StbcAiCrew
{
    public enum ServerState { Stopped, Starting, Ready, Failed }

    /// <summary>
    /// Startet llama.cpp (llama-server.exe) als Kindprozess des Spiels und beendet ihn wieder.
    /// Bewusst ein eigener Prozess statt In-Process: Ein CUDA-/Treiberfehler reißt so nicht das Spiel mit,
    /// und das Spiel (Mono 2.x) muss keine nativen llama.cpp-Strukturen marshallen.
    /// Über ein Windows-Job-Objekt stirbt der Server automatisch mit, falls das Spiel abstürzt.
    /// </summary>
    public sealed class LlamaServerHost : IDisposable
    {
        readonly ManualLogSource _log;
        readonly string _exePath;
        readonly string _arguments;
        readonly string _baseUrl;
        readonly int _startupTimeoutSec;
        Process _process;
        IntPtr _job = IntPtr.Zero;
        volatile ServerState _state = ServerState.Stopped;
        volatile string _error;

        public ServerState State => _state;
        public string Error => _error;
        public string BaseUrl => _baseUrl;

        public LlamaServerHost(ManualLogSource log, string exePath, string arguments, string baseUrl, int startupTimeoutSec)
        {
            _log = log;
            _exePath = exePath;
            _arguments = arguments;
            _baseUrl = baseUrl.TrimEnd('/');
            _startupTimeoutSec = startupTimeoutSec;
        }

        /// <summary>Startet den Server (falls exePath gesetzt) und wartet im Hintergrund auf /health.</summary>
        public void Start()
        {
            _state = ServerState.Starting;
            var t = new Thread(StartWorker) { IsBackground = true, Name = "StbcAiCrew.LlamaStart" };
            t.Start();
        }

        void StartWorker()
        {
            try
            {
                if (!string.IsNullOrEmpty(_exePath))
                {
                    if (!File.Exists(_exePath))
                        throw new FileNotFoundException("llama-server nicht gefunden: " + _exePath);

                    var psi = new ProcessStartInfo(_exePath, _arguments)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_exePath)
                    };
                    _log.LogInfo("Starte llama-server: \"" + _exePath + "\" " + _arguments);
                    _process = Process.Start(psi);
                    AttachToKillOnCloseJob(_process.Id);
                }
                else
                {
                    _log.LogInfo("Kein llama-server-Pfad gesetzt – nutze bereits laufenden Server unter " + _baseUrl);
                }

                var deadline = DateTime.UtcNow.AddSeconds(_startupTimeoutSec);
                while (DateTime.UtcNow < deadline)
                {
                    if (_process != null && _process.HasExited)
                        throw new Exception("llama-server wurde beendet (Exit-Code " + _process.ExitCode + "). Details im llama-Log.");
                    if (IsHealthy())
                    {
                        _state = ServerState.Ready;
                        _log.LogInfo("llama-server bereit: " + _baseUrl);
                        return;
                    }
                    Thread.Sleep(1000);
                }
                throw new TimeoutException("llama-server nicht innerhalb von " + _startupTimeoutSec + " s bereit.");
            }
            catch (Exception e)
            {
                _error = e.Message;
                _state = ServerState.Failed;
                _log.LogError("llama-server-Start fehlgeschlagen: " + e);
            }
        }

        bool IsHealthy()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(_baseUrl + "/health");
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
                    _log.LogInfo("Beende llama-server.");
                    _process.Kill();
                    _process.WaitForExit(3000);
                }
            }
            catch (Exception e)
            {
                _log.LogWarning("llama-server ließ sich nicht sauber beenden: " + e.Message);
            }
            if (_job != IntPtr.Zero)
            {
                CloseHandle(_job);
                _job = IntPtr.Zero;
            }
            _state = ServerState.Stopped;
        }

        // ---- Job-Objekt: Kindprozess stirbt mit dem Spiel ----

        void AttachToKillOnCloseJob(int pid)
        {
            try
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero) return;

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ptr, (uint)size))
                        _log.LogWarning("SetInformationJobObject fehlgeschlagen: " + Marshal.GetLastWin32Error());
                }
                finally { Marshal.FreeHGlobal(ptr); }

                IntPtr hProc = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, false, pid);
                if (hProc == IntPtr.Zero) return;
                if (!AssignProcessToJobObject(_job, hProc))
                    _log.LogWarning("AssignProcessToJobObject fehlgeschlagen: " + Marshal.GetLastWin32Error());
                CloseHandle(hProc);
            }
            catch (Exception e)
            {
                _log.LogWarning("Job-Objekt nicht verfügbar (Server läuft bei Absturz evtl. weiter): " + e.Message);
            }
        }

        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        const uint PROCESS_SET_QUOTA = 0x0100;
        const uint PROCESS_TERMINATE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpInfo, uint cbInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);
    }
}
