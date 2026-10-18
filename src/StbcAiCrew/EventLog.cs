using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StbcAiCrew
{
    public sealed class GameEvent
    {
        public float Time;
        public string Text;
        public bool Important;   // weckt die Crew auf (autonome Reaktion)
    }

    /// <summary>
    /// Was die Crew vom Spiel "wahrnimmt": Konsolenmeldungen, Befehle des Captains, Treffer, Alarmstufen.
    /// Wird ausschließlich im Unity-Hauptthread befüllt (Harmony-Hooks).
    /// </summary>
    public static class EventLog
    {
        const int Capacity = 300;
        static readonly List<GameEvent> Events = new List<GameEvent>();

        public static int ImportantSinceMark { get; private set; }
        public static int MissionEventCount { get; private set; }

        // Treffer werden gebündelt, sonst flutet ein Phaserbeschuss das Protokoll
        static int _hitCount;
        static float _hitDamage;
        static float _hitFirst = -1f;

        public static void Add(string text, bool important)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace('\n', ' ').Trim();
            // Gleiche Meldung kurz hintereinander nur einmal
            if (Events.Count > 0)
            {
                var last = Events[Events.Count - 1];
                if (last.Text == text && Time.time - last.Time < 3f) return;
            }
            Events.Add(new GameEvent { Time = Time.time, Text = text, Important = important });
            if (Events.Count > Capacity) Events.RemoveAt(0);
            MissionEventCount++;
            if (important) ImportantSinceMark++;
            Plugin.Log.LogDebug("[Ereignis] " + text);
        }

        public static void AddHit(float damage)
        {
            if (_hitFirst < 0f) _hitFirst = Time.time;
            _hitCount++;
            if (damage > 0f && damage < 1e6f) _hitDamage += damage;
        }

        public static void Update()
        {
            if (_hitFirst >= 0f && Time.time - _hitFirst > 2f)
            {
                Add("Wir werden getroffen: " + _hitCount + " Treffer, Schaden ca. " + Mathf.RoundToInt(_hitDamage), true);
                _hitCount = 0;
                _hitDamage = 0f;
                _hitFirst = -1f;
            }
        }

        /// <summary>Markiert alle bisherigen Ereignisse als von der Crew gesehen.</summary>
        public static void Mark() => ImportantSinceMark = 0;

        public static void ResetMission()
        {
            Events.Clear();
            MissionEventCount = 0;
            ImportantSinceMark = 0;
        }

        /// <summary>Die letzten n Ereignisse als Text mit Zeitangabe relativ zu jetzt.</summary>
        public static string Recent(int n)
        {
            var sb = new StringBuilder();
            float now = Time.time;
            int start = Mathf.Max(0, Events.Count - n);
            for (int i = start; i < Events.Count; i++)
            {
                var e = Events[i];
                sb.Append("- vor ").Append(Mathf.RoundToInt(now - e.Time)).Append(" s: ").Append(e.Text).Append('\n');
            }
            return sb.Length == 0 ? "- (keine)\n" : sb.ToString();
        }

        /// <summary>Komplettes Missionsprotokoll (für die Nachbesprechung), auf maxChars gekürzt.</summary>
        public static string FullLog(int maxChars)
        {
            var sb = new StringBuilder();
            float t0 = Events.Count > 0 ? Events[0].Time : 0f;
            for (int i = 0; i < Events.Count; i++)
            {
                var e = Events[i];
                sb.Append("[").Append(Mathf.RoundToInt(e.Time - t0)).Append(" s] ").Append(e.Text).Append('\n');
            }
            string s = sb.ToString();
            return s.Length > maxChars ? "…" + s.Substring(s.Length - maxChars) : s;
        }
    }
}
