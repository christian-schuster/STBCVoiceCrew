using System;
using System.Runtime.InteropServices;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Erkennt die Startinstanz des Spiels: Steam startet stbc.exe, diese Instanz startet Ubisoft Connect und beendet
    /// sich; Ubisoft Connect (UbisoftGameLauncher.exe) startet dann das eigentliche Spiel. In der Startinstanz bleibt
    /// der Mod still und entfernt die DOORSTOP_*-Variablen, damit das neu gestartete Spiel BepInEx wieder lädt
    /// (Doorstop lädt nicht, wenn DOORSTOP_INITIALIZED geerbt wird).
    /// </summary>
    public static class LaunchInstance
    {
        public static string ParentProcessName()
        {
            try
            {
                int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                int parent = -1;
                IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return null;
                try
                {
                    var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W)) };
                    for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                        if (e.th32ProcessID == me) { parent = (int)e.th32ParentProcessID; break; }
                    if (parent < 0) return null;
                    e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
                    for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                        if (e.th32ProcessID == parent) return e.szExeFile;
                }
                finally { CloseHandle(snap); }
            }
            catch { }
            return null;
        }

        /// <summary>Startinstanz = von Steam gestartet (das eigentliche Spiel startet Ubisoft Connect neu).</summary>
        public static bool IsSteamLauncherInstance(out string parent)
        {
            parent = ParentProcessName();
            return parent != null && parent.Equals("steam.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>DOORSTOP_*-Variablen aus der Prozessumgebung entfernen (werden sonst an Ubisoft Connect und das Spiel vererbt).</summary>
        public static int ClearDoorstopVariables()
        {
            int n = 0;
            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            {
                string k = kv.Key as string;
                if (k == null || !k.StartsWith("DOORSTOP_", StringComparison.OrdinalIgnoreCase)) continue;
                SetEnvironmentVariableW(k, null);
                n++;
            }
            return n;
        }

        const uint TH32CS_SNAPPROCESS = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetEnvironmentVariableW(string name, string value);
    }
}
