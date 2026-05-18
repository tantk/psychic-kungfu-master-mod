using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JinGuCheats;

// Tiny JSON writer/reader for the limited shapes our HTTP API uses.
// We control both ends so we can be strict about format.
internal static class Json
{
    // ---------- Writer ----------
    public sealed class Obj
    {
        private readonly StringBuilder _sb = new("{");
        private bool _first = true;

        public Obj Add(string key, string? value)        => Field(key, value == null ? "null" : Quote(value));
        public Obj Add(string key, int value)            => Field(key, value.ToString(CultureInfo.InvariantCulture));
        public Obj Add(string key, long value)           => Field(key, value.ToString(CultureInfo.InvariantCulture));
        public Obj Add(string key, float value)          => Field(key, value.ToString("R", CultureInfo.InvariantCulture));
        public Obj Add(string key, double value)         => Field(key, value.ToString("R", CultureInfo.InvariantCulture));
        public Obj Add(string key, bool value)           => Field(key, value ? "true" : "false");
        public Obj AddRaw(string key, string rawJson)    => Field(key, rawJson);

        public Obj AddDict(string key, Dictionary<string, bool> d)
        {
            var inner = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) inner.Append(',');
                inner.Append(Quote(kv.Key)).Append(':').Append(kv.Value ? "true" : "false");
                first = false;
            }
            inner.Append('}');
            return Field(key, inner.ToString());
        }

        public Obj AddDict(string key, Dictionary<string, int> d)
        {
            var inner = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) inner.Append(',');
                inner.Append(Quote(kv.Key)).Append(':').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                first = false;
            }
            inner.Append('}');
            return Field(key, inner.ToString());
        }

        public Obj AddDict(string key, Dictionary<int, float> d)
        {
            var inner = new StringBuilder("{");
            bool first = true;
            foreach (var kv in d)
            {
                if (!first) inner.Append(',');
                inner.Append(Quote(kv.Key.ToString(CultureInfo.InvariantCulture)))
                     .Append(':')
                     .Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
                first = false;
            }
            inner.Append('}');
            return Field(key, inner.ToString());
        }

        private Obj Field(string key, string rawValue)
        {
            if (!_first) _sb.Append(',');
            _sb.Append(Quote(key)).Append(':').Append(rawValue);
            _first = false;
            return this;
        }

        public override string ToString() => _sb + "}";
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"", s.Length + 2);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                    else          sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ---------- Reader (tolerant, only the fields we ask for) ----------
    public sealed class Reader
    {
        private readonly string _src;
        public Reader(string s) { _src = s ?? ""; }

        public string? GetString(string key)
        {
            int i = FindKey(key); if (i < 0) return null;
            // Expect "key":"value"
            i = _src.IndexOf('"', i); if (i < 0) return null;
            int j = i + 1;
            var sb = new StringBuilder();
            while (j < _src.Length && _src[j] != '"')
            {
                if (_src[j] == '\\' && j + 1 < _src.Length) { sb.Append(_src[j + 1]); j += 2; }
                else { sb.Append(_src[j]); j++; }
            }
            return sb.ToString();
        }

        public int? GetInt(string key)
        {
            string? raw = ReadScalar(key);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
        }

        public float? GetFloat(string key)
        {
            string? raw = ReadScalar(key);
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (float?)null;
        }

        public bool? GetBool(string key)
        {
            string? raw = ReadScalar(key);
            if (raw == "true")  return true;
            if (raw == "false") return false;
            return null;
        }

        private string? ReadScalar(string key)
        {
            int i = FindKey(key); if (i < 0) return null;
            // Skip whitespace + colon
            while (i < _src.Length && (_src[i] == ' ' || _src[i] == ':' || _src[i] == '\t')) i++;
            int j = i;
            while (j < _src.Length && _src[j] != ',' && _src[j] != '}' && _src[j] != ' ' && _src[j] != '\n' && _src[j] != '\r' && _src[j] != '\t')
                j++;
            return _src.Substring(i, j - i);
        }

        private int FindKey(string key)
        {
            string needle = "\"" + key + "\"";
            int i = _src.IndexOf(needle);
            return i < 0 ? -1 : i + needle.Length;
        }
    }
}
