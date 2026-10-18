using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StbcAiCrew
{
    /// <summary>
    /// Spielt Sprachausgabe direkt über die Windows-Audio-API (winmm waveOut) ab, unabhängig von der
    /// Audio-Engine des Spiels (Wwise). Das Ausgabegerät ist wählbar, z. B. die Kopfhörer der VR-Brille.
    /// Immer nur ein Satz gleichzeitig; nur aus dem Unity-Hauptthread benutzen.
    /// </summary>
    public sealed class WavePlayer : IDisposable
    {
        IntPtr _hWaveOut = IntPtr.Zero;
        IntPtr _hdr = IntPtr.Zero, _data = IntPtr.Zero;
        int _openRate;
        DateTime _endsAt = DateTime.MinValue;

        public string DeviceFilter = "";
        public float Volume = 0.9f;
        public string DeviceName { get; private set; } = "Windows-Standard";
        public string Error { get; private set; }

        public bool Playing => DateTime.UtcNow < _endsAt;

        public static List<string> ListDevices()
        {
            var list = new List<string>();
            int n = waveOutGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WAVEOUTCAPS();
                if (waveOutGetDevCaps(new UIntPtr((uint)i), ref caps, Marshal.SizeOf(typeof(WAVEOUTCAPS))) == 0)
                    list.Add(caps.szPname);
            }
            return list;
        }

        /// <summary>Spielt Mono-16-Bit-PCM ab und gibt die Dauer in Sekunden zurück (0 bei Fehler).</summary>
        public float Play(short[] pcm, int sampleRate)
        {
            if (pcm == null || pcm.Length == 0) return 0f;
            try
            {
                Stop();
                if (_hWaveOut == IntPtr.Zero || _openRate != sampleRate) Open(sampleRate);

                int bytes = pcm.Length * 2;
                _data = Marshal.AllocHGlobal(bytes);
                Marshal.Copy(pcm, 0, _data, pcm.Length);
                int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
                _hdr = Marshal.AllocHGlobal(hdrSize);
                Marshal.StructureToPtr(new WAVEHDR { lpData = _data, dwBufferLength = bytes }, _hdr, false);
                Check(waveOutPrepareHeader(_hWaveOut, _hdr, hdrSize), "waveOutPrepareHeader");
                uint vol = (uint)(Math.Max(0f, Math.Min(1f, Volume)) * 0xFFFF);
                waveOutSetVolume(_hWaveOut, vol | (vol << 16));
                Check(waveOutWrite(_hWaveOut, _hdr, hdrSize), "waveOutWrite");

                float seconds = pcm.Length / (float)sampleRate;
                _endsAt = DateTime.UtcNow.AddSeconds(seconds);
                Error = null;
                return seconds;
            }
            catch (Exception e)
            {
                Error = e.Message;
                if (Plugin.Log != null) Plugin.Log.LogWarning("Sprachausgabe: " + e.Message);
                Close();
                return 0f;
            }
        }

        void Open(int sampleRate)
        {
            Close();
            uint device = WAVE_MAPPER;
            DeviceName = "Windows-Standard";
            if (!string.IsNullOrEmpty(DeviceFilter))
            {
                var devices = ListDevices();
                int idx = devices.FindIndex(d => d.IndexOf(DeviceFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                if (idx >= 0) { device = (uint)idx; DeviceName = devices[idx]; }
                else if (Plugin.Log != null) Plugin.Log.LogWarning("Kein Ausgabegerät passt zu '" + DeviceFilter + "' (" + string.Join(" | ", devices.ToArray()) + "), nehme Standard.");
            }
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1, nChannels = 1, nSamplesPerSec = sampleRate,
                nAvgBytesPerSec = sampleRate * 2, nBlockAlign = 2, wBitsPerSample = 16
            };
            Check(waveOutOpen(out _hWaveOut, new UIntPtr(device), ref fmt, IntPtr.Zero, IntPtr.Zero, 0), "waveOutOpen");
            _openRate = sampleRate;
        }

        /// <summary>Bricht den laufenden Satz ab und gibt den Puffer frei.</summary>
        public void Stop()
        {
            if (_hWaveOut != IntPtr.Zero && _hdr != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                waveOutUnprepareHeader(_hWaveOut, _hdr, Marshal.SizeOf(typeof(WAVEHDR)));
            }
            if (_hdr != IntPtr.Zero) { Marshal.FreeHGlobal(_hdr); _hdr = IntPtr.Zero; }
            if (_data != IntPtr.Zero) { Marshal.FreeHGlobal(_data); _data = IntPtr.Zero; }
            _endsAt = DateTime.MinValue;
        }

        void Close()
        {
            Stop();
            if (_hWaveOut != IntPtr.Zero) { waveOutClose(_hWaveOut); _hWaveOut = IntPtr.Zero; }
            _openRate = 0;
        }

        /// <summary>Gerät neu wählen lassen (z. B. nachdem die Brille verbunden wurde).</summary>
        public void Reopen() => Close();

        public void Dispose() => Close();

        static void Check(int res, string what)
        {
            if (res != 0) throw new Exception(what + " fehlgeschlagen (Code " + res + ")");
        }

        const uint WAVE_MAPPER = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEOUTCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")] static extern int waveOutGetDevCaps(UIntPtr id, ref WAVEOUTCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, UIntPtr deviceId, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr h, uint volume);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
    }
}
