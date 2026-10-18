using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Diagnosewerkzeug für das Reverse Engineering: schreibt alle einfachen Felder (Zahlen, Bools, Strings)
    /// der Komponenten am Spielerschiff in eine Datei. Zwei Dumps vergleichen (z. B. vor/nach einem Treffer)
    /// zeigt, welches obfuskierte Feld Hülle, Schilde, Energie usw. enthält.
    /// </summary>
    public static class StateProbe
    {
        public static string Dump(string dir)
        {
            var ship = GameBridge.PlayerShip;
            if (ship == null) return "Kein Spielerschiff (nicht in einer Mission).";

            var sb = new StringBuilder();
            sb.Append("# Zustand ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(", Spielzeit ").Append(Time.time).Append('\n');
            foreach (var comp in ship.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp is Transform) continue;
                var type = comp.GetType();
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine")) continue;
                sb.Append("\n## ").Append(comp.gameObject.name).Append(" / ").Append(type.FullName).Append('\n');
                for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        var ft = f.FieldType;
                        if (!(ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || ft == typeof(Vector3))) continue;
                        object v;
                        try { v = f.GetValue(comp); } catch { continue; }
                        sb.Append(t.Name).Append('.').Append(f.Name).Append(" (").Append(ft.Name).Append(") = ").Append(v).Append('\n');
                    }
                }
            }

            // Schwarzes Brett der Bot-Crew: alle Einträge mit allen Feldern (zur Prüfung der Deutung)
            try
            {
                var bb = AI.CommandManager.JOMJCMPJGEK;
                if (bb != null && bb.AACGBEMCAGH != null)
                {
                    sb.Append("\n## Schwarzes Brett (CommandManager.JOMJCMPJGEK)\n");
                    foreach (var kv in bb.AACGBEMCAGH)
                    {
                        var v = kv.Value;
                        if (v == null) continue;
                        sb.Append(kv.Key).Append(": bVal=").Append(v.bVal).Append(" iVal=").Append(v.iVal).Append(" fVal=").Append(v.fVal)
                          .Append(" subsystem=").Append(v.subsystem).Append(" moveMode=").Append(v.moveMode)
                          .Append(" targetable=").Append(v.targetable != null ? "ja" : "nein").Append(" body=").Append(v.body != null ? v.body.AKDKEDEMFDJ : "-").Append('\n');
                    }
                }
                var st = ShipStatus.Read();
                if (st != null)
                    foreach (var s in new[] { Station.Helm, Station.Tactical, Station.Engineering })
                        sb.Append("\n## Konsole ").Append(s).Append(": ").Append(st.ForStation(s)).Append(" | offen: ").Append(st.PendingFor(s) ?? "-");
            }
            catch (Exception e) { sb.Append("\nSchwarzes Brett nicht lesbar: ").Append(e.Message).Append('\n'); }

            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "zustand_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
