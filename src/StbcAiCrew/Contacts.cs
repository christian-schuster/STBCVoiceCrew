using System;
using System.Collections.Generic;
using System.Text;
using Mission.Scripting;
using Sector;
using Sector.Spacecraft;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>Ein Kontakt, so wie ihn die Konsolen zeigen.</summary>
    public sealed class Contact
    {
        public string Name;
        public HHHDDNLEDBN Category;
        public double DistanceKm;
        public string Bearing;
        public bool Helm, Tactical, Engineering, Captain;
        public JMHPEPBOFIB Detectable;

        public string KindDe()
        {
            switch (Category)
            {
                case HHHDDNLEDBN.Friendly: return "Freund";
                case HHHDDNLEDBN.Enemy: return "Feind";
                case HHHDDNLEDBN.Neutral: return "neutral";
                default: return "?";
            }
        }

        public bool VisibleTo(Station st)
        {
            switch (st)
            {
                case Station.Helm: return Helm;
                case Station.Tactical: return Tactical;
                case Station.Engineering: return Engineering;
                case Station.Captain: return Captain;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Kontakte und gewählte Ziele, genau wie die Konsolen des Spiels sie zeigen: nur was auf der Navigationskarte
    /// erscheint (NavMapManager.BodyInfos, wie das Zielpanel des Spiels), je Station nur, was dort auswählbar ist
    /// (_helmTargetable usw.), Name wie auf dem Bildschirm (SystemTransform.BaseName + Gruppe + Nummer).
    /// </summary>
    public static class Contacts
    {
        public static List<Contact> Read()
        {
            var list = new List<Contact>();
            var nav = NavMapManager.IEFBKOMBFGO;
            if (nav == null || nav.BodyInfos == null) return list;
            SystemTransform own = null;
            foreach (var bi in nav.BodyInfos)
                if (bi != null && bi.Body != null && bi.Body == nav.OwnShip) own = bi.Transform;

            foreach (var bi in nav.BodyInfos)
            {
                try
                {
                    if (bi == null || bi.Body == null || bi.Body == nav.OwnShip || !bi.Body.PPCACELIBNF) continue;
                    var t = bi.Body.GetDetectable() as DIENHALJFIO;
                    if (t == null || t.AHMPFHDGMKJ is FDCEDEFLFPI) continue;
                    var c = new Contact
                    {
                        Detectable = t,
                        Name = NameOf(t),
                        Category = t.GetCategory(),
                        Captain = t.IBJCLAGDNAH,
                        Helm = t.NFAACJCEHJL,
                        Tactical = t.HDELICIMMOE,
                        Engineering = t.IIIBGCMJBJA,
                        DistanceKm = -1
                    };
                    var st = bi.Transform ?? (t.AHMPFHDGMKJ != null ? t.AHMPFHDGMKJ.ALPAOOKFIEE : null);
                    if (own != null && st != null)
                    {
                        var rel = st.Position - own.Position;
                        c.DistanceKm = rel.magnitude / 1000.0;
                        c.Bearing = BearingDe((Vector3)rel, own.Forward, own.Right);
                    }
                    if (!string.IsNullOrEmpty(c.Name)) list.Add(c);
                }
                catch { }
            }
            list.Sort((a, b) => a.DistanceKm.CompareTo(b.DistanceKm));
            return list;
        }

        /// <summary>Angezeigter Name eines Ziels, wie TargetableComponent.SetTextWithName ihn bildet.</summary>
        public static string NameOf(JMHPEPBOFIB d)
        {
            try
            {
                var st = d != null && d.AHMPFHDGMKJ != null ? d.AHMPFHDGMKJ.ALPAOOKFIEE : null;
                if (st == null) return null;
                string n = GameBridge.StripLocId(st.BaseName);
                if (string.IsNullOrEmpty(n)) n = GameBridge.StripLocId(st.Name);
                if (string.IsNullOrEmpty(n)) return null;
                if (st.NameGroup != AKFJHINCLIK.None)
                {
                    string g = null;
                    try { g = GameBridge.StripLocId(NAEPJGAFAHA.GetString(st.NameGroup)); } catch { }
                    if (!string.IsNullOrEmpty(g)) n += " " + g;
                }
                if (st.GroupCounter > 0) n += " " + st.GroupCounter;
                return n.Trim();
            }
            catch { return null; }
        }

        static string BearingDe(Vector3 rel, Vector3 fwd, Vector3 right)
        {
            float angle = Mathf.Atan2(Vector3.Dot(rel, right), Vector3.Dot(rel, fwd)) * Mathf.Rad2Deg;   // 0 = voraus, + = rechts
            float a = Mathf.Abs(angle);
            string side = angle >= 0 ? "rechts" : "links";
            if (a < 20f) return "voraus";
            if (a < 70f) return side + " voraus";
            if (a < 110f) return side;
            if (a < 160f) return side + " achtern";
            return "achtern";
        }

        /// <summary>Das an einer Konsole gewählte Ziel (ConsoleShipInterface.GetTarget, je Station eigenes).</summary>
        public static string TargetOf(Station station)
        {
            try
            {
                var ship = GameBridge.PlayerShip;
                if (ship == null) return null;
                ConsoleShipInterface console = null;
                switch (station)
                {
                    case Station.Helm: console = ship.GetComponent<Crew.ConsoleHelmShipInterface>(); break;
                    case Station.Tactical: console = ship.GetComponent<Crew.ConsoleTacticalShipInterface>(); break;
                    case Station.Engineering: console = ship.GetComponent<Crew.ConsoleEngineeringShipInterface>(); break;
                    case Station.Captain: console = ship.GetComponent<Crew.ConsoleCaptainShipInterface>(); break;
                }
                var t = console != null ? console.GetTarget() : null;
                if (t == null) return null;
                return NameOf(t) ?? "unbekanntes Objekt";
            }
            catch { return null; }
        }

        /// <summary>Kontaktliste für den Prompt einer Station (nur, was diese Konsole zeigt).</summary>
        public static string ForStation(List<Contact> contacts, Station st, int max = 8)
        {
            if (contacts == null) return null;
            var sb = new StringBuilder();
            int n = 0;
            foreach (var c in contacts)
            {
                if (!c.VisibleTo(st)) continue;
                if (n++ >= max) break;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(c.Name).Append(" (").Append(c.KindDe());
                if (c.DistanceKm >= 0) sb.Append(", ").Append(c.DistanceKm.ToString(c.DistanceKm < 10 ? "0.0" : "0")).Append(" km");
                if (st == Station.Helm && c.Bearing != null) sb.Append(", ").Append(c.Bearing);
                sb.Append(')');
            }
            return n == 0 ? "keine" : sb.ToString();
        }

        /// <summary>Alle angezeigten Namen (für den Whisper-Wortschatz).</summary>
        public static string Names(List<Contact> contacts)
        {
            var seen = new HashSet<string>();
            var parts = new List<string>();
            foreach (var c in contacts) if (seen.Add(c.Name)) parts.Add(c.Name);
            return string.Join(", ", parts.ToArray());
        }
    }
}
