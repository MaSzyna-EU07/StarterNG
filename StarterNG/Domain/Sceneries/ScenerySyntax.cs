using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StarterNG.Domain.Sceneries;

/// <summary>
/// What the simulator makes of an include: whether it opens the file - and when
/// it does not, the file that is there under a name differing only in letter case.
/// </summary>
public readonly record struct IncludeLookup(bool Opens, string? OnDisk = null);

/// <summary>
/// Goes through a scenery's text for the parts the starter reads - trainsets,
/// vehicles, includes - finding where each trainset and loose vehicle is, and
/// what in them is written wrong. Whatever else the text holds is passed over.
/// </summary>
/// <remarks>
/// The text is read twice. Once word by word as the simulator reads it, which
/// takes every field it expects whatever stands there, and after a vehicle
/// written wrong goes on to the next <c>enddynamic</c> - that finds the faults,
/// and what each makes the simulator lose. Once as the author meant it, each
/// entry ending where it was meant to - that finds the trainsets and vehicles,
/// so one the simulator would lose is still listed, and written right when
/// the starter writes it.
/// </remarks>
public static class ScenerySyntax
{
    public sealed record Result(
        IReadOnlyList<(int Start, int End)> Trainsets,
        IReadOnlyList<(int Start, int End)> LooseVehicles,
        IReadOnlyList<SceneryFault> Faults,
        SceneryLines Lines);

    /// <param name="text">The scenery's text, its includes put in place.</param>
    /// <param name="file">The scenery's file name, for faults in its own text.</param>
    /// <param name="findInclude">
    /// What the simulator makes of an include, named as the include names it; null
    /// leaves includes unchecked.
    /// </param>
    public static Result Scan(string text, string file, Func<string, IncludeLookup>? findInclude = null)
    {
        string code = SceneryCode.Mask(text, out int unclosed);
        var tokens = SceneryCode.Tokens(code);
        var lines = new SceneryLines(text, file);

        var simulator = new Reading(tokens, lines, findInclude, asSimulator: true);
        simulator.Run();
        var author = new Reading(tokens, lines, null, asSimulator: false);
        author.Run();

        var faults = Unmade(simulator, author, lines);
        if (unclosed >= 0)
            faults.Insert(0, lines.Fault(SceneryFaultKind.UnclosedComment, unclosed));

        return new Result(author.Trainsets.Select(t => (t.Start, t.End)).ToList(), author.Loose, faults, lines);
    }

    /// <summary>
    /// The simulator's faults, with those in a trainset whose header it swallowed
    /// left out: they come of reading its vehicles with no trainset, and the
    /// trainset is told of instead, with the fault it came of - the first of a
    /// run of them, where the run started.
    /// </summary>
    private static List<SceneryFault> Unmade(Reading simulator, Reading author, SceneryLines lines)
    {
        var unmade = new List<(int Start, int End, int Cause)>();
        var lost = new Dictionary<int, List<string>>();
        foreach (var (start, end, name) in author.Trainsets)
        {
            int cause = -1;
            foreach (var (from, to, at) in simulator.Swallowed)
                if (start >= from && start <= to)
                {
                    cause = at;
                    break;
                }
            if (cause < 0)
                continue;
            foreach (var earlier in unmade)
                if (cause >= earlier.Start && cause < earlier.End)
                {
                    cause = earlier.Cause;
                    break;
                }
            unmade.Add((start, end, cause));

            var (file, line) = lines.At(start);
            bool named = name.Length > 0 && !name.Equals("none", StringComparison.OrdinalIgnoreCase);
            if (!lost.TryGetValue(cause, out var names))
                lost[cause] = names = new List<string>();
            names.Add(named ? name : $"{file}:{line}");
        }

        if (unmade.Count == 0)
            return simulator.Faults;

        var faults = new List<SceneryFault>();
        foreach (var fault in simulator.Faults)
        {
            if (unmade.Any(u => fault.Offset >= u.Start && fault.Offset < u.End))
                continue;
            faults.Add(fault.Lost is not null && lost.Remove(fault.Offset, out var names)
                ? fault with { Unmade = Listed(names) }
                : fault);
        }
        return faults;
    }

    private static string Listed(List<string> entries)
    {
        const int Named = 6;
        string named = string.Join(", ", entries.Take(Named));
        return entries.Count > Named ? $"{named} … +{entries.Count - Named}" : named;
    }

    private sealed class Reading(
        List<SceneryCode.Token> tokens, SceneryLines lines, Func<string, IncludeLookup>? findInclude, bool asSimulator)
    {
        public readonly List<(int Start, int End, string Name)> Trainsets = new();
        public readonly List<(int, int)> Loose = new();
        public readonly List<SceneryFault> Faults = new();

        /// <summary>The text each fault makes the simulator pass over - from, to - and where the fault is.</summary>
        public readonly List<(int From, int To, int At)> Swallowed = new();

        // The trainset being read: where it starts, its name, how many vehicles so
        // far, and the word its header starts at.
        private (int Index, string Name, int Vehicles, int At)? _open;

        public void Run()
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (token.Is("trainset"))
                    i = Header(i);
                else if (token.Is("endtrainset"))
                {
                    // One with no vehicles is no mistake: a place for the player's own
                    // consist, filled in the depot.
                    if (_open is { } ended)
                        Trainsets.Add((ended.Index, token.End, ended.Name));
                    else
                        Fault(SceneryFaultKind.StrayEndTrainset, token.Index);
                    _open = null;
                }
                else if (token.Is("reversed"))
                {
                    // Turns the trainset round only while none of its vehicles is placed yet.
                    if (_open is not { Vehicles: 0 })
                        Fault(SceneryFaultKind.ReversedMisplaced, token.Index);
                }
                else if (token.Is("assignment") || token.Is("assignments"))
                {
                    while (i + 1 < tokens.Count && !tokens[i].Is("endassignment"))
                        i++;
                }
                else if (token.Is("node"))
                {
                    // Not a vehicle: only its kind is stepped over, what follows is its own.
                    if (i + 4 >= tokens.Count || !tokens[i + 4].Is("dynamic"))
                        i += 4;
                    else
                        i = Vehicle(i);
                }
                else if (token.Is("include"))
                    i = Include(i);
            }

            if (_open is { } last)
                NotClosed(last);
        }

        // Unless its header is told incomplete already: the endtrainset taken for a field.
        private void NotClosed((int Index, string Name, int Vehicles, int At) trainset)
        {
            if (!Faults.Any(f => f.Offset == trainset.Index))
                Fault(SceneryFaultKind.TrainsetNotClosed, trainset.Index, trainset.Name);
        }

        // Each reader below takes the entry starting at the given word and gives
        // back the last word it took.

        private int Header(int at)
        {
            var token = tokens[at];
            if (_open is { } unended)
            {
                // The same header twice over, the first with nothing under it: the
                // simulator ends that one as empty - noting it in its log - and reads on.
                if (unended.Vehicles == 0 && Same(unended.At, at, 4))
                    Fault(SceneryFaultKind.TrainsetHeaderRepeated, unended.Index, unended.Name);
                else
                    NotClosed(unended);
            }
            _open = null;

            if (!Fields(at, 4))
            {
                // Still opened, so its endtrainset ends it rather than standing alone.
                int given = 0;
                while (given < 4 && at + given + 1 < tokens.Count && !Keyword(tokens[at + given + 1]))
                    given++;
                string? partial = given > 0 ? tokens[at + 1].Text : null;
                _open = (token.Index, partial ?? "", 0, at);
                if (!asSimulator)
                    return at + given;

                // The simulator takes four words, whatever they are.
                int taken = Math.Min(at + 4, tokens.Count - 1);
                Fault(SceneryFaultKind.TrainsetIncomplete, token.Index, partial, swallowed: (at + given, taken));
                return taken;
            }

            string name = tokens[at + 1].Text;
            if (SplitTrack(tokens, at) is { } words)
            {
                // Its offset is the rest of the track's name - a lost letter, often.
                string track = string.Join(' ', tokens.Skip(at + 2).Take(words).Select(t => t.Text));
                Fault(SceneryFaultKind.TrackNameSplit, tokens[at + 2].Index, name, track);
            }
            else
            {
                Number(tokens[at + 3], name);
                Number(tokens[at + 4], name);
            }
            _open = (token.Index, name, 0, at);
            return at + 4;
        }

        private int Vehicle(int at)
        {
            bool inTrainset = _open is not null;
            // node range range name dynamic, then folder skin mmd [track] offset driver [coupling | speed]
            int fields = inTrainset ? 10 : 11;
            string name = tokens[at + 3].Text;
            bool complete = Fields(at, fields);

            // Where the author meant it to end: its enddynamic, or with that missing
            // the word before whatever comes next.
            int meant = at + 5;
            for (; meant < tokens.Count && !Keyword(tokens[meant]); meant++)
                if (tokens[meant].Is("enddynamic"))
                    break;
            bool closed = meant < tokens.Count && tokens[meant].Is("enddynamic");
            if (!closed)
                meant--;

            if (!asSimulator)
                return ListVehicle(at, complete, closed, meant);

            if (at + fields + 1 >= tokens.Count)
            {
                Fault(complete ? SceneryFaultKind.VehicleNotClosed : SceneryFaultKind.VehicleIncomplete,
                      tokens[at].Index, name);
                return tokens.Count - 1;
            }

            var (fault, trackless) = CheckFields(at, complete, inTrainset, name);
            if (fault is null && !closed)
                fault = SceneryFaultKind.VehicleNotClosed;

            int end = LoadEnd(at + fields + 1, trackless, meant, ref fault, out string? faulty);

            if (fault is { } kind)
                Fault(kind, tokens[at].Index, name, faulty, end > meant ? (meant, end) : null);

            if (_open is { } open)
                _open = open with { Vehicles = open.Vehicles + 1 };
            return end;
        }

        // As the author meant it: there to be listed, whatever is wrong with it.
        private int ListVehicle(int at, bool complete, bool closed, int meant)
        {
            // Without even the fields every vehicle has it is no vehicle to list.
            if (!complete)
                return at;
            // One with its end missing still counts in its trainset: the starter
            // reads it all the same, and writes it whole for a start. One written
            // for a trainset with none around it is no loose vehicle either.
            if (_open is { } current)
                _open = current with { Vehicles = current.Vehicles + 1 };
            else if (closed && !TrainsetStyle(at))
                Loose.Add((tokens[at].Index, tokens[meant].End));
            return meant;
        }

        // What is written wrong in a vehicle's fields, the numbers told of right away.
        private (SceneryFaultKind? Fault, bool Trackless) CheckFields(int at, bool complete, bool inTrainset, string name)
        {
            SceneryFaultKind? fault = null;
            // A track named by a number is none the simulator has: it drops the vehicle
            // right after its load, going to the next enddynamic.
            bool trackless = false;
            if (!complete)
                fault = SceneryFaultKind.VehicleIncomplete;
            else
            {
                Number(tokens[at + 1], name);
                Number(tokens[at + 2], name);
                if (inTrainset)
                {
                    Number(tokens[at + 8], name);
                    string coupling = tokens[at + 10].Text;
                    int dot = coupling.IndexOf('.');
                    string flags = dot < 0 ? coupling : coupling[..dot];
                    if (flags.Length > 0 && !int.TryParse(flags, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        Fault(SceneryFaultKind.NotANumber, tokens[at + 10].Index, name, coupling);
                }
                else if (TrainsetStyle(at))
                {
                    // Its header missing or commented out.
                    fault = SceneryFaultKind.VehicleOutsideTrainset;
                    trackless = true;
                }
                else
                {
                    Number(tokens[at + 9], name);
                    Number(tokens[at + 11], name);
                    trackless = IsNumber(tokens[at + 8]);
                }
            }
            return (fault, trackless);
        }

        // The last word of a vehicle, from its load count on, with what is written wrong
        // there unless something before it already was.
        private int LoadEnd(int next, bool trackless, int meant, ref SceneryFaultKind? fault, out string? faulty)
        {
            faulty = null;

            // Then the load: its count, and only after a count other than 0 its type.
            var count = tokens[next++];
            if (Counted(count.Text))
            {
                if (next < tokens.Count && tokens[next].Is("enddynamic") && fault is null)
                    (fault, faulty) = (SceneryFaultKind.LoadTypeMissing, count.Text);
                next++;
            }
            else if (count.Is("enddynamic"))
                fault ??= SceneryFaultKind.LoadCountMissing;

            // Then enddynamic - or a destination, one word more, and on to the next enddynamic.
            int end;
            if (trackless)
                end = EndDynamic(next);
            else if (next < tokens.Count && tokens[next].Is("enddynamic"))
                end = next;
            else
            {
                end = EndDynamic(next + 2);
                if (fault is null && end > meant)
                    (fault, faulty) = (SceneryFaultKind.WordBeforeEnd, tokens[next].Text);
            }
            return end;
        }

        private int Include(int at)
        {
            var token = tokens[at];
            string name = at + 1 < tokens.Count ? tokens[at + 1].Text : "";

            if (!asSimulator)
            {
                // Its parameters run to "end". Another include, a trainset or a node on
                // the way means the end is missing - read on as what they are.
                int stop = at + 2;
                while (stop < tokens.Count && !tokens[stop].Is("end") && !Keyword(tokens[stop]) &&
                       !tokens[stop].Is("include") && !tokens[stop].Is("firstinit"))
                    stop++;
                return stop < tokens.Count && tokens[stop].Is("end") ? stop : stop - 1;
            }

            if (findInclude is not null && Plain(name) && findInclude(name) is { Opens: false } lookup)
            {
                if (lookup.OnDisk is { } onDisk)
                    Fault(SceneryFaultKind.IncludeLetterCase, token.Index, onDisk, name);
                else
                    Fault(SceneryFaultKind.MissingInclude, token.Index, token: name);
            }

            // The simulator takes its parameters up to the word "end", written just so,
            // whatever comes before it.
            int end = at + 2;
            while (end < tokens.Count && tokens[end].Text != "end")
                end++;
            int last = Math.Min(end, tokens.Count - 1);
            if (Lost(at + 1, last) is not null)
                Fault(SceneryFaultKind.IncludeNotClosed, token.Index, token: name, swallowed: (at + 1, last));
            return last;
        }

        // A vehicle with no trainset around it, written as within one - offset, crew,
        // coupling - where its track, offset and crew belong.
        private bool TrainsetStyle(int at) =>
            at + 9 < tokens.Count && IsNumber(tokens[at + 8]) && Driver(tokens[at + 9]);

        // The first enddynamic from the given word on, or the last word there is.
        private int EndDynamic(int from)
        {
            int at = from;
            while (at < tokens.Count && !tokens[at].Is("enddynamic"))
                at++;
            return Math.Min(at, tokens.Count - 1);
        }

        /// <summary>
        /// The entries starting past <paramref name="after"/> up to <paramref name="last"/>,
        /// as they would be named in the text - vehicles by their names - or null
        /// when none start there.
        /// </summary>
        private string? Lost(int after, int last)
        {
            var entries = new List<string>();
            for (int at = after + 1; at <= last && at < tokens.Count; at++)
            {
                var token = tokens[at];
                if (token.Is("node") && at + 4 < tokens.Count)
                    entries.Add(tokens[at + 4].Is("dynamic")
                        ? tokens[at + 3].Text
                        : $"{tokens[at + 4].Text} {tokens[at + 3].Text}");
                else if ((token.Is("trainset") || token.Is("include")) && at + 1 < tokens.Count)
                    entries.Add($"{token.Text} {tokens[at + 1].Text}");
                else if (token.Is("endtrainset") || token.Is("firstinit"))
                    entries.Add(token.Text);
            }

            return entries.Count == 0 ? null : Listed(entries);
        }

        // Whether the words after the two given are the same, so many of them.
        private bool Same(int first, int second, int count)
        {
            for (int i = 1; i <= count; i++)
                if (second + i >= tokens.Count || !tokens[first + i].Text.Equals(tokens[second + i].Text, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        /// <summary>Whether the <paramref name="count"/> words after the one at <paramref name="at"/> are all there and none starts something else.</summary>
        private bool Fields(int at, int count)
        {
            if (at + count >= tokens.Count)
                return false;
            for (int i = at + 1; i <= at + count; i++)
                if (Keyword(tokens[i]) || tokens[i].Is("enddynamic"))
                    return false;
            return true;
        }

        private void Number(SceneryCode.Token field, string subject)
        {
            if (!IsNumber(field))
                Fault(SceneryFaultKind.NotANumber, field.Index, subject, field.Text);
        }

        /// <param name="swallowed">The words the simulator takes along with it, past the one given up to the last.</param>
        private void Fault(SceneryFaultKind kind, int index, string? subject = null, string? token = null,
                           (int After, int Last)? swallowed = null)
        {
            // Faults are the simulator's reading's to tell.
            if (!asSimulator)
                return;
            string? lost = null;
            if (swallowed is var (after, last) && Lost(after, last) is { } entries)
            {
                lost = entries;
                Swallowed.Add((tokens[after + 1].Index, tokens[last].Index, index));
            }
            Faults.Add(lines.Fault(kind, index, subject, token) with { Lost = lost });
        }
    }

    /// <summary>
    /// How many words a trainset's track name was split into by spaces, judged by
    /// the two numbers - offset and speed - that follow them; null when it was not.
    /// </summary>
    private static int? SplitTrack(List<SceneryCode.Token> tokens, int header)
    {
        if (IsNumber(tokens[header + 3]))
            return null;

        for (int words = 2; words <= 4; words++)
        {
            int offset = header + 2 + words;
            if (offset + 1 >= tokens.Count || Keyword(tokens[offset - 1]) || IsNumber(tokens[offset - 1]))
                return null;
            if (IsNumber(tokens[offset]) && IsNumber(tokens[offset + 1]))
                return words;
        }

        return null;
    }

    /// <summary>
    /// Whether a load count reads as other than 0, the way the simulator reads a
    /// whole number: the digits it starts with, and 0 for one that does not.
    /// </summary>
    private static bool Counted(string text)
    {
        int at = text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;
        bool any = false;
        for (; at < text.Length && char.IsAsciiDigit(text[at]); at++)
            any |= text[at] != '0';
        return any;
    }

    private static bool Driver(SceneryCode.Token token) =>
        token.Is("headdriver") || token.Is("reardriver") || token.Is("passenger") || token.Is("nobody");

    private static bool IsNumber(SceneryCode.Token token) =>
        double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool Keyword(SceneryCode.Token token) =>
        token.Is("node") || token.Is("trainset") || token.Is("endtrainset");

    /// <summary>
    /// An include named outright - not one built from a template's parameters or
    /// picked at random, which only the simulator can tell.
    /// </summary>
    private static bool Plain(string name) =>
        name.Length > 0 && !name.Contains("(p", StringComparison.OrdinalIgnoreCase) &&
        name.IndexOfAny(new[] { '*', '?' }) < 0;
}
