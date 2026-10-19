using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Logging;

namespace StbcAiCrew
{
    /// <summary>
    /// Liefert die Standard-Umgebung des angemeldeten Benutzers (wie bei einem normal gestarteten Programm),
    /// statt der Umgebung des Spiels, die Doorstop/BepInEx, Mono und Unity verändern.
    /// </summary>
    public static class UserEnvironment
    {
        static Dictionary<string, string> _cached;
        static bool _dumped;

        public static void Apply(ProcessStartInfo psi, ManualLogSource log)
        {
            DumpGameEnvironment(log);
            var env = Get(log);
            if (env == null) return;   // dann eben die geerbte Umgebung
            psi.EnvironmentVariables.Clear();
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
        }

        static Dictionary<string, string> Get(ManualLogSource log)
        {
            if (_cached != null) return _cached;
            IntPtr token = IntPtr.Zero, block = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out token)) return null;
                if (!CreateEnvironmentBlock(out block, token, false)) return null;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int offset = 0;
                while (true)
                {
                    string entry = Marshal.PtrToStringUni(new IntPtr(block.ToInt64() + offset));
                    if (string.IsNullOrEmpty(entry)) break;
                    offset += (entry.Length + 1) * 2;
                    int eq = entry.IndexOf('=', 1);   // Einträge wie "=C:=C:\" beginnen mit '='
                    if (eq <= 0) continue;
                    d[entry.Substring(0, eq)] = entry.Substring(eq + 1);
                }
                _cached = d;
                log.LogInfo("Server bekommen die Standard-Umgebung des Benutzers (" + d.Count + " Variablen).");
                return d;
            }
            catch (Exception e)
            {
                log.LogWarning("Standard-Umgebung nicht verfügbar, Server erben die Umgebung des Spiels: " + e.Message);
                return null;
            }
            finally
            {
                if (block != IntPtr.Zero) DestroyEnvironmentBlock(block);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        /// <summary>Zur Diagnose: die Umgebung des Spiels einmal in eine Datei schreiben.</summary>
        static void DumpGameEnvironment(ManualLogSource log)
        {
            if (_dumped) return;
            _dumped = true;
            try
            {
                var sb = new StringBuilder();
                var keys = new List<string>();
                foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) keys.Add((string)e.Key);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var k in keys) sb.Append(k).Append('=').Append(Environment.GetEnvironmentVariable(k)).Append('\n');
                string path = Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "StbcAiCrew"), "spiel-umgebung.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { log.LogDebug("Umgebung nicht protokolliert: " + e.Message); }
        }

        const uint TOKEN_QUERY = 0x0008, TOKEN_DUPLICATE = 0x0002;

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool CreateEnvironmentBlock(out IntPtr block, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool DestroyEnvironmentBlock(IntPtr block);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    }
}
