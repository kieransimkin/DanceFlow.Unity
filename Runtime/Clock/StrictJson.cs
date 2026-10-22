using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Small bounded JSON reader for source-bound timing data; rejects duplicate keys and invalid numbers.</summary>
    internal sealed class StrictJson
    {
        private readonly string _text;
        private int _index;
        private const int MaxDepth = 48;
        private StrictJson(string text) { _text = text; }

        public static object Parse(string text, int maxBytes = 32 * 1024 * 1024)
        {
            if (maxBytes <= 0 || maxBytes > 64 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            // Do the inexpensive UTF-16 character bound before counting UTF-8 bytes.
            if (text == null || text.Length > maxBytes || Encoding.UTF8.GetByteCount(text) > maxBytes)
                throw new FormatException(maxBytes == 32 * 1024 * 1024
                    ? "Beat timeline JSON is missing or exceeds 32 MiB."
                    : "Recording JSON is missing or exceeds 64 MiB.");
            var parser = new StrictJson(text);
            object result = parser.Read(0);
            parser.SkipWhitespace();
            if (parser._index != text.Length) throw new FormatException("Unexpected text after JSON value.");
            return result;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length &&
                   (_text[_index] == ' ' || _text[_index] == '\t' || _text[_index] == '\n' || _text[_index] == '\r'))
                _index++;
        }

        private char Peek()
        {
            if (_index >= _text.Length) throw new FormatException("Unexpected end of JSON.");
            return _text[_index];
        }

        private void Expect(char c)
        {
            if (Peek() != c) throw new FormatException("Expected '" + c + "' in JSON.");
            _index++;
        }

        private bool Consume(char c)
        {
            SkipWhitespace();
            if (_index < _text.Length && _text[_index] == c) { _index++; return true; }
            return false;
        }

        private object Read(int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nesting too deep.");
            SkipWhitespace();
            char c = Peek();
            if (c == '{')
            {
                _index++;
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Consume('}')) return map;
                do
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace(); Expect(':');
                    object value = Read(depth + 1);
                    if (map.ContainsKey(key)) throw new FormatException("Duplicate JSON object key: " + key);
                    map.Add(key, value);
                    if (Consume('}')) return map;
                    Expect(',');
                } while (true);
            }
            if (c == '[')
            {
                _index++;
                var list = new List<object>();
                if (Consume(']')) return list;
                do
                {
                    list.Add(Read(depth + 1));
                    if (Consume(']')) return list;
                    Expect(',');
                } while (true);
            }
            if (c == '"') return ReadString();
            if (c == 't') return Literal("true", true);
            if (c == 'f') return Literal("false", false);
            if (c == 'n') return Literal("null", null);
            if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
            throw new FormatException("Illegal JSON token.");
        }

        private object Literal(string literal, object value)
        {
            if (_index + literal.Length > _text.Length ||
                string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                throw new FormatException("Invalid JSON literal.");
            _index += literal.Length;
            return value;
        }

        private string ReadString()
        {
            Expect('"');
            var result = new StringBuilder();
            bool terminated = false;
            while (_index < _text.Length)
            {
                char c = _text[_index++];
                if (c == '"') { terminated = true; break; }
                if (c < ' ') throw new FormatException("Unescaped control character in JSON string.");
                if (c == '\\')
                {
                    c = Peek(); _index++;
                    switch (c)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw new FormatException("Short Unicode escape.");
                            int scalar = 0;
                            for (int j = 0; j < 4; j++)
                            {
                                char h = _text[_index++];
                                int nibble = h >= '0' && h <= '9' ? h - '0' :
                                    h >= 'a' && h <= 'f' ? h - 'a' + 10 :
                                    h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                                if (nibble < 0) throw new FormatException("Invalid Unicode escape.");
                                scalar = (scalar << 4) + nibble;
                            }
                            result.Append((char)scalar);
                            break;
                        default: throw new FormatException("Unknown JSON escape.");
                    }
                }
                else result.Append(c);
            }
            if (!terminated) throw new FormatException("Unterminated JSON string.");
            // Reject unpaired UTF-16 surrogates (which UTF8 encoders can silently replace).
            for (int i = 0; i < result.Length; i++)
            {
                if (char.IsHighSurrogate(result[i]))
                {
                    if (i + 1 == result.Length || !char.IsLowSurrogate(result[i + 1]))
                        throw new FormatException("Unpaired Unicode high surrogate.");
                    i++;
                }
                else if (char.IsLowSurrogate(result[i]))
                    throw new FormatException("Unpaired Unicode low surrogate.");
            }
            return result.ToString();
        }

        private object ReadNumber()
        {
            int start = _index;
            if (Peek() == '-') _index++;
            if (Peek() == '0') _index++;
            else
            {
                if (Peek() < '1' || Peek() > '9') throw new FormatException("Invalid leading JSON digit.");
                while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9') _index++;
            }
            bool integer = true;
            if (_index < _text.Length && _text[_index] == '.')
            {
                integer = false; _index++;
                if (_index >= _text.Length || _text[_index] < '0' || _text[_index] > '9')
                    throw new FormatException("JSON number needs decimal digits.");
                while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9') _index++;
            }
            if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                integer = false; _index++;
                if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;
                if (_index >= _text.Length || _text[_index] < '0' || _text[_index] > '9')
                    throw new FormatException("JSON exponent needs digits.");
                while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9') _index++;
            }
            string raw = _text.Substring(start, _index - start);
            if (integer)
            {
                if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
                    throw new FormatException("JSON integer exceeds Int64 bounds.");
                return n;
            }
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ||
                double.IsNaN(d) || double.IsInfinity(d))
                throw new FormatException("JSON floating number must be finite.");
            return d;
        }
    }
}
