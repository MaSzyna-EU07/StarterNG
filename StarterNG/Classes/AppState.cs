namespace StarterNG.Classes;

/// <summary>
/// Shared selection state across views: the scenery and trainset currently in
/// focus. The depot edits this trainset in place, so changes made in the depot
/// are reflected on the scenery (both views share the same Scenery/Trainset
/// objects from <see cref="GameData"/>).
/// </summary>
public sealed class AppState
{
    public static AppState Instance { get; } = new();

    public Scenery? CurrentScenery { get; set; }
    public Trainset? CurrentTrainset { get; set; }
}
