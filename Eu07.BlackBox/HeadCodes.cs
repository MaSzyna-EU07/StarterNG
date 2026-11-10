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

    /// <summary>Whether both ends showed what they were to while the train moved; null when nothing tells it.</summary>
    public static bool? Right(CabSample sample)
    {
        if (RequiredHead(sample) is not { } head || RequiredTail(sample) is not { } tail)
            return null;
        return Of(sample.LightsHead) == head && Of(sample.LightsTail) == tail;
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
