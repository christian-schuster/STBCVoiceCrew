using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace STBCVoiceCrew
{
    /// <summary>Rangleiter der Sternenflotte (Offizierslaufbahn bis Commander).</summary>
    public static class Ranks
    {
        static readonly int[] Thresholds = { 0, 300, 800, 1600, 3000 };
        static readonly string[] Names = { "Fähnrich", "Lieutenant Junior Grade", "Lieutenant", "Lieutenant Commander", "Commander" };
        static readonly string[] Short = { "Fähnrich", "Lt. j.g.", "Lt.", "Lt. Cmdr.", "Cmdr." };

        public static int Index(int xp)
        {
            int i = 0;
            while (i + 1 < Thresholds.Length && xp >= Thresholds[i + 1]) i++;
            return i;
        }

        public static string Name(int xp) => Names[Index(xp)];
        public static string ShortName(int xp) => Short[Index(xp)];

        /// <summary>XP bis zum nächsten Rang, -1 = höchster Rang.</summary>
        public static int NextAt(int xp)
        {
            int i = Index(xp);
            return i + 1 < Thresholds.Length ? Thresholds[i + 1] : -1;
        }

        /// <summary>Was der Rang an Eigenständigkeit erlaubt (fließt in Prompt und Befehlsfreigabe ein).</summary>
        public static string Conduct(int xp)
        {
            switch (Index(xp))
            {
                case 0: return "Als Fähnrich bist du noch unerfahren: Routine an deiner Station erledigst du selbst, weitreichende Schritte schlägst du vor und wartest auf den Befehl. Du fragst lieber einmal mehr nach.";
                case 1:
                case 2: return "Du bist ein erfahrener Offizier: Routine erledigst du selbst, bei Gefahr im Verzug handelst du auch weitreichend und meldest es sofort.";
                default: return "Als Stabsoffizier hast du das Vertrauen des Captains: Du handelst auch bei weitreichenden Entscheidungen in deinem Bereich eigenständig, berätst den Captain und widersprichst respektvoll, wenn du einen Befehl für falsch hältst.";
            }
        }

        /// <summary>Darf weitreichende Befehle (Feuer, Warp …) ohne ausdrücklichen Befehl auslösen?</summary>
        public static bool MayActMajor(int xp, bool underThreat)
        {
            int i = Index(xp);
            return i >= 3 || (i >= 1 && underThreat);
        }
    }

    /// <summary>
    /// Ein KI-Crewmitglied: Profil, Fortschritt und eigenes Gedächtnis in einer JSON-Datei.
    /// Bleibt über Spielsessions erhalten. Profile lassen sich von Hand bearbeiten oder neu anlegen.
    /// </summary>
    public sealed class Character
    {
        // ---- Profil (von Hand editierbar) ----
        public string Id = "";
        public string Name = "";
        public string Species = "Mensch";
        public string Pronouns = "";          // optional, z. B. "sie/ihr"; leer = Name verwenden
        public string Personality = "";
        public string SpeechStyle = "";
        public string Background = "";
        public string Appearance = "";        // Beschreibung; im Spiel angewendet, sobald das Avatar-System erschlossen ist
        public string Voice = "";             // Piper-Stimme: "modell" oder "modell#sprecher"
        public float VoiceSpeed = 1f;         // Sprechtempo (>1 schneller)
        public float VoicePitch = 1f;         // Tonhöhe (>1 höher)
        public string VoiceDesign;            // Stimmbeschreibung für Qwen3-TTS (englisch am zuverlässigsten); null = wird abgeleitet
        public string Laugh;                  // wie die Figur lacht, lautmalerisch ("Har har har!"); "" = lacht nie, null = noch nicht festgelegt
        public List<string> Stations = new List<string>();   // helm | tactical | engineering, bevorzugte zuerst
        public Dictionary<string, int> Traits = Traits0();    // 0..10, siehe TraitNames

        /// <summary>Charakterzüge mit Beschreibung für niedrige und hohe Werte.</summary>
        public static readonly string[][] TraitNames =
        {
            new[] { "Humor", "todernst", "witzig, macht gern Scherze" },
            new[] { "Mut", "ängstlich, wird unter Beschuss nervös", "furchtlos, behält im Gefecht die Nerven" },
            new[] { "Aggressivität", "friedfertig, sucht Deeskalation", "angriffslustig, will zuschlagen" },
            new[] { "Beredsamkeit", "wortkarg, sagt nur das Nötigste", "redselig und wortgewandt" },
            new[] { "Disziplin", "locker, nimmt Vorschriften nicht so genau", "streng nach Protokoll" },
            new[] { "Empathie", "kühl und distanziert", "einfühlsam, achtet auf die Crew" },
            new[] { "Ehrgeiz", "genügsam", "ehrgeizig, will sich auszeichnen" },
            new[] { "Gelassenheit", "hitzköpfig, reagiert impulsiv", "stoisch, bleibt immer ruhig" },
            new[] { "Benehmen", "rüpelhaft, flucht gern und nimmt kein Blatt vor den Mund", "tadellose Umgangsformen, stets höflich und gewählt" },
        };

        static Dictionary<string, int> Traits0()
        {
            var d = new Dictionary<string, int>();
            foreach (var t in TraitNames) d[t[0]] = 5;
            return d;
        }

        public int Trait(string name)
        {
            int v;
            return Traits.TryGetValue(name, out v) ? v : 5;
        }

        public void SetTrait(string name, int v) => Traits[name] = v < 0 ? 0 : v > 10 ? 10 : v;

        // ---- Fortschritt ----
        public int Xp;
        public int Missions;
        public int Commendations;
        public string LastPlayed = "";
        public List<string> PromotionNews = new List<string>();   // noch nicht verkündete Beförderungen

        // ---- Gedächtnis ----
        public List<string> Lessons = new List<string>();
        public string Personal = "";             // Charakterentwicklung, Eigenheiten, Gefühle
        public string CaptainImpression = "";
        public string Relationships = "";        // Eindruck von Kollegen
        public List<string> Chronicle = new List<string>();
        public List<string> MissionNotes = new List<string>();
        public List<string> Anecdotes = new List<string>();   // komische Momente aus Missionen, für ruhige Minuten

        // ---- laufende / ausstehende Mission (absturzsicher) ----
        public string UnfinishedLog = "";
        public string PendingDebriefLog = "";
        public string PendingStation = "";
        public int PendingMinutes, PendingCommands, PendingOrdersFollowed;
        public int UnfinishedMinutes, UnfinishedCommands, UnfinishedOrdersFollowed;

        [NonSerialized] public string FilePath;

        public string Rank => Ranks.Name(Xp);
        public string Title => Ranks.ShortName(Xp) + " " + Name;
        public bool HasPendingDebrief => !string.IsNullOrEmpty(PendingDebriefLog);

        public bool CanServe(string station) => Stations.Contains(station);

        // ---------------- Persistenz ----------------

        public static Character Load(string path)
        {
            var o = Json.Parse(File.ReadAllText(path, Encoding.UTF8));
            var c = new Character { FilePath = path };
            c.Id = Json.Str(o, "id") ?? Path.GetFileNameWithoutExtension(path);
            c.Name = Json.Str(o, "name") ?? c.Id;
            c.Species = Json.Str(o, "species") ?? "";
            c.Pronouns = Json.Str(o, "pronouns") ?? "";
            c.Personality = Json.Str(o, "personality") ?? "";
            c.SpeechStyle = Json.Str(o, "speechStyle") ?? "";
            c.Background = Json.Str(o, "background") ?? "";
            c.Appearance = Json.Str(o, "appearance") ?? "";
            c.Voice = Json.Str(o, "voice") ?? "";
            c.VoiceSpeed = Float(o, "voiceSpeed", 1f);
            c.VoicePitch = Float(o, "voicePitch", 1f);
            c.Laugh = Json.Get(o, "laugh") as string;
            c.VoiceDesign = Json.Get(o, "voiceDesign") as string;   // fehlt = null, dann vergibt der Roster eines
            Strings(Json.Get(o, "stations"), c.Stations);
            var traits = Json.Obj(Json.Get(o, "traits"));
            if (traits != null)
                foreach (var kv in traits)
                    if (kv.Value is double) c.SetTrait(kv.Key, (int)Math.Round((double)kv.Value));

            c.Xp = Int(o, "xp");
            c.Missions = Int(o, "missions");
            c.Commendations = Int(o, "commendations");
            c.LastPlayed = Json.Str(o, "lastPlayed") ?? "";
            Strings(Json.Get(o, "promotionNews"), c.PromotionNews);

            var mem = Json.Get(o, "memory");
            Strings(Json.Get(mem, "lessons"), c.Lessons);
            c.Personal = Json.Str(mem, "personal") ?? "";
            c.CaptainImpression = Json.Str(mem, "captainImpression") ?? "";
            c.Relationships = Json.Str(mem, "relationships") ?? "";
            Strings(Json.Get(mem, "chronicle"), c.Chronicle);
            Strings(Json.Get(mem, "missionNotes"), c.MissionNotes);
            Strings(Json.Get(mem, "anecdotes"), c.Anecdotes);

            var p = Json.Get(o, "pending");
            c.UnfinishedLog = Json.Str(p, "unfinishedLog") ?? "";
            c.PendingDebriefLog = Json.Str(p, "debriefLog") ?? "";
            c.PendingStation = Json.Str(p, "station") ?? "";
            c.PendingMinutes = Int(p, "minutes");
            c.PendingCommands = Int(p, "commands");
            c.PendingOrdersFollowed = Int(p, "ordersFollowed");
            c.UnfinishedMinutes = Int(p, "unfinishedMinutes");
            c.UnfinishedCommands = Int(p, "unfinishedCommands");
            c.UnfinishedOrdersFollowed = Int(p, "unfinishedOrdersFollowed");

            // Mission wurde nicht sauber beendet (Absturz, Alt+F4): Nachbesprechung beim nächsten Start nachholen
            if (!string.IsNullOrEmpty(c.UnfinishedLog))
            {
                c.PendingDebriefLog = string.IsNullOrEmpty(c.PendingDebriefLog) ? c.UnfinishedLog : c.PendingDebriefLog + "\n" + c.UnfinishedLog;
                c.UnfinishedLog = "";
                c.PendingMinutes += c.UnfinishedMinutes;
                c.PendingCommands += c.UnfinishedCommands;
                c.PendingOrdersFollowed += c.UnfinishedOrdersFollowed;
                c.UnfinishedMinutes = c.UnfinishedCommands = c.UnfinishedOrdersFollowed = 0;
                Plugin.Log.LogInfo(c.Name + ": unvollendete Mission aus letzter Session, Nachbesprechung wird nachgeholt.");
            }
            return c;
        }

        public void Save()
        {
            try
            {
                var root = new Dictionary<string, object>
                {
                    { "id", Id }, { "name", Name }, { "species", Species }, { "pronouns", Pronouns },
                    { "personality", Personality }, { "speechStyle", SpeechStyle }, { "background", Background },
                    { "appearance", Appearance }, { "voice", Voice }, { "voiceSpeed", VoiceSpeed }, { "voicePitch", VoicePitch }, { "laugh", Laugh }, { "voiceDesign", VoiceDesign }, { "stations", Stations }, { "traits", Traits },
                    { "rank", Rank }, // nur zur Info, maßgeblich ist xp
                    { "xp", Xp }, { "missions", Missions }, { "commendations", Commendations },
                    { "lastPlayed", LastPlayed }, { "promotionNews", PromotionNews },
                    { "memory", new Dictionary<string, object>
                        {
                            { "personal", Personal }, { "captainImpression", CaptainImpression },
                            { "relationships", Relationships }, { "lessons", Lessons },
                            { "chronicle", Chronicle }, { "missionNotes", MissionNotes }, { "anecdotes", Anecdotes }
                        }
                    },
                    { "pending", new Dictionary<string, object>
                        {
                            { "unfinishedLog", UnfinishedLog }, { "debriefLog", PendingDebriefLog },
                            { "station", PendingStation }, { "minutes", PendingMinutes },
                            { "commands", PendingCommands }, { "ordersFollowed", PendingOrdersFollowed },
                            { "unfinishedMinutes", UnfinishedMinutes }, { "unfinishedCommands", UnfinishedCommands },
                            { "unfinishedOrdersFollowed", UnfinishedOrdersFollowed }
                        }
                    }
                };
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(root), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, FilePath + ".bak", true);
                    File.Delete(FilePath);
                }
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(Name + ": Speichern fehlgeschlagen: " + e.Message);
            }
        }

        public VoiceSpec VoiceSpec => STBCVoiceCrew.VoiceSpec.Parse(Voice, VoiceSpeed, VoicePitch);

        static float Float(object o, string key, float fallback)
        {
            var v = Json.Get(o, key);
            return v is double ? (float)(double)v : fallback;
        }

        static int Int(object o, string key)
        {
            var v = Json.Get(o, key);
            return v is double ? (int)(double)v : 0;
        }

        static void Strings(object arr, List<string> into)
        {
            into.Clear();
            var l = Json.Arr(arr);
            if (l == null) return;
            foreach (var o in l)
            {
                var s = o as string;
                if (!string.IsNullOrEmpty(s)) into.Add(s);
            }
        }

        // ---------------- Prompt-Bausteine ----------------

        public string ProfileForPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("Du bist ").Append(Rank).Append(' ').Append(Name);
            if (!string.IsNullOrEmpty(Species)) sb.Append(" (").Append(Species).Append(')');
            sb.Append(".\n");
            if (!string.IsNullOrEmpty(Pronouns)) sb.Append("Pronomen: ").Append(Pronouns).Append(".\n");
            if (!string.IsNullOrEmpty(Personality)) sb.Append("Persönlichkeit: ").Append(Personality).Append('\n');
            sb.Append("Charakterzüge (0–10): ");
            foreach (var tn in TraitNames)
            {
                int v = Trait(tn[0]);
                sb.Append(tn[0]).Append(' ').Append(v);
                if (v <= 2) sb.Append(" (").Append(tn[1]).Append(')');
                else if (v >= 8) sb.Append(" (").Append(tn[2]).Append(')');
                sb.Append("; ");
            }
            sb.Append("Verhalte dich konsequent danach.\n");
            if (!string.IsNullOrEmpty(SpeechStyle)) sb.Append("So sprichst du: ").Append(SpeechStyle).Append('\n');
            sb.Append(string.IsNullOrEmpty(Laugh) ? "Du lachst nie laut.\n" : "So lachst du: " + Laugh + "\n");
            if (!string.IsNullOrEmpty(Background)) sb.Append("Hintergrund: ").Append(Background).Append('\n');
            sb.Append("Dienstakte: ").Append(Missions).Append(" Missionen, ").Append(Commendations).Append(" Belobigungen, ").Append(Xp).Append(" Erfahrungspunkte.\n");
            sb.Append(Ranks.Conduct(Xp)).Append('\n');
            return sb.ToString();
        }

        public string MemoryForPrompt()
        {
            var sb = new StringBuilder("DEIN GEDÄCHTNIS\n");
            if (!string.IsNullOrEmpty(Personal)) sb.Append("Wie du dich entwickelt hast: ").Append(Personal).Append('\n');
            if (!string.IsNullOrEmpty(CaptainImpression)) sb.Append("Dein Eindruck vom Captain: ").Append(CaptainImpression).Append('\n');
            if (!string.IsNullOrEmpty(Relationships)) sb.Append("Deine Kollegen: ").Append(Relationships).Append('\n');
            if (Lessons.Count > 0)
            {
                sb.Append("Deine Lektionen:\n");
                foreach (var l in Lessons) sb.Append("- ").Append(l).Append('\n');
            }
            if (Chronicle.Count > 0)
            {
                sb.Append("Frühere Missionen:\n");
                foreach (var c in Chronicle) sb.Append("- ").Append(c).Append('\n');
            }
            if (Missions == 0 && Lessons.Count == 0) sb.Append("Das ist deine erste Mission auf der Aegis.\n");
            return sb.ToString();
        }
    }
}
