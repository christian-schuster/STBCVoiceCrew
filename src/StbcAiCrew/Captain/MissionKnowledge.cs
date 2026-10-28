using System.Globalization;
using System.Text.RegularExpressions;

namespace StbcAiCrew.Captain
{
    /// <summary>
    /// Missionswissen des First Officers, gewonnen aus den Missionsskripten des Spiels (Kampagne M0–M5 und alle prozeduralen
    /// Missionen: Rescue, Recover, Research, Intercept, Defend, Explore, Borg). Die Skripte setzen Ziele und Teilziele mit
    /// festen englischen Texten; die Teilziele sind die eigentlichen Schrittanweisungen ("Approach to within 10km …",
    /// "Scan for life signs …", "Do not expect them to lower their shields …"). Die Regeln werten zuerst das erste offene
    /// Teilziel aus, dann das Ziel selbst, und liefern Art, Reichweite und einen Sonderhinweis.
    /// Neue Ziele stehen mit Text und erkannter Art im Log ("First Officer Ziel: …").
    /// </summary>
    public static class MissionKnowledge
    {
        sealed class Rule
        {
            public Regex Text;
            public GoalKind? Kind;   // null = Art bleibt, nur Hinweis/Reichweite
            public double Reach;     // km bis zum Ziel, 0 = Standard der Art
            public string Note;      // Sonderhinweis (OfficerLines "note_…")
            public string Subsystem; // Stören: engines, comms, weapons, shields
            public Rule(string pattern, GoalKind? kind, double reach = 0, string note = null, string subsystem = null)
            {
                Text = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Kind = kind; Reach = reach; Note = note; Subsystem = subsystem;
            }
        }

        // Reihenfolge zählt: die erste passende Regel gilt.
        static readonly Rule[] Rules =
        {
            // Warten (Skript läuft von selbst weiter) – außer es wartet auf uns
            new Rule(@"waiting for tactical scan", GoalKind.Scan, 12, "note_anomalies"),
            new Rule(@"\bawait\b|arriving shortly|please wait|wait for .*(complete|survey)|^status:|survey underway|will be installed after", GoalKind.Wait, 0, "note_wait"),
            new Rule(@"time until|minimum time until|reactivate once|begun regenerating", GoalKind.Wait),

            // Flucht: Stern bricht aus, Explosion, Verfolgung
            new Rule(@"warp out before|escape the erupting star|escape protomatter explosion|reach edge of nebula to escape|impulse to nebula terminus to escape", GoalKind.Escape, 0, "note_escape"),
            new Rule(@"use impulse to temporarily escape", GoalKind.Escape, 0, "note_impulse_escape"),
            new Rule(@"minimum safe distance (\d+)", null, 0, "note_min_distance"),
            new Rule(@"disengage at your discretion", GoalKind.Escape),

            // Tarnung
            new Rule(@"avoid (being )?detect", null, 0, "note_stealth"),   // Art kommt vom Ziel ("Scan Transport ship")
            new Rule(@"rescue as many as you can, then warp out", GoalKind.Rescue, 3, "note_rescue_then_warp"),

            // Ruf annehmen
            new Rule(@"accept (the )?(incoming )?hail", GoalKind.Hail, 0, "note_hail"),

            // Teilchen sammeln: durch die Anomalie fliegen, schnell
            new Rule(@"fly through the anomaly|ramscoop|harvest|bussard collector|gather particles", GoalKind.Gather, 0, "note_gather"),
            new Rule(@"fly within (\d+) ?km of planet killer", GoalKind.Gather, 0, "note_planet_killer"),

            // Energiewelle
            new Rule(@"raise shields to avoid energy wave", null, 0, "note_raise_shields"),
            new Rule(@"lower shields and de-?allocate power", null, 0, "note_lower_shields"),

            // Nur beobachten
            new Rule(@"scan data only - do not transport|do not transport", GoalKind.Scan, 15, "note_scan_only"),
            new Rule(@"do not engage|full scan data only", GoalKind.Scan, 15, "note_scan_only"),

            // Stören / Hacken
            new Rule(@"disrupt the vessel'?s comms|disrupt .*comm|hack .*(sensor|network)|disrupt klingon sensors|point to hack into the network|infiltrate the klingon network", GoalKind.Disable, 10, null, "comms"),
            new Rule(@"disrupt weapons", GoalKind.Disable, 10, null, "weapons"),
            new Rule(@"\bintercept\b.*before it warps", GoalKind.Disable, 10, "note_intercept", "engines"),

            // Nicht zerstören, nur beschädigen / Schilde runter, dann beamen
            new Rule(@"damage .* to force them to eject|destroying the .* will destroy", GoalKind.Attack, 8, "note_no_destroy"),
            new Rule(@"force enemy to drop shields and transport", GoalKind.Attack, 3, "note_shields_then_beam"),
            new Rule(@"do not expect them to lower their shields", GoalKind.Rescue, 3, "note_shields_then_beam"),

            // Beamen
            new Rule(@"transport the fugitive|beam the fugitive|use the transporter|transport .*aboard|beam .*aboard|transport survivors|beam the survivors|transport the prototype|transport protomatter weapon|transport the crew|rescue .*escape pods|recover the stolen prototype|recover prototype", GoalKind.Rescue, 3),
            new Rule(@"get close to the fugitive", GoalKind.Travel, 3),

            // Scannen
            new Rule(@"scan for life signs|scan .*for (a single )?life sign|scan for survivors|any life sign detected", GoalKind.Scan, 12),
            new Rule(@"launch (a )?(long range )?probe|fire (a )?(long range )?probe|fire probe|long range scan", GoalKind.Scan, 0, "note_probe"),
            new Rule(@"tactical scan on anomalies|scan anomalies|impulse to different locations .* anomalies", GoalKind.Scan, 12, "note_anomalies"),
            new Rule(@"scan (the )?relay buoy|scan .*buoy|scan results will", GoalKind.Scan, 12),
            new Rule(@"scan ships in the system|scan asteroids|scan the freighters|scan convoy|find the asteroid that matches|locate the (contraband|fugitive|vessel)|identify the ship carrying", GoalKind.Scan, 12, "note_scan_all"),
            new Rule(@"search for freighters|look for freighters|shipping routes are marked", GoalKind.Travel, 0, "note_shipping_routes"),
            new Rule(@"search for a concealed location|search for a point", GoalKind.Travel, 0, "note_search_poi"),

            // Gebiet sichern, Gegner bekämpfen
            new Rule(@"secure the area|cannot take place while enemies|keep the area secure|engage any hostile|eliminate|destroy all (enemy|klingon)|defeat enemy force|destroy (klingon|romulan) attacking force|destroy enemy forces", GoalKind.Attack, 10, "note_secure"),
            new Rule(@"protect (ally|your ship)|defend the|keep hull of all convoy|hull above \d+|survive until", GoalKind.Defend, 6),

            // Treffpunkte, Stationen, Abstände
            new Rule(@"get within (\d+) ?km of the station and come to a stop", GoalKind.Travel, 0, "note_stop"),
            new Rule(@"(approach|get|fly) (to )?within (\d+(\.\d+)?) ?(km|m)\b", GoalKind.Travel),
            new Rule(@"rendezvous|approach the (contact|freighter|patrol point|survey point)|proceed to waypoint|secure (patrol|survey|dropoff) point", GoalKind.Travel, 5),
            new Rule(@"\bescort\b|\baccompany\b", GoalKind.Defend, 6),
            new Rule(@"\bdock\b|meet (up )?with", GoalKind.Travel, 3),

            // Allgemein
            new Rule(@"\b(cargo|pods?|debris|wreckage|canisters?|containers?)\b", GoalKind.Rescue, 3),
            new Rule(@"\b(anomaly|nebula|ion storm|radiation)\b.*\b(scan|investigate|study)\b|\b(scan|investigate|study)\b.*\b(anomaly|nebula|ion storm|radiation)\b", GoalKind.Scan, 15),
        };

        static readonly Regex Within = new Regex(@"within (\d+(?:\.\d+)?) ?(km|m)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex MinDist = new Regex(@"minimum safe distance (\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Missionswissen auf ein Ziel anwenden: Art, Reichweite, Sonderhinweis, Mindestabstand, optional.</summary>
        public static void Apply(Goal g)
        {
            g.Optional = Regex.IsMatch(g.English ?? "", @"^\s*optional\b", RegexOptions.IgnoreCase);
            // Erst das erste offene Teilziel (konkrete Anweisung), dann das Ziel selbst
            foreach (var sub in g.SubTexts) if (ApplyText(g, sub)) return;
            ApplyText(g, g.English ?? "");
        }

        static bool ApplyText(Goal g, string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var r in Rules)
            {
                if (!r.Text.IsMatch(text)) continue;
                if (r.Kind.HasValue) g.Kind = r.Kind.Value;
                if (r.Note != null) g.Note = r.Note;
                if (r.Subsystem != null) g.Subsystem = r.Subsystem;
                g.Reach = r.Reach;
                var w = Within.Match(text);
                if (w.Success) g.Reach = double.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture);   // "10m" in den Skripten meint 10 km
                var m = MinDist.Match(text);
                if (m.Success) g.MinDistance = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                // Nur ein Hinweis (ohne Art)? Dann weiter nach der Art suchen, Hinweis bleibt
                if (!r.Kind.HasValue) continue;
                return true;
            }
            return false;
        }
    }
}
