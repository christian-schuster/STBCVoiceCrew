using System;
using System.Reflection;
using AI;
using HarmonyLib;
using Sector.Spacecraft;
using UI;

namespace StbcAiCrew
{
    /// <summary>
    /// Harmony-Hooks: liefern der Crew ihre "Wahrnehmung" des Spiels.
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
                // Bedienhinweise sind keine Lageinformation
                if (string.IsNullOrEmpty(text) || text.IndexOf("<#", StringComparison.Ordinal) >= 0) return;
                string who = ECBMIBNCBNP == MIFGHKACDIH.All ? "Alle Konsolen" : ECBMIBNCBNP.ToString();
                EventLog.Add("Konsolenmeldung (" + who + "): " + text, true);
            }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // CommandManager.TriggerCommand(BHPPPGKNAHK befehl) – auch die Menüauswahl des Captains läuft hier durch
        static void TriggerCommandPrefix(BHPPPGKNAHK GOOFNMAKIJB)
        {
            if (GameBridge.Injecting) return;
            try
            {
                var cmd = CrewCommands.Find(GOOFNMAKIJB.ToString());
                string desc = cmd != null ? cmd.Description : GOOFNMAKIJB.ToString();
                EventLog.Add("Captain gibt Befehl: " + desc + " (" + GOOFNMAKIJB + ")", true);
            }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // CommandManager.SetRedAlert(bool an)
        static void RedAlertPrefix(bool LLLOEELGIJN)
        {
            try { EventLog.Add(LLLOEELGIJN ? "Roter Alarm ausgelöst" : "Roter Alarm beendet", true); }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // USSAegisCrew.CrewbotPlayVO(string wwiseEvent, KFLPAPJAAGO sprecher, float verzögerung)
        // Returnwert false unterdrückt die vorgefertigte Sprachausgabe der Bots (Option).
        static bool CrewbotVoPrefix(string GPICMMEIAPM, KFLPAPJAAGO KFLGELOFJDK)
        {
            try
            {
                if (Plugin.Instance != null && Plugin.Instance.LogBotVoiceLines.Value)
                    EventLog.Add("Sprachzeile " + KFLGELOFJDK + ": " + GPICMMEIAPM, false);
                if (Plugin.Instance != null && Plugin.Instance.MuteBotVoiceLines.Value && !GameBridge.Injecting)
                    return false;
            }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
            return true;
        }

        // PlayerSpacecraft.takeHit(Vector3 pos, float schaden, ...)
        static void TakeHitPostfix(float AFLKODIBBCC)
        {
            try { EventLog.AddHit(AFLKODIBBCC); }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }
    }
}
