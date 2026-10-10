using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FomaPay
{
    /// <summary>
    /// JSON minimal : JsonUtility ne sait pas lire un dictionnaire, et le SDK n'impose aucun paquet.
    /// Objets → Dictionary&lt;string, object&gt;, tableaux → List&lt;object&gt;, nombres → double.
    /// </summary>
    internal sealed class Json
    {
        private readonly string _text;
        private int _pos;

        private Json(string text)
        {
            _text = text;
        }

        /// <exception cref="FormatException">Le texte n'est pas du JSON valide.</exception>
        public static object Parse(string text)
        {
            var parser = new Json(text ?? "");
            parser.Spaces();
            var value = parser.Read(0);
            parser.Spaces();
            if (parser._pos != parser._text.Length)
            {
                throw parser.Error("contenu inattendu après la valeur");
            }
            return value;
        }

        public static string Str(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) && value is string text ? text : "";

        public static double Num(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) && value is double number ? number : 0;

        public static bool Flag(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) && value is bool flag && flag;

        public static List<string> Strings(Dictionary<string, object> json, string key)
        {
            var result = new List<string>();
            if (json.TryGetValue(key, out var value) && value is List<object> items)
            {
                foreach (var item in items)
                {
                    if (item is string text)
                    {
                        result.Add(text);
                    }
                }
            }
            return result;
        }

        // ───────────────────────────── Écriture ─────────────────────────────

        public static string Stringify(object value)
        {
            var output = new StringBuilder();
            Write(output, value);
            return output.ToString();
        }

        private static void Write(StringBuilder output, object value)
        {
            switch (value)
            {
                case null:
                    output.Append("null");
                    break;
                case string text:
                    WriteString(output, text);
                    break;
                case bool flag:
                    output.Append(flag ? "true" : "false");
                    break;
                case Dictionary<string, object> map:
                    output.Append('{');
                    var firstMember = true;
                    foreach (var entry in map)
                    {
                        if (!firstMember)
                        {
                            output.Append(',');
                        }
                        firstMember = false;
                        WriteString(output, entry.Key);
                        output.Append(':');
                        Write(output, entry.Value);
                    }
                    output.Append('}');
                    break;
                case List<object> list:
                    output.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                        {
                            output.Append(',');
                        }
                        Write(output, list[i]);
                    }
                    output.Append(']');
                    break;
                case IFormattable number:
                    // Culture invariante : jamais de virgule décimale, quelle que soit la langue de l'appareil.
                    output.Append(number.ToString(value is double || value is float ? "0.############" : null, CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteString(output, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder output, string text)
        {
            output.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            output.Append(c);
                        }
                        break;
                }
            }
            output.Append('"');
        }

        // ───────────────────────────── Lecture ─────────────────────────────

        private object Read(int depth)
        {
            if (depth > 64)
            {
                throw Error("imbrication trop profonde");
            }
            var c = Peek();
            if (c == '{')
            {
                var map = new Dictionary<string, object>();
                _pos++;
                Spaces();
                if (Peek() == '}')
                {
                    _pos++;
                    return map;
                }
                while (true)
                {
                    Spaces();
                    if (Peek() != '"')
                    {
                        throw Error("clé attendue");
                    }
                    var key = ReadString();
                    Spaces();
                    Expect(':');
                    Spaces();
                    map[key] = Read(depth + 1);
                    Spaces();
                    if (Peek() == ',')
                    {
                        _pos++;
                        continue;
                    }
                    Expect('}');
                    return map;
                }
            }
            if (c == '[')
            {
                var list = new List<object>();
                _pos++;
                Spaces();
                if (Peek() == ']')
                {
                    _pos++;
                    return list;
                }
                while (true)
                {
                    Spaces();
                    list.Add(Read(depth + 1));
                    Spaces();
                    if (Peek() == ',')
                    {
                        _pos++;
                        continue;
                    }
                    Expect(']');
                    return list;
                }
            }
            if (c == '"')
            {
                return ReadString();
            }
            if (Literal("true"))
            {
                return true;
            }
            if (Literal("false"))
            {
                return false;
            }
            if (Literal("null"))
            {
                return null;
            }

            var start = _pos;
            while (_pos < _text.Length && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0)
            {
                _pos++;
            }
            if (_pos > start
                && double.TryParse(_text.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }
            _pos = start;
            throw Error("valeur inattendue");
        }

        private string ReadString()
        {
            var output = new StringBuilder();
            _pos++;
            while (true)
            {
                if (_pos >= _text.Length)
                {
                    throw Error("chaîne non terminée");
                }
                var c = _text[_pos++];
                if (c == '"')
                {
                    return output.ToString();
                }
                if (c != '\\')
                {
                    output.Append(c);
                    continue;
                }
                if (_pos >= _text.Length)
                {
                    throw Error("échappement incomplet");
                }
                var escaped = _text[_pos++];
                switch (escaped)
                {
                    case 'n': output.Append('\n'); break;
                    case 'r': output.Append('\r'); break;
                    case 't': output.Append('\t'); break;
                    case 'b': output.Append('\b'); break;
                    case 'f': output.Append('\f'); break;
                    case '"': case '\\': case '/': output.Append(escaped); break;
                    case 'u':
                        if (_pos + 4 > _text.Length
                            || !int.TryParse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            throw Error("échappement unicode invalide");
                        }
                        output.Append((char)code);
                        _pos += 4;
                        break;
                    default:
                        throw Error("échappement inconnu");
                }
            }
        }

        private bool Literal(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
            {
                return false;
            }
            _pos += word.Length;
            return true;
        }

        private char Peek()
        {
            if (_pos >= _text.Length)
            {
                throw Error("fin inattendue");
            }
            return _text[_pos];
        }

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw Error("« " + c + " » attendu");
            }
            _pos++;
        }

        private void Spaces()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        private FormatException Error(string message) => new FormatException("JSON invalide (position " + _pos + ") : " + message);
    }
}
