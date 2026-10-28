using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace STBCVoiceCrew
{
    public struct AudioDevice
    {
        public uint Id;          // winmm-Geräteindex
        public string Name;
        public string Source;    // "Spiel", "Windows-Standard", "Config"
        public bool Valid;
    }

    /// <summary>
    /// Wählt Mikrofon und Lautsprecher so wie das Spiel. Bei Oculus nimmt Wwise das in der Oculus-App
    /// gewählte Gerät (AkInitializer.OculusAudioOutID/InID, eine WASAPI-Endpunkt-GUID), sonst das
    /// Windows-Standardgerät. Hier wird die GUID auf den passenden winmm-Index abgebildet
    /// (DRV_QUERYFUNCTIONINSTANCEID). Der Windows-Standard wird als konkreter Index aufgelöst,
    /// damit ein späterer Wechsel des Standardgeräts erkannt wird.
    /// </summary>
    public static class AudioDevices
    {
        public static AudioDevice ResolveOutput(string configFilter, string gameEndpointGuid) =>
            Resolve(false, configFilter, gameEndpointGuid);

        public static AudioDevice ResolveInput(string configFilter, string gameEndpointGuid, string gameDeviceName = null)
        {
            // Im Spiel ausgewähltes Mikrofon: winmm kürzt Namen auf 31 Zeichen, daher Präfixvergleich
            if (string.IsNullOrEmpty(configFilter) && !string.IsNullOrEmpty(gameDeviceName))
            {
                int count = waveInGetNumDevs();
                for (uint i = 0; i < count; i++)
                {
                    string n = Name(true, i);
                    if (n.Length >= 4 && (gameDeviceName.StartsWith(n, StringComparison.OrdinalIgnoreCase) || n.StartsWith(gameDeviceName, StringComparison.OrdinalIgnoreCase)))
                        return new AudioDevice { Id = i, Name = n, Source = Loc.T("im Spiel gewählt", "selected in game"), Valid = true };
                }
            }
            return Resolve(true, configFilter, gameEndpointGuid);
        }

        static AudioDevice Resolve(bool input, string filter, string gameGuid)
        {
            int count = input ? waveInGetNumDevs() : waveOutGetNumDevs();
            if (count <= 0) return new AudioDevice { Valid = false, Name = input ? "kein Mikrofon" : "kein Ausgabegerät" };

            // 1. Ausdrücklich in der Config gewählt (Ausnahmefall)
            if (!string.IsNullOrEmpty(filter))
                for (uint i = 0; i < count; i++)
                {
                    string n = Name(input, i);
                    if (n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        return new AudioDevice { Id = i, Name = n, Source = "Config", Valid = true };
                }

            // 2. Dasselbe Gerät wie das Spiel (Oculus-Audioeinstellung)
            if (!string.IsNullOrEmpty(gameGuid))
            {
                string g = gameGuid.Trim('{', '}').ToLowerInvariant();
                for (uint i = 0; i < count; i++)
                {
                    string endpoint = EndpointId(input, i);
                    if (endpoint != null && endpoint.ToLowerInvariant().Contains(g))
                        return new AudioDevice { Id = i, Name = Name(input, i), Source = Loc.T("Spiel", "game"), Valid = true };
                }
            }

            // 3. Windows-Standardgerät (wie Wwise ohne Oculus)
            uint preferred;
            if (PreferredDevice(input, out preferred) && preferred < count)
                return new AudioDevice { Id = preferred, Name = Name(input, preferred), Source = Loc.T("Windows-Standard", "Windows default"), Valid = true };
            return new AudioDevice { Id = 0, Name = Name(input, 0), Source = Loc.T("Windows-Standard", "Windows default"), Valid = true };
        }

        public static List<string> List(bool input)
        {
            var l = new List<string>();
            int count = input ? waveInGetNumDevs() : waveOutGetNumDevs();
            for (uint i = 0; i < count; i++) l.Add(Name(input, i));
            return l;
        }

        static string Name(bool input, uint id)
        {
            if (input)
            {
                var caps = new WAVEINCAPS();
                return waveInGetDevCaps(new UIntPtr(id), ref caps, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0 ? caps.szPname : "?";
            }
            else
            {
                var caps = new WAVEOUTCAPS();
                return waveOutGetDevCaps(new UIntPtr(id), ref caps, Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0 ? caps.szPname : "?";
            }
        }

        /// <summary>WASAPI-Endpunkt-ID des winmm-Geräts, z. B. "{0.0.0.00000000}.{guid}".</summary>
        static string EndpointId(bool input, uint id)
        {
            IntPtr sizePtr = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(sizePtr, 0);
                int res = input ? waveInMessage(new IntPtr(id), DRV_QUERYFUNCTIONINSTANCEIDSIZE, sizePtr, IntPtr.Zero)
                                : waveOutMessage(new IntPtr(id), DRV_QUERYFUNCTIONINSTANCEIDSIZE, sizePtr, IntPtr.Zero);
                int size = Marshal.ReadInt32(sizePtr);
                if (res != 0 || size <= 0 || size > 4096) return null;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    res = input ? waveInMessage(new IntPtr(id), DRV_QUERYFUNCTIONINSTANCEID, buf, new IntPtr(size))
                                : waveOutMessage(new IntPtr(id), DRV_QUERYFUNCTIONINSTANCEID, buf, new IntPtr(size));
                    return res == 0 ? Marshal.PtrToStringUni(buf) : null;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(sizePtr); }
        }

        static bool PreferredDevice(bool input, out uint id)
        {
            id = 0;
            IntPtr idPtr = Marshal.AllocHGlobal(4), flagsPtr = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(idPtr, -1);
                int res = input ? waveInMessage(new IntPtr(-1), DRVM_MAPPER_PREFERRED_GET, idPtr, flagsPtr)
                                : waveOutMessage(new IntPtr(-1), DRVM_MAPPER_PREFERRED_GET, idPtr, flagsPtr);
                int v = Marshal.ReadInt32(idPtr);
                if (res != 0 || v < 0) return false;
                id = (uint)v;
                return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(idPtr); Marshal.FreeHGlobal(flagsPtr); }
        }

        const uint DRV_QUERYFUNCTIONINSTANCEIDSIZE = 0x0811;
        const uint DRV_QUERYFUNCTIONINSTANCEID = 0x0812;
        const uint DRVM_MAPPER_PREFERRED_GET = 0x2015;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEINCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEOUTCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] static extern int waveInGetDevCaps(UIntPtr id, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")] static extern int waveOutGetDevCaps(UIntPtr id, ref WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveInMessage(IntPtr deviceId, uint msg, IntPtr dw1, IntPtr dw2);
        [DllImport("winmm.dll")] static extern int waveOutMessage(IntPtr deviceId, uint msg, IntPtr dw1, IntPtr dw2);
    }
}
