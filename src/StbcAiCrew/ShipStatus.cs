using System;
using System.Text;
using Sector;
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

        // Aus dem "Schwarzen Brett" der Bot-Crew (CommandManager.JOMJCMPJGEK): Zustand und offene Befehle
        public float Speed = -1f;
        public bool FullStop, Evading, WarpPreparing, WarpEngaged, ImpulsePreparing, ImpulseEngaged;
        public int PowerPhasers = -1, PowerShields = -1, PowerEngines = -1;
        public string RepairTarget;
        public string TargetName;   // Ziel aus den Befehlen der Bot-Crew (Ersatz, falls eine Konsole keins liefert)
        public string HelmTarget, TacticalTarget;   // an der jeweiligen Konsole gewählt
        public System.Collections.Generic.List<Contact> ContactList;
        public bool Moving;
        public double MeasuredSpeed = -1;   // Positionsänderung je Sekunde (Systemeinheiten), zur Kalibrierung
        public readonly System.Collections.Generic.Dictionary<Station, System.Collections.Generic.List<string>> PendingOrders =
            new System.Collections.Generic.Dictionary<Station, System.Collections.Generic.List<string>>();

        // Offene Befehle an die Stationen: das Spiel setzt das Flag, wenn ein Befehl erteilt ist, der Bot löscht es nach Ausführung
        static readonly object[][] OrderFlags =
        {
            new object[] { AI.IAHDEGHOPAK.CanRaiseShields, Station.Tactical, "Schilde hoch" },
            new object[] { AI.IAHDEGHOPAK.CanLowerShields, Station.Tactical, "Schilde runter" },
            new object[] { AI.IAHDEGHOPAK.CanArmTorpedoes, Station.Tactical, "Torpedos scharf machen" },
            new object[] { AI.IAHDEGHOPAK.CanDisarmTorpedoes, Station.Tactical, "Torpedos entschärfen" },
            new object[] { AI.IAHDEGHOPAK.CanFirePhasersOnTarget, Station.Tactical, "Phaser auf das Ziel feuern" },
            new object[] { AI.IAHDEGHOPAK.CanFireTorpedoesOnTarget, Station.Tactical, "Torpedos auf das Ziel feuern" },
            new object[] { AI.IAHDEGHOPAK.CanScanTarget, Station.Tactical, "Ziel scannen" },
            new object[] { AI.IAHDEGHOPAK.CanModulateShields, Station.Tactical, "Schildfrequenz modulieren" },
            new object[] { AI.IAHDEGHOPAK.CanMoveToTarget, Station.Helm, "Ziel anfliegen" },
            new object[] { AI.IAHDEGHOPAK.CanSetYaw, Station.Helm, "Kurs ändern" },
            new object[] { AI.IAHDEGHOPAK.CanSetImpulseTarget, Station.Helm, "Impulssprung-Ziel setzen" },
            new object[] { AI.IAHDEGHOPAK.CanSetWarpDirection, Station.Helm, "Warpkurs setzen" },
            new object[] { AI.IAHDEGHOPAK.CanSetPhaserPower, Station.Engineering, "Energie auf Phaser" },
            new object[] { AI.IAHDEGHOPAK.CanSetShieldPower, Station.Engineering, "Energie auf Schilde" },
            new object[] { AI.IAHDEGHOPAK.CanSetEnginePower, Station.Engineering, "Energie auf Antrieb" },
            new object[] { AI.IAHDEGHOPAK.CanRepairSystems, Station.Engineering, "Reparatur" },
            new object[] { AI.IAHDEGHOPAK.CanTeleportTarget, Station.Engineering, "Transporter" },
        };

        static System.Reflection.FieldInfo _crewOrdersField;

        void ReadBlackboard()
        {
            var bb = AI.CommandManager.JOMJCMPJGEK;
            var d = bb != null ? bb.AACGBEMCAGH : null;
            if (d == null) return;
            Func<AI.IAHDEGHOPAK, AI.OJADMPIBCFG> get = k => { AI.OJADMPIBCFG v; return d.TryGetValue(k, out v) ? v : null; };
            Func<AI.IAHDEGHOPAK, bool> flag = k => { var v = get(k); return v != null && v.bVal; };

            var speed = get(AI.IAHDEGHOPAK.MoveSpeed);
            if (speed != null) Speed = speed.fVal;
            FullStop = flag(AI.IAHDEGHOPAK.FullStop);
            Evading = flag(AI.IAHDEGHOPAK.IsEvading);
            WarpPreparing = flag(AI.IAHDEGHOPAK.PrepareWarp);
            WarpEngaged = flag(AI.IAHDEGHOPAK.EngageWarp);
            ImpulsePreparing = flag(AI.IAHDEGHOPAK.PrepareImpulse);
            ImpulseEngaged = flag(AI.IAHDEGHOPAK.EngageImpulse);
            var pp = get(AI.IAHDEGHOPAK.PhaserPowerLevel); if (pp != null) PowerPhasers = pp.iVal;
            var ps = get(AI.IAHDEGHOPAK.ShieldPowerLevel); if (ps != null) PowerShields = ps.iVal;
            var pe = get(AI.IAHDEGHOPAK.EnginePowerLevel); if (pe != null) PowerEngines = pe.iVal;
            var rt = get(AI.IAHDEGHOPAK.RepairTarget);
            if (rt != null && rt.subsystem != BaseSpacecraft.HGKJKJMPFMB.EMPTY && rt.subsystem != BaseSpacecraft.HGKJKJMPFMB.CHOOSE)
                RepairTarget = SubsystemDe(rt.subsystem);

            foreach (var o in OrderFlags)
            {
                if (!flag((AI.IAHDEGHOPAK)o[0])) continue;
                var st = (Station)o[1];
                System.Collections.Generic.List<string> list;
                if (!PendingOrders.TryGetValue(st, out list)) PendingOrders[st] = list = new System.Collections.Generic.List<string>();
                list.Add((string)o[2]);
            }
        }

        void ReadTarget()
        {
            var crew = GameBridge.Crew;
            if (crew == null) return;
            if (_crewOrdersField == null) _crewOrdersField = HarmonyLib.AccessTools.Field(typeof(AI.USSAegisCrew), "NPKHBJCCMAE");
            var orders = _crewOrdersField != null ? _crewOrdersField.GetValue(crew) as AI.MNIJEDINGOI : null;
            var target = orders != null ? orders.Target : null;
            if (target == null) return;
            string name = GameBridge.StripLocId(target.AKDKEDEMFDJ);
            TargetName = string.IsNullOrEmpty(name) ? "unbekanntes Objekt" : name;
        }

        static string SubsystemDe(BaseSpacecraft.HGKJKJMPFMB s)
        {
            switch (s)
            {
                case BaseSpacecraft.HGKJKJMPFMB.Engines: return "Antrieb";
                case BaseSpacecraft.HGKJKJMPFMB.Shields: return "Schilde";
                case BaseSpacecraft.HGKJKJMPFMB.Phasers: return "Phaser";
                case BaseSpacecraft.HGKJKJMPFMB.Lauchers: return "Torpedorampen";
                case BaseSpacecraft.HGKJKJMPFMB.Sensors: return "Sensoren";
                case BaseSpacecraft.HGKJKJMPFMB.Hull: return "Hülle";
                case BaseSpacecraft.HGKJKJMPFMB.Warpcore: return "Warpkern";
                case BaseSpacecraft.HGKJKJMPFMB.TransporterRoom: return "Transporter";
                case BaseSpacecraft.HGKJKJMPFMB.ComputerCore: return "Computerkern";
                case BaseSpacecraft.HGKJKJMPFMB.Sickbay: return "Krankenstation";
                case BaseSpacecraft.HGKJKJMPFMB.ScienceLab: return "Wissenschaftslabor";
                default: return s.ToString();
            }
        }

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
                // Schildpunkte im Verhältnis zum Schild-Maximum (NPPHMOIOCJD, wie die Schild-Anzeige des Spiels), nicht zu den Trefferpunkten des Subsystems
                return sh != null && sh.NPPHMOIOCJD > 0.1f ? sh.DNMGBKPIOOC / sh.NPPHMOIOCJD * 100f : -1f;
            });
            try { s.ShieldState = ship.HBLINKJLOKB != null ? ship.HBLINKJLOKB.statusReport().ToString() : "?"; } catch { }
            try { s.TorpedoesFore = ship.HGGDEFFGBKA != null ? ship.HGGDEFFGBKA.TotalAmmo : -1; } catch { }
            try { s.TorpedoesAft = ship.OAPDHHGKBLJ != null ? ship.OAPDHHGKBLJ.TotalAmmo : -1; } catch { }
            try { s.ReadBlackboard(); } catch { }
            try { s.ReadTarget(); } catch { }
            s.HelmTarget = Contacts.TargetOf(Station.Helm);
            s.TacticalTarget = Contacts.TargetOf(Station.Tactical);
            try { s.ContactList = Contacts.Read(); } catch { }
            try { s.MeasureSpeed(ship); } catch { }
            return s;
        }

        static Vector3d _lastPos;
        static float _lastPosTime = -1f;
        static double _lastSpeed = -1;

        /// <summary>Tatsächliche Fahrt aus der Positionsänderung (die Sollfahrt im Schwarzen Brett blieb im Test bei 0).</summary>
        void MeasureSpeed(OBIMAHNLBCJ ship)
        {
            var body = ship as HOLJLCLENCA;
            var st = body != null ? body.ALPAOOKFIEE : null;
            if (st == null) return;
            var pos = st.Position;
            float now = Time.time;
            if (_lastPosTime >= 0f && now - _lastPosTime > 0.2f && now - _lastPosTime < 10f)
                _lastSpeed = (pos - _lastPos).magnitude / (now - _lastPosTime);
            if (_lastPosTime < 0f || now - _lastPosTime > 0.2f) { _lastPos = pos; _lastPosTime = now; }
            MeasuredSpeed = _lastSpeed;
            Moving = _lastSpeed > 1.0;
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
                    sb.Append("Ziel: ").Append(TacticalTarget ?? "keins gewählt")
                      .Append(", Schilde ").Append(P(Shields)).Append(" (").Append(ShieldStateDe()).Append(')')
                      .Append(", Phaser ").Append(P(Phasers))
                      .Append(", Torpedos vorn ").Append(N(TorpedoesFore)).Append(" / achtern ").Append(N(TorpedoesAft));
                    sb.Append("\nKontakte: ").Append(Contacts.ForStation(ContactList, Station.Tactical));
                    break;
                case Station.Engineering:
                    sb.Append("Hülle ").Append(P(Hull))
                      .Append(", Antrieb ").Append(P(Engines))
                      .Append(", Warpkern ").Append(P(WarpCore))
                      .Append(", Phaser ").Append(P(Phasers));
                    if (PowerEngines >= 0 || PowerShields >= 0 || PowerPhasers >= 0)
                        sb.Append(", Energiestufen Antrieb/Schilde/Phaser ").Append(N(PowerEngines)).Append('/').Append(N(PowerShields)).Append('/').Append(N(PowerPhasers));
                    if (RepairTarget != null) sb.Append(", Reparatur läuft an: ").Append(RepairTarget);
                    if (WarpPreparing) sb.Append(", Warpkern wird geladen");
                    break;
                case Station.Helm:
                    sb.Append("Ziel: ").Append(HelmTarget ?? "keins gewählt")
                      .Append(", Antrieb ").Append(P(Engines))
                      .Append(", Fahrt ").Append(FullStop ? "Stopp" : Moving ? "in Bewegung" : "steht")
                      .Append(", Sollfahrt ").Append(SpeedText());
                    if (Evading) sb.Append(", Ausweichmanöver aktiv");
                    if (ImpulsePreparing) sb.Append(", Impulssprung wird vorbereitet");
                    if (ImpulseEngaged) sb.Append(", Impulssprung läuft");
                    if (WarpPreparing) sb.Append(", Warp wird vorbereitet");
                    if (WarpEngaged) sb.Append(", Warp aktiv");
                    sb.Append("\nKontakte auf der Navigationskarte: ").Append(Contacts.ForStation(ContactList, Station.Helm));
                    break;
            }
            return sb.ToString();
        }

        string SpeedText()
        {
            if (Speed < 0f) return "?";
            if (Speed <= 1.05f) return Mathf.RoundToInt(Speed * 100f) + " % Impuls";
            return Speed.ToString("0.0");
        }

        /// <summary>Befehle, die an diese Station erteilt und noch nicht ausgeführt sind (so zeigt sie auch ihre Konsole).</summary>
        public string PendingFor(Station st)
        {
            System.Collections.Generic.List<string> list;
            return PendingOrders.TryGetValue(st, out list) && list.Count > 0 ? string.Join(", ", list.ToArray()) : null;
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
