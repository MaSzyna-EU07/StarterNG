namespace StarterNG.Domain.Sceneries;

public enum SceneryFaultKind
{
    /// <summary><c>/*</c> never closed: the simulator skips the rest of the file.</summary>
    UnclosedComment,

    /// <summary>A trainset with no <c>endtrainset</c>; the simulator ends it at the next one.</summary>
    TrainsetNotClosed,

    /// <summary>
    /// A trainset's header written twice: the simulator ends the first as an empty
    /// trainset, noting it in its log, and reads the trainset under the second.
    /// </summary>
    TrainsetHeaderRepeated,

    /// <summary>The header lacks its name, track, offset or speed.</summary>
    TrainsetIncomplete,

    StrayEndTrainset,

    /// <summary><c>reversed</c> outside a trainset, or after its first vehicle.</summary>
    ReversedMisplaced,

    /// <summary>A vehicle with no <c>enddynamic</c>, which swallows what follows.</summary>
    VehicleNotClosed,

    VehicleIncomplete,

    /// <summary>
    /// A vehicle with no load count: the simulator takes its <c>enddynamic</c> for
    /// one and goes on to the next.
    /// </summary>
    LoadCountMissing,

    /// <summary>
    /// A load count other than 0 with no type after it - the simulator takes the
    /// <c>enddynamic</c> for the type; <see cref="SceneryFault.Token"/> holds the count.
    /// </summary>
    LoadTypeMissing,

    /// <summary>
    /// One word between the load and <c>enddynamic</c>: the simulator takes it for
    /// a destination, the <c>enddynamic</c> for its name, and goes on to the next.
    /// <see cref="SceneryFault.Token"/> holds the word.
    /// </summary>
    WordBeforeEnd,

    /// <summary>A vehicle written as a trainset's is, with no trainset around it.</summary>
    VehicleOutsideTrainset,

    /// <summary>
    /// A trainset's track named with a space, which splits it into words; the
    /// <see cref="SceneryFault.Token"/> holds the name as written.
    /// </summary>
    TrackNameSplit,

    /// <summary>Text where a number belongs; <see cref="SceneryFault.Token"/> holds it.</summary>
    NotANumber,

    /// <summary>An include whose file is not there; <see cref="SceneryFault.Token"/> names it.</summary>
    MissingInclude,

    /// <summary>
    /// An include whose file is there only under a name in other letter case: the
    /// simulator looks for it in lower case, which on Linux misses it. The
    /// <see cref="SceneryFault.Subject"/> holds the name on disk.
    /// </summary>
    IncludeLetterCase,

    /// <summary>An include with no <c>end</c>, which swallows what follows it.</summary>
    IncludeNotClosed,

    /// <summary>A trainset the starter could not read, for a reason none of the above names.</summary>
    TrainsetUnreadable,

    /// <summary>A vehicle outside a trainset the starter could not read.</summary>
    VehicleUnreadable
}

/// <summary>
/// A mistake in a scenery's text: where it is - the scenery's file or one it
/// includes, and the line, as an editor counts them - and what it concerns.
/// </summary>
/// <param name="Subject">The trainset or vehicle it concerns, when it has a name.</param>
public sealed record SceneryFault(SceneryFaultKind Kind, string File, int Line, string? Subject = null,
                                  string? Token = null)
{
    /// <summary>Where in the scenery's text it was found, to tell which trainset it falls in.</summary>
    public int Offset { get; init; }

    /// <summary>
    /// What the simulator does not read because of it - the vehicles, trainsets and
    /// includes it takes as part of this entry - or null when it loses none.
    /// </summary>
    public string? Lost { get; init; }

    /// <summary>
    /// The trainsets the simulator does not make because of it, their headers
    /// swallowed by it or by what it set going - named, or placed when unnamed -
    /// or null when there are none.
    /// </summary>
    public string? Unmade { get; init; }
}
