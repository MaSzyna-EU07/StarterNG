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
    Passanger,
    Nobody
}

public class Trainset
{
    public string Name;
    public string Track;
    public float Offset;
    public float Velocity;
    public List<Dynamic> Vehicles;
    public Trainset(string trainsetEntry)
    {

        Vehicles = new List<Dynamic>();
        
        List<string> tokens = Regex
            .Matches(trainsetEntry, @"\r\n|\n|[^\s]+")
            .Select(m => m.Value)
            .ToList();

        for (int i = 0; i < tokens.Count; i++)
        {
            // handle comments
            if (tokens[i].StartsWith("//"))
            {
                while (tokens[i] != "\n" && tokens.Count < i) i++;
            }


            // trainset properties
            if (tokens[i] == "trainset")
            {
                i++;
                this.Name = tokens[i++];
                this.Track = tokens[i++];
                this.Offset = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                this.Velocity = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                continue;
            }

            // skip entire assignments block
            if (tokens[i] == "assignments")
            {
                while (tokens[i] != "endassignment") i++;
            }
            
            // load vehicles
            if (tokens[i] == "node")
            {
                i++; // node keyword
                Dynamic nodeDynamic = new Dynamic();
                nodeDynamic.RangeMax = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                nodeDynamic.RangeMin = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                nodeDynamic.Name = tokens[i++];
                i++; // dynamic keyword
                nodeDynamic.DataFolder = tokens[i++];
                nodeDynamic.SkinFile = tokens[i++];
                nodeDynamic.MmdFile = tokens[i++];
                i++; // skip offset
                string driverType = tokens[i++];
                switch (driverType)
                {
                    case "headdriver":
                        nodeDynamic.DriverType = eDriverType.Headdriver;
                        break;
                    case "reardriver":
                        nodeDynamic.DriverType = eDriverType.Reardriver;
                        break;
                    case "passanger":
                        nodeDynamic.DriverType = eDriverType.Passanger;
                        break;
                    default:
                        nodeDynamic.DriverType = eDriverType.Nobody;
                        break;
                }

                nodeDynamic.couplingData = (byte)int.Parse(tokens[i++].Split('.')[0]);

                nodeDynamic.Tail = "";
                while (tokens[i] != "enddynamic")
                {
                    nodeDynamic.Tail += " " + tokens[i];
                    i++;
                }

                Vehicles.Add(nodeDynamic);
            }
        }
    }
    
    public string GetTrainsetEntry()
    {
        string entry = "";

        entry += "trainset ";
        entry += this.Name + " ";
        entry += this.Track + " ";
        entry += this.Offset + " ";
        entry += this.Velocity + " ";
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
                case eDriverType.Passanger:
                    driverType = "passanger";
                    break;
                default:
                    driverType = "nobody";
                    break;
            }

            entry +=
                $"node {vehicle.RangeMax} {vehicle.RangeMin} {vehicle.Name} dynamic {vehicle.DataFolder} {vehicle.SkinFile} {vehicle.MmdFile} {driverType} {vehicle.couplingData} {vehicle.Tail}\n";
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
}