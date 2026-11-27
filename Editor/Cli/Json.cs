using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cuvara.VFX.Editor
{
    /// <summary>A JSON object that keeps the order of insertion, so written JSON is stable.</summary>
    public sealed class JsonObject : List<KeyValuePair<string, object>>
    {
        /// <summary>Appends a key and its value (no check for duplicates).</summary>
        public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));

        /// <summary>Replaces the value of an existing key, or appends it.</summary>
        public void Set(string key, object value)
        {
            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(this[i].Key, key, StringComparison.Ordinal))
                {
                    this[i] = new KeyValuePair<string, object>(key, value);
                    return;
                }
            }
            Add(key, value);
        }

        /// <summary>True when the key is present.</summary>
        public bool Has(string key) => FindIndex(p => string.Equals(p.Key, key, StringComparison.Ordinal)) >= 0;

        /// <summary>The value of the key, or null.</summary>
        public object Get(string key)
        {
            foreach (var pair in this)
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                    return pair.Value;
            return null;
        }
    }

    /// <summary>
    /// Minimal JSON reader and writer for the agent interface (no third-party dependency).
    /// </summary>
    /// <remarks>
    /// <para>Reading gives <see cref="JsonObject"/>, <c>List&lt;object&gt;</c>, string, bool, null,
    /// <c>long</c> for integers that fit and <c>double</c> for every other number.</para>
    /// <para>Writing accepts those plus any <c>IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt;</c>
    /// (written in its own order), any <see cref="IDictionary"/> (written with keys sorted ordinally),
    /// any other <see cref="IEnumerable"/> (an array), enums (by name) and every numeric type. Floats are
    /// written with the shortest round-trip form, and NaN or an infinity as a string, so the same value
    /// always gives the same text.</para>
    /// </remarks>
    public static class Json
    {
        // ------------------------------------------------------------------ writing

        /// <summary>The value as compact JSON, or indented by two spaces when <paramref name="indent"/> is true.</summary>
        public static string Write(object value, bool indent = false)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, indent, 0);
            return builder.ToString();
        }

        static void NewLine(StringBuilder builder, bool indent, int level)
        {
            if (!indent)
                return;
            builder.Append('\n');
            builder.Append(' ', level * 2);
        }

        static void WriteValue(StringBuilder builder, object value, bool indent, int level)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    return;
                case string s:
                    WriteString(builder, s);
                    return;
                case bool b:
                    builder.Append(b ? "true" : "false");
                    return;
                case Enum e:
                    WriteString(builder, e.ToString());
                    return;
                case float f:
                    WriteFloat(builder, f);
                    return;
                case double d:
                    WriteDouble(builder, d);
                    return;
                case decimal m:
                    builder.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case int _:
                case long _:
                case short _:
                case byte _:
                case sbyte _:
                case ushort _:
                case uint _:
                case ulong _:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case char c:
                    WriteString(builder, c.ToString());
                    return;
                case IDictionary dictionary:
                {
                    var rows = new List<KeyValuePair<string, object>>();
                    foreach (DictionaryEntry entry in dictionary)
                        rows.Add(new KeyValuePair<string, object>(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), entry.Value));
                    WriteObject(builder, rows.OrderBy(r => r.Key, StringComparer.Ordinal), indent, level);
                    return;
                }
                case IEnumerable<KeyValuePair<string, object>> ordered:
                    WriteObject(builder, ordered, indent, level);
                    return;
                case IEnumerable enumerable:
                    WriteArray(builder, enumerable, indent, level);
                    return;
                default:
                    WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        static void WriteObject(StringBuilder builder, IEnumerable<KeyValuePair<string, object>> pairs, bool indent, int level)
        {
            builder.Append('{');
            bool first = true;
            foreach (var pair in pairs)
            {
                if (!first)
                    builder.Append(',');
                first = false;
                NewLine(builder, indent, level + 1);
                WriteString(builder, pair.Key ?? string.Empty);
                builder.Append(indent ? ": " : ":");
                WriteValue(builder, pair.Value, indent, level + 1);
            }
            if (!first)
                NewLine(builder, indent, level);
            builder.Append('}');
        }

        static void WriteArray(StringBuilder builder, IEnumerable items, bool indent, int level)
        {
            builder.Append('[');
            bool first = true;
            foreach (var item in items)
            {
                if (!first)
                    builder.Append(',');
                first = false;
                NewLine(builder, indent, level + 1);
                WriteValue(builder, item, indent, level + 1);
            }
            if (!first)
                NewLine(builder, indent, level);
            builder.Append(']');
        }

        static void WriteFloat(StringBuilder builder, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                WriteString(builder, value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteDouble(StringBuilder builder, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                WriteString(builder, value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        // ------------------------------------------------------------------ reading

        /// <summary>Parses JSON text. Throws <see cref="FormatException"/> with the character position on invalid input.</summary>
        public static object Parse(string text)
        {
            if (text == null)
                throw new FormatException("JSON text is null");
            var reader = new Reader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw reader.Error("unexpected text after the JSON value");
            return value;
        }

        sealed class Reader
        {
            const int MaxDepth = 256;
            readonly string m_Text;
            int m_Position;

            public Reader(string text)
            {
                m_Text = text;
            }

            public bool AtEnd => m_Position >= m_Text.Length;

            public FormatException Error(string message) => new FormatException($"invalid JSON at character {m_Position}: {message}");

            public void SkipWhitespace()
            {
                while (m_Position < m_Text.Length && char.IsWhiteSpace(m_Text[m_Position]))
                    m_Position++;
            }

            char Peek() => m_Position < m_Text.Length ? m_Text[m_Position] : '\0';

            void Expect(char c)
            {
                if (Peek() != c)
                    throw Error($"expected '{c}'");
                m_Position++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth)
                    throw Error("nested too deep");
                SkipWhitespace();
                if (AtEnd)
                    throw Error("unexpected end");
                var c = Peek();
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadWord("true"); return true;
                    case 'f': ReadWord("false"); return false;
                    case 'n': ReadWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Error($"unexpected character '{c}'");
                }
            }

            void ReadWord(string word)
            {
                if (string.CompareOrdinal(m_Text, m_Position, word, 0, word.Length) != 0)
                    throw Error($"expected '{word}'");
                m_Position += word.Length;
            }

            JsonObject ReadObject(int depth)
            {
                Expect('{');
                var result = new JsonObject();
                SkipWhitespace();
                if (Peek() == '}')
                {
                    m_Position++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                        throw Error("expected a property name");
                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    var value = ReadValue(depth + 1);
                    if (result.Has(key))
                        throw Error($"duplicate property '{key}'");
                    result.Add(key, value);
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        m_Position++;
                        continue;
                    }
                    Expect('}');
                    return result;
                }
            }

            List<object> ReadArray(int depth)
            {
                Expect('[');
                var result = new List<object>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    m_Position++;
                    return result;
                }
                while (true)
                {
                    result.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        m_Position++;
                        continue;
                    }
                    Expect(']');
                    return result;
                }
            }

            string ReadString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("unterminated string");
                    var c = m_Text[m_Position++];
                    if (c == '"')
                        return builder.ToString();
                    if (c != '\\')
                    {
                        if (c < 0x20)
                            throw Error("control character in a string");
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd)
                        throw Error("unterminated escape");
                    var e = m_Text[m_Position++];
                    switch (e)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (m_Position + 4 > m_Text.Length)
                                throw Error("short \\u escape");
                            if (!int.TryParse(m_Text.Substring(m_Position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw Error("bad \\u escape");
                            builder.Append((char)code);
                            m_Position += 4;
                            break;
                        default:
                            throw Error($"bad escape '\\{e}'");
                    }
                }
            }

            object ReadNumber()
            {
                int start = m_Position;
                if (Peek() == '-')
                    m_Position++;
                bool integer = true;
                while (!AtEnd)
                {
                    var c = Peek();
                    if (c >= '0' && c <= '9')
                    {
                        m_Position++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        integer = false;
                        m_Position++;
                    }
                    else
                    {
                        break;
                    }
                }
                var token = m_Text.Substring(start, m_Position - start);
                if (integer && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                throw Error($"bad number '{token}'");
            }
        }
    }
}
