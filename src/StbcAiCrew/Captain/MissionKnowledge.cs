using System.Text.RegularExpressions;

namespace StbcAiCrew.Captain
{
    /// <summary>
    /// Missionswissen des Captains: Sonderregeln für bestimmte Missionsziele (englischer Originaltext), die der allgemeine
    /// Entscheidungsbaum nicht von selbst richtig einordnet. Wächst mit jeder Testrunde: neue Ziele stehen mit Text und
    /// erkannter Art im Log ("KI-Captain Ziel: …"), hängende Ziele mit einem Zustandsbericht ("KI-Captain hängt: …").
    /// </summary>
    public static class MissionKnowledge
    {
        sealed class Rule
        {
            public Regex Text;
            public GoalKind Kind;
            public double Reach;   // km bis zum Ziel, 0 = Standard der Art
            public Rule(string pattern, GoalKind kind, double reach = 0)
            {
                Text = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Kind = kind;
                Reach = reach;
            }
        }

        static readonly Rule[] Rules =
        {
            // Begleitschutz und Treffpunkte: in der Nähe bleiben, nicht nur hinfliegen
            new Rule(@"\bescort\b|\baccompany\b", GoalKind.Defend, 6),
            new Rule(@"\brendezvous\b|\bmeet (up )?with\b|\bdock\b", GoalKind.Travel, 3),
            // Bergen von Objekten im Raum (Fracht, Kapseln, Wrackteile) braucht Transporterreichweite
            new Rule(@"\b(cargo|pods?|debris|wreckage|canisters?|containers?)\b", GoalKind.Rescue, 3),
            // Abfangen = einholen und dann kampfunfähig machen
            new Rule(@"\bintercept\b", GoalKind.Disable, 8),
            // Gefahrenzonen untersuchen, ohne hineinzufliegen
            new Rule(@"\b(anomaly|nebula|ion storm|radiation)\b.*\b(scan|investigate|study)\b|\b(scan|investigate|study)\b.*\b(anomaly|nebula|ion storm|radiation)\b", GoalKind.Scan, 15),
        };

        /// <summary>Sonderregel für ein Ziel anwenden (Art und Reichweite), sonst unverändert lassen.</summary>
        public static double Apply(Goal g)
        {
            foreach (var r in Rules)
                if (r.Text.IsMatch(g.English ?? ""))
                {
                    g.Kind = r.Kind;
                    return r.Reach;
                }
            return 0;
        }
    }
}
