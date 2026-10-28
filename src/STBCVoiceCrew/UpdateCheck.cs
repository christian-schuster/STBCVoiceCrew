using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Fragt beim Start einmal im Hintergrund die neueste Version auf GitHub ab (über curl.exe, das Spiel-Mono kann kein
    /// aktuelles HTTPS). Ist sie neuer, zeigt das Overlay einen Hinweis; aktualisiert wird mit dem Installer oder dem
    /// Installationsbefehl (bei laufendem Spiel ist die DLL gesperrt). Abschaltbar: [Allgemein] UpdatePruefen.
    /// </summary>
    public static class UpdateCheck
    {
        public const string Repo = "__REPO__";   // GitHub owner/repository
        public static volatile string Available;  // neuere Version, sonst null

        public static void Start(string current)
        {
            new Thread(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("curl.exe", "-s -L --max-time 15 -H \"User-Agent: STBC-Voice-Crew\" https://api.github.com/repos/" + Repo + "/releases/latest")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    string json;
                    using (var p = Process.Start(psi)) { json = p.StandardOutput.ReadToEnd(); p.WaitForExit(20000); }
                    var m = Regex.Match(json ?? "", "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                    if (!m.Success) return;
                    if (new Version(m.Groups[1].Value) > new Version(current))
                    {
                        Available = m.Groups[1].Value;
                        Plugin.Log.LogInfo("Update verfügbar: " + Available + " (installiert " + current + ")");
                    }
                }
                catch (Exception e) { Plugin.Log.LogDebug("Update-Prüfung: " + e.Message); }
            }) { IsBackground = true, Name = "UpdateCheck" }.Start();
        }
    }
}
