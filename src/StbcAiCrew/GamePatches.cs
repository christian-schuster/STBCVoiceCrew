using System;
using System.Reflection;
using AI;
using HarmonyLib;
using Sector.Spacecraft;
using UI;

namespace StbcAiCrew
{
    /// <summary>
    /// Harmony-Hooks: liefern den Offizieren ihre Wahrnehmung. Jede Information geht nur an die
    /// Station, deren Konsole sie anzeigt. Was die Captain-Konsole zeigt, sieht kein KI-Offizier.
    /// Jeder Patch wird einzeln angewendet – schlägt einer fehl, laufen die anderen weiter.
    /// </summary>
    public static class GamePatches
    {
        public static void Apply(Harmony harmony)
        {
            Patch(harmony, typeof(Bridge), "SendConsoleMessage", nameof(ConsoleMessagePrefix));
            Patch(harmony, typeof(CommandManager), "TriggerCommand", nameof(TriggerCommandPrefix));
            Patch(harmony, typeof(CommandManager), "SetRedAlert", nameof(RedAlertPrefix));
            Patch(harmony, typeof(USSAegisCrew), "CrewbotPlayVO", nameof(CrewbotVoPrefix));
            Patch(harmony, typeof(PlayerSpacecraft), "takeHit", nameof(TakeHitPostfix), postfix: true);
        }

        static void Patch(Harmony harmony, Type type, string method, string hook, bool postfix = false)
        {
            try
            {
                var original = AccessTools.Method(type, method);
                if (original == null) throw new MissingMethodException(type.Name, method);
                var hm = new HarmonyMethod(typeof(GamePatches).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic));
                if (postfix) harmony.Patch(original, postfix: hm);
                else harmony.Patch(original, prefix: hm);
                Plugin.Log.LogInfo("Hook aktiv: " + type.Name + "." + method);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Hook fehlgeschlagen: " + type.Name + "." + method + " – " + e.Message);
            }
        }

        // Bridge.SendConsoleMessage(MIFGHKACDIH ziel, string text, float dauer, ...)
        static void ConsoleMessagePrefix(MIFGHKACDIH ECBMIBNCBNP, string IPMGLGJABGP)
        {
            if (GameBridge.Injecting) return;
            try
            {
                string text = GameBridge.StripLocId(IPMGLGJABGP);
                // Bedienhinweise (Farbcodes) sind keine Lageinformation
                if (string.IsNullOrEmpty(text) || text.IndexOf("<#", StringComparison.Ordinal) >= 0) return;
                string line = "Meldung auf deiner Konsole: " + text;
                switch (ECBMIBNCBNP)
                {
                    case MIFGHKACDIH.All: EventLog.ToAll(line, Priority.Important); break;
                    case MIFGHKACDIH.Helm: EventLog.ToStation(Station.Helm, line, Priority.Important); break;
                    case MIFGHKACDIH.Tactical: EventLog.ToStation(Station.Tactical, line, Priority.Important); break;
                    case MIFGHKACDIH.Engineering: EventLog.ToStation(Station.Engineering, line, Priority.Important); break;
                    // MIFGHKACDIH.Captain: nur der Captain sieht das
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // CommandManager.TriggerCommand(BHPPPGKNAHK befehl): Befehl, den der Captain an seiner Konsole gibt.
        // Er erscheint auf der Konsole der ausführenden Station.
        static void TriggerCommandPrefix(BHPPPGKNAHK GOOFNMAKIJB)
        {
            if (GameBridge.Injecting) return;
            try
            {
                var cmd = CrewCommands.Find(GOOFNMAKIJB.ToString());
                string desc = cmd != null ? cmd.Description : GOOFNMAKIJB.ToString();
                string line = "Befehl des Captains auf deiner Konsole: " + desc + " (" + GOOFNMAKIJB + ")";
                if (cmd == null || cmd.Station == Station.Captain) EventLog.ToAll(line, Priority.Urgent, fromCaptain: true);
                else EventLog.ToStation(cmd.Station, line, Priority.Urgent, fromCaptain: true);
            }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // CommandManager.SetRedAlert(bool an)
        static void RedAlertPrefix(bool LLLOEELGIJN)
        {
            try { EventLog.SetRedAlert(LLLOEELGIJN); }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // USSAegisCrew.CrewbotPlayVO(string wwiseEvent, KFLPAPJAAGO sprecher, float verzögerung)
        // false unterdrückt die vorgefertigte Sprachausgabe der Bots (die KI-Offiziere sprechen selbst).
        static bool CrewbotVoPrefix(KFLPAPJAAGO KFLGELOFJDK)
        {
            try
            {
                var p = Plugin.Instance;
                if (p != null && p.MuteBotVoiceLines.Value && !GameBridge.Injecting &&
                    (KFLGELOFJDK == KFLPAPJAAGO.Crewbot_Helm_Rao || KFLGELOFJDK == KFLPAPJAAGO.Crewbot_Tac_Hoyle || KFLGELOFJDK == KFLPAPJAAGO.Crewbot_Eng_Tnar))
                    return false;
            }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
            return true;
        }

        // PlayerSpacecraft.takeHit(...): Treffer spürt jeder auf der Brücke
        static void TakeHitPostfix()
        {
            try { EventLog.AddHit(); }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }
    }
}
