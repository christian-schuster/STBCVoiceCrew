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
        /// <summary>Zur Fehlersuche abschaltbar (Konfiguration [Diagnose]).</summary>
        public static bool VoipHooks = true, MicSelectHook = true;
        /// <summary>Testlauf: Sprachereignisse der Bots ins Log schreiben (zum Zuordnen von Rückmeldungen).</summary>
        public static bool LogVoiceEvents;

        public static void Apply(Harmony harmony)
        {
            Patch(harmony, typeof(Bridge), "SendConsoleMessage", nameof(ConsoleMessagePrefix));
            Patch(harmony, typeof(CommandManager), "TriggerCommand", nameof(TriggerCommandPrefix));
            Patch(harmony, typeof(CommandManager), "SetRedAlert", nameof(RedAlertPrefix));
            Patch(harmony, typeof(USSAegisCrew), "CrewbotPlayVO", nameof(CrewbotVoPrefix));
            Patch(harmony, typeof(VOManager), "Play", nameof(VoPlayPrefix));
            if (VoipHooks)
            {
                // Mehrspieler: Stimmen der Mitspieler abhören, eigenes Mikro ruhen lassen, solange die Crew spricht
                Patch(harmony, typeof(DaikonForge.VoIP.UnityAudioPlayer), "BufferAudio", nameof(RemoteAudioPrefix));
                Patch(harmony, typeof(WwiseAudioPlayer), "BufferAudio", nameof(RemoteAudioPrefix));
                Patch(harmony, typeof(DaikonForge.VoIP.VoiceControllerBase), "LOEAHNOJEOK", nameof(MicBufferPrefix));
            }
            else Plugin.Log.LogInfo("Sprachchat-Hooks aus (Konfiguration).");
            if (MicSelectHook)
            {
                // Spiel-Mikrofon: ohne Rift nicht das stumme Oculus-Mikrofon nehmen
                Patch(harmony, typeof(WwiseMicrophoneInputDevice), "NJIJNNMMLIG", nameof(MicSelectPostfix), postfix: true);
                Patch(harmony, typeof(WwiseMicrophoneInputDevice), "PBDGLAKFHCM", nameof(MicSelectPostfix), postfix: true);
            }
            else Plugin.Log.LogInfo("Mikrofonauswahl-Hook aus (Konfiguration).");
            PatchAllOverloads(harmony, typeof(PlayerSpacecraft), "takeHit", nameof(TakeHitPostfix));
        }

        // takeHit gibt es mehrfach (Überladungen): alle patchen, der Postfix braucht keine Parameter
        static void PatchAllOverloads(Harmony harmony, Type type, string method, string hook)
        {
            var hm = new HarmonyMethod(typeof(GamePatches).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic));
            int n = 0;
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != method || m.IsAbstract) continue;
                try { harmony.Patch(m, postfix: hm); n++; }
                catch (Exception e) { Plugin.Log.LogError("Hook fehlgeschlagen: " + type.Name + "." + method + " (" + m + ") – " + e.Message); }
            }
            Plugin.Log.LogInfo("Hook aktiv: " + type.Name + "." + method + " (" + n + " Überladungen)");
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
        /// <summary>Letzte Konsolenmeldungen des Spiels (englischer Originaltext, Zeit), z. B. "Target already scanned".</summary>
        static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, string>> _console =
            new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, string>>();

        static void Remember(string english)
        {
            if (string.IsNullOrEmpty(english)) return;
            lock (_console)
            {
                _console.Add(new System.Collections.Generic.KeyValuePair<float, string>(UnityEngine.Time.time, english));
                if (_console.Count > 40) _console.RemoveAt(0);
            }
        }

        /// <summary>Kam seit 'since' (Spielzeit) eine Konsolenmeldung, die das Muster enthält (Groß-/Kleinschreibung egal)?</summary>
        public static bool ConsoleSaid(string pattern, float since)
        {
            lock (_console)
                for (int i = _console.Count - 1; i >= 0; i--)
                {
                    if (_console[i].Key < since) break;
                    if (System.Text.RegularExpressions.Regex.IsMatch(_console[i].Value, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
                }
            return false;
        }

        static void ConsoleMessagePrefix(MIFGHKACDIH ECBMIBNCBNP, string IPMGLGJABGP)
        {
            if (GameBridge.Injecting) return;
            try
            {
                string text = GameBridge.StripLocId(IPMGLGJABGP);
                Remember(GameBridge.EnglishOf(IPMGLGJABGP));
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

        // AudioPlayerBase.BufferAudio(Puffer): entpackte Stimme eines Mitspielers
        static void RemoteAudioPrefix(UnityEngine.Component __instance, DaikonForge.VoIP.DHEDECPPLGP<float> LGNKMOBGEAD)
        {
            try { var tap = RemoteVoiceTap.Instance; if (tap != null && LGNKMOBGEAD != null) tap.OnRemoteAudio(__instance, LGNKMOBGEAD); }
            catch (Exception e) { Plugin.Log.LogDebug("VoIP-Hook: " + e.Message); }
        }

        static void MicSelectPostfix(object __instance) => GameMicFix.AfterSelect(__instance);

        // VoiceControllerBase.LOEAHNOJEOK(Puffer, Rate): Mikrofonpuffer zum Senden; ruht, solange die Crew über den Sprachchat spricht
        static bool MicBufferPrefix()
        {
            var s = CrewVoiceSender.Instance;
            if (CrewVoiceSender.Injecting || s == null || !s.Active) return true;
            // Der Mensch spricht über die Crew hinweg: seine Stimme geht vor (sonst klang er für die anderen zerhackt)
            if (CrewVoiceSender.HostTalking()) { s.Clear(); return true; }
            return false;
        }

        /// <summary>Wird gesetzt, wenn das Mithören läuft: (Ereignisname, Sprecher).</summary>
        public static Action<string, KFLPAPJAAGO> OnGameVoice;

        // VOManager.Play(string ereignis, KFLPAPJAAGO sprecher, ...): jede Sprachausgabe des Spiels
        /// <summary>Zeitpunkt der letzten Sprachausgabe des Spiels (Echtzeit), auch Briefing und Logbuch.</summary>
        public static float LastGameVoice = -999f;

        static void VoPlayPrefix(string MKMCDCLOPCH, KFLPAPJAAGO DIDHMLCOFMH)
        {
            try
            {
                if (DIDHMLCOFMH != KFLPAPJAAGO.Crewbot_Eng_Tnar && DIDHMLCOFMH != KFLPAPJAAGO.Crewbot_Helm_Rao && DIDHMLCOFMH != KFLPAPJAAGO.Crewbot_Tac_Hoyle)
                    LastGameVoice = UnityEngine.Time.realtimeSinceStartup;
                if (OnGameVoice != null) OnGameVoice(MKMCDCLOPCH, DIDHMLCOFMH);
            }
            catch (Exception e) { Plugin.Log.LogDebug("VO-Hook: " + e.Message); }
        }

        // CommandManager.SetRedAlert(bool an)
        static void RedAlertPrefix(bool LLLOEELGIJN)
        {
            try { EventLog.SetRedAlert(LLLOEELGIJN); }
            catch (Exception e) { Plugin.Log.LogDebug(e); }
        }

        // USSAegisCrew.CrewbotPlayVO(string wwiseEvent, KFLPAPJAAGO sprecher, float verzögerung)
        // false unterdrückt die vorgefertigte Sprachausgabe der Bots (die KI-Offiziere sprechen selbst).
        static bool CrewbotVoPrefix(KFLPAPJAAGO KFLGELOFJDK, object[] __args)
        {
            try
            {
                // Sprachmeldungen der Bots merken ("Scan abgeschlossen", "außer Reichweite" …), Ereignisname als Text
                string ev = __args != null && __args.Length > 0 ? __args[0] as string : null;
                if (!string.IsNullOrEmpty(ev)) { Remember("VO:" + ev); if (LogVoiceEvents) Plugin.Log.LogInfo("Spielstimme: " + ev + " (" + KFLGELOFJDK + ")"); }
                var p = Plugin.Instance;
                if (p != null && !p.WatsonMode && p.MuteBotVoiceLines.Value && !GameBridge.Injecting &&
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
