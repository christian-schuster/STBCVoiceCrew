using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StbcAiCrew
{
    /// <summary>
    /// Langzeitgedächtnis der Crew: Lektionen aus früheren Missionen, über die Nachbesprechung
    /// vom LLM selbst gepflegt. So "lernt" die Crew ohne Training der Modellgewichte –
    /// die Erfahrungen landen bei jeder Entscheidung im Prompt.
    /// </summary>
    public sealed class CrewMemory
    {
        public sealed class Lesson
        {
            public string Officer;   // helm | tactical | engineering | crew
            public string Text;
        }

        readonly string _path;
        public readonly List<Lesson> Lessons = new List<Lesson>();
        public int MissionsPlayed;
        public string CaptainProfile = "";

        public CrewMemory(string path)
        {
            _path = path;
        }

        public void Load()
        {
            Lessons.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                var root = Json.Parse(File.ReadAllText(_path, Encoding.UTF8));
                var n = Json.Get(root, "missionsPlayed");
                MissionsPlayed = n is double ? (int)(double)n : 0;
                CaptainProfile = Json.Str(root, "captainProfile") ?? "";
                var arr = Json.Arr(Json.Get(root, "lessons"));
                if (arr != null)
                    foreach (var o in arr)
                    {
                        string text = Json.Str(o, "text");
                        if (!string.IsNullOrEmpty(text))
                            Lessons.Add(new Lesson { Officer = Json.Str(o, "officer") ?? "crew", Text = text });
                    }
                Plugin.Log.LogInfo("Gedächtnis geladen: " + Lessons.Count + " Lektionen, " + MissionsPlayed + " Missionen.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Gedächtnis unlesbar, starte leer (Datei bleibt erhalten): " + e.Message);
                try { File.Copy(_path, _path + ".kaputt", true); } catch { }
            }
        }

        public void Save()
        {
            try
            {
                var list = new List<object>();
                foreach (var l in Lessons)
                    list.Add(new Dictionary<string, object> { { "officer", l.Officer }, { "text", l.Text } });
                var root = new Dictionary<string, object>
                {
                    { "missionsPlayed", MissionsPlayed },
                    { "captainProfile", CaptainProfile },
                    { "lessons", list }
                };
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(root), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Gedächtnis konnte nicht gespeichert werden: " + e.Message);
            }
        }

        public string ForPrompt()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(CaptainProfile))
                sb.Append("Was ihr über euren Captain wisst: ").Append(CaptainProfile).Append('\n');
            if (Lessons.Count == 0)
            {
                sb.Append("Lektionen aus früheren Missionen: noch keine (erste gemeinsame Mission).\n");
                return sb.ToString();
            }
            sb.Append("Lektionen aus ").Append(MissionsPlayed).Append(" früheren Missionen:\n");
            foreach (var l in Lessons) sb.Append("- [").Append(l.Officer).Append("] ").Append(l.Text).Append('\n');
            return sb.ToString();
        }
    }
}
