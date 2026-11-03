// =====================================================================
// SimJson.cs
// Minimal, dependency-free JSON reader for the TheSpike native snapshot
// contract (UTF-8 JSON produced by SpikeCrossCore, see THE_SPIKE_NATIVE_API.md).
//
// - Objects keep their key order (JsonObject) so dumps and flattening are
//   deterministic; duplicate keys are rejected (never silently overwritten).
// - Numbers keep their original token (JsonNumber.Raw) in addition to the
//   parsed double, so field-by-field comparisons can be made against the
//   exact native text.
// - No UnityEngine dependency: the same file is compiled by the out-of-Unity
//   decoder harness (MyGame/Source/Simulation/Tools/UnityDecoderHarness).
// =====================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Native.Snapshot
{
    public sealed class JsonFormatException : Exception
    {
        public JsonFormatException(string message) : base(message) { }
    }

    /// <summary>A JSON number with its exact source token.</summary>
    public sealed class JsonNumber
    {
        public readonly string Raw;
        public readonly double Value;

        public JsonNumber(string raw, double value)
        {
            Raw = raw;
            Value = value;
        }

        /// <summary>True when the token has no fraction/exponent (an integer literal).</summary>
        public bool IsIntegerToken => Raw.IndexOf('.') < 0 && Raw.IndexOf('e') < 0 && Raw.IndexOf('E') < 0;

        public override string ToString() => Raw;
    }

    /// <summary>Insertion-ordered JSON object.</summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<string> keys = new List<string>();
        private readonly Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);

        public int Count => keys.Count;
        public IReadOnlyList<string> Keys => keys;

        public bool ContainsKey(string key) => map.ContainsKey(key);

        public bool TryGetValue(string key, out object value) => map.TryGetValue(key, out value);

        public object this[string key] => map.TryGetValue(key, out var v) ? v : null;

        internal void Add(string key, object value)
        {
            if (map.ContainsKey(key))
                throw new JsonFormatException($"duplicate key '{key}'");
            keys.Add(key);
            map.Add(key, value);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var k in keys)
                yield return new KeyValuePair<string, object>(k, map[k]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class SimJson
    {
        private const int MaxDepth = 64;

        // ------------------------------------------------------------------
        // Parsing
        // ------------------------------------------------------------------
        /// <summary>
        /// Parses JSON text. Result types: null, bool, JsonNumber, string,
        /// List&lt;object&gt;, JsonObject. Throws JsonFormatException on any error.
        /// </summary>
        public static object Parse(string text)
        {
            if (text == null) throw new JsonFormatException("null input");
            var p = new Parser(text);
            p.SkipWs();
            object v = p.ReadValue(0);
            p.SkipWs();
            if (p.Pos != text.Length)
                throw new JsonFormatException($"trailing characters at offset {p.Pos}");
            return v;
        }

        private sealed class Parser
        {
            private readonly string s;
            public int Pos;

            public Parser(string text) { s = text; }

            public void SkipWs()
            {
                while (Pos < s.Length)
                {
                    char c = s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') Pos++;
                    else break;
                }
            }

            private JsonFormatException Err(string what) => new JsonFormatException($"{what} at offset {Pos}");

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Err("nesting too deep");
                if (Pos >= s.Length) throw Err("unexpected end of input");
                char c = s[Pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Err($"unexpected character '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, Pos, word, 0, word.Length) != 0) throw Err($"expected '{word}'");
                Pos += word.Length;
            }

            private JsonObject ReadObject(int depth)
            {
                var obj = new JsonObject();
                Pos++; // {
                SkipWs();
                if (Pos < s.Length && s[Pos] == '}') { Pos++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Pos >= s.Length || s[Pos] != '"') throw Err("expected object key");
                    string key = ReadString();
                    SkipWs();
                    if (Pos >= s.Length || s[Pos] != ':') throw Err("expected ':'");
                    Pos++;
                    SkipWs();
                    obj.Add(key, ReadValue(depth + 1));
                    SkipWs();
                    if (Pos >= s.Length) throw Err("unterminated object");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == '}') { Pos++; return obj; }
                    throw Err("expected ',' or '}'");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                Pos++; // [
                SkipWs();
                if (Pos < s.Length && s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue(depth + 1));
                    SkipWs();
                    if (Pos >= s.Length) throw Err("unterminated array");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == ']') { Pos++; return list; }
                    throw Err("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                Pos++; // opening quote
                StringBuilder sb = null;
                int start = Pos;
                while (true)
                {
                    if (Pos >= s.Length) throw Err("unterminated string");
                    char c = s[Pos];
                    if (c == '"')
                    {
                        string result = sb == null ? s.Substring(start, Pos - start) : sb.Append(s, start, Pos - start).ToString();
                        Pos++;
                        return result;
                    }
                    if (c < 0x20) throw Err("control character in string");
                    if (c == '\\')
                    {
                        if (sb == null) sb = new StringBuilder();
                        sb.Append(s, start, Pos - start);
                        Pos++;
                        if (Pos >= s.Length) throw Err("bad escape");
                        char e = s[Pos++];
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
                                if (Pos + 4 > s.Length) throw Err("bad \\u escape");
                                if (!int.TryParse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                    throw Err("bad \\u escape");
                                sb.Append((char)code);
                                Pos += 4;
                                break;
                            default: throw Err($"bad escape '\\{e}'");
                        }
                        start = Pos;
                        continue;
                    }
                    Pos++;
                }
            }

            private JsonNumber ReadNumber()
            {
                int start = Pos;
                if (s[Pos] == '-') Pos++;
                if (Pos >= s.Length) throw Err("bad number");
                if (s[Pos] == '0') Pos++;
                else if (s[Pos] >= '1' && s[Pos] <= '9') { while (Pos < s.Length && char.IsDigit(s[Pos])) Pos++; }
                else throw Err("bad number");
                if (Pos < s.Length && s[Pos] == '.')
                {
                    Pos++;
                    if (Pos >= s.Length || !char.IsDigit(s[Pos])) throw Err("bad fraction");
                    while (Pos < s.Length && char.IsDigit(s[Pos])) Pos++;
                }
                if (Pos < s.Length && (s[Pos] == 'e' || s[Pos] == 'E'))
                {
                    Pos++;
                    if (Pos < s.Length && (s[Pos] == '+' || s[Pos] == '-')) Pos++;
                    if (Pos >= s.Length || !char.IsDigit(s[Pos])) throw Err("bad exponent");
                    while (Pos < s.Length && char.IsDigit(s[Pos])) Pos++;
                }
                string raw = s.Substring(start, Pos - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Err($"unparsable number '{raw}'");
                return new JsonNumber(raw, d);
            }
        }

        // ------------------------------------------------------------------
        // Typed helpers (contract: non-finite floats are encoded as strings)
        // ------------------------------------------------------------------
        public const string NaNToken = "NaN";
        public const string PosInfToken = "+Inf";
        public const string NegInfToken = "-Inf";

        public static bool IsNonFiniteToken(object v) =>
            v is string s && (s == NaNToken || s == PosInfToken || s == NegInfToken);

        /// <summary>Float field value (number or NaN/+Inf/-Inf token).</summary>
        public static bool TryGetFloat(object v, out float f)
        {
            switch (v)
            {
                case JsonNumber n:
                    f = (float)n.Value;
                    return true;
                case string s when s == NaNToken:
                    f = float.NaN;
                    return true;
                case string s when s == PosInfToken:
                    f = float.PositiveInfinity;
                    return true;
                case string s when s == NegInfToken:
                    f = float.NegativeInfinity;
                    return true;
                default:
                    f = 0f;
                    return false;
            }
        }

        public static bool TryGetLong(object v, out long l)
        {
            if (v is JsonNumber n && n.IsIntegerToken && long.TryParse(n.Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l))
                return true;
            l = 0;
            return false;
        }

        public static bool TryGetULong(object v, out ulong u)
        {
            if (v is JsonNumber n && n.IsIntegerToken && ulong.TryParse(n.Raw, NumberStyles.None, CultureInfo.InvariantCulture, out u))
                return true;
            u = 0;
            return false;
        }

        public static float GetFloat(JsonObject o, string key, float fallback = 0f) =>
            o != null && o.TryGetValue(key, out var v) && TryGetFloat(v, out var f) ? f : fallback;

        public static long GetLong(JsonObject o, string key, long fallback = 0) =>
            o != null && o.TryGetValue(key, out var v) && TryGetLong(v, out var l) ? l : fallback;

        public static bool GetBool(JsonObject o, string key, bool fallback = false) =>
            o != null && o.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        public static string GetString(JsonObject o, string key, string fallback = null) =>
            o != null && o.TryGetValue(key, out var v) && v is string s ? s : fallback;

        public static JsonObject GetObject(JsonObject o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as JsonObject : null;

        public static List<object> GetArray(JsonObject o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as List<object> : null;

        // ------------------------------------------------------------------
        // Flattening (deterministic leaf list: "a.b[2].c" -> value)
        // Empty objects/arrays are emitted as leaves ("{}" / "[]") so that an
        // absent component set (e.g. the NET entity) stays observable.
        // ------------------------------------------------------------------
        public static List<KeyValuePair<string, object>> Flatten(object root)
        {
            var output = new List<KeyValuePair<string, object>>(512);
            FlattenInto(root, string.Empty, output);
            return output;
        }

        private static void FlattenInto(object node, string path, List<KeyValuePair<string, object>> output)
        {
            switch (node)
            {
                case JsonObject o:
                    if (o.Count == 0) { output.Add(new KeyValuePair<string, object>(path, EmptyObjectMarker)); return; }
                    foreach (var kv in o)
                        FlattenInto(kv.Value, path.Length == 0 ? kv.Key : path + "." + kv.Key, output);
                    return;
                case List<object> a:
                    if (a.Count == 0) { output.Add(new KeyValuePair<string, object>(path, EmptyArrayMarker)); return; }
                    for (int i = 0; i < a.Count; i++)
                        FlattenInto(a[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", output);
                    return;
                default:
                    output.Add(new KeyValuePair<string, object>(path, node));
                    return;
            }
        }

        public static readonly object EmptyObjectMarker = new EmptyMarker("{}");
        public static readonly object EmptyArrayMarker = new EmptyMarker("[]");

        private sealed class EmptyMarker
        {
            private readonly string text;
            public EmptyMarker(string t) { text = t; }
            public override string ToString() => text;
        }

        /// <summary>Text form of a leaf for dumps/comparisons.</summary>
        public static string LeafToString(object v)
        {
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case JsonNumber n: return n.Raw;
                case string s: return "\"" + s + "\"";
                default: return v.ToString();
            }
        }
    }
}
