using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;

namespace StarterNG.Classes;

public enum eDriverType
{
    Headdriver,
    Reardriver, 
    Passenger,
    Nobody
}

public class Trainset
{
    public string Name;
    public string Track;
    public float Offset;
    public float Velocity;
    public string Description;
    public List<Dynamic> Vehicles;

    /// <summary>Original .scn text of this trainset block (incl. endtrainset).</summary>
    public string RawEntry;

    /// <summary>True if the block parsed cleanly; false blocks are exported verbatim.</summary>
    public bool Parsed;

    public Trainset(string trainsetEntry)
    {
        RawEntry = trainsetEntry;
        Vehicles = new List<Dynamic>();
        Description = "";
        try
        {
            ParseEntry(trainsetEntry);
            Parsed = true;
        }
        catch
        {
            Parsed = false;
        }
    }

    private void ParseEntry(string trainsetEntry)
    {
        List<string> tokens = Regex
            .Matches(trainsetEntry, @"/\*[\s\S]*?\*/|//[^\r\n]*|[^\s\r\n]+")
            .Select(m => m.Value)
            .Where(t => !t.StartsWith("//") && !t.StartsWith("/*"))
            .ToList();

        // handle specific descriptions
        // //$o
        var match = Regex.Match(
            trainsetEntry,
            @"^\s*//\$o\s*(.*)$",
            RegexOptions.Multiline
        );
        if (match.Success)
            this.Description = match.Groups[1].Value.Trim();
        
        // parse trainset
        
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "endtrainset")
                break;
            
            // trainset properties
            if (tokens[i] == "trainset")
            {
                this.Name = tokens[++i];
                this.Track = tokens[++i];
                this.Offset = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                this.Velocity = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                continue;
            }
            
            // skip entire assignments block
            if (tokens[i] == "assignments")
            {
                while (i < tokens.Count && tokens[i] != "endassignment")
                    i++;
                i++; // jump over endassignment
                continue;
            }
            
            // load vehicles
            if (tokens[i] == "node")
            {
                //i++; // node keyword
                Dynamic nodeDynamic = new Dynamic();
                nodeDynamic.RangeMax = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                nodeDynamic.RangeMin = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                nodeDynamic.Name = tokens[++i];
                i++; // dynamic keyword
                nodeDynamic.DataFolder = tokens[++i];
                nodeDynamic.SkinFile = tokens[++i];
                nodeDynamic.MmdFile = tokens[++i];
                nodeDynamic.Offset = float.Parse(tokens[++i], CultureInfo.InvariantCulture);
                string driverType = tokens[++i];
                switch (driverType)
                {
                    case "headdriver":
                        nodeDynamic.DriverType = eDriverType.Headdriver;
                        break;
                    case "reardriver":
                        nodeDynamic.DriverType = eDriverType.Reardriver;
                        break;
                    case "passenger":
                        nodeDynamic.DriverType = eDriverType.Passenger;
                        break;
                    default:
                        nodeDynamic.DriverType = eDriverType.Nobody;
                        break;
                }

                nodeDynamic.couplingData = (byte)int.Parse(tokens[++i].Split('.')[0]);

                nodeDynamic.Tail = "";
                while (i + 1 < tokens.Count && tokens[i + 1] != "enddynamic")
                {
                    nodeDynamic.Tail += " " + tokens[++i];
                }
                i++; // jump over enddynamic

                Vehicles.Add(nodeDynamic);
            }
        }
    }
    
    /// <summary>
    /// The .scn text for this trainset for export. Unparsed blocks are written
    /// back verbatim; parsed blocks are regenerated from the current vehicles.
    /// </summary>
    public string ToSceneryEntry() =>
        Parsed ? GetTrainsetEntry() + "endtrainset\n" : RawEntry;

    public string GetTrainsetEntry()
    {
        string entry = "";

        entry += "trainset ";
        entry += this.Name + " ";
        entry += this.Track + " ";
        entry += this.Offset.ToString(CultureInfo.InvariantCulture) + " ";
        entry += this.Velocity.ToString(CultureInfo.InvariantCulture) + " ";
        entry += "\n";
        foreach (Dynamic vehicle in Vehicles)
        {
            string driverType = "";
            switch (vehicle.DriverType)
            {
                case eDriverType.Headdriver:
                    driverType = "headdriver";
                    break;
                case eDriverType.Reardriver:
                    driverType = "reardriver";
                    break;
                case eDriverType.Passenger:
                    driverType = "passenger";
                    break;
                default:
                    driverType = "nobody";
                    break;
            }

            entry +=
                $"node {vehicle.RangeMax} {vehicle.RangeMin} {vehicle.Name} dynamic " +
                $"{vehicle.DataFolder} {vehicle.SkinFile} {vehicle.MmdFile} " +
                $"{vehicle.Offset.ToString(CultureInfo.InvariantCulture)} " +
                $"{driverType} {vehicle.couplingData}{vehicle.Tail} enddynamic\n";
        }
        
        
        return entry;
    }
}

public class Dynamic
{
    public float RangeMax;
    public float RangeMin;
    public string Name;
    public string DataFolder;
    public string SkinFile;
    public string MmdFile;
    public string PathName;
    public float Offset;
    public eDriverType DriverType;
    public byte couplingData;
    public string Tail;

    // --- depot / consist-builder extras (not part of the .scn syntax) ---

    /// <summary>Explicit miniature name (from the vehicle database). Falls back to SkinFile.</summary>
    public string? MiniName;

    /// <summary>Whether the vehicle is visually reversed in the consist.</summary>
    public bool Flipped;

    /// <summary>Deep copy, used when importing a trainset into the editable consist.</summary>
    public Dynamic Clone() => new Dynamic
    {
        RangeMax = RangeMax,
        RangeMin = RangeMin,
        Name = Name,
        DataFolder = DataFolder,
        SkinFile = SkinFile,
        MmdFile = MmdFile,
        PathName = PathName,
        Offset = Offset,
        DriverType = DriverType,
        couplingData = couplingData,
        Tail = Tail,
        MiniName = MiniName,
        Flipped = Flipped
    };
}