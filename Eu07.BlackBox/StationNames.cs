namespace Eu07.BlackBox;

/// <summary>The names of the stations as shown: the scenery tells the stop points of one station apart after a <c>#</c>.</summary>
public static class StationNames
{
    /// <summary>The name up to a <c>#</c>, with the underscores of the scenery as spaces.</summary>
    public static string Display(string name)
    {
        int hash = name.IndexOf('#');
        var shown = hash >= 0 ? name[..hash] : name;
        return shown.Replace('_', ' ').Trim();
    }
}
