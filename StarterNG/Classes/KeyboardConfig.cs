using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StarterNG.Infrastructure.Adapters;

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

    /// <summary>The combination in a form to compare, however the key is spelled.</summary>
    public string ComboKey => $"{(Ctrl ? 1 : 0)}|{(Shift ? 1 : 0)}|{Key.ToLowerInvariant()}";

    public void Clear()
    {
        Shift = Ctrl = false;
        Key = "none";
    }

    public bool IsOn(string token, bool shift, bool ctrl) =>
        IsAssigned &&
        string.Equals(Key, token, StringComparison.OrdinalIgnoreCase) &&
        Shift == shift && Ctrl == ctrl;
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
            return LegacyText.CodePage1250;
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
        LoadPolishDescriptions();
        Dirty = false;
    }

    /// <summary>
    /// The game ships its key file with the descriptions stripped of Polish letters
    /// ("zwiekszenie nastawnika glownego"); the copy bundled with the starter has
    /// them. Read once, shown in place of the bare ones, never written back.
    /// </summary>
    private readonly Dictionary<string, string> _polishDescriptions = new(StringComparer.OrdinalIgnoreCase);

    private void LoadPolishDescriptions()
    {
        _polishDescriptions.Clear();
        try
        {
            string path = BundledDefaultPath();
            if (!File.Exists(path))
                return;

            var bundled = new KeyboardConfig();
            bundled.Parse(File.ReadAllText(path, FileEncoding));
            foreach (var b in bundled.Bindings)
                if (!string.IsNullOrEmpty(b.Description))
                    _polishDescriptions.TryAdd(b.Command, b.Description);
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log("reading the bundled key descriptions", ex);
        }
    }

    /// <summary>
    /// The description to show: the file's own when someone wrote their own, else
    /// the <paramref name="translated"/> one for the starter's language, else the
    /// bundled one with Polish letters.
    /// </summary>
    public string DescriptionOf(KeyBinding binding, Func<string, string>? translated = null)
    {
        bool stock = string.IsNullOrEmpty(binding.Description);
        if (_polishDescriptions.TryGetValue(binding.Command, out string? polish))
            stock |= string.Equals(StripPolish(polish), binding.Description.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!stock)
            return binding.Description;

        if (translated?.Invoke(binding.Command) is { Length: > 0 } text)
            return text;

        return polish ?? binding.Description;
    }

    public KeyBinding? BindingAt(string token, bool shift, bool ctrl) =>
        Bindings.FirstOrDefault(b => b.IsOn(token, shift, ctrl));

    /// <summary>
    /// Puts <paramref name="binding"/> on a key combination, or nothing for null, and
    /// frees it from the rest: the simulator maps a combination to one command - in
    /// driving and outside the cab alike, the editor has its own fixed keys - so of
    /// two, one would silently never fire.
    /// </summary>
    public void Put(KeyBinding? binding, string token, bool shift, bool ctrl)
    {
        foreach (var b in Bindings)
            if (!ReferenceEquals(b, binding) && b.IsOn(token, shift, ctrl))
                b.Clear();

        if (binding is null)
            return;

        binding.Key = token;
        binding.Shift = shift;
        binding.Ctrl = ctrl;
    }

    /// <summary>The <see cref="KeyBinding.ComboKey"/> of each combination more than one binding is on.</summary>
    public HashSet<string> ConflictingCombos()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Bindings)
        {
            if (!b.IsAssigned)
                continue;
            string k = b.ComboKey;
            if (!seen.Add(k))
                dup.Add(k);
        }
        return dup;
    }

    public int BindingsOnNumpad() =>
        Bindings.Count(b => b.IsAssigned && b.Key.StartsWith(KeyMap.NumpadPrefix, StringComparison.OrdinalIgnoreCase));

    public static string StripPolish(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Trim())
            sb.Append(c switch
            {
                'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z',
                'Ą' => 'A', 'Ć' => 'C', 'Ę' => 'E', 'Ł' => 'L', 'Ń' => 'N', 'Ó' => 'O', 'Ś' => 'S', 'Ź' => 'Z', 'Ż' => 'Z',
                _ => c
            });
        return sb.ToString();
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
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log($"saving {_savePath}", ex);
        }
    }
}
