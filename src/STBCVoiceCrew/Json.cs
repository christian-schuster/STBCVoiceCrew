using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Minimaler JSON-Parser/-Serializer. Das Spiel bringt nur ein obfuskiertes Newtonsoft mit,
    /// deshalb hier eine eigene, kleine Implementierung für .NET 3.5.
    /// Objekte → Dictionary&lt;string, object&gt;, Arrays → List&lt;object&gt;, Zahlen → double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            var value = p.ReadValue();
            p.SkipWs();
            if (!p.AtEnd) throw new FormatException("Unerwartete Zeichen nach JSON-Ende bei " + p.Pos);
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        // ---- Bequeme Zugriffe ----

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;

        public static object Get(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(object o, string key) => Get(o, key) as string;

        // ---- Serializer ----

        static void Write(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is IDictionary)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in (IDictionary)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, e.Key.ToString());
                    sb.Append(':');
                    Write(sb, e.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is IEnumerable)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in (IEnumerable)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            if (v is int || v is long || v is short || v is byte || v is uint || v is ulong)
            {
                sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (v is float || v is double || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- Parser ----

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s ?? ""; }

            public int Pos => _i;
            public bool AtEnd => _i >= _s.Length;

            public void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            char Peek() => _i < _s.Length ? _s[_i] : '\0';

            void Expect(char c)
            {
                if (Peek() != c) throw new FormatException("'" + c + "' erwartet bei " + _i);
                _i++;
            }

            public object ReadValue()
            {
                SkipWs();
                char c = Peek();
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new FormatException("Unerwartetes Zeichen '" + c + "' bei " + _i);
                }
            }

            void Literal(string lit)
            {
                if (string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0)
                    throw new FormatException(lit + " erwartet bei " + _i);
                _i += lit.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                Expect('{');
                SkipWs();
                if (Peek() == '}') { _i++; return d; }
                while (true)
                {
                    SkipWs();
                    string key = ReadString();
                    SkipWs();
                    Expect(':');
                    d[key] = ReadValue();
                    SkipWs();
                    if (Peek() == ',') { _i++; continue; }
                    Expect('}');
                    return d;
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                Expect('[');
                SkipWs();
                if (Peek() == ']') { _i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (Peek() == ',') { _i++; continue; }
                    Expect(']');
                    return l;
                }
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length) throw new FormatException("Unterminierter String");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) throw new FormatException("Unterminierter Escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("Kurzer \\u-Escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException("Unbekannter Escape \\" + e);
                    }
                }
            }

            double ReadNumber()
            {
                int start = _i;
                if (Peek() == '-') _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
