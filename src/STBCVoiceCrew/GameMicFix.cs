using System;
using System.Reflection;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Mikrofon des Spiels (Sprachchat, Mikrofontest): Das Spiel nimmt das "VR-Mikrofon" der Oculus-Software
    /// (AkInitializer.OculusAudioInID), sonst das erste Gerät. Ohne aufgesetzte Rift ist das Oculus Virtual Audio
    /// Device stumm, der Mikrofontest im Mehrspieler schlägt fehl. Dann auf ein echtes Mikrofon umstellen.
    /// </summary>
    public static class GameMicFix
    {
        /// <summary>Gerätename (Teilstring) aus der Konfiguration; leer = automatisch.</summary>
        public static string Preferred = "";
        static FieldInfo _deviceField;
        static string _logged;   // wird alle ~60 Frames aufgerufen: nur Änderungen protokollieren

        static bool IsVirtual(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.Contains("oculus") || n.Contains("steam streaming") || n.Contains("virtual");
        }

        /// <summary>Postfix nach der Geräteauswahl von WwiseMicrophoneInputDevice.</summary>
        public static void AfterSelect(object device)
        {
            try
            {
                // Nur das eigene Mikrofon (jeder Mitspieler hat auch so eine Komponente, die nie ein Gerät wählt)
                var wmic = device as WwiseMicrophoneInputDevice;
                if (wmic == null || !wmic.EOHGJMMKPPC) return;
                if (_deviceField == null)
                    _deviceField = typeof(DaikonForge.VoIP.AudioInputDeviceBase).GetField("GMNCBFBICKK", BindingFlags.Instance | BindingFlags.NonPublic);
                string current = _deviceField != null ? _deviceField.GetValue(device) as string : null;
                var devices = Microphone.devices;
                if (devices == null || devices.Length == 0) return;

                string want = null;
                if (!string.IsNullOrEmpty(Preferred))
                {
                    foreach (var d in devices) if (d.IndexOf(Preferred, StringComparison.OrdinalIgnoreCase) >= 0) { want = d; break; }
                }
                else if (string.IsNullOrEmpty(current) || (IsVirtual(current) && !RiftActive()))
                {
                    // Kein Gerät gewählt (im Test: Name leer, Mikrofontest schlug fehl) oder stummes Oculus-Gerät ohne Rift
                    bool rift = RiftActive();
                    foreach (var d in devices) if (rift || !IsVirtual(d)) { want = d; break; }
                    if (want == null) want = devices[0];
                }
                if (want == null || want == current)
                {
                    if (current != _logged) { _logged = current; Plugin.Log.LogInfo("Spiel-Mikrofon: " + (string.IsNullOrEmpty(current) ? "(keins)" : current) + " | verfügbar: " + string.Join(" | ", devices)); }
                    return;
                }
                _logged = want;
                Plugin.Log.LogInfo("Spiel-Mikrofon: " + (current ?? "?") + " → " + want + (string.IsNullOrEmpty(Preferred) ? (string.IsNullOrEmpty(current) ? " (Spiel hatte keins gewählt)" : " (Rift nicht aktiv)") : " (Konfiguration)"));
                _deviceField.SetValue(device, want);
                ((DaikonForge.VoIP.AudioInputDeviceBase)device).ChangeMicrophoneDevice(want);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Spiel-Mikrofon nicht umgestellt: " + e.Message); }
        }

        static float _nextProbe;
        static string _lastProbe;

        /// <summary>Mikrofon, das im Spiel gewählt ist (Unity-Gerätename); die Mod hört über dasselbe Gerät.</summary>
        public static volatile string CurrentGameMic;

        /// <summary>Nur beobachten (ändert nichts): Gerät, Aufnahme und Pegel des eigenen Spiel-Mikrofons ins Log, wenn sich etwas ändert.</summary>
        public static void Probe()
        {
            if (Time.realtimeSinceStartup < _nextProbe) return;
            _nextProbe = Time.realtimeSinceStartup + 2f;
            try
            {
                var mgr = VoIpManager.IEFBKOMBFGO;
                var dev = mgr != null ? mgr.GetLocalMicrophoneInputDevice() : null;
                string s;
                if (mgr == null) s = "kein VoIpManager";
                else if (dev == null) s = "kein lokales Mikrofon-Objekt";
                else
                {
                    if (_deviceField == null)
                        _deviceField = typeof(DaikonForge.VoIP.AudioInputDeviceBase).GetField("GMNCBFBICKK", BindingFlags.Instance | BindingFlags.NonPublic);
                    string name = _deviceField != null ? _deviceField.GetValue(dev) as string : "?";
                    if (!string.IsNullOrEmpty(name) && name != "?") CurrentGameMic = name;
                    bool rec = !string.IsNullOrEmpty(name) && Microphone.IsRecording(name);
                    s = dev.GetType().Name + ", Gerät \"" + name + "\", nimmt auf: " + (rec ? "ja" : "nein") +
                        ", Pegel " + (dev.IEBPIBFAIKE > 1f ? "hoch" : dev.IEBPIBFAIKE > 0.01f ? "Signal" : "still") +
                        ", Unity-Geräte: " + string.Join(" | ", Microphone.devices);
                }
                // Pegel schwankt: nur Gerät/Aufnahme vergleichen, damit das Log nicht vollläuft
                string key = s.Substring(0, Math.Max(0, s.IndexOf(", Pegel") > 0 ? s.IndexOf(", Pegel") : s.Length));
                if (key == _lastProbe && !s.Contains("Signal") && !s.Contains("hoch")) return;
                _lastProbe = key;
                Plugin.Log.LogInfo("Spiel-Mikrofon (Beobachtung): " + s);
                foreach (var d in Microphone.devices)
                    if (string.IsNullOrEmpty(d))
                    {
                        Plugin.Log.LogWarning("Ein Mikrofon hat für das Spiel einen leeren Namen (Umlaute wie in \"Kopfhörermikrofon\" kann die Unity-Version des Spiels nicht). " +
                                              "Das Spiel bekommt dann kein Mikrofon. Abhilfe: das Mikrofon in Windows ohne Umlaute umbenennen.");
                        break;
                    }
            }
            catch (Exception e) { Plugin.Log.LogDebug("Mikrofon-Beobachtung: " + e.Message); }
        }

        static bool RiftActive()
        {
            try { return IEDCLJPAJKO.IsRiftActive(); } catch { return false; }
        }
    }
}
