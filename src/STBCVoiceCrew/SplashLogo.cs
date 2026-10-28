using System;
using System.IO;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Phuntasy-Productions-Logo kurz beim Spielstart einblenden: direkt vor den Logos des Spiels
    /// (die Startsequenz wartet so lange, siehe GamePatches.HoldIntro). Nur auf dem Desktop-Bildschirm; das Bild steckt als
    /// Ressource in der DLL.
    /// </summary>
    public sealed class SplashLogo
    {
        const float FadeIn = 0.5f, Hold = 1.5f, FadeOut = 0.7f;

        Texture2D _logo;
        float _start = -1f;
        bool _done;

        /// <summary>Logo starten (beim ersten Aufruf) und true liefern, solange es noch läuft.</summary>
        public bool Running()
        {
            if (_start < 0f) _start = Time.realtimeSinceStartup;
            return !_done && Time.realtimeSinceStartup - _start <= FadeIn + Hold + FadeOut;
        }

        public void Draw()
        {
            if (_done || _start < 0f) return;
            if (_logo == null && !Load()) { _done = true; return; }
            float t = Time.realtimeSinceStartup - _start;
            if (t > FadeIn + Hold + FadeOut) { _done = true; UnityEngine.Object.Destroy(_logo); _logo = null; return; }
            float a = t < FadeIn ? t / FadeIn : t < FadeIn + Hold ? 1f : 1f - (t - FadeIn - Hold) / FadeOut;

            var old = GUI.color;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            float w = Mathf.Min(Screen.width * 0.6f, _logo.width), h = w * _logo.height / _logo.width;
            GUI.color = new Color(1f, 1f, 1f, a);
            float top = (Screen.height - h) / 2f;
            GUI.DrawTexture(new Rect((Screen.width - w) / 2f, top, w, h), _logo, ScaleMode.ScaleToFit, true);
            if (_caption == null)
                _caption = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(18, Mathf.RoundToInt(w * 0.045f)), alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0f, top + h + w * 0.03f, Screen.width, w * 0.1f), "STBC Voice Crew Mod", _caption);
            GUI.color = old;
        }

        GUIStyle _caption;

        bool Load()
        {
            try
            {
                using (var s = typeof(SplashLogo).Assembly.GetManifestResourceStream("StbcAiCrew.phuntasy-logo.png"))
                {
                    if (s == null) return false;
                    var data = new byte[s.Length];
                    int read = 0;
                    while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                    _logo = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    return _logo.LoadImage(data);
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug("Logo nicht geladen: " + e.Message); return false; }
        }
    }
}
