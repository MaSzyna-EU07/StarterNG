namespace StarterNG.Domain.Settings;

/// <summary>Which builds the starter offers as updates.</summary>
public enum UpdateChannel
{
    /// <summary>The versioned releases of main.</summary>
    Stable,

    /// <summary>The rolling pre-release of the staging branch, for testers.</summary>
    Staging
}
