using System;

namespace Eu07.BlackBox;

/// <summary>What the driver of the user's train went by: the lights it is to show depend on it.</summary>
public enum Movement : byte
{
    /// <summary>No driver, or standing by.</summary>
    Other = 0,
    /// <summary>A train run: Pc 1 or Pc 2 at the head, Pc 5 at the tail.</summary>
    Train = 1,
    /// <summary>Shunting: Tb 1 at both ends.</summary>
    Shunting = 2,
    /// <summary>A recording made before it was written.</summary>
    Unknown = 0xFF
}

/// <summary>The light signals at an end of a train.</summary>
public enum HeadCode
{
    Off,
    /// <summary>The head of a train: both headlights and the upper one.</summary>
    Pc1,
    /// <summary>The head of a train on the wrong track: the left red marker, the right headlight and the upper one.</summary>
    Pc2,
    /// <summary>The tail of a train: both red markers.</summary>
    Pc5,
    /// <summary>Shunting: one headlight.</summary>
    Tb1,
    /// <summary>Any other pattern.</summary>
    Other
}

/// <summary>The light signals a train shows and those it is to show, as the simulator's driver sets them.</summary>
public static class HeadCodes
{
    private const Lights Signals = Lights.HeadlightLeft | Lights.RedMarkerLeft | Lights.HeadlightUpper | Lights.HeadlightRight |
                                   Lights.RedMarkerRight | Lights.RearEndSignals;

    /// <summary>The signal the lamps given show; the high beams and the auxiliary lamps tell nothing.</summary>
    public static HeadCode Of(Lights lights)
    {
        var lit = lights & Signals;
        if ((lit & Lights.RearEndSignals) != 0)
            lit = (lit & ~Lights.RearEndSignals) | Lights.RedMarkerLeft | Lights.RedMarkerRight;
        return lit switch
        {
            Lights.None => HeadCode.Off,
            Lights.HeadlightLeft | Lights.HeadlightRight | Lights.HeadlightUpper => HeadCode.Pc1,
            Lights.RedMarkerLeft | Lights.HeadlightRight | Lights.HeadlightUpper => HeadCode.Pc2,
            Lights.RedMarkerLeft | Lights.RedMarkerRight => HeadCode.Pc5,
            Lights.HeadlightLeft or Lights.HeadlightRight => HeadCode.Tb1,
            _ => HeadCode.Other
        };
    }

    /// <summary>The signal the head of the train is to show, null when nothing tells it.</summary>
    public static HeadCode? RequiredHead(CabSample sample) => sample.Movement switch
    {
        Movement.Train => sample.LightHintHead < 0 ? HeadCode.Pc1 : Of((Lights)sample.LightHintHead),
        Movement.Shunting => HeadCode.Tb1,
        _ => null
    };

    /// <summary>The signal the tail of the train is to show, null when nothing tells it.</summary>
    public static HeadCode? RequiredTail(CabSample sample) => sample.Movement switch
    {
        Movement.Train => sample.LightHintTail < 0 ? HeadCode.Pc5 : Of((Lights)sample.LightHintTail),
        Movement.Shunting => HeadCode.Tb1,
        _ => null
    };

    /// <summary>Whether the head of the train showed what it was to; null when nothing tells it.</summary>
    public static bool? HeadRight(CabSample sample) => NoLamps(sample.EquippedHead) ? null : sample.Movement switch
    {
        Movement.Train => Meets(sample.LightsHead, sample.EquippedHead, sample.LightHintHead, HeadCode.Pc1),
        Movement.Shunting => Shows(HeadCode.Tb1, sample.LightsHead, sample.EquippedHead),
        _ => null
    };

    /// <summary>Whether the tail of the train showed what it was to; null when nothing tells it.</summary>
    public static bool? TailRight(CabSample sample) => NoLamps(sample.EquippedTail) ? null : sample.Movement switch
    {
        Movement.Train => Meets(sample.LightsTail, sample.EquippedTail, sample.LightHintTail, HeadCode.Pc5),
        Movement.Shunting => Shows(HeadCode.Tb1, sample.LightsTail, sample.EquippedTail),
        _ => null
    };

    /// <summary>
    /// An end with no lamp at all, a draisine's: the simulator still asks it for a signal it can't show, so it
    /// is not judged. A recording made before the lamps were written can't tell.
    /// </summary>
    public static bool NoLamps(Lights? equipped) => equipped == Lights.None;

    /// <summary>Whether both ends showed what they were to; null when nothing tells it.</summary>
    public static bool? Right(CabSample sample) =>
        HeadRight(sample) is { } head && TailRight(sample) is { } tail ? head && tail : null;

    private const Lights HighBeams = Lights.HighBeamLeft | Lights.HighBeamRight;
    private const Lights Auxiliary = Lights.AuxiliaryLeft | Lights.AuxiliaryRight;

    // the pattern asked for at an end, the usual one when none is: the driver sets one asked for as it is
    private static bool Meets(Lights lit, Lights? equipped, short hint, HeadCode usual)
    {
        if (hint < 0)
            return Shows(usual, lit, equipped);
        var pattern = (Lights)hint;
        if (usual == HeadCode.Pc1 && pattern == (Lights.RedMarkerLeft | Lights.HeadlightRight | Lights.HeadlightUpper))
            return Shows(HeadCode.Pc2, lit, equipped);
        // a recording made before the lamps were written: the signal the pattern shows, as before
        return equipped is { } lamps ? (lit & ~HighBeams) == (pattern & lamps) : Of(lit) == Of(pattern);
    }

    /// <summary>
    /// Whether the lamps lit at an end show the signal, as the simulator's driver checks it against the lamps
    /// the vehicle has there: a vehicle without the upper headlight shows Pc 1 with the two others, one with
    /// auxiliary lamps may show them instead, and one with no lamps for it the rear-end signals. A recording
    /// made before the lamps were written has the pattern alone. The high beams tell nothing.
    /// </summary>
    public static bool Shows(HeadCode code, Lights lit, Lights? equipped)
    {
        if (equipped is not { } lamps)
            return Of(lit) == code;
        lit &= ~HighBeams;
        bool auxiliary = (lamps & Auxiliary) != 0;
        switch (code)
        {
            case HeadCode.Pc1:
            {
                var pattern = lamps & (Lights.HeadlightLeft | Lights.HeadlightRight | Lights.HeadlightUpper);
                var fallback = lamps & (Auxiliary | Lights.HeadlightUpper);
                return (pattern != 0 && lit == pattern) || (auxiliary && lit == fallback) ||
                       (pattern == 0 && fallback == 0 && lit == Lights.RearEndSignals);
            }
            case HeadCode.Pc2:
            {
                var pattern = lamps & (Lights.RedMarkerLeft | Lights.HeadlightRight | Lights.HeadlightUpper);
                var fallback = lamps & (Lights.RedMarkerLeft | Lights.AuxiliaryRight | Lights.HeadlightUpper);
                return lit == pattern || (auxiliary && lit == fallback);
            }
            case HeadCode.Pc5:
            {
                var pattern = lamps & (Lights.RedMarkerLeft | Lights.RedMarkerRight);
                var fallback = lamps & Lights.RearEndSignals;
                return (pattern != 0 && lit == pattern) || (fallback != 0 && lit == fallback);
            }
            case HeadCode.Tb1:
                // one headlight, of those there are
                return lit == (lamps & Lights.HeadlightLeft) || lit == (lamps & Lights.HeadlightRight);
            case HeadCode.Off:
                return lit == Lights.None;
            default:
                return false;
        }
    }

    /// <summary>The name of a signal as the rules give it.</summary>
    public static string Name(HeadCode code) => code switch
    {
        HeadCode.Pc1 => "Pc 1",
        HeadCode.Pc2 => "Pc 2",
        HeadCode.Pc5 => "Pc 5",
        HeadCode.Tb1 => "Tb 1",
        HeadCode.Off => "—",
        _ => "?"
    };
}
