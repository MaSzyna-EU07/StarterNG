using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StarterNG.Classes;

public sealed class ConfigFile
{
    private readonly List<string> _lines = new();

    private readonly Dictionary<string, int> _index =
        new(StringComparer.OrdinalIgnoreCase);

    // The simulator stops reading at this word (Globals.cpp), whatever its case.
    private const string EndKey = "endconfig";

    // Its first line, or -1 when the file runs to its end.
    private int _end = -1;

    public static ConfigFile Parse(string text)
    {
        var cfg = new ConfigFile();

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in text.Split('\n'))
            cfg.AddLine(line);
        return cfg;
    }

    private void AddLine(string raw)
    {
        _lines.Add(raw);
        IndexLine(_lines.Count - 1);
    }

    private void IndexLine(int line)
    {
        if (!TryParseKey(_lines[line], out string key))
            return;

        if (_end < 0 && key.Equals(EndKey, StringComparison.OrdinalIgnoreCase))
            _end = line;

        // Of a key on both sides of the end, the line the simulator reads counts.
        if (_end < 0 || line == _end || !_index.TryGetValue(key, out int seen) || seen > _end)
            _index[key] = line;
    }

    private void Reindex()
    {
        _index.Clear();
        _end = -1;
        for (int line = 0; line < _lines.Count; line++)
            IndexLine(line);
    }

    public bool Has(string key) => _index.ContainsKey(key);

    /// <summary>Whether the line setting the key is one the simulator never gets to.</summary>
    public bool IsPastEnd(string key) =>
        _end >= 0 && _index.TryGetValue(key, out int line) && line > _end;

    /// <summary>Every key the file sets, in the order of its lines, with its value.</summary>
    public IEnumerable<KeyValuePair<string, string>> Entries()
    {
        foreach (var (key, _) in _index.OrderBy(pair => pair.Value))
            yield return new KeyValuePair<string, string>(key, GetRaw(key) ?? string.Empty);
    }

    public string? GetRaw(string key)
    {
        if (!_index.TryGetValue(key, out int i))
            return null;
        SplitLine(_lines[i], out _, out _, out string value, out _);
        return value;
    }

    public string GetString(string key, string fallback) =>
        GetRaw(key) is { Length: > 0 } v ? v : fallback;

    public bool GetBool(string key, bool fallback)
    {
        var v = FirstToken(GetRaw(key));
        if (v is null) return fallback;
        return v.ToLowerInvariant() switch
        {
            "yes" or "true" or "1" or "tak" => true,
            "no" or "false" or "0" or "nie" => false,
            _ => fallback
        };
    }

    public int GetInt(string key, int fallback)
    {
        var v = FirstToken(GetRaw(key));
        return v != null && int.TryParse(v, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int n) ? n : fallback;
    }

    public double GetDouble(string key, double fallback)
    {
        var v = FirstToken(GetRaw(key));
        return v != null && double.TryParse(v, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double d) ? d : fallback;
    }

    public string[] GetTokens(string key)
    {
        var v = GetRaw(key);
        return string.IsNullOrEmpty(v)
            ? Array.Empty<string>()
            : v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    public void Set(string key, string value)
    {
        value ??= string.Empty;
        if (_index.TryGetValue(key, out int i))
        {
            SplitLine(_lines[i], out string indent, out _, out _, out string comment);
            string line = Line(indent, key, value, comment);

            if (i > _end && _end >= 0)
            {
                _lines.RemoveAt(i);
                InsertBeforeEnd(line);
            }
            else
            {
                _lines[i] = line;
            }
        }
        else if (_end >= 0)
        {
            InsertBeforeEnd(Line("", key, value, ""));
        }
        else
        {
            int idx = _lines.Count;
            _lines.Add(Line("", key, value, ""));
            _index[key] = idx;
        }
    }

    private static string Line(string indent, string key, string value, string comment)
    {
        var sb = new StringBuilder();
        sb.Append(indent).Append(key);
        if (value.Length > 0)
            sb.Append(' ').Append(value);
        if (comment.Length > 0)
            sb.Append('\t').Append(comment);
        return sb.ToString();
    }

    private void InsertBeforeEnd(string line)
    {
        _lines.Insert(_end, line);
        Reindex();
    }

    /// <summary>Comments out every line setting the key: the last one alone would bring the one before it back.</summary>
    public void Remove(string key)
    {
        if (!_index.ContainsKey(key))
            return;

        for (int i = 0; i < _lines.Count; i++)
        {
            SplitLine(_lines[i], out string indent, out string found, out string value, out string comment);
            if (!found.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            _lines[i] = Line(indent + "// ", found, value, comment);
        }

        _index.Remove(key);
    }

    public void SetBool(string key, bool value) => Set(key, value ? "yes" : "no");
    public void SetInt(string key, int value) =>
        Set(key, value.ToString(CultureInfo.InvariantCulture));
    public void SetDouble(string key, double value) =>
        Set(key, value.ToString("0.###", CultureInfo.InvariantCulture));

    public string ToText() => string.Join(Environment.NewLine, _lines);

    private static bool TryParseKey(string raw, out string key)
    {
        SplitLine(raw, out _, out key, out _, out _);
        return key.Length > 0;
    }

    private static void SplitLine(string raw, out string indent, out string key,
        out string value, out string comment)
    {
        key = value = comment = string.Empty;

        int start = 0;
        while (start < raw.Length && char.IsWhiteSpace(raw[start]))
            start++;
        indent = raw.Substring(0, start);

        string body = raw.Substring(start);

        if (body.StartsWith("//"))
        {
            comment = body;
            return;
        }
        if (body.Length == 0)
            return;

        int commentAt = FindCommentStart(body);
        string content = commentAt >= 0 ? body.Substring(0, commentAt) : body;
        if (commentAt >= 0)
            comment = body.Substring(commentAt).TrimEnd();

        content = content.TrimEnd();
        if (content.Length == 0)
            return;

        int sp = IndexOfWhitespace(content);
        if (sp < 0)
        {
            key = content;
        }
        else
        {
            key = content.Substring(0, sp);
            value = content.Substring(sp).Trim();
        }
    }

    private static int FindCommentStart(string body)
    {
        for (int i = 0; i + 1 < body.Length; i++)
        {
            if (body[i] == '/' && body[i + 1] == '/' &&
                (i == 0 || char.IsWhiteSpace(body[i - 1])))
                return i;
        }
        return -1;
    }

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (char.IsWhiteSpace(s[i]))
                return i;
        return -1;
    }

    private static string? FirstToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        int sp = IndexOfWhitespace(value);
        return sp < 0 ? value : value.Substring(0, sp);
    }
}
