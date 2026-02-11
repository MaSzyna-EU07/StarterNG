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
    public Scenery(string path)
    {
        this.Path = path;
        Trainsets = new List<Trainset>();
        if (!File.Exists(path))
            throw new FileNotFoundException(path);
        var encoding = Encoding.GetEncoding(1250); // Windows-1250
        string content = File.ReadAllText(path, encoding);
        
        // property scanning
        var match = Regex.Match(
            content,
            @"^//\$l\s*([^\r\n]*)",
            RegexOptions.Multiline
        );
        this.Group = match.Success ? match.Groups[1].Value : null;
        
        
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

        foreach (string trainsetEntry in trainsetEntries)
        {
            Trainsets.Add(new Trainset(trainsetEntry));
        }
    }
}