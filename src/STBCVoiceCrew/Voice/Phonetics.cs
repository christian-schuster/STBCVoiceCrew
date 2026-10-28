using System;
using System.Collections.Generic;
using System.Text;

namespace STBCVoiceCrew.Voice
{
    /// <summary>
    /// Klangabgleich für Eigennamen, die die Spracherkennung nicht kennt ("Rekusa" → "Ragusa", "Typus" → "Typos").
    /// Kölner Phonetik (für deutsche Aussprache) plus Levenshtein-Distanz auf Buchstaben.
    /// </summary>
    public static class Phonetics
    {
        /// <summary>Kölner Phonetik: Ziffernfolge, die ähnlich klingende Wörter gleich kodiert.</summary>
        public static string Koelner(string word)
        {
            if (string.IsNullOrEmpty(word)) return "";
            string s = Normalize(word);
            var raw = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char prev = i > 0 ? s[i - 1] : '\0';
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                string code;
                switch (c)
                {
                    case 'a': case 'e': case 'i': case 'j': case 'o': case 'u': case 'y': code = "0"; break;
                    case 'h': code = ""; break;
                    case 'b': code = "1"; break;
                    case 'p': code = next == 'h' ? "3" : "1"; break;
                    case 'd': case 't': code = (next == 'c' || next == 's' || next == 'z') ? "8" : "2"; break;
                    case 'f': case 'v': case 'w': code = "3"; break;
                    case 'g': case 'k': case 'q': code = "4"; break;
                    case 'c':
                        if (i == 0) code = "ahkloqrux".IndexOf(next) >= 0 ? "4" : "8";
                        else if (prev == 's' || prev == 'z') code = "8";
                        else code = "ahkoqux".IndexOf(next) >= 0 ? "4" : "8";
                        break;
                    case 'x': code = (prev == 'c' || prev == 'k' || prev == 'q') ? "8" : "48"; break;
                    case 'l': code = "5"; break;
                    case 'm': case 'n': code = "6"; break;
                    case 'r': code = "7"; break;
                    case 's': case 'z': code = "8"; break;
                    default: code = char.IsDigit(c) ? c.ToString() : ""; break;   // Ziffern unterscheiden ("Alpha 2" ≠ "Alpha 4")
                }
                raw.Append(code);
            }
            // Doppelte zusammenfassen, Nullen außer am Anfang entfernen
            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && raw[i] == raw[i - 1]) continue;
                if (raw[i] == '0' && sb.Length > 0) continue;
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }

        /// <summary>Kleinbuchstaben, Umlaute ausgeschrieben, nur Buchstaben und Ziffern.</summary>
        public static string Normalize(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant())
            {
                switch (ch)
                {
                    case 'ä': sb.Append("ae"); break;
                    case 'ö': sb.Append("oe"); break;
                    case 'ü': sb.Append("ue"); break;
                    case 'ß': sb.Append("ss"); break;
                    default: if (char.IsLetterOrDigit(ch)) sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        public static int Levenshtein(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var d = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = d[0];
                d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int tmp = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                    prev = tmp;
                }
            }
            return d[b.Length];
        }

        /// <summary>Ähnlichkeit 0..1 aus Klang (Kölner Phonetik) und Schreibweise.</summary>
        public static float Similarity(string heard, string name)
        {
            string a = Normalize(heard), b = Normalize(name);
            if (a.Length == 0 || b.Length == 0) return 0f;
            if (a == b) return 1f;
            float spell = 1f - (float)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
            string ka = Koelner(heard), kb = Koelner(name);
            float sound = ka.Length == 0 || kb.Length == 0 ? 0f : 1f - (float)Levenshtein(ka, kb) / Math.Max(ka.Length, kb.Length);
            return 0.45f * spell + 0.55f * sound;
        }

        /// <summary>
        /// Bester Name aus der Liste für das Gehörte. Wörter werden einzeln und in Gruppen verglichen
        /// ("Plazmakülmittel A" ↔ "Plasma-Kühlmittel A"). null, wenn nichts ähnlich genug oder zweideutig ist.
        /// </summary>
        public static string BestMatch(string heard, IList<string> names, out float score, float min = 0.62f)
        {
            score = 0f;
            if (string.IsNullOrEmpty(heard) || names == null || names.Count == 0) return null;
            string best = null; float bestScore = 0f, second = 0f;
            foreach (var n in names)
                if (Normalize(n) == Normalize(heard)) { score = 1f; return n; }   // exakter Treffer gewinnt immer
            foreach (var n in names)
            {
                float s = Similarity(heard, n);
                // Der Name kann auch nur ein Teil des Gehörten sein ("die tallinn auf leben")
                foreach (var part in Windows(heard, CountWords(n))) s = Math.Max(s, Similarity(part, n));
                if (s > bestScore) { second = bestScore; bestScore = s; best = n; }
                else if (s > second) second = s;
            }
            score = bestScore;
            if (bestScore < min) return null;
            if (bestScore - second < 0.04f && second >= min) return null;   // zweideutig: lieber nachfragen
            return best;
        }

        static int CountWords(string s) => s.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Length;

        static IEnumerable<string> Windows(string text, int size)
        {
            var w = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int len = Math.Max(1, size - 1); len <= size + 1; len++)
                for (int i = 0; i + len <= w.Length; i++)
                    yield return string.Join(" ", w, i, len);
        }
    }
}
