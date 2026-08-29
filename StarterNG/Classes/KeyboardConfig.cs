using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarterNG.Classes;

public sealed class KeyBinding
{
    public string Command = string.Empty;

    public bool Shift;

    public bool Ctrl;

    public string Key = "none";

    public string Description = string.Empty;

    internal int LineIndex = -1;

    public bool IsAssigned =>
        !string.IsNullOrEmpty(Key) &&
        !Key.Equals("none", StringComparison.OrdinalIgnoreCase);
}

public sealed class KeyboardConfig
{
    public static KeyboardConfig Instance { get; } = new();

    private readonly List<string> _lines = new();

    public List<KeyBinding> Bindings { get; private set; } = new();

    public bool Dirty { get; set; }

    private string _savePath = string.Empty;

    private static readonly Encoding FileEncoding = ResolveEncoding();

    private static Encoding ResolveEncoding()
    {

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

    private static string WorkingDirDefaultPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), "eu07_input-keyboard.ini");

    private static string BundledDefaultPath() =>
        Path.Combine(AppContext.BaseDirectory, "startercfg", "eu07_input-keyboard.ini");

    public void Load()
    {
        _savePath = UserConfigPath();
        string source = FirstExisting(_savePath, WorkingDirDefaultPath(), BundledDefaultPath());
        ParseSource(source);
        Dirty = false;
    }

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

    private static bool TryParseBinding(string raw, int lineIndex, out KeyBinding binding)
    {
        binding = new KeyBinding { LineIndex = lineIndex };

        string body = raw;
        string description = string.Empty;

        int commentAt = body.IndexOf("//", StringComparison.Ordinal);
        if (commentAt >= 0)
        {
            description = body.Substring(commentAt + 2).Trim();
            body = body.Substring(0, commentAt);
        }

        body = body.Trim();
        if (body.Length == 0)
            return false;

        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        binding.Command = tokens[0];
        binding.Description = description;

        bool shift = false, ctrl = false;
        string key = "none";
        for (int i = 1; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (t.Equals("shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (t.Equals("ctrl", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else key = t;
        }

        binding.Shift = shift;
        binding.Ctrl = ctrl;
        binding.Key = key;
        return true;
    }

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

        }
    }
}
