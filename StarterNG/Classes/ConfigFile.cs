using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StarterNG.Classes;

/// <summary>
/// A line-oriented reader/writer for the EU07 <c>eu07.ini</c> configuration
/// file.
///
/// The file is not a classic INI: it is a flat list of <c>key value [value…]</c>
/// entries, one per line, with <c>//</c> comments (either trailing a line or
/// commenting the whole line out). The simulator parses it as a stream of
/// whitespace-separated tokens.
///
/// This class keeps every line verbatim and only rewrites the value of a key
/// when <see cref="Set"/> is called. Comments, blank lines, commented-out
/// entries and — crucially — any keys the launcher does not understand are
/// preserved on save, so editing a handful of settings never drops the rest of
/// the user's configuration.
/// </summary>
public sealed class ConfigFile
{
    private readonly List<string> _lines = new();

    // Active (uncommented) setting key -> index into _lines. Last occurrence wins,
    // matching the simulator which lets later entries override earlier ones.
    private readonly Dictionary<string, int> _index =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses raw file text, preserving line breaks and content.</summary>
    public static ConfigFile Parse(string text)
    {
        var cfg = new ConfigFile();
        // Normalise CRLF/CR to LF for splitting; output uses Environment.NewLine.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in text.Split('\n'))
            cfg.AddLine(line);
        return cfg;
    }

    private void AddLine(string raw)
    {
        int idx = _lines.Count;
        _lines.Add(raw);
        if (TryParseKey(raw, out string key))
            _index[key] = idx; // later duplicates override earlier ones
    }

    /// <summary>True if the key is present as an active (uncommented) entry.</summary>
    public bool Has(string key) => _index.ContainsKey(key);

    /// <summary>Raw value (all tokens after the key, comment stripped), or null.</summary>
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

    /// <summary>All whitespace-separated value tokens for a key (never null).</summary>
    public string[] GetTokens(string key)
    {
        var v = GetRaw(key);
        return string.IsNullOrEmpty(v)
            ? Array.Empty<string>()
            : v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Sets the value of <paramref name="key"/>. If the key already exists its
    /// line is rewritten in place, preserving original indentation and any
    /// trailing comment. Otherwise a new line is appended.
    /// </summary>
    public void Set(string key, string value)
    {
        value ??= string.Empty;
        if (_index.TryGetValue(key, out int i))
        {
            SplitLine(_lines[i], out string indent, out _, out _, out string comment);
            var sb = new StringBuilder();
            sb.Append(indent).Append(key);
            if (value.Length > 0)
                sb.Append(' ').Append(value);
            if (comment.Length > 0)
                sb.Append('\t').Append(comment);
            _lines[i] = sb.ToString();
        }
        else
        {
            int idx = _lines.Count;
            _lines.Add(value.Length > 0 ? $"{key} {value}" : key);
            _index[key] = idx;
        }
    }

    public void SetBool(string key, bool value) => Set(key, value ? "yes" : "no");
    public void SetInt(string key, int value) =>
        Set(key, value.ToString(CultureInfo.InvariantCulture));
    public void SetDouble(string key, double value) =>
        Set(key, value.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>Serialises back to text using the platform newline.</summary>
    public string ToText() => string.Join(Environment.NewLine, _lines);

    // --- line parsing helpers -------------------------------------------------

    private static bool TryParseKey(string raw, out string key)
    {
        SplitLine(raw, out _, out key, out _, out _);
        return key.Length > 0;
    }

    /// <summary>
    /// Decomposes a raw line into leading whitespace, key, value (tokens after
    /// the key) and trailing comment (including the <c>//</c>). For blank or
    /// fully commented-out lines, key is empty.
    /// </summary>
    private static void SplitLine(string raw, out string indent, out string key,
        out string value, out string comment)
    {
        indent = key = value = comment = string.Empty;

        // leading whitespace
        int start = 0;
        while (start < raw.Length && char.IsWhiteSpace(raw[start]))
            start++;
        indent = raw.Substring(0, start);

        string body = raw.Substring(start);

        // A line whose first non-space content is "//" is a pure comment.
        if (body.StartsWith("//"))
        {
            comment = body;
            return;
        }
        if (body.Length == 0)
            return;

        // Split off a trailing comment: the first "//" preceded by whitespace
        // (so URLs such as tcp://… inside a value are not mistaken for one).
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
