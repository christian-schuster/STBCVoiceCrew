using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace STBCVoiceCrew
{
    public enum Priority
    {
        Background = 0,   // wird gemerkt, weckt aber niemanden
        Important = 1,    // Reaktion nach dem Reaktionsabstand
        Urgent = 2        // sofortige Reaktion (Captain spricht, man wird direkt angesprochen)
    }

    public sealed class GameEvent
    {
        public float Time;
        public string Text;
        public Priority Priority;
    }

    /// <summary>
    /// Was EIN Offizier wahrnimmt: nur seine eigene Konsole und das, was auf der Brücke gesagt wird.
    /// </summary>
    public sealed class Perception
    {
        const int Capacity = 300;
        readonly List<GameEvent> _events = new List<GameEvent>();

        public Priority Pending { get; private set; } = Priority.Background;
        public bool HasPending { get; private set; }
        /// <summary>Hat der Captain seit der letzten Entscheidung gesprochen oder einen Befehl gegeben?</summary>
        public bool CaptainSinceMark { get; private set; }
        /// <summary>Wurde dieser Offizier seit der letzten Entscheidung direkt angesprochen?</summary>
        public bool AddressedSinceMark { get; private set; }
        public int Count { get; private set; }

        public void Add(string text, Priority priority, bool fromCaptain = false, bool addressed = false)
        {
            if (fromCaptain) CaptainSinceMark = true;
            if (addressed) AddressedSinceMark = true;
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace('\n', ' ').Trim();
            if (_events.Count > 0)
            {
                var last = _events[_events.Count - 1];
                if (last.Text == text && Time.time - last.Time < 3f) return;
            }
            _events.Add(new GameEvent { Time = Time.time, Text = text, Priority = priority });
            if (_events.Count > Capacity) _events.RemoveAt(0);
            Count++;
            if (priority > Priority.Background)
            {
                if (!HasPending || priority > Pending) Pending = priority;
                HasPending = true;
            }
        }

        /// <summary>Alles bisher Wahrgenommene gilt als verarbeitet.</summary>
        public void Mark()
        {
            HasPending = false;
            CaptainSinceMark = false;
            AddressedSinceMark = false;
            Pending = Priority.Background;
        }

        public void Reset()
        {
            _events.Clear();
            Count = 0;
            Mark();
        }

        public string Recent(int n)
        {
            var sb = new StringBuilder();
            float now = Time.time;
            for (int i = Mathf.Max(0, _events.Count - n); i < _events.Count; i++)
            {
                var e = _events[i];
                sb.Append("- vor ").Append(Mathf.RoundToInt(now - e.Time)).Append(" s: ").Append(e.Text).Append('\n');
            }
            return sb.Length == 0 ? "- (nichts)\n" : sb.ToString();
        }

        public string FullLog(int maxChars)
        {
            var sb = new StringBuilder();
            float t0 = _events.Count > 0 ? _events[0].Time : 0f;
            foreach (var e in _events)
                sb.Append('[').Append(Mathf.RoundToInt(e.Time - t0)).Append(" s] ").Append(e.Text).Append('\n');
            string s = sb.ToString();
            return s.Length > maxChars ? "…" + s.Substring(s.Length - maxChars) : s;
        }
    }

    /// <summary>
    /// Verteilt Spielereignisse an die Offiziere, und zwar nur an die, die sie tatsächlich sehen,
    /// hören oder spüren können. Wird ausschließlich im Unity-Hauptthread benutzt.
    /// </summary>
    public static class EventLog
    {
        public static readonly Station[] CrewStations = { Station.Helm, Station.Tactical, Station.Engineering };

        static readonly Dictionary<Station, Perception> ByStation = new Dictionary<Station, Perception>
        {
            { Station.Helm, new Perception() },
            { Station.Tactical, new Perception() },
            { Station.Engineering, new Perception() }
        };

        public static Perception Of(Station s)
        {
            Perception p;
            return ByStation.TryGetValue(s, out p) ? p : null;
        }

        /// <summary>Nur auf der Konsole dieser Station sichtbar.</summary>
        public static void ToStation(Station s, string text, Priority prio, bool fromCaptain = false)
        {
            var p = Of(s);
            if (p != null) p.Add(text, prio, fromCaptain);
            Plugin.Log.LogDebug("[" + s + "] " + text);
        }

        /// <summary>Für alle auf der Brücke wahrnehmbar (Alarm, Erschütterungen, Meldungen auf allen Konsolen).</summary>
        public static void ToAll(string text, Priority prio, bool fromCaptain = false)
        {
            foreach (var s in CrewStations) ByStation[s].Add(text, prio, fromCaptain);
            Plugin.Log.LogDebug("[alle] " + text);
        }

        // ---- physisch wahrnehmbare Gefahr (Alarmlicht, Erschütterungen) ----

        public static bool RedAlert { get; private set; }
        public static float LastHitTime { get; private set; } = -999f;
        public static bool UnderThreat(float withinSec) => RedAlert || Time.time - LastHitTime < withinSec;

        static int _hitCount;
        static float _hitFirst = -1f;

        public static void SetRedAlert(bool on)
        {
            if (on == RedAlert) return;
            RedAlert = on;
            ToAll(on ? "Roter Alarm: Alarmsirene und rotes Licht auf der Brücke" : "Roter Alarm aufgehoben", Priority.Important);
        }

        public static void AddHit()
        {
            LastHitTime = Time.time;
            if (_hitFirst < 0f) _hitFirst = Time.time;
            _hitCount++;
        }

        public static void Update()
        {
            // Treffer bündeln: man spürt die Erschütterung, die Schadenswerte sieht nur, wessen Konsole sie zeigt
            if (_hitFirst >= 0f && Time.time - _hitFirst > 2f)
            {
                ToAll(_hitCount > 2 ? "Das Schiff erbebt unter mehreren Treffern" : "Erschütterung, wir wurden getroffen", Priority.Important);
                _hitCount = 0;
                _hitFirst = -1f;
            }
        }

        public static int TotalCount
        {
            get
            {
                int n = 0;
                foreach (var p in ByStation.Values) n += p.Count;
                return n;
            }
        }

        public static void ResetMission()
        {
            foreach (var p in ByStation.Values) p.Reset();
            RedAlert = false;
            LastHitTime = -999f;
            _hitCount = 0;
            _hitFirst = -1f;
        }
    }
}
