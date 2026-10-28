using System;
using System.Collections.Generic;
using System.IO;

namespace StbcAiCrew
{
    /// <summary>
    /// Alle KI-Crewmitglieder (ein JSON je Charakter in BepInEx\config\StbcAiCrew\crew\).
    /// Beim ersten Start wird eine Stammbesetzung angelegt. Eigene Charaktere: Datei kopieren und anpassen.
    /// </summary>
    public sealed class Roster
    {
        readonly string _dir;
        public readonly List<Character> All = new List<Character>();

        public Roster(string dir) { _dir = dir; }

        public void Load()
        {
            Directory.CreateDirectory(_dir);
            if (Directory.GetFiles(_dir, "*.json").Length == 0) CreateDefaults();
            All.Clear();
            foreach (var f in Directory.GetFiles(_dir, "*.json"))
            {
                try { All.Add(Character.Load(f)); }
                catch (Exception e) { Plugin.Log.LogError("Charakter " + Path.GetFileName(f) + " unlesbar: " + e.Message); }
            }
            foreach (var c in All)
            {
                bool changed = false;
                if (string.IsNullOrEmpty(c.Voice)) { AssignVoice(c); changed = true; }
                if (c.Laugh == null) { AssignLaugh(c); changed = true; }
                if (c.VoiceDesign == null) { AssignVoiceDesign(c); changed = true; }
                if (changed) c.Save();
            }
            Plugin.Log.LogInfo("Crew-Liste: " + All.Count + " Charaktere.");
        }

        // Stammbesetzung: feste Stimmen. Tempo und Tonhöhe passend zum Charakter.
        static readonly Dictionary<string, object[]> DefaultVoices = new Dictionary<string, object[]>
        {
            { "rao",      new object[] { "de_DE-thorsten-medium", 1.00f, 1.00f } },
            { "hoyle",    new object[] { "de_DE-kerstin-low", 1.05f, 1.00f } },
            { "tnar",     new object[] { "de_DE-pavoque-low", 0.92f, 0.97f } },
            { "zhal",     new object[] { "de_DE-ramona-low", 1.10f, 1.06f } },
            { "kerrigan", new object[] { "de_DE-karlsson-low", 1.00f, 0.93f } },
            { "venn",     new object[] { "de_DE-eva_k-x_low", 1.08f, 1.08f } },
            { "brannok",  new object[] { "de_DE-thorsten_emotional-medium#1", 0.95f, 0.86f } },   // Sprecher 1 = "angry"
        };

        // Jeder lacht anders (Teil des Charakters). T'nar als Vulkanier lacht nie.
        static readonly Dictionary<string, string> DefaultLaughs = new Dictionary<string, string>
        {
            { "rao", "Heh. Hehe." },
            { "hoyle", "Ha. Ha. Ha." },
            { "tnar", "" },
            { "zhal", "Hah! Hahahaaa!" },
            { "kerrigan", "Haaaahahaha! Hohohoho!" },
            { "venn", "Hihihihi!" },
            { "brannok", "Hrmpf. Har har har." },
        };

        // Stimmbeschreibungen für den Stimmentwurf (Qwen3-TTS VoiceDesign versteht Englisch am besten)
        static readonly Dictionary<string, string> DefaultVoiceDesigns = new Dictionary<string, string>
        {
            { "rao", "Male, around 35, calm and precise baritone, clear diction, composed and steady, a starship helmsman." },
            { "hoyle", "Female, around 40, firm and alert alto voice, direct and clipped delivery, confident security officer." },
            { "tnar", "Male, middle-aged, low and perfectly even voice, emotionless and precise, slow measured pace, cool and logical." },
            { "zhal", "Female, adult, low husky alto voice, intense and fiery, fast confident delivery, proud warrior." },
            { "kerrigan", "Male, older, low rumbling deep voice, gravelly and warm, cheerful, slight Irish accent." },
            { "venn", "Female, young adult, gentle low-mid voice, calm and warm, soft-spoken and a little unsure." },
            { "brannok", "Male, middle-aged, very deep, gruff and growly voice, grumpy and argumentative, gravelly and blunt." },
        };

        /// <summary>Stimmbeschreibung: Stammbesetzung fest, sonst aus Pronomen, Rang und Charakterzügen abgeleitet.</summary>
        public static void AssignVoiceDesign(Character c)
        {
            string v;
            if (DefaultVoiceDesigns.TryGetValue(c.Id, out v)) { c.VoiceDesign = v; return; }
            string p = (c.Pronouns ?? "").ToLowerInvariant();
            string who = p.StartsWith("sie") ? "Female" : p.StartsWith("er") ? "Male" : "Adult";
            string age = Ranks.Index(c.Xp) == 0 ? "young adult" : Ranks.Index(c.Xp) >= 3 ? "middle-aged" : "around 30";
            var parts = new List<string> { who + ", " + age };
            parts.Add(c.Trait("Gelassenheit") >= 7 ? "calm and steady voice" : c.Trait("Gelassenheit") <= 3 ? "tense, quick-tempered voice" : "natural voice");
            parts.Add(c.Trait("Beredsamkeit") >= 7 ? "talkative, lively pace" : c.Trait("Beredsamkeit") <= 3 ? "terse and slow" : "moderate pace");
            if (c.Trait("Humor") >= 7) parts.Add("cheerful and playful");
            if (c.Trait("Mut") <= 3) parts.Add("a little nervous");
            if (c.Trait("Aggressivität") >= 7) parts.Add("forceful");
            if (c.Trait("Empathie") >= 8) parts.Add("warm and gentle");
            c.VoiceDesign = string.Join(", ", parts.ToArray()) + ".";
        }

        /// <summary>Lachen für einen Charakter ohne eigenes: Stammbesetzung fest, sonst nach Humor.</summary>
        public static void AssignLaugh(Character c)
        {
            string l;
            if (DefaultLaughs.TryGetValue(c.Id, out l)) { c.Laugh = l; return; }
            int h = c.Trait("Humor");
            c.Laugh = h <= 2 ? "" : h <= 4 ? "Heh." : h <= 6 ? "Haha!" : "Hahahaha!";
        }

        /// <summary>
        /// Gibt einem Charakter ohne Stimme eine: Stammbesetzung fest, alle anderen einen eigenen
        /// Sprecher aus dem Mehrsprecher-Modell (236 Stimmen), Tempo nach Beredsamkeit und Gelassenheit.
        /// </summary>
        public void AssignVoice(Character c)
        {
            object[] def;
            if (DefaultVoices.TryGetValue(c.Id, out def))
            {
                c.Voice = (string)def[0];
                c.VoiceSpeed = (float)def[1];
                c.VoicePitch = (float)def[2];
                return;
            }
            var used = new HashSet<string>();
            foreach (var o in All) if (o != c && !string.IsNullOrEmpty(o.Voice)) used.Add(o.Voice);
            int seed = Math.Abs((c.Id + c.Name).GetHashCode());
            for (int i = 0; i < 236; i++)
            {
                string v = "de_DE-mls-medium#" + ((seed + i * 37) % 236);
                if (!used.Contains(v)) { c.Voice = v; break; }
            }
            c.VoiceSpeed = 0.95f + (c.Trait("Beredsamkeit") - 5) * 0.02f - (c.Trait("Gelassenheit") - 5) * 0.01f;
            c.VoicePitch = 1f;
        }

        string DismissedDir => Path.Combine(_dir, "entlassen");

        /// <summary>Neuen Charakter aufnehmen (Id wird aus dem Namen gebildet und eindeutig gemacht).</summary>
        public Character Add(Character c)
        {
            string baseId = Slug(string.IsNullOrEmpty(c.Id) ? c.Name : c.Id);
            string id = baseId;
            for (int i = 2; Find(id) != null || File.Exists(Path.Combine(DismissedDir, id + ".json")); i++) id = baseId + i;
            c.Id = id;
            c.FilePath = Path.Combine(_dir, id + ".json");
            if (string.IsNullOrEmpty(c.Voice)) AssignVoice(c);
            if (c.Laugh == null) AssignLaugh(c);
            if (c.VoiceDesign == null) AssignVoiceDesign(c);
            c.Save();
            All.Add(c);
            return c;
        }

        /// <summary>Entlassen: Datei wandert nach crew\entlassen\ und kann zurückgeholt werden.</summary>
        public void Dismiss(Character c)
        {
            Directory.CreateDirectory(DismissedDir);
            string target = Path.Combine(DismissedDir, Path.GetFileName(c.FilePath));
            if (File.Exists(target)) File.Delete(target);
            File.Move(c.FilePath, target);
            c.FilePath = target;
            All.Remove(c);
            Plugin.Log.LogInfo(c.Name + " wurde entlassen.");
        }

        public List<Character> LoadDismissed()
        {
            var list = new List<Character>();
            if (!Directory.Exists(DismissedDir)) return list;
            foreach (var f in Directory.GetFiles(DismissedDir, "*.json"))
            {
                try { list.Add(Character.Load(f)); }
                catch (Exception e) { Plugin.Log.LogWarning("Entlassener Charakter " + Path.GetFileName(f) + " unlesbar: " + e.Message); }
            }
            return list;
        }

        public void Restore(Character c)
        {
            string target = Path.Combine(_dir, Path.GetFileName(c.FilePath));
            if (File.Exists(target)) throw new IOException("Es gibt schon einen aktiven Charakter mit dieser Datei.");
            File.Move(c.FilePath, target);
            c.FilePath = target;
            All.Add(c);
            Plugin.Log.LogInfo(c.Name + " ist zurück im Dienst.");
        }

        static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToLowerInvariant())
            {
                if (ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9') sb.Append(ch);
                else if (ch == 'ä') sb.Append("ae");
                else if (ch == 'ö') sb.Append("oe");
                else if (ch == 'ü') sb.Append("ue");
                else if (ch == 'ß') sb.Append("ss");
            }
            return sb.Length > 0 ? sb.ToString() : "offizier";
        }

        public Character Find(string id)
        {
            foreach (var c in All) if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>
        /// Besetzt eine Station. Feste Wahl per Config (Charakter-Id) oder "auto": wer die Station kann
        /// und am längsten nicht gespielt hat, damit alle Erfahrung sammeln.
        /// </summary>
        public Character Pick(string station, string wish, ICollection<Character> taken)
        {
            if (!string.IsNullOrEmpty(wish) && !wish.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var c = Find(wish);
                if (c != null && !taken.Contains(c)) return c;
                Plugin.Log.LogWarning("Besetzung '" + wish + "' für " + station + " nicht verfügbar, wähle automatisch.");
            }
            Character best = null;
            foreach (var c in All)
            {
                if (taken.Contains(c) || !c.CanServe(station)) continue;
                if (best == null || string.CompareOrdinal(c.LastPlayed, best.LastPlayed) < 0 ||
                    (c.LastPlayed == best.LastPlayed && c.Stations.IndexOf(station) < best.Stations.IndexOf(station)))
                    best = c;
            }
            if (best != null) return best;
            foreach (var c in All) if (!taken.Contains(c)) return c;   // Notbesetzung
            return null;
        }

        void CreateDefaults()
        {
            Plugin.Log.LogInfo("Lege Stammbesetzung an in " + _dir);
            Add("rao", "Humor=4,Mut=7,Aggressivität=3,Beredsamkeit=3,Disziplin=8,Empathie=5,Ehrgeiz=4,Gelassenheit=9,Benehmen=7", "Rao", "Mensch", 800, new[] { "helm" },
                "Ruhig, präzise und kaum aus der Ruhe zu bringen. Fliegt lieber elegant als schnell.",
                "Knapp und sachlich, nennt Kurs und Geschwindigkeit in Zahlen.",
                "Seit der Akademie Pilot, hat schon auf mehreren Schiffen der Miranda-Klasse gedient.",
                "Ruhige Ausstrahlung, Uniform stets makellos.");
            Add("hoyle", "Humor=3,Mut=8,Aggressivität=7,Beredsamkeit=4,Disziplin=7,Empathie=5,Ehrgeiz=5,Gelassenheit=5,Benehmen=6", "Hoyle", "Mensch", 800, new[] { "tactical" },
                "Direkt, wachsam und beschützend. Denkt zuerst an Schiff und Crew, mag keine Überraschungen.",
                "Kurze, entschlossene Sätze; meldet Bedrohungen sofort.",
                "Diente früher in der Grenzpatrouille an der Neutralen Zone zum klingonischen Reich.",
                "Kräftige Statur, kurzes Haar, aufmerksamer Blick.");
            Add("tnar", "Humor=1,Mut=7,Aggressivität=2,Beredsamkeit=4,Disziplin=9,Empathie=3,Ehrgeiz=3,Gelassenheit=10,Benehmen=10", "T'nar", "Vulkanier", 800, new[] { "engineering" },
                "Logisch, nüchtern, gelassen. Zeigt keine Gefühle, schätzt Effizienz über alles.",
                "Präzise, mit exakten Werten und gelegentlich trockener vulkanischer Bemerkung.",
                "Hat an der Vulkanischen Wissenschaftsakademie Warpfeldtheorie studiert.",
                "Typisch vulkanische Frisur und spitze Ohren, aufrechte Haltung.");
            Add("zhal", "Humor=5,Mut=9,Aggressivität=9,Beredsamkeit=6,Disziplin=5,Empathie=4,Ehrgeiz=9,Gelassenheit=2,Benehmen=4", "Zhal sh'Vehra", "Andorianer", 300, new[] { "tactical", "helm" },
                "Temperamentvoll, ehrgeizig und kampfeslustig, dabei unbedingt loyal. Ehre ist ihr wichtig.",
                "Leidenschaftlich, manchmal etwas zu forsch; benutzt gern andorianische Redewendungen.",
                "Jüngste ihres Clans in der Sternenflotte, will sich beweisen.",
                "Blaue Haut, weißes Haar, Antennen, die ihre Stimmung verraten.",
                pronouns: "sie/ihr");
            Add("kerrigan", "Humor=9,Mut=7,Aggressivität=3,Beredsamkeit=8,Disziplin=4,Empathie=7,Ehrgeiz=2,Gelassenheit=7,Benehmen=4", "Tomas Kerrigan", "Mensch", 1600, new[] { "engineering", "helm" },
                "Gutmütiger Veteran mit Humor, improvisiert brillant, wenn alles zusammenbricht. Redet mit seinen Maschinen.",
                "Locker und bildhaft, irischer Einschlag, wird im Notfall plötzlich sehr konzentriert.",
                "Zwanzig Jahre im Maschinenraum, hat schon drei Warpkernbrüche überlebt.",
                "Grau meliert, Lachfalten, ölverschmierte Ärmel.",
                pronouns: "er/ihm");
            Add("venn", "Humor=5,Mut=2,Aggressivität=1,Beredsamkeit=5,Disziplin=6,Empathie=10,Ehrgeiz=6,Gelassenheit=4,Benehmen=9", "Lira Venn", "Betazoid", 0, new[] { "helm", "tactical" },
                "Empathisch, neugierig und noch etwas unsicher. Spürt Stimmungen auf der Brücke.",
                "Höflich, manchmal zögerlich, stellt Rückfragen; wird mit jeder Mission selbstbewusster.",
                "Frisch von der Akademie, erste Mission auf einem Raumschiff.",
                "Schwarze Augen, dunkles Haar, offenes Lächeln.",
                pronouns: "sie/ihr");
            Add("brannok", "Humor=3,Mut=6,Aggressivität=6,Beredsamkeit=7,Disziplin=6,Empathie=2,Ehrgeiz=5,Gelassenheit=3,Benehmen=2", "Brannok", "Tellarit", 800, new[] { "engineering", "tactical" },
                "Mürrisch, streitlustig und gründlich. Hält Widerspruch für Höflichkeit und hat meistens recht.",
                "Brummig, widerspricht gern, lobt nur widerwillig.",
                "Ingenieur aus einer Werftfamilie auf Tellar Prime.",
                "Rüssel-Nase, Bart, gedrungene Statur.");
        }

        void Add(string id, string traits, string name, string species, int xp, string[] stations,
                 string personality, string speech, string background, string appearance, string pronouns = "")
        {
            var c = new Character
            {
                Id = id, Name = name, Species = species, Pronouns = pronouns, Xp = xp,
                Personality = personality, SpeechStyle = speech, Background = background, Appearance = appearance,
                FilePath = Path.Combine(_dir, id + ".json")
            };
            c.Stations.AddRange(stations);
            foreach (var pair in traits.Split(','))
            {
                var kv = pair.Split('=');
                c.SetTrait(kv[0], int.Parse(kv[1]));
            }
            AssignVoice(c);
            AssignLaugh(c);
            AssignVoiceDesign(c);
            c.Save();
        }
    }
}
