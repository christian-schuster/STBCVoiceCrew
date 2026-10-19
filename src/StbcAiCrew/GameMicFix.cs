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
                else if (IsVirtual(current) && !RiftActive())
                {
                    foreach (var d in devices) if (!IsVirtual(d)) { want = d; break; }
                }
                if (want == null || want == current)
                {
                    if (current != _logged) { _logged = current; Plugin.Log.LogInfo("Spiel-Mikrofon: " + (current ?? "?")); }
                    return;
                }
                _logged = want;
                Plugin.Log.LogInfo("Spiel-Mikrofon: " + (current ?? "?") + " → " + want + (string.IsNullOrEmpty(Preferred) ? " (Rift nicht aktiv)" : " (Konfiguration)"));
                _deviceField.SetValue(device, want);
                ((DaikonForge.VoIP.AudioInputDeviceBase)device).ChangeMicrophoneDevice(want);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Spiel-Mikrofon nicht umgestellt: " + e.Message); }
        }

        static bool RiftActive()
        {
            try { return IEDCLJPAJKO.IsRiftActive(); } catch { return false; }
        }
    }
}
