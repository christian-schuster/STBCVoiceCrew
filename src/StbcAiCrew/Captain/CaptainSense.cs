using System;
using System.Collections.Generic;
using Map;
using Mission.Scripting.Framework;
using UnityEngine;

namespace StbcAiCrew.Captain
{
    /// <summary>Art eines Missionsziels (aus dem englischen Originaltext erkannt, gilt so in jeder Spielsprache).</summary>
    public enum GoalKind { Travel, Scan, Attack, Disable, Rescue, Defend, Hail, Wait, Escape, Gather, Stealth }

    /// <summary>Ein offenes Missionsziel, so wie es die Captain-Konsole zeigt.</summary>
    public sealed class Goal
    {
        public string Text;          // in der Spielsprache (für die Anzeige)
        public string English;       // Originaltext (für die Erkennung)
        public GoalKind Kind;
        public SystemTransform Target;
        public SystemRoot System;    // Sternsystem des Ziels (null = unbekannt)
        public string SystemName;
        public int Count, Total;     // Zähler ("Überlebende 3/5"), Total 0 = keiner
        public int OpenSubGoals;
        /// <summary>Offene Teilziele (Originaltext), in der Reihenfolge der Konsole: die eigentlichen Schrittanweisungen.</summary>
        public List<string> SubTexts = new List<string>();
        public bool HasTimer;
        /// <summary>Reichweite aus dem Missionswissen (km), 0 = Standard der Art.</summary>
        public double Reach;
        /// <summary>Missionswissen: Sonderhinweis (Schlüssel in OfficerLines, "note_…"), Mindestabstand, Teilsystem fürs Stören.</summary>
        public string Note;
        public double MinDistance;
        public string Subsystem;
        /// <summary>Optionales Ziel ("OPTIONAL: …"): erst, wenn nichts anderes ansteht.</summary>
        public bool Optional;
        public string Key => English + "|" + (Target != null ? Target.GetInstanceID().ToString() : "") + "|" + SystemName;
    }

    /// <summary>
    /// Was der Captain wissen kann: Missionsziele (Captain-Konsole), Sternsysteme (Warpkarte), Rufe, Alarmstatus und wer an
    /// welcher Station sitzt. Alles aus dem laufenden Spiel gelesen, nichts verändert.
    /// </summary>
    public static class CaptainSense
    {
        // ---------------- Missionsziele ----------------

        public static List<Goal> OpenGoals()
        {
            var list = new List<Goal>();
            try
            {
                var mm = Mission.MissionManager.IEFBKOMBFGO;
                var dirs = mm != null ? mm.CBAADNDACIO : null;
                if (dirs == null) return list;
                foreach (var d in dirs)
                {
                    if (d == null || d.DKOOMIPABAC.ToString() != "Normal") continue;   // Normal = offen, sonst Completed/Failed
                    var g = new Goal
                    {
                        Text = GameBridge.StripLocId(d.DescriptionString) ?? "",
                        English = GameBridge.EnglishOf(d.DescriptionString) ?? "",
                        Target = d.Target ?? d.Body,
                        SystemName = GameBridge.StripLocId(d.SystemName),
                        Count = d.CurrentCount,
                        Total = d.ShowCounter ? d.TotalCount : 0,
                        HasTimer = d.ShowTimer
                    };
                    if (d.SubDirectives != null)
                        foreach (var s in d.SubDirectives)
                            if (s != null && !s.Completed && !s.Failed)
                            {
                                g.OpenSubGoals++;
                                string t = GameBridge.EnglishOf(s.DescriptionString);
                                if (!string.IsNullOrEmpty(t)) g.SubTexts.Add(t);
                            }
                    try { g.System = g.Target != null ? g.Target.System : null; } catch { }
                    if (g.System == null && !string.IsNullOrEmpty(d.SystemName)) g.System = FindSystem(d.SystemName);
                    if (g.System == null)
                    {
                        // "Warp to the MALKEV System": Name steht nur im Text
                        var m = System.Text.RegularExpressions.Regex.Match(g.English, @"warp to (?:the )?(.+?) system", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success) g.System = FindSystem(m.Groups[1].Value);
                    }
                    g.Kind = Classify(g.English + " " + (d.ItemName ?? ""));
                    MissionKnowledge.Apply(g);
                    list.Add(g);
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug("Missionsziele nicht lesbar: " + e.Message); }
            return list;
        }

        /// <summary>Art des Ziels aus dem englischen Text ("Scan the …", "Rescue the survivors", "Destroy …").</summary>
        public static GoalKind Classify(string english)
        {
            string s = (english ?? "").ToLowerInvariant();
            if (Has(s, "disable", "cripple", "immobili", "disrupt")) return GoalKind.Disable;
            if (Has(s, "rescue", "beam", "transport", "evacuat", "survivor", "retrieve", "recover", "collect", "cargo", "pod", "pick up", "colonist", "crew"))
                return GoalKind.Rescue;
            if (Has(s, "destroy", "eliminate", "defeat", "attack", "neutrali", "engage", "fight", "kill", "take out")) return GoalKind.Attack;
            if (Has(s, "defend", "protect", "escort", "guard", "survive", "hold off", "repel")) return GoalKind.Defend;
            if (Has(s, "scan", "investigate", "analy", "survey", "examine", "search", "locate", "identify", "inspect", "study")) return GoalKind.Scan;
            if (Has(s, "hail", "make contact", "contact the", "respond to", "answer the")) return GoalKind.Hail;
            if (Has(s, "wait", "standby", "stand by")) return GoalKind.Wait;
            return GoalKind.Travel;
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }

        // ---------------- Sternsysteme ----------------

        static Sector.EGGMAHHMNCE Map
        {
            get
            {
                try { var mm = Mission.MissionManager.IEFBKOMBFGO as Mission.MissionManager; return mm != null ? mm.GCDDPJCIJOK : null; }
                catch { return null; }
            }
        }

        /// <summary>Sternsystem, in dem die Aegis gerade ist.</summary>
        public static SystemRoot CurrentSystem
        {
            get { try { var m = Map; return m != null ? m.KALBLMGFNIN : null; } catch { return null; } }
        }

        public static SystemRoot FindSystem(string name)
        {
            try
            {
                var m = Map;
                if (m == null || m.MLHNDAEKLHO == null || string.IsNullOrEmpty(name)) return null;
                string want = GameBridge.StripLocId(name);
                foreach (var s in m.MLHNDAEKLHO)
                    if (s != null && (s.SystemName == name || string.Equals(GameBridge.StripLocId(s.SystemName), want, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(SVR.Localization.GetLocalizedText(s.SystemName), want, StringComparison.OrdinalIgnoreCase))) return s;
            }
            catch { }
            return null;
        }

        /// <summary>Irgendein anderes Sternsystem (Fluchtziel).</summary>
        public static SystemRoot OtherSystem(SystemRoot here)
        {
            try
            {
                var m = Map;
                if (m == null || m.MLHNDAEKLHO == null) return null;
                foreach (var s in m.MLHNDAEKLHO) if (s != null && s != here) return s;
            }
            catch { }
            return null;
        }

        public static string NameOf(SystemRoot s) => s == null ? "?" : GameBridge.StripLocId(s.SystemName);

        /// <summary>Warpziel auf der Karte setzen (wie der Captain auf der Warpkarte).</summary>
        public static bool SetWarpTarget(SystemRoot s)
        {
            try
            {
                var ship = GameBridge.PlayerShip;
                var helm = ship != null ? ship.GetComponent<Crew.ConsoleHelmShipInterface>() : null;
                if (helm == null || s == null) return false;
                if (helm.LLGKIKMABOJ != s) helm.LLGKIKMABOJ = s;   // WarpTarget
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Warpziel nicht gesetzt: " + e.Message); return false; }
        }

        /// <summary>Kann die Steuer zum gesetzten Warpziel springen (Ziel gesetzt, anderes System, freigegeben)? null = unbekannt.</summary>
        public static bool? CanWarp
        {
            get
            {
                try { var ship = GameBridge.PlayerShip; var helm = ship != null ? ship.GetComponent<Crew.ConsoleHelmShipInterface>() : null; return helm != null ? helm.OIMCGDEPIKM : (bool?)null; }
                catch { return null; }
            }
        }

        /// <summary>Ausrichtung auf das Warpziel (1 = genau, wie die Steuerkonsole sie misst), null = unbekannt.</summary>
        public static float? WarpAlignment
        {
            get
            {
                try { var ship = GameBridge.PlayerShip; var helm = ship != null ? ship.GetComponent<Crew.ConsoleHelmShipInterface>() : null; return helm != null && helm.LLGKIKMABOJ != null ? helm.DNBAEBBOIDI() : (float?)null; }
                catch { return null; }
            }
        }

        public static SystemRoot WarpTarget
        {
            get
            {
                try { var ship = GameBridge.PlayerShip; var helm = ship != null ? ship.GetComponent<Crew.ConsoleHelmShipInterface>() : null; return helm != null ? helm.LLGKIKMABOJ : null; }
                catch { return null; }
            }
        }

        // ---------------- Captain-Konsole ----------------

        static ConsoleCaptain _console;
        static ConsoleCaptain Console
        {
            get
            {
                if (_console == null) { try { _console = UnityEngine.Object.FindObjectOfType<ConsoleCaptain>(); } catch { } }
                return _console;
            }
        }

        public static bool HailPending
        {
            get
            {
                try { var ship = GameBridge.PlayerShip; var c = ship != null ? ship.GetComponent<Crew.ConsoleCaptainShipInterface>() : null; return c != null && c.NBDOCPPOBLD; }
                catch { return false; }
            }
        }

        public static bool AnswerHail()
        {
            try { var c = Console; if (c == null) return false; c.ReplyToHail(); return true; }
            catch (Exception e) { Plugin.Log.LogWarning("Ruf nicht angenommen: " + e.Message); return false; }
        }

        public static bool RedAlert
        {
            get { try { var c = Console; return c != null && c.FNGDCHIOCGC; } catch { return false; } }
        }

        public static void SetRedAlert(bool on)
        {
            try { var c = Console; if (c != null && c.FNGDCHIOCGC != on) c.ToggleRedAlert(); }
            catch (Exception e) { Plugin.Log.LogWarning("Alarm nicht geschaltet: " + e.Message); }
        }

        // ---------------- Tutorial ----------------

        static System.Reflection.PropertyInfo _tutProp;
        static Type _tutType;
        static bool _tutSearched, _tut;
        static float _tutNext;

        /// <summary>Läuft das Tutorial? Dort führt das Spiel selbst; der First Officer hält sich raus.</summary>
        public static bool InTutorial
        {
            get
            {
                if (Time.time < _tutNext) return _tut;
                _tutNext = Time.time + 5f;
                try
                {
                    if (!_tutSearched)
                    {
                        _tutSearched = true;
                        Type[] types;
                        try { types = typeof(ConsoleCaptain).Assembly.GetTypes(); }
                        catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                        foreach (var t in types)
                        {
                            var p = t != null ? t.GetProperty("IsTutorialInProgress", all) : null;
                            if (p == null || p.PropertyType != typeof(bool)) continue;
                            _tutType = t; _tutProp = p;
                            Plugin.Log.LogInfo("Tutorial-Erkennung: " + t.FullName + "." + p.Name);
                            break;
                        }
                    }
                    _tut = false;
                    if (_tutProp == null) return _tut;
                    var get = _tutProp.GetGetMethod(true);
                    object owner = null;
                    if (!get.IsStatic)
                    {
                        if (!typeof(UnityEngine.Object).IsAssignableFrom(_tutType)) return _tut;
                        owner = UnityEngine.Object.FindObjectOfType(_tutType);
                        if (owner == null) return _tut;
                    }
                    _tut = (bool)get.Invoke(owner, null);
                }
                catch { _tut = false; }
                return _tut;
            }
        }

        // ---------------- Wer sitzt wo ----------------

        /// <summary>Sitzt ein Mensch an der Captain-Konsole? (Sonst führt die KI.)</summary>
        public static bool HumanAtCaptainConsole()
        {
            try
            {
                var pm = NovaPlayerManager.IEFBKOMBFGO;
                if (pm == null) return true;
                foreach (var p in pm.GetPlayers()) if (p != null && p.GHJLPNMIOAJ is ConsoleCaptain) return true;
                return false;
            }
            catch { return true; }
        }

        /// <summary>Station, an der der lokale Spieler gerade sitzt (null = unbekannt).</summary>
        public static Station? LocalSeat()
        {
            try
            {
                var p = LocalPlayer();
                var c = p != null ? p.GHJLPNMIOAJ : null;
                if (c is ConsoleCaptain) return Station.Captain;
                if (c is ConsoleHelm) return Station.Helm;
                if (c is ConsoleTactical) return Station.Tactical;
                if (c is ConsoleEngineering) return Station.Engineering;
            }
            catch { }
            return null;
        }

        static NovaPlayerInfo LocalPlayer()
        {
            var pm = NovaPlayerManager.IEFBKOMBFGO;
            var me = NDIOGIGMPBH.DIMFCNEMMPD;
            return pm != null && me != null ? pm.GetInfoForPlayer(me) : null;
        }

        /// <summary>Den lokalen Spieler an eine andere Station setzen (eingebauter Stationswechsel des Spiels).</summary>
        public static bool MoveLocalPlayerTo(Station st)
        {
            try
            {
                var sm = NovaSpawnManager.IEFBKOMBFGO as NovaSpawnManager;
                if (sm == null) return false;
                FLCHMJJDKDL s = st == Station.Helm ? FLCHMJJDKDL.Helm : st == Station.Tactical ? FLCHMJJDKDL.Tactical : st == Station.Engineering ? FLCHMJJDKDL.Engineering : FLCHMJJDKDL.Captain;
                sm.SwapStations(s, true);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Stationswechsel fehlgeschlagen: " + e.Message); return false; }
        }
    }
}
