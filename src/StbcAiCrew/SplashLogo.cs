using System;
using System.IO;
using UnityEngine;

namespace StbcAiCrew
{
    /// <summary>
    /// Phuntasy-Productions-Logo kurz beim Spielstart einblenden, während der Startbildschirme des Spiels
    /// (nur auf dem Desktop-Bildschirm). Das Bild steckt als Ressource in der DLL.
    /// </summary>
    public sealed class SplashLogo
    {
        const float FadeIn = 0.5f, Hold = 1.5f, FadeOut = 0.7f;

        Texture2D _logo;
        float _start = -1f;
        bool _done;

        public void Draw()
        {
            if (_done) return;
            if (_logo == null && !Load()) { _done = true; return; }
            if (_start < 0f) _start = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - _start;
            if (t > FadeIn + Hold + FadeOut) { _done = true; UnityEngine.Object.Destroy(_logo); _logo = null; return; }
            float a = t < FadeIn ? t / FadeIn : t < FadeIn + Hold ? 1f : 1f - (t - FadeIn - Hold) / FadeOut;

            var old = GUI.color;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            float w = Mathf.Min(Screen.width * 0.6f, _logo.width), h = w * _logo.height / _logo.width;
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), _logo, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

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
