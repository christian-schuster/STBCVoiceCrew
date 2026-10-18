using System;
using AI;
using Sector.Spacecraft;
using UI;

namespace StbcAiCrew
{
    /// <summary>
    /// Einzige Stelle, die obfuskierte Spiel-Member anfasst. Die Namen stammen aus der dekompilierten
    /// Assembly-CSharp.dll (Spielversion von 2018, wird nicht mehr aktualisiert – Namen bleiben stabil).
    /// </summary>
    public static class GameBridge
    {
        /// <summary>True, während der Mod selbst Befehle/Meldungen auslöst – damit die Hooks sie nicht als Spielereignis werten.</summary>
        [ThreadStatic] public static bool Injecting;

        public static PlayerSpacecraft PlayerShip
        {
            get
            {
                var bridge = Bridge.Instance;
                return bridge != null ? bridge.FMDNNFLLMMN as PlayerSpacecraft : null;
            }
        }

        public static bool InMission => PlayerShip != null && CommandManager.IEFBKOMBFGO != null;

        public static USSAegisCrew Crew
        {
            get
            {
                var ship = PlayerShip;
                return ship != null ? ship.GetComponent<USSAegisCrew>() : null;
            }
        }

        /// <summary>Wird die Station von einem Bot bedient (true) oder von einem Menschen (false)?</summary>
        public static bool IsStationAI(Station station)
        {
            var crew = Crew;
            if (crew == null) return false;
            switch (station)
            {
                case Station.Helm: return crew.IsConsoleAI(FLCHMJJDKDL.Helm);
                case Station.Tactical: return crew.IsConsoleAI(FLCHMJJDKDL.Tactical);
                case Station.Engineering: return crew.IsConsoleAI(FLCHMJJDKDL.Engineering);
                case Station.Captain:
                    // Mehrstationen-Abläufe: sinnvoll, sobald mindestens ein Bot mitspielt
                    return crew.IsConsoleAI(FLCHMJJDKDL.Helm) || crew.IsConsoleAI(FLCHMJJDKDL.Tactical) || crew.IsConsoleAI(FLCHMJJDKDL.Engineering);
            }
            return false;
        }

        /// <summary>Löst einen Crew-Befehl aus – genau wie früher die Watson-Sprachsteuerung.</summary>
        public static void Trigger(CrewCommand cmd)
        {
            if (!InMission) throw new InvalidOperationException("Nicht in einer Mission.");
            Injecting = true;
            try
            {
                CommandManager.UpdateSimpleOrders(FLCHMJJDKDL.Engineering);
                CommandManager.UpdateSimpleOrders(FLCHMJJDKDL.Tactical);
                CommandManager.UpdateSimpleOrders(FLCHMJJDKDL.Helm);
                CommandManager.TriggerCommand(cmd.GameCommand);
            }
            finally { Injecting = false; }
        }

        /// <summary>Zeigt Text auf einer Brückenkonsole an (in VR sichtbar).</summary>
        public static void ShowConsoleMessage(Station target, string text, float seconds) => Show(ToConsole(target), text, seconds);

        /// <summary>Gesprochenes hört die ganze Brücke: bis zur Sprachausgabe als Text auf allen Konsolen.</summary>
        public static void ShowOnAllConsoles(string text, float seconds) => Show(MIFGHKACDIH.All, text, seconds);

        static void Show(MIFGHKACDIH target, string text, float seconds)
        {
            var bridge = Bridge.Instance;
            if (bridge == null) return;
            // '|' trennt im Spiel Lokalisierungs-ID und Text – nicht durchreichen
            text = text.Replace('|', '/');
            Injecting = true;
            try { bridge.SendConsoleMessage(target, text, seconds); }
            finally { Injecting = false; }
        }

        static MIFGHKACDIH ToConsole(Station s)
        {
            switch (s)
            {
                case Station.Helm: return MIFGHKACDIH.Helm;
                case Station.Tactical: return MIFGHKACDIH.Tactical;
                case Station.Engineering: return MIFGHKACDIH.Engineering;
                default: return MIFGHKACDIH.Captain;
            }
        }

        public static Station? FromGameStation(FLCHMJJDKDL s)
        {
            switch (s)
            {
                case FLCHMJJDKDL.Captain: return Station.Captain;
                case FLCHMJJDKDL.Helm: return Station.Helm;
                case FLCHMJJDKDL.Tactical: return Station.Tactical;
                case FLCHMJJDKDL.Engineering: return Station.Engineering;
            }
            return null;
        }

        /// <summary>Entfernt das Lokalisierungs-Präfix "1234|" aus Spieltexten.</summary>
        public static string StripLocId(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int bar = s.IndexOf('|');
            if (bar > 0 && bar < 8)
            {
                bool digits = true;
                for (int i = 0; i < bar; i++) if (!char.IsDigit(s[i])) { digits = false; break; }
                if (digits) return s.Substring(bar + 1);
            }
            return s;
        }
    }
}
