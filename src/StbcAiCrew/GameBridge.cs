using System;
using AI;
using Sector.Spacecraft;
using UI;
using UnityEngine;

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
        static System.Reflection.FieldInfo _voQueues;

        /// <summary>
        /// Spricht gerade eine Stimme des Spiels (Missionssprecher, Sternenflotte, fremde Captains, Schiffscomputer)?
        /// VOManager führt je Sprecher eine Warteschlange; die stummgeschalteten Bot-Stimmen zählen nicht.
        /// </summary>
        /// <summary>Stimmen, bei denen die Crew nicht schweigen muss: die stummen Bots und das leise Hintergrundgemurmel der Brücke.</summary>
        static bool IgnoredVoice(int i)
        {
            switch ((KFLPAPJAAGO)i)
            {
                case KFLPAPJAAGO.Crewbot_Eng_Tnar:
                case KFLPAPJAAGO.Crewbot_Helm_Rao:
                case KFLPAPJAAGO.Crewbot_Tac_Hoyle:
                case KFLPAPJAAGO.Ambient_Female_1:
                case KFLPAPJAAGO.Ambient_Female_2:
                case KFLPAPJAAGO.Ambient_Female_3:
                case KFLPAPJAAGO.Ambient_Male_1:
                case KFLPAPJAAGO.Ambient_Male_2:
                case KFLPAPJAAGO.Ambient_Male_3:
                case KFLPAPJAAGO.Ambient_Female_Extra_1:
                    return true;
                default:
                    return false;
            }
        }

        public static bool GameVoicePlaying()
        {
            try
            {
                var vm = VOManager.Instance;
                if (vm == null) return false;
                if (_voQueues == null)
                    _voQueues = typeof(VOManager).GetField("NJONONPGJGB", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var list = _voQueues != null ? _voQueues.GetValue(vm) as System.Collections.IList : null;
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (IgnoredVoice(i)) continue;
                    var q = list[i] as DHECOBKMDDB;
                    try { if (q != null && q.IsPlaying()) return true; } catch { }   // leere Warteschlange wirft
                }
            }
            catch { }
            return false;
        }

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

        /// <summary>Audio-Ausgabegerät, das das Spiel (Wwise) bei Oculus nutzt; leer = Windows-Standard.</summary>
        public static string GameAudioOutId
        {
            get { try { return AkInitializer.OculusAudioOutID ?? ""; } catch { return ""; } }
        }

        /// <summary>Mikrofon, das das Spiel bei Oculus nutzt; leer = Windows-Standard.</summary>
        public static string GameAudioInId
        {
            get { try { return AkInitializer.OculusAudioInID ?? ""; } catch { return ""; } }
        }

        /// <summary>
        /// Lautstärke für Stimmen laut Optionsmenü: Master × Sprachchat (wie menschliche Mitspieler), 0–1.
        /// </summary>
        public static float GameVoiceVolume
        {
            get
            {
                try
                {
                    var core = SVR.Network.NetworkCore.IEFBKOMBFGO;
                    var opt = core != null && core.CMDONECIBMP != null ? core.CMDONECIBMP.Options : null;
                    if (opt == null) return 1f;
                    return Mathf.Clamp01(opt.MasterVolume / 100f) * Mathf.Clamp01(opt.ChatVolume / 100f);
                }
                catch { return 1f; }
            }
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
