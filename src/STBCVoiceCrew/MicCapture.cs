using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Logging;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Nimmt das Mikrofon direkt über die Windows-Audio-API (winmm) auf, mit 16 kHz, Mono und 16 Bit.
    /// Bewusst nicht über UnityEngine.Microphone: Das nutzt der Sprachchat des Spiels, und zwei Aufnahmen
    /// desselben Geräts über Unity stören sich. Windows erlaubt dagegen mehrere Aufnahmen parallel.
    /// Die Audiodaten laufen auf einem Hintergrund-Thread in den Callback onSamples.
    /// </summary>
    public sealed class MicCapture : IDisposable
    {
        public const int SampleRate = 16000;
        const int BufferSamples = 640;   // 40 ms
        const int BufferCount = 10;

        readonly ManualLogSource _log;
        readonly Action<short[], int> _onSamples;
        Thread _thread;
        volatile bool _running;
        IntPtr _hWaveIn = IntPtr.Zero;

        public string DeviceName { get; private set; } = "?";
        public string Error { get; private set; }
        /// <summary>false, sobald die Aufnahme endet (Gerät getrennt, Fehler, Dispose).</summary>
        public bool Running => _running;

        public MicCapture(ManualLogSource log, Action<short[], int> onSamples)
        {
            _log = log;
            _onSamples = onSamples;
        }

        public static List<string> ListDevices()
        {
            var list = new List<string>();
            int n = waveInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WAVEINCAPS();
                if (waveInGetDevCaps(new UIntPtr((uint)i), ref caps, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0)
                    list.Add(caps.szPname);
            }
            return list;
        }

        /// <summary>Gerät, das gerade aufnimmt (für den Vergleich, ob ein anderes gewählt werden muss).</summary>
        public uint DeviceId { get; private set; } = uint.MaxValue;

        /// <summary>Startet die Aufnahme auf dem angegebenen Gerät (siehe AudioDevices.ResolveInput).</summary>
        public bool Start(AudioDevice device)
        {
            if (!device.Valid) { Error = device.Name; return false; }
            uint deviceId = device.Id;
            DeviceName = device.Name + " (" + device.Source + ")";
            DeviceId = device.Id;

            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate,
                nAvgBytesPerSec = SampleRate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0
            };
            int res = waveInOpen(out _hWaveIn, new UIntPtr(deviceId), ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
            if (res != 0)
            {
                Error = "waveInOpen fehlgeschlagen (Code " + res + ")";
                return false;
            }

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "STBCVoiceCrew.Mic" };
            _thread.Start();
            _log.LogInfo("Mikrofon aktiv: " + DeviceName);
            return true;
        }

        void Loop()
        {
            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
            int flagsOffset = Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags").ToInt32();
            int recordedOffset = Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded").ToInt32();
            var headers = new IntPtr[BufferCount];
            var data = new IntPtr[BufferCount];
            var samples = new short[BufferSamples];
            try
            {
                for (int i = 0; i < BufferCount; i++)
                {
                    data[i] = Marshal.AllocHGlobal(BufferSamples * 2);
                    headers[i] = Marshal.AllocHGlobal(hdrSize);
                    var hdr = new WAVEHDR { lpData = data[i], dwBufferLength = BufferSamples * 2 };
                    Marshal.StructureToPtr(hdr, headers[i], false);
                    Check(waveInPrepareHeader(_hWaveIn, headers[i], hdrSize), "waveInPrepareHeader");
                    Check(waveInAddBuffer(_hWaveIn, headers[i], hdrSize), "waveInAddBuffer");
                }
                Check(waveInStart(_hWaveIn), "waveInStart");

                int next = 0;
                var lastData = DateTime.UtcNow;
                while (_running)
                {
                    int flags = Marshal.ReadInt32(headers[next], flagsOffset);
                    if ((flags & WHDR_DONE) == 0)
                    {
                        // Kommen keine Daten mehr, wurde das Gerät meist getrennt (z. B. Brille abgesteckt)
                        if ((DateTime.UtcNow - lastData).TotalSeconds > 3) throw new Exception("keine Audiodaten mehr (Gerät getrennt?)");
                        Thread.Sleep(10);
                        continue;
                    }
                    lastData = DateTime.UtcNow;

                    int bytes = Marshal.ReadInt32(headers[next], recordedOffset);
                    int count = Math.Min(bytes / 2, BufferSamples);
                    Marshal.Copy(data[next], samples, 0, count);
                    try { _onSamples(samples, count); }
                    catch (Exception e) { _log.LogError("Mikrofon-Verarbeitung: " + e); }

                    if (_running) waveInAddBuffer(_hWaveIn, headers[next], hdrSize);
                    next = (next + 1) % BufferCount;
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
                _log.LogWarning("Mikrofon: " + e.Message);
            }
            finally
            {
                _running = false;
                waveInReset(_hWaveIn);
                for (int i = 0; i < BufferCount; i++)
                {
                    if (headers[i] != IntPtr.Zero)
                    {
                        waveInUnprepareHeader(_hWaveIn, headers[i], hdrSize);
                        Marshal.FreeHGlobal(headers[i]);
                    }
                    if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
                }
                waveInClose(_hWaveIn);
                _hWaveIn = IntPtr.Zero;
            }
        }

        static void Check(int res, string what)
        {
            if (res != 0) throw new Exception(what + " fehlgeschlagen (Code " + res + ")");
        }

        public void Dispose()
        {
            _running = false;
            if (_thread != null) _thread.Join(1000);
        }

        // ---- winmm ----

        const uint WAVE_MAPPER = 0xFFFFFFFF;
        const int CALLBACK_NULL = 0;
        const int WHDR_DONE = 0x1;

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
        struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] static extern int waveInGetDevCaps(UIntPtr id, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, UIntPtr deviceId, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
    }
}
