using System;
using System.Text;
using Sector.Spacecraft;
using Sector.Spacecraft.Subsystems;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Liest den Zustand des eigenen Schiffs, mit denselben Formeln, die die Captain-Konsole des Spiels
    /// für "Hull Critical" und "Shield Collapse Imminent" nutzt (ConsoleCaptain.AANKNHNPNBF / ILLDNPKKNEE).
    /// Obfuskierte Member: Subsystem.AAHEMKKMKOH = Trefferpunkte, .HCHPFKBAIPJ = Maximum,
    /// Schilde.DNMGBKPIOOC = aktuelle Schildstärke, LauncherGroup.TotalAmmo = Torpedos.
    /// </summary>
    public sealed class ShipStatus
    {
        public float Hull = -1f, Shields = -1f, Engines = -1f, WarpCore = -1f, Phasers = -1f;
        public string ShieldState = "?";
        public int TorpedoesFore = -1, TorpedoesAft = -1;

        public static ShipStatus Read()
        {
            var ship = GameBridge.PlayerShip as OBIMAHNLBCJ;
            if (ship == null) return null;
            var s = new ShipStatus();
            s.Hull = Try(() => Percent(ship.GCAFAJGANIL));
            s.Engines = Try(() => Percent(ship.GCOAODPDBEI));
            s.WarpCore = Try(() => Percent(ship.OANIPEEJPKG));
            s.Phasers = Try(() => Percent(ship.BJGGBPPMGNK));
            s.Shields = Try(() =>
            {
                var sh = ship.HBLINKJLOKB;
                return sh != null && sh.HCHPFKBAIPJ > 0f ? sh.DNMGBKPIOOC / sh.HCHPFKBAIPJ * 100f : -1f;
            });
            try { s.ShieldState = ship.HBLINKJLOKB != null ? ship.HBLINKJLOKB.statusReport().ToString() : "?"; } catch { }
            try { s.TorpedoesFore = ship.HGGDEFFGBKA != null ? ship.HGGDEFFGBKA.TotalAmmo : -1; } catch { }
            try { s.TorpedoesAft = ship.OAPDHHGKBLJ != null ? ship.OAPDHHGKBLJ.TotalAmmo : -1; } catch { }
            return s;
        }

        static float Percent(PFKMFIIAAFC sub) =>
            sub != null && sub.HCHPFKBAIPJ > 0f ? sub.AAHEMKKMKOH / sub.HCHPFKBAIPJ * 100f : -1f;

        static float Try(Func<float> f)
        {
            try { return f(); } catch { return -1f; }
        }

        /// <summary>Nur die Werte, die die Konsole dieser Station anzeigt.</summary>
        public string ForStation(Station st)
        {
            var sb = new StringBuilder();
            switch (st)
            {
                case Station.Tactical:
                    sb.Append("Schilde ").Append(P(Shields)).Append(" (").Append(ShieldStateDe()).Append(')')
                      .Append(", Phaser ").Append(P(Phasers))
                      .Append(", Torpedos vorn ").Append(N(TorpedoesFore)).Append(" / achtern ").Append(N(TorpedoesAft));
                    break;
                case Station.Engineering:
                    sb.Append("Hülle ").Append(P(Hull))
                      .Append(", Antrieb ").Append(P(Engines))
                      .Append(", Warpkern ").Append(P(WarpCore))
                      .Append(", Phaser ").Append(P(Phasers));
                    break;
                case Station.Helm:
                    sb.Append("Antrieb ").Append(P(Engines));
                    break;
            }
            return sb.ToString();
        }

        static string N(int v) => v >= 0 ? v.ToString() : "?";

        string ShieldStateDe()
        {
            switch (ShieldState)
            {
                case "PoweringUp": return "fahren hoch";
                case "Up": return "oben";
                case "Recharge": return "laden nach";
                case "Depleted": return "zusammengebrochen";
                case "PoweredOff": return "unten";
                default: return ShieldState;
            }
        }

        static string P(float v) => v < 0f ? "?" : Mathf.RoundToInt(v) + " %";

        /// <summary>
        /// Vergleicht mit dem letzten Stand und erzeugt Meldungen, wie sie ein Offizier machen würde
        /// ("Hülle bei 50 %"), nur beim Überschreiten von Schwellen, damit nichts flutet.
        /// </summary>
        public static void ReportChanges(ShipStatus prev, ShipStatus now)
        {
            if (prev == null || now == null) return;
            Threshold("Hülle", prev.Hull, now.Hull, Station.Engineering);
            Threshold("Schilde", prev.Shields, now.Shields, Station.Tactical);
            Threshold("Antrieb", prev.Engines, now.Engines, Station.Engineering, Station.Helm);
            Threshold("Warpkern", prev.WarpCore, now.WarpCore, Station.Engineering);
            Threshold("Phaser", prev.Phasers, now.Phasers, Station.Engineering, Station.Tactical);
            if (prev.ShieldState != now.ShieldState && now.ShieldState != "?")
                EventLog.ToStation(Station.Tactical, "Anzeige: Schilde " + now.ShieldStateDe(), Priority.Important);
            if (prev.TorpedoesFore > 0 && now.TorpedoesFore == 0) EventLog.ToStation(Station.Tactical, "Anzeige: vordere Torpedos verbraucht", Priority.Important);
            if (prev.TorpedoesAft > 0 && now.TorpedoesAft == 0) EventLog.ToStation(Station.Tactical, "Anzeige: achtere Torpedos verbraucht", Priority.Important);
        }

        static readonly float[] Steps = { 75f, 50f, 25f, 10f };

        static void Threshold(string name, float before, float after, params Station[] seenBy)
        {
            if (before < 0f || after < 0f) return;
            string line = null;
            var prio = Priority.Important;
            foreach (var step in Steps)
            {
                if (before > step && after <= step) { line = "Anzeige: " + name + " unter " + step + " % (jetzt " + Mathf.RoundToInt(after) + " %)"; break; }
                if (before < step && after >= step && step >= 50f) { line = "Anzeige: " + name + " wieder über " + step + " % (jetzt " + Mathf.RoundToInt(after) + " %)"; prio = Priority.Background; break; }
            }
            if (line == null) return;
            foreach (var st in seenBy) EventLog.ToStation(st, line, prio);
        }
    }
}
