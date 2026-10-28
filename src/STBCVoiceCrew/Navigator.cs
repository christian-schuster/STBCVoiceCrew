using System;
using System.Collections.Generic;
using AI;
using Sector.Spacecraft;
using UnityEngine;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Fliegt Manöver ohne LLM: Annähern (auch außerhalb gegnerischer Ortung), Gefahren umfliegen, Abstand halten,
    /// Kreisen, Flucht. Gesteuert wird über die Bot-Steuer: Kurswinkel auf dem Schwarzen Brett
    /// (IsManualHeading/ManualHeading, Winkel = Atan2(x, z) wie im Spiel), Fahrt über die Fahrtbefehle.
    /// Kursberechnung: Ziel zieht an, Gefahrenzonen (Minen, Anomalien, gegnerische Ortungskreise) stoßen ab und
    /// lenken seitlich vorbei (Potenzialfeld mit Tangentialanteil), alle 0,5 s neu.
    /// </summary>
    public sealed class Navigator
    {
        public enum Mode { None, Approach, Avoid, KeepDistance, Circle, Flee }

        public Mode Current { get; private set; } = Mode.None;
        public string Status { get; private set; } = "aus";
        /// <summary>Daueranweisung: gegnerische Ortungskreise meiden (gilt für jedes Manöver).</summary>
        public bool StayOutOfSensors;
        /// <summary>Gefahren, die umflogen werden (Teil des Kontaktnamens, z. B. "mine", "anomalie").</summary>
        public readonly List<string> AvoidKinds = new List<string>();

        string _targetName;           // Zielkontakt (Name wie angezeigt)
        float _distanceKm = 10f;      // für Abstand halten / Kreisen
        float _next;
        float _side = 1f;             // Umfliegen links/rechts, einmal gewählt beibehalten
        readonly Action<Station, string> _command;   // Fahrtbefehle an die Steuer

        public Navigator(Action<Station, string> command) { _command = command; }

        string _lastSpeed;
        /// <summary>Fahrtbefehl nur bei Änderung senden (sonst alle 0,5 s derselbe Knopfdruck).</summary>
        void Speed(string cmd) { if (_lastSpeed == cmd) return; _lastSpeed = cmd; _command(Station.Helm, cmd); }

        public void Approach(string target) { Start(Mode.Approach, target); }
        public void Avoid(string kind, string goalTarget)
        {
            if (!string.IsNullOrEmpty(kind) && !AvoidKinds.Contains(kind)) AvoidKinds.Add(kind);
            Start(Mode.Avoid, goalTarget);
        }
        public void KeepDistance(string target, float km) { _distanceKm = km > 0 ? km : 10f; Start(Mode.KeepDistance, target); }
        public void Circle(string target, float km) { _distanceKm = km > 0 ? km : 5f; Start(Mode.Circle, target); }
        public void Flee() { Start(Mode.Flee, null); Speed("SetSpeedFull"); }

        public void Stop(string why)
        {
            if (Current == Mode.None) return;
            Current = Mode.None;
            Status = Loc.T("aus (", "off (") + why + ")";
            SetManualHeading(false, 0f);
            Plugin.Log.LogInfo("Navigator: Manöver beendet (" + why + ").");
        }

        void Start(Mode m, string target)
        {
            Current = m;
            _targetName = target;
            _lastSpeed = null;
            _side = 0f;
            _next = 0f;
            Plugin.Log.LogInfo("Navigator: " + m + (target != null ? " → " + target : "") + (StayOutOfSensors ? " (außerhalb der Ortung)" : "") +
                               (AvoidKinds.Count > 0 ? " (meidet: " + string.Join(", ", AvoidKinds.ToArray()) + ")" : ""));
        }

        public void Tick(List<Contact> contacts, PlayerSpacecraft ship)
        {
            if (Current == Mode.None || Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (contacts == null || !Contacts.HasOwn) return;

            Vector3d own = Contacts.OwnPosition;
            Contact target = Find(contacts, _targetName);
            Vector2 goal = Vector2.zero;
            double targetDist = -1;
            if (target != null && target.HasPosition)
            {
                var rel = Flat(target.Position - own);
                targetDist = rel.magnitude;
                goal = rel.normalized;
            }

            switch (Current)
            {
                case Mode.Approach:
                    if (target == null) { Stop(Loc.T("Ziel nicht mehr sichtbar", "target lost")); return; }
                    if (targetDist < 2500) { Speed("SetSpeedStop"); Stop(Loc.T("Ziel erreicht", "target reached")); return; }
                    break;
                case Mode.Avoid:
                    // ohne Ziel: Kurs halten (vorwärts) und nur ausweichen
                    if (target == null) goal = Flat(Contacts.OwnForward).normalized;
                    else if (targetDist < 2500) { Speed("SetSpeedStop"); Stop(Loc.T("Ziel erreicht", "target reached")); return; }
                    break;
                case Mode.KeepDistance:
                    if (target == null) { Stop(Loc.T("Ziel nicht mehr sichtbar", "target lost")); return; }
                    double want = _distanceKm * 1000.0;
                    if (Math.Abs(targetDist - want) < want * 0.15) { Speed("SetSpeedStop"); Status = Loc.T("hält ", "holding ") + _distanceKm + " km"; return; }
                    if (targetDist < want) goal = -goal;   // zu nah: weg
                    Speed("SetSpeedHalf");
                    break;
                case Mode.Circle:
                    if (target == null) { Stop(Loc.T("Ziel nicht mehr sichtbar", "target lost")); return; }
                    // tangential um das Ziel, mit Korrektur zum Sollradius
                    var tangent = new Vector2(-goal.y, goal.x);
                    float radial = (float)((targetDist - _distanceKm * 1000.0) / (_distanceKm * 1000.0));
                    goal = (tangent + goal * Mathf.Clamp(radial, -1f, 1f)).normalized;
                    break;
                case Mode.Flee:
                    Contact enemy = Nearest(contacts, c => c.Category == Sector.HHHDDNLEDBN.Enemy);
                    if (enemy == null) { Stop(Loc.T("kein Feind mehr in Sicht", "no enemy in sight")); return; }
                    goal = -Flat(enemy.Position - own).normalized;
                    break;
            }

            // Gefahrenzonen: Minen/Anomalien (feste Radien), gegnerische Ortung (Radius aus dem Spiel)
            Vector2 push = Vector2.zero;
            double sensor = SensorRadius(ship);
            foreach (var c in contacts)
            {
                if (!c.HasPosition || c == target) continue;
                double radius = HazardRadius(c, sensor);
                if (radius <= 0) continue;
                var away = Flat(own - c.Position);
                double d = away.magnitude;
                double reach = radius * 1.4;
                if (d >= reach) continue;
                float strength = (float)Math.Pow((reach - d) / (reach - radius * 0.6), 2);
                var dir = away.normalized;
                // Seite für das Umfliegen einmal festlegen (die, auf der das Ziel liegt), sonst Zickzack
                if (_side == 0f) _side = Cross(goal, -dir) >= 0 ? 1f : -1f;
                var around = new Vector2(-dir.y, dir.x) * _side;
                push += (dir * 0.6f + around) * strength;
            }

            Vector2 steer = goal + push;
            if (steer.sqrMagnitude < 0.0001f) steer = goal.sqrMagnitude > 0 ? goal : Flat(Contacts.OwnForward);
            float heading = Mathf.Atan2(steer.x, steer.y) * Mathf.Rad2Deg;
            SetManualHeading(true, heading);
            Status = Current + (target != null ? " " + target.Name + " " + (targetDist / 1000.0).ToString("0.0") + " km" : "") +
                     Loc.T(", Kurs ", ", heading ") + Mathf.RoundToInt((heading + 360f) % 360f) + "°" + (push.sqrMagnitude > 0.01f ? Loc.T(", weicht aus", ", evading") : "");
        }

        /// <summary>Wie weit Gegner uns gerade orten können (Spielwert, Meter), mit Sicherheitsaufschlag.</summary>
        double SensorRadius(PlayerSpacecraft ship)
        {
            try { return ship != null ? ship.DODJNPKPIIJ * 1.15 : 20000; } catch { return 20000; }
        }

        double HazardRadius(Contact c, double sensor)
        {
            string n = (c.Name ?? "").ToLowerInvariant();
            foreach (var kind in AvoidKinds)
                if (n.Contains(kind))
                    return n.Contains("mine") ? 5000 : n.Contains("anomal") ? 6000 : c.Category == Sector.HHHDDNLEDBN.Enemy ? sensor : 4000;
            if (StayOutOfSensors && c.Category == Sector.HHHDDNLEDBN.Enemy) return sensor;
            return 0;
        }

        static Contact Find(List<Contact> contacts, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var c in contacts) if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        static Contact Nearest(List<Contact> contacts, Func<Contact, bool> pred)
        {
            Contact best = null;
            foreach (var c in contacts) if (pred(c) && (best == null || c.DistanceKm < best.DistanceKm)) best = c;
            return best;
        }

        static Vector2 Flat(Vector3d v) => new Vector2((float)v.x, (float)v.z);
        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Kurswinkel für die Bot-Steuer setzen (bzw. freigeben).</summary>
        static void SetManualHeading(bool on, float degrees)
        {
            try
            {
                var bb = CommandManager.JOMJCMPJGEK != null ? CommandManager.JOMJCMPJGEK.AACGBEMCAGH : null;
                if (bb == null) return;
                OJADMPIBCFG v;
                if (bb.TryGetValue(IAHDEGHOPAK.IsManualHeading, out v)) v.bVal = on;
                if (bb.TryGetValue(IAHDEGHOPAK.ManualHeading, out v)) { v.fVal = degrees; v.bVal = on; }
                if (bb.TryGetValue(IAHDEGHOPAK.CanSetYaw, out v)) v.bVal = on;
            }
            catch (Exception e) { Plugin.Log.LogDebug("Kurs nicht gesetzt: " + e.Message); }
        }
    }
}
