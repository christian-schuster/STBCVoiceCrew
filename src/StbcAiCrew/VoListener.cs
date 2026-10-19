using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx.Logging;

namespace StbcAiCrew
{
    /// <summary>
    /// Die Crew hört mit wie der Captain: Jede Sprachausgabe des Spiels (Briefing, Funksprüche, Logbuch,
    /// Schiffscomputer) wird in dem Moment, in dem sie abgespielt wird, von Whisper abgehört. Abgegriffen wird
    /// die Aufnahme selbst (ohne Musik und Effekte darüber); verstanden wird nur, was Whisper versteht.
    /// </summary>
    public sealed class VoListener : IDisposable
    {
        readonly ManualLogSource _log;
        readonly string _sttUrl;
        readonly string _language;
        readonly string _banksDir;
        readonly Func<bool> _sttReady;
        readonly Action<string, string> _heard;   // (Sprecher, Text), im Hauptthread
        VoArchive _archive;
        readonly Queue<KeyValuePair<string, string>> _jobs = new Queue<KeyValuePair<string, string>>();
        readonly object _lock = new object();
        readonly AutoResetEvent _signal = new AutoResetEvent(false);
        Thread _worker;
        volatile bool _stop;
        string _lastEvent;
        DateTime _lastEventAt;

        public VoListener(ManualLogSource log, string banksDir, string gameLanguage, string sttBaseUrl, Func<bool> sttReady, Action<string, string> heard)
        {
            _log = log;
            _banksDir = banksDir;
            _language = gameLanguage;
            _sttUrl = sttBaseUrl.TrimEnd('/') + "/inference";
            _sttReady = sttReady;
            _heard = heard;
            _worker = new Thread(Work) { IsBackground = true, Name = "StbcAiCrew.VoListener" };
            _worker.Start();
        }

        /// <summary>Aus dem Hook auf VOManager.Play (Hauptthread).</summary>
        public void OnPlay(string eventName, string speaker)
        {
            if (string.IsNullOrEmpty(eventName) || speaker == null) return;
            var now = DateTime.UtcNow;
            if (eventName == _lastEvent && (now - _lastEventAt).TotalSeconds < 5) return;   // Netzwerk-Echo / Doppelaufruf
            _lastEvent = eventName; _lastEventAt = now;
            lock (_lock)
            {
                if (_jobs.Count > 20) _jobs.Dequeue();
                _jobs.Enqueue(new KeyValuePair<string, string>(eventName, speaker));
            }
            _signal.Set();
        }

        void Work()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _archive = VoArchive.Load(_banksDir, _language);
                _log.LogInfo("Mithören: " + _archive.Count + " Sprachaufnahmen (" + _language + ") gefunden, " + sw.ElapsedMilliseconds + " ms.");
            }
            catch (Exception e) { _log.LogWarning("Mithören nicht möglich: " + e.Message); return; }

            while (!_stop)
            {
                _signal.WaitOne(1000);
                while (!_stop)
                {
                    KeyValuePair<string, string> job;
                    lock (_lock) { if (_jobs.Count == 0) break; job = _jobs.Dequeue(); }
                    if (!_sttReady()) continue;
                    try
                    {
                        var pcm = _archive.Pcm16k(job.Key);
                        if (pcm == null || pcm.Length < 16000 / 4) continue;   // unbekannt oder zu kurz
                        string text = VoiceInput.Transcribe(_sttUrl, pcm, "de");
                        text = VoiceInput.Clean(text);
                        if (string.IsNullOrEmpty(text)) continue;
                        string speaker = job.Value;
                        _log.LogInfo("Mitgehört (" + speaker + "): " + text);
                        MainThread.Post(() => _heard(speaker, text));
                    }
                    catch (Exception e) { _log.LogDebug("Mithören fehlgeschlagen (" + job.Key + "): " + e.Message); }
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
        }
    }
}
