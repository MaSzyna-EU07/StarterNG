using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace StarterNG.Classes;

public class Scenery
{
    public List<string> Lines;
    public List<Trainset> Trainsets;
    public string Group;
    public string Path;

    // Starter directives (comment-syntax metadata, see wiki "Plik scenerii").
    // They do not affect the simulation, only how a starter presents the scenery.
    public string Name;        // //$n - scenery name
    public string Description; // //$d - scenery description
    public string ImageName;   // //$i - main-window image (scenery thumbnail)

    // The file content with each trainset block replaced by a {{i}} placeholder,
    // used to rebuild the .scn on export.
    private readonly string _template;

    // Lazily resolved, cached path to the //$i image on disk (null if not found).
    private string _imagePath;
    private bool _imagePathResolved;

    public Scenery(string path)
    {
        this.Path = path;
        Trainsets = new List<Trainset>();
        if (!File.Exists(path))
            throw new FileNotFoundException(path);
        var encoding = Encoding.GetEncoding(1250); // Windows-1250
        string content = File.ReadAllText(path, encoding);

        // property scanning - starter directives written as // comments.
        // \b after the letter keeps //$d from matching //$decor, //$i from
        // matching //$it, etc.
        this.Group = MatchDirective(content, "l");
        this.Name = MatchDirective(content, "n");
        this.Description = MatchDirective(content, "d");
        this.ImageName = MatchDirective(content, "i");


        // parsing trainsets
        List<string> trainsetEntries = new  List<string>();
        Regex regex = new Regex(
            @"trainset\b[\s\S]*?\bendtrainset\b",
            RegexOptions.IgnoreCase
        );
        int idx = 0;
        content = regex.Replace(content, match =>
        {
            trainsetEntries.Add(match.Value);
            return $"{{{{{idx++}}}}}";
        });
        _template = content;

        // 1:1 with placeholders - the Trainset ctor never throws (unparsable
        // blocks are kept verbatim), so indices stay aligned for export.
        foreach (string trainsetEntry in trainsetEntries)
            Trainsets.Add(new Trainset(trainsetEntry));
    }

    /// <summary>
    /// Rebuilds the full .scn content with the (possibly modified) trainsets
    /// substituted back into their original positions.
    /// </summary>
    public string BuildExportContent()
    {
        string result = _template;
        for (int i = 0; i < Trainsets.Count; i++)
            result = result.Replace("{{" + i + "}}", Trainsets[i].ToSceneryEntry());
        return result;
    }

    /// <summary>
    /// Reads a single starter directive value (the text after //$&lt;letter&gt;).
    /// Returns null when the directive is absent. The trailing whitespace
    /// requirement separates e.g. //$d from //$decor and //$i from //$it.
    /// </summary>
    private static string MatchDirective(string content, string letter)
    {
        var match = Regex.Match(
            content,
            @"^//\$" + letter + @"\b[ \t]*([^\r\n]*)",
            RegexOptions.Multiline
        );
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Resolved on-disk path of the //$i scenery image, or null if none is
    /// declared or the file cannot be found. The value of //$i may be a bare
    /// file name or a path; common locations are probed and the result cached.
    /// </summary>
    public string ImagePath
    {
        get
        {
            if (_imagePathResolved)
                return _imagePath;
            _imagePathResolved = true;
            _imagePath = ResolveImagePath();
            return _imagePath;
        }
    }

    private string ResolveImagePath()
    {
        if (string.IsNullOrWhiteSpace(ImageName))
            return null;

        // normalise legacy back-slashes so paths work cross-platform
        string name = ImageName.Replace('\\', '/').Trim();
        string scnDir = System.IO.Path.GetDirectoryName(Path) ?? ".";   // e.g. scenery/
        string root = System.IO.Path.GetDirectoryName(scnDir) ?? ".";   // MaSzyna root

        // probe the usual places, first hit wins
        var candidates = new List<string>
        {
            name,                                                       // as given (cwd / absolute)
            System.IO.Path.Combine(root, name),                         // relative to MaSzyna root
            System.IO.Path.Combine(scnDir, name),                       // next to the .scn
            System.IO.Path.Combine(scnDir, "images", name),             // scenery/images/
            System.IO.Path.Combine(root, "scenery", "images", name),    // scenery/images/ from root
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
