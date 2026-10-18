using System.Collections.Generic;
using AI;

namespace StbcAiCrew
{
    public enum Station { Captain, Helm, Tactical, Engineering }

    public sealed class CrewCommand
    {
        public readonly BHPPPGKNAHK GameCommand;   // obfuskiertes Befehls-Enum des Spiels
        public readonly Station Station;           // Station laut Spiel (Captain = Mehrstationen-Ablauf)
        public readonly Station Owner;             // welcher KI-Offizier ihn auslösen darf
        public readonly bool Major;                // weitreichend: eigenmächtig nur bei Gefahr bzw. auf Befehl
        public readonly string Description;
        public string Name => GameCommand.ToString();

        public CrewCommand(BHPPPGKNAHK cmd, Station station, string description, Station owner, bool major)
        {
            GameCommand = cmd;
            Station = station;
            Owner = owner;
            Major = major;
            Description = description;
        }
    }

    /// <summary>
    /// Alle Befehle, die die Bot-Crew ausführen kann – dieselben, die früher die Watson-Sprachsteuerung
    /// an CommandManager.TriggerCommand übergeben hat. Stationszuordnung entspricht
    /// CommandManager.LHPHCBLOOLE im Spielcode.
    /// </summary>
    public static class CrewCommands
    {
        public static readonly List<CrewCommand> All = new List<CrewCommand>
        {
            // Steuer (Helm)
            C(BHPPPGKNAHK.SetSpeedFull, Station.Helm, "Volle Impulsgeschwindigkeit"),
            C(BHPPPGKNAHK.SetSpeedHalf, Station.Helm, "Halbe Impulsgeschwindigkeit"),
            C(BHPPPGKNAHK.SetSpeedQuarter, Station.Helm, "Viertel Impulsgeschwindigkeit"),
            C(BHPPPGKNAHK.SetSpeedStop, Station.Helm, "Alle Maschinen stopp"),
            C(BHPPPGKNAHK.SetSpeedReverse, Station.Helm, "Rückwärts"),
            C(BHPPPGKNAHK.MoveTo, Station.Helm, "Auf das gewählte Ziel zufliegen"),
            C(BHPPPGKNAHK.MoveAwayFrom, Station.Helm, "Vom gewählten Ziel wegfliegen"),

            // Taktik (Tactical)
            C(BHPPPGKNAHK.RaiseShields, Station.Tactical, "Schilde hoch"),
            C(BHPPPGKNAHK.LowerShields, Station.Tactical, "Schilde runter", major: true),
            C(BHPPPGKNAHK.ArmTorpedoes, Station.Tactical, "Torpedos scharf machen"),
            C(BHPPPGKNAHK.DisarmTorpedoes, Station.Tactical, "Torpedos entschärfen"),
            C(BHPPPGKNAHK.FirePhasers, Station.Tactical, "Phaser auf das Ziel feuern", major: true),
            C(BHPPPGKNAHK.FireTorpedoes, Station.Tactical, "Torpedos auf das Ziel feuern", major: true),
            C(BHPPPGKNAHK.CeaseFire, Station.Tactical, "Feuer einstellen"),
            C(BHPPPGKNAHK.TargetWeapons, Station.Tactical, "Waffen des Ziels anvisieren"),
            C(BHPPPGKNAHK.TargetEngines, Station.Tactical, "Antrieb des Ziels anvisieren"),
            C(BHPPPGKNAHK.TargetShields, Station.Tactical, "Schilde des Ziels anvisieren"),
            C(BHPPPGKNAHK.CeaseTargeting, Station.Tactical, "Zielerfassung lösen"),
            C(BHPPPGKNAHK.ScanTarget, Station.Tactical, "Ziel scannen"),
            C(BHPPPGKNAHK.ModulateShields, Station.Tactical, "Schildfrequenz modulieren"),
            C(BHPPPGKNAHK.DisruptShields, Station.Tactical, "Schilde des Ziels stören"),
            C(BHPPPGKNAHK.DisruptWeapons, Station.Tactical, "Waffen des Ziels stören"),
            C(BHPPPGKNAHK.DisruptEngines, Station.Tactical, "Antrieb des Ziels stören"),
            C(BHPPPGKNAHK.DisruptComms, Station.Tactical, "Kommunikation des Ziels stören"),

            // Technik (Engineering)
            C(BHPPPGKNAHK.PowerToEngines, Station.Engineering, "Energie auf Antrieb"),
            C(BHPPPGKNAHK.PowerToShields, Station.Engineering, "Energie auf Schilde"),
            C(BHPPPGKNAHK.PowerToPhasers, Station.Engineering, "Energie auf Phaser"),
            C(BHPPPGKNAHK.PowerBalanced, Station.Engineering, "Energie ausgeglichen verteilen"),
            C(BHPPPGKNAHK.RepairBalanced, Station.Engineering, "Reparaturen ausgeglichen"),
            C(BHPPPGKNAHK.RepairShields, Station.Engineering, "Schilde reparieren"),
            C(BHPPPGKNAHK.RepairEngines, Station.Engineering, "Antrieb reparieren"),
            C(BHPPPGKNAHK.RepairPhasers, Station.Engineering, "Phaser reparieren"),
            C(BHPPPGKNAHK.RepairTorpedoes, Station.Engineering, "Torpedos reparieren"),
            C(BHPPPGKNAHK.RepairWarpcore, Station.Engineering, "Warpkern reparieren"),
            C(BHPPPGKNAHK.RepairSensors, Station.Engineering, "Sensoren reparieren"),
            C(BHPPPGKNAHK.RepairScience, Station.Engineering, "Wissenschaftslabor reparieren"),
            C(BHPPPGKNAHK.RepairTransporter, Station.Engineering, "Transporter reparieren"),
            C(BHPPPGKNAHK.RepairComputerCore, Station.Engineering, "Computerkern reparieren"),
            C(BHPPPGKNAHK.RepairSickbay, Station.Engineering, "Krankenstation reparieren"),
            C(BHPPPGKNAHK.OptimizeBalanced, Station.Engineering, "Systeme ausgeglichen optimieren"),
            C(BHPPPGKNAHK.OptimizeWeapons, Station.Engineering, "Waffen optimieren"),
            C(BHPPPGKNAHK.OptimizeShields, Station.Engineering, "Schilde optimieren"),
            C(BHPPPGKNAHK.OptimizeEngines, Station.Engineering, "Antrieb optimieren"),
            C(BHPPPGKNAHK.OptimizeTransporter, Station.Engineering, "Transporter optimieren"),
            C(BHPPPGKNAHK.OptimizeScanners, Station.Engineering, "Scanner optimieren"),
            C(BHPPPGKNAHK.OptimzeComputerCore, Station.Engineering, "Computerkern optimieren"),

            // Abläufe über mehrere Stationen (im Spiel "Playbook"-Befehle des Captains)
            Cap(BHPPPGKNAHK.EngageTarget, Station.Tactical, "Gewähltes Ziel angreifen (Anflug + Feuer)"),
            Cap(BHPPPGKNAHK.AnalyseTarget, Station.Tactical, "Gewähltes Ziel analysieren"),
            Cap(BHPPPGKNAHK.TransportFromTarget, Station.Engineering, "Vom Ziel beamen"),
            Cap(BHPPPGKNAHK.PrepareImpulse, Station.Helm, "Impulssprung zum Ziel vorbereiten"),
            Cap(BHPPPGKNAHK.EngageImpulse, Station.Helm, "Impulssprung ausführen"),
            Cap(BHPPPGKNAHK.PrepareWarp, Station.Engineering, "Warp vorbereiten"),
            Cap(BHPPPGKNAHK.EngageWarp, Station.Helm, "Warp ausführen"),
            Cap(BHPPPGKNAHK.ReduceSignature, Station.Engineering, "Signatur verringern (Tarnung erschweren)"),
            Cap(BHPPPGKNAHK.CloakHunt, Station.Tactical, "Getarnte Schiffe jagen"),
        };

        static readonly Dictionary<string, CrewCommand> ByName = new Dictionary<string, CrewCommand>();

        static CrewCommands()
        {
            foreach (var c in All) ByName[c.Name] = c;
        }

        public static CrewCommand Find(string name)
        {
            CrewCommand c;
            return name != null && ByName.TryGetValue(name, out c) ? c : null;
        }

        static CrewCommand C(BHPPPGKNAHK cmd, Station st, string desc, bool major = false) => new CrewCommand(cmd, st, desc, st, major);

        // Mehrstationen-Abläufe: im Spiel löst sie der Captain aus. Auf seinen Befehl hin darf sie der zuständige Offizier anstoßen.
        static CrewCommand Cap(BHPPPGKNAHK cmd, Station owner, string desc) => new CrewCommand(cmd, Station.Captain, desc, owner, true);

        public static IEnumerable<CrewCommand> ForOfficer(Station owner)
        {
            foreach (var c in All) if (c.Owner == owner) yield return c;
        }
    }
}
