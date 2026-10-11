using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VoidTyrant.ModKit
{
    /// <summary>
    /// Minimal JSON reader/writer. The game ships no JSON library and Unity's JsonUtility
    /// can't read dictionaries, so content mods are parsed with this instead.
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string / bool / null.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text) => new Reader(text).ParseDocument();

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string text) { s = text; }

            public object ParseDocument()
            {
                var v = ParseValue();
                SkipWhitespace();
                if (i < s.Length) throw Error("unexpected trailing content");
                return v;
            }

            object ParseValue()
            {
                SkipWhitespace();
                if (i >= s.Length) throw Error("unexpected end of input");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c)) return ParseNumber();
                        throw Error($"unexpected character '{c}'");
                }
            }

            Dictionary<string, object> ParseObject()
            {
                var obj = new Dictionary<string, object>();
                i++; // {
                SkipWhitespace();
                if (Peek() == '}') { i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("expected property name");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("expected ':'");
                    i++;
                    obj[key] = ParseValue();
                    SkipWhitespace();
                    char c = Peek();
                    i++;
                    if (c == '}') return obj;
                    if (c != ',') throw Error("expected ',' or '}'");
                    SkipWhitespace();
                    if (Peek() == '}') { i++; return obj; } // tolerate trailing comma
                }
            }

            List<object> ParseArray()
            {
                var list = new List<object>();
                i++; // [
                SkipWhitespace();
                if (Peek() == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    i++;
                    if (c == ']') return list;
                    if (c != ',') throw Error("expected ',' or ']'");
                    SkipWhitespace();
                    if (Peek() == ']') { i++; return list; }
                }
            }

            string ParseString()
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (true)
                {
                    if (i >= s.Length) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Error("unterminated escape");
                    char e = s[i++];
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
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier,
                                    CultureInfo.InvariantCulture, out int code))
                                throw Error("bad unicode escape");
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: throw Error($"bad escape '\\{e}'");
                    }
                }
            }

            double ParseNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    i = start;
                    throw Error("invalid number");
                }
                return value;
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"expected '{word}'");
                i += word.Length;
            }

            void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    if (char.IsWhiteSpace(s[i])) { i++; continue; }
                    // Allow // line comments so mod authors can annotate their files.
                    if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }
                    break;
                }
            }

            char Peek() => i < s.Length ? s[i] : '\0';

            FormatException Error(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                {
                    if (s[k] == '\n') { line++; col = 1; } else col++;
                }
                return new FormatException($"JSON {msg} at line {line}, column {col}");
            }
        }

        static void WriteValue(StringBuilder sb, object v, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case Enum e: WriteString(sb, e.ToString()); break;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IConvertible c when IsNumber(v): sb.Append(c.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary dict:
                    if (dict.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    bool first = true;
                    foreach (DictionaryEntry kv in dict)
                    {
                        if (!first) sb.Append(",\n");
                        first = false;
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, kv.Key.ToString());
                        sb.Append(": ");
                        WriteValue(sb, kv.Value, indent + 1);
                    }
                    sb.Append('\n').Append(' ', indent * 2).Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(", ");
                        firstItem = false;
                        WriteValue(sb, item, indent + 1);
                    }
                    sb.Append(']');
                    break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static bool IsNumber(object v) =>
            v is int || v is long || v is short || v is byte || v is uint || v is ulong || v is ushort || v is sbyte || v is decimal;

        static void WriteString(StringBuilder sb, string str)
        {
            sb.Append('"');
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
