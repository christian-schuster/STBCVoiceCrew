using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using DaikonForge.VoIP;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Mehrspieler: Die KI-Crew läuft nur beim Host (Master Client, steuert die Bots der freien Stationen).
    /// Sie hört die Mitspieler über den Sprachchat des Spiels (DaikonForge VoIP über Photon) und spricht selbst
    /// über diesen Sprachchat, sodass alle sie hören. Die Mitspieler brauchen die Mod nicht.
    /// </summary>
    public static class Net
    {
        /// <summary>
        /// Crew läuft hier, außer man sitzt in einem fremden Raum (in einem Raum, aber nicht Master Client:
        /// NDIOGIGMPBH.PDANNCPHHIN). Im Menü, offline und als Host also immer.
        /// </summary>
        public static bool IsHost
        {
            get { try { return !NDIOGIGMPBH.PDANNCPHHIN; } catch { return true; } }
        }

        /// <summary>In einem Mehrspieler-Raum (nicht offline).</summary>
        public static bool Online
        {
            get { try { return !NDIOGIGMPBH.HECOODODCEP && NDIOGIGMPBH.PGJDPPDBGBL; } catch { return false; } }
        }

        public static int LocalPlayerId
        {
            get { try { var p = NDIOGIGMPBH.DIMFCNEMMPD; return p != null ? p.KLNDGCGHADM : -1; } catch { return -1; } }
        }

        /// <summary>Station eines Spielers (NovaPlayerInfo.FJMHMOOJNJG), null wenn unbekannt.</summary>
        public static Station? StationOf(int photonId)
        {
            try
            {
                var player = CBBCCGMALMP.Find(photonId);
                var mgr = NovaPlayerManager.IEFBKOMBFGO;
                if (player == null || mgr == null) return null;
                var info = mgr.GetInfoForPlayer(player);
                return info != null ? GameBridge.FromGameStation(info.FJMHMOOJNJG) : null;
            }
            catch { return null; }
        }

        /// <summary>Stationen, auf denen laut Spielerliste ein Mensch sitzt (Mehrspieler).</summary>
        public static bool HumanAt(Station st)
        {
            try
            {
                var mgr = NovaPlayerManager.IEFBKOMBFGO;
                if (mgr == null || !Online) return false;
                foreach (var info in mgr.GetPlayers())
                    if (info != null && GameBridge.FromGameStation(info.FJMHMOOJNJG) == st) return true;
            }
            catch { }
            return false;
        }

        public static string NameOf(int photonId)
        {
            try { var p = CBBCCGMALMP.Find(photonId); return p != null ? p.GPICMMEIAPM : null; }
            catch { return null; }
        }
    }

    /// <summary>Hört die Stimmen der Mitspieler ab (entpackter Sprachchat je Spieler) und lässt Whisper sie verstehen.</summary>
    public sealed class RemoteVoiceTap : IDisposable
    {
        sealed class Talker
        {
            public int PlayerId;
            public readonly List<float> Samples = new List<float>();
            public int Rate;
            public float LastVoice = -999f;   // zuletzt Sprache (Pegel über der Schwelle), nicht nur Audio
            public bool Voiced;               // in der laufenden Äußerung wurde gesprochen
        }

        /// <summary>RMS-Schwelle für "spricht" (Sprachchat liefert auch in Pausen Audio).</summary>
        public float VoiceThreshold = 0.015f;

        readonly ManualLogSource _log;
        readonly string _sttUrl;
        readonly string _language;
        readonly Func<bool> _ready;
        readonly Action<int, string> _heard;   // (Photon-ID, Text), im Hauptthread
        readonly Dictionary<int, Talker> _talkers = new Dictionary<int, Talker>();
        readonly Queue<KeyValuePair<int, short[]>> _jobs = new Queue<KeyValuePair<int, short[]>>();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        volatile bool _stop;

        public static RemoteVoiceTap Instance;
        /// <summary>Echtzeit, zu der zuletzt ein Mitspieler gesprochen hat.</summary>
        public float LastRemoteSpeech = -999f;
        /// <summary>Statt Whisper: eigene Erkennung (Watson-Modus: Vosk).</summary>
        public Func<short[], string> Transcriber;

        public RemoteVoiceTap(ManualLogSource log, string sttBaseUrl, string language, Func<bool> ready, Action<int, string> heard)
        {
            _log = log;
            _sttUrl = sttBaseUrl.TrimEnd('/') + "/inference";
            _language = language;
            _ready = ready;
            _heard = heard;
            new Thread(Work) { IsBackground = true, Name = "StbcAiCrew.RemoteVoice" }.Start();
            Instance = this;
        }

        /// <summary>Aus dem Hook auf UnityAudioPlayer.BufferAudio (Hauptthread).</summary>
        public void OnRemoteAudio(Component player, DHEDECPPLGP<float> chunk)
        {
            var view = player.GetComponent<PhotonView>();
            if (view == null || view.ownerId == Net.LocalPlayerId) return;
            int id = view.ownerId;
            Talker t;
            if (!_talkers.TryGetValue(id, out t)) _talkers[id] = t = new Talker { PlayerId = id };
            t.Rate = NJPHCGOEOPO.GetOutputSampleRate();
            int n = Math.Min(chunk.PICFPDJILEK, chunk.BMFLNPNGNDF.Length);
            var data = chunk.BMFLNPNGNDF;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += data[i] * data[i];
            bool voice = n > 0 && Math.Sqrt(sum / n) > VoiceThreshold;
            float now = Time.realtimeSinceStartup;
            if (voice) { t.LastVoice = now; t.Voiced = true; LastRemoteSpeech = now; }
            // Stille vor einer Äußerung nicht sammeln, nur ein kurzes Polster
            if (!t.Voiced && t.Samples.Count > t.Rate / 2) t.Samples.RemoveRange(0, t.Samples.Count - t.Rate / 2);
            for (int i = 0; i < n; i++) t.Samples.Add(data[i]);
            if (t.Samples.Count > t.Rate * 30) Flush(t);   // sehr lange Rede: zwischendurch abschicken
        }

        /// <summary>Jeden Frame: Wer 0,7 s nichts mehr gesagt hat, dessen Äußerung geht an Whisper.</summary>
        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var t in _talkers.Values)
                if (t.Voiced && now - t.LastVoice > 0.7f) Flush(t);
        }

        void Flush(Talker t)
        {
            int rate = t.Rate > 0 ? t.Rate : 24000;
            bool voiced = t.Voiced;
            t.Voiced = false;
            if (!voiced || t.Samples.Count < rate * 0.35f) { t.Samples.Clear(); return; }   // Stille oder nur ein Geräusch
            var pcm = To16k(t.Samples, rate);
            t.Samples.Clear();
            lock (_jobs) _jobs.Enqueue(new KeyValuePair<int, short[]>(t.PlayerId, pcm));
            _signal.Set();
        }

        static short[] To16k(List<float> src, int rate)
        {
            double step = rate / 16000.0;
            int n = (int)(src.Count / step);
            var o = new short[n];
            for (int i = 0; i < n; i++)
            {
                double pos = i * step;
                int a = (int)pos;
                float f = (float)(pos - a);
                float v = a + 1 < src.Count ? src[a] * (1 - f) + src[a + 1] * f : src[Math.Min(a, src.Count - 1)];
                o[i] = (short)Mathf.Clamp(v * 32767f, -32768f, 32767f);
            }
            return o;
        }

        void Work()
        {
            while (!_stop)
            {
                _signal.WaitOne(1000);
                while (!_stop)
                {
                    KeyValuePair<int, short[]> job;
                    lock (_jobs) { if (_jobs.Count == 0) break; job = _jobs.Dequeue(); }
                    if (!_ready()) continue;
                    try
                    {
                        string raw = Transcriber != null ? Transcriber(job.Value) : VoiceInput.Transcribe(_sttUrl, job.Value, _language);
                        string text = VoiceInput.Clean(raw);
                        if (string.IsNullOrEmpty(text)) continue;
                        int id = job.Key;
                        MainThread.Post(() => _heard(id, text));
                    }
                    catch (Exception e) { _log.LogDebug("Mitspieler nicht verstanden: " + e.Message); }
                }
            }
        }

        public void Dispose() { _stop = true; _signal.Set(); if (Instance == this) Instance = null; }
    }

    /// <summary>
    /// Spricht die Crew über den Sprachchat des Spiels: Ihre Stimme wird im Echtzeittakt in den Sende-Encoder
    /// des lokalen Voice-Controllers gespeist (wie Mikrofonpuffer). Solange sie spricht, ruht das eigene Mikrofon.
    /// </summary>
    public sealed class CrewVoiceSender
    {
        public static CrewVoiceSender Instance;
        public float Gain = 0.8f;

        readonly Queue<float> _pending = new Queue<float>();
        double _carry;
        static MethodInfo _send;
        static FieldInfo _rateField;
        [ThreadStatic] public static bool Injecting;

        public CrewVoiceSender() { Instance = this; }

        public bool Active => _pending.Count > 0;

        /// <summary>Der Mensch spricht: Crew-Stimme für die Mitspieler abbrechen, sein Mikrofon hat Vorrang.</summary>
        public void Clear() { _pending.Clear(); _carry = 0; }

        /// <summary>Liegt das eigene Spiel-Mikrofon gerade über der Sprechschwelle des Spiels?</summary>
        public static bool HostTalking()
        {
            try
            {
                var mgr = VoIpManager.IEFBKOMBFGO;
                var dev = mgr != null ? mgr.GetLocalMicrophoneInputDevice() : null;
                return dev != null && dev.BBDPHCJHMEG;
            }
            catch { return false; }
        }

        /// <summary>Crew-Satz zum Senden vormerken (pcm in der Abtastrate rate).</summary>
        public void Enqueue(short[] pcm, int rate)
        {
            var vc = LocalController();
            if (vc == null || pcm == null || rate <= 0) return;
            int target = TargetRate(vc);
            double step = (double)rate / target;
            int n = (int)(pcm.Length / step);
            for (int i = 0; i < n; i++)
            {
                double pos = i * step;
                int a = (int)pos;
                float f = (float)(pos - a);
                float v = a + 1 < pcm.Length ? pcm[a] * (1 - f) + pcm[a + 1] * f : pcm[Math.Min(a, pcm.Length - 1)];
                _pending.Enqueue(v / 32768f * Gain);
            }
        }

        /// <summary>Jeden Frame: so viele Samples senden, wie seit dem letzten Frame Zeit vergangen ist.</summary>
        public void Tick()
        {
            if (_pending.Count == 0) { _carry = 0; return; }
            var vc = LocalController();
            if (vc == null) { _pending.Clear(); return; }
            int rate = TargetRate(vc);
            _carry += Time.unscaledDeltaTime * rate;
            int n = Math.Min(_pending.Count, (int)_carry);
            if (n <= 0) return;
            _carry -= n;
            var arr = new float[n];
            for (int i = 0; i < n; i++) arr[i] = _pending.Dequeue();
            var buf = new DHEDECPPLGP<float>(n, 0);
            buf.Resize(n);
            buf.CopyFrom(arr, 0, 0, n * 4);
            try
            {
                if (_send == null) _send = typeof(VoiceControllerBase).GetMethod("LOEAHNOJEOK", BindingFlags.Instance | BindingFlags.NonPublic);
                Injecting = true;
                _send.Invoke(vc, new object[] { buf, rate });
            }
            catch (Exception e) { Plugin.Log.LogWarning("Crew-Stimme nicht gesendet: " + e.Message); _pending.Clear(); }
            finally { Injecting = false; }
        }

        static int TargetRate(VoiceControllerBase vc)
        {
            try
            {
                if (_rateField == null) _rateField = typeof(VoiceControllerBase).GetField("FGJILPHGMHD", BindingFlags.Instance | BindingFlags.NonPublic);
                int r = _rateField != null ? (int)_rateField.GetValue(vc) : 24000;
                return r > 0 ? r : 24000;
            }
            catch { return 24000; }
        }

        static VoiceControllerBase LocalController()
        {
            try
            {
                var mgr = VoIpManager.IEFBKOMBFGO;
                var go = mgr != null ? mgr.GetLocalVoiceControllerGameObject() : null;
                return go != null ? go.GetComponent<VoiceControllerBase>() : null;
            }
            catch { return null; }
        }
    }
}
