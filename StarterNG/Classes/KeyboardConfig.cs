using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarterNG.Classes;

/// <summary>
/// A single keyboard command binding parsed from <c>eu07_input-keyboard.ini</c>.
/// A binding is a simulator command name, an optional pair of modifier flags
/// (<c>shift</c> / <c>ctrl</c>, either or both) and the key it is mapped to.
/// <c>none</c> (or an empty key) means the command is intentionally unbound.
/// </summary>
public sealed class KeyBinding
{
    /// <summary>Simulator command token, e.g. <c>mastercontrollerincrease</c>.</summary>
    public string Command = string.Empty;

    /// <summary>True when the binding requires the Shift modifier.</summary>
    public bool Shift;

    /// <summary>True when the binding requires the Ctrl modifier.</summary>
    public bool Ctrl;

    /// <summary>The key token (e.g. <c>q</c>, <c>num_+</c>) or <c>none</c> when unbound.</summary>
    public string Key = "none";

    /// <summary>Human description taken from the line's trailing comment (no <c>//</c>).</summary>
    public string Description = string.Empty;

    /// <summary>Index into the backing line list, so the original line can be rewritten in place.</summary>
    internal int LineIndex = -1;

    /// <summary>True when the command is mapped to an actual key.</summary>
    public bool IsAssigned =>
        !string.IsNullOrEmpty(Key) &&
        !Key.Equals("none", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reader/writer for the simulator's keyboard layout file,
/// <c>eu07_input-keyboard.ini</c>, which lives next to <c>eu07.ini</c>
/// (<c>%APPDATA%\MaSzyna\</c> on Windows, <c>~/.config/MaSzyna/</c> elsewhere).
///
/// <para>The file is a flat list of <c>command [shift] [ctrl] key // comment</c>
/// lines. Modifiers are the literal tokens <c>shift</c> and <c>ctrl</c> appearing
/// in any order between the command and the key; the last value token is the key.
/// Blank lines, pure comments and any lines the launcher does not recognise are
/// preserved verbatim on save - only the binding lines the user actually edits
/// are rewritten.</para>
/// </summary>
public sealed class KeyboardConfig
{
    public static KeyboardConfig Instance { get; } = new();

    private readonly List<string> _lines = new();

    /// <summary>All recognised bindings, in file order.</summary>
    public List<KeyBinding> Bindings { get; private set; } = new();

    /// <summary>True when a binding has been changed since the last load/save.</summary>
    public bool Dirty { get; set; }

    private string _savePath = string.Empty;

    private static readonly Encoding FileEncoding = ResolveEncoding();

    private static Encoding ResolveEncoding()
    {
        // Comments use Polish diacritics in Windows-1250, like eu07.ini; preserve them.
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>Per-user keyboard config path for the current OS (next to eu07.ini).</summary>
    public static string UserConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string? appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, "MaSzyna", "eu07_input-keyboard.ini");
        }
        else if (OperatingSystem.IsMacOS())
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, "Library", "Application Support", "MaSzyna", "eu07_input-keyboard.ini");
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".config", "MaSzyna", "eu07_input-keyboard.ini");
        }
        return Path.Combine(AppContext.BaseDirectory, "eu07_input-keyboard.ini");
    }

    /// <summary>Default file shipped in the simulator's working directory.</summary>
    private static string WorkingDirDefaultPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), "eu07_input-keyboard.ini");

    /// <summary>Fallback template bundled with the launcher (startercfg/).</summary>
    private static string BundledDefaultPath() =>
        Path.Combine(AppContext.BaseDirectory, "startercfg", "eu07_input-keyboard.ini");

    /// <summary>Loads the per-user file, falling back to the working-dir or bundled default.</summary>
    public void Load()
    {
        _savePath = UserConfigPath();
        string source = FirstExisting(_savePath, WorkingDirDefaultPath(), BundledDefaultPath());
        ParseSource(source);
        Dirty = false;
    }

    /// <summary>Reloads the shipped defaults, discarding the user's current edits.</summary>
    public void LoadDefaults()
    {
        if (string.IsNullOrEmpty(_savePath))
            _savePath = UserConfigPath();
        string source = FirstExisting(WorkingDirDefaultPath(), BundledDefaultPath());
        ParseSource(source);
    }

    private void ParseSource(string source)
    {
        try
        {
            string text = File.Exists(source) ? File.ReadAllText(source, FileEncoding) : string.Empty;
            Parse(text);
        }
        catch
        {
            _lines.Clear();
            Bindings = new List<KeyBinding>();
        }
    }

    private static string FirstExisting(params string[] paths)
    {
        foreach (var p in paths)
            if (!string.IsNullOrEmpty(p) && File.Exists(p))
                return p;
        return paths.Length > 0 ? paths[^1] : string.Empty;
    }

    /// <summary>Parses raw file text into <see cref="Bindings"/>, keeping every line verbatim.</summary>
    public void Parse(string text)
    {
        _lines.Clear();
        var bindings = new List<KeyBinding>();

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var rawLine in text.Split('\n'))
        {
            int idx = _lines.Count;
            _lines.Add(rawLine);

            if (TryParseBinding(rawLine, idx, out var binding))
                bindings.Add(binding);
        }

        Bindings = bindings;
    }

    /// <summary>
    /// Parses a single line into a binding. Returns false for blank lines, pure
    /// comments and anything without a command token.
    /// </summary>
    private static bool TryParseBinding(string raw, int lineIndex, out KeyBinding binding)
    {
        binding = new KeyBinding { LineIndex = lineIndex };

        string body = raw;
        string description = string.Empty;

        // Split off a trailing "// comment".
        int commentAt = body.IndexOf("//", StringComparison.Ordinal);
        if (commentAt >= 0)
        {
            description = body.Substring(commentAt + 2).Trim();
            body = body.Substring(0, commentAt);
        }

        body = body.Trim();
        if (body.Length == 0)
            return false; // blank or pure-comment line

        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        binding.Command = tokens[0];
        binding.Description = description;

        // Everything after the command is modifiers + (at most) one key.
        bool shift = false, ctrl = false;
        string key = "none";
        for (int i = 1; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (t.Equals("shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (t.Equals("ctrl", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else key = t; // last non-modifier token wins as the key
        }

        binding.Shift = shift;
        binding.Ctrl = ctrl;
        binding.Key = key;
        return true;
    }

    /// <summary>Rewrites each binding's line, then serialises the file to text.</summary>
    public string ToText()
    {
        foreach (var b in Bindings)
        {
            if (b.LineIndex < 0 || b.LineIndex >= _lines.Count)
                continue;
            _lines[b.LineIndex] = FormatLine(b);
        }
        return string.Join(Environment.NewLine, _lines);
    }

    /// <summary>Formats one binding as <c>command [shift] [ctrl] key // comment</c>.</summary>
    private static string FormatLine(KeyBinding b)
    {
        var sb = new StringBuilder();
        sb.Append(b.Command);
        sb.Append(' ');
        if (b.Shift) sb.Append("shift ");
        if (b.Ctrl) sb.Append("ctrl ");
        sb.Append(b.IsAssigned ? b.Key : "none");
        if (!string.IsNullOrEmpty(b.Description))
            sb.Append(" // ").Append(b.Description);
        return sb.ToString();
    }

    /// <summary>Writes the bindings back to the per-user keyboard file.</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_savePath))
            _savePath = UserConfigPath();

        try
        {
            string? dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_savePath, ToText(), FileEncoding);
            Dirty = false;
        }
        catch
        {
            // Could not write (permissions / path). Leave values in memory.
        }
    }
}
