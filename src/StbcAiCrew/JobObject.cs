using System;
using System.Runtime.InteropServices;

namespace StbcAiCrew
{
    /// <summary>
    /// Windows-Job-Objekt mit "Kill on close": Alle hier eingehängten Kindprozesse (llama-server,
    /// whisper-server, piper) sterben automatisch mit dem Spiel, auch wenn es abstürzt.
    /// </summary>
    public static class JobObject
    {
        static IntPtr _job = IntPtr.Zero;
        static readonly object Lock = new object();

        public static void Add(int pid)
        {
            try
            {
                lock (Lock)
                {
                    if (_job == IntPtr.Zero) _job = Create();
                    if (_job == IntPtr.Zero) return;
                }
                IntPtr hProc = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, false, pid);
                if (hProc == IntPtr.Zero) return;
                if (!AssignProcessToJobObject(_job, hProc))
                    Plugin.Log.LogWarning("AssignProcessToJobObject fehlgeschlagen: " + Marshal.GetLastWin32Error());
                CloseHandle(hProc);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Job-Objekt nicht verfügbar (Kindprozess läuft bei Absturz evtl. weiter): " + e.Message);
            }
        }

        static IntPtr Create()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return job;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
                    Plugin.Log.LogWarning("SetInformationJobObject fehlgeschlagen: " + Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return job;   // Handle bleibt bis zum Prozessende offen, dann räumt Windows den Job ab
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
