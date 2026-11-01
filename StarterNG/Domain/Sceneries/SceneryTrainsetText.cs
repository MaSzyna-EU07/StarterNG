using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Classes;

namespace StarterNG.Domain.Sceneries;

/// <summary>
/// A trainset written back into its own scenery file. Only the vehicles that
/// changed are the starter's to rewrite: everything else the author put there -
/// the header as typed, the description, comments, an assignments block, the other
/// vehicles as they are spelled - stays as it was, and the rest of the file is not
/// touched at all.
/// </summary>
public static class SceneryTrainsetText
{
    private static readonly Regex LoneLineFeed = new(@"(?<!\r)\n");

    private static readonly Regex RestOfLine = new(@"^[ \t]*\r?\n?");

    /// <summary>
    /// The trainset as the file should now hold it, in the file's own line ends.
    /// Ends at <c>endtrainset</c>, as the parser's match of the old one did.
    /// </summary>
    /// <remarks>
    /// What stands between the vehicles stays in its place among those that stay.
    /// A comment inside a vehicle's own node goes when that node is rewritten or
    /// the vehicle removed: there is no longer a place for it.
    /// </remarks>
    public static string Compose(Trainset trainset, string newline)
    {
        string raw = trainset.RawEntry;
        var saved = new Trainset(raw);

        // Not read to its end: what stands there is not known, so it all stays.
        if (!saved.Parsed)
            return raw;

        var (speed, vehicles, end) = saved.Layout;

        // Line ends of both kinds are left as they are: there is no one kind to give them.
        bool mixed = raw.Contains("\r\n", StringComparison.Ordinal) && LoneLineFeed.IsMatch(raw);
        string Own(string text) => mixed ? text : Normalize(text).Replace("\n", newline);

        int first = vehicles.Count > 0 ? vehicles[0].Start : end;
        string head = raw[..first];

        // The speed is the one header field the depot changes - the ready switch.
        // Rewritten only when it differs, so "0.0" stays "0.0".
        if (saved.Velocity != trainset.Velocity && speed is { } field)
            head = raw[..field.Index] + trainset.Velocity.ToString(CultureInfo.InvariantCulture) + raw[field.End..first];

        string[] was = saved.Vehicles.Select(v => v.ToTrainsetNode()).ToArray();
        string[] now = trainset.Vehicles.Select(v => v.ToTrainsetNode()).ToArray();
        string ending = end < raw.Length ? raw[end..] : "endtrainset";
        if (was.SequenceEqual(now))
            return Own(head + raw[first..end] + ending);

        string Written(int i) => raw[vehicles[i].Start..vehicles[i].End];
        string After(int i) => raw[vehicles[i].End..(i + 1 < vehicles.Count ? vehicles[i + 1].Start : end)];

        var sb = new StringBuilder(Own(head));

        void AtLineStart()
        {
            if (sb.Length > 0 && sb[^1] != '\n')
                sb.Append(newline);
        }

        // One moved here is still written as it was.
        void Rewrite(int j)
        {
            AtLineStart();
            int written = Array.IndexOf(was, now[j]);
            sb.Append(written >= 0 ? Own(Written(written)) : now[j].TrimEnd('\n'));
        }

        int[,] staying = Staying(was, now);
        for (int i = 0, j = 0; i < was.Length || j < now.Length;)
        {
            bool both = i < was.Length && j < now.Length;
            if (both && was[i] == now[j])
            {
                sb.Append(Own(Written(i) + After(i)));
                i++;
                j++;
            }
            else if (both && staying[i + 1, j + 1] == staying[i, j])
            {
                // In the place of the one there before, so what followed that one stays.
                Rewrite(j++);
                sb.Append(Own(After(i++)));
            }
            else if (j < now.Length && (i == was.Length || staying[i, j + 1] == staying[i, j]))
            {
                Rewrite(j++);
                sb.Append(newline);
            }
            else
            {
                // Gone, the end of its line with it; what followed it stays.
                string after = After(i++);
                if (!string.IsNullOrWhiteSpace(after))
                {
                    AtLineStart();
                    sb.Append(Own(RestOfLine.Replace(after, "", 1)));
                }
            }
        }

        AtLineStart();
        return sb.Append(Own(ending)).ToString();
    }

    /// <summary>
    /// How many vehicles can stay in the order they are in, of those from each place on
    /// in the old list and in the new one: the longest common run of the two.
    /// </summary>
    private static int[,] Staying(string[] was, string[] now)
    {
        var staying = new int[was.Length + 1, now.Length + 1];
        for (int i = was.Length - 1; i >= 0; i--)
            for (int j = now.Length - 1; j >= 0; j--)
                staying[i, j] = was[i] == now[j]
                    ? staying[i + 1, j + 1] + 1
                    : Math.Max(staying[i + 1, j], staying[i, j + 1]);
        return staying;
    }

    /// <summary>
    /// The file with the trainset's old text swapped for <paramref name="entry"/>,
    /// or null when that text cannot be told apart in the file: missing, or found
    /// more often than the scenery has trainsets written that way.
    /// </summary>
    public static string? Replace(string fileText, Trainset trainset, IReadOnlyList<Trainset> sceneryTrainsets,
                                  string entry)
    {
        var twins = sceneryTrainsets.Where(t => t.RawEntry == trainset.RawEntry).ToList();
        int ordinal = twins.FindIndex(t => ReferenceEquals(t, trainset));
        if (ordinal < 0 || trainset.RawEntry.Length == 0)
            return null;

        var found = new List<int>();
        for (int at = fileText.IndexOf(trainset.RawEntry, StringComparison.Ordinal);
             at >= 0;
             at = fileText.IndexOf(trainset.RawEntry, at + trainset.RawEntry.Length, StringComparison.Ordinal))
            found.Add(at);

        if (found.Count != twins.Count)
            return null;

        int start = found[ordinal];
        return fileText[..start] + entry + fileText[(start + trainset.RawEntry.Length)..];
    }

    /// <summary>
    /// Whether the trainset now differs from what its file holds, compared the way
    /// both would be written so a different spelling of the same number is no change.
    /// </summary>
    public static bool IsModified(Trainset trainset) =>
        trainset.Parsed && Signature(trainset) != SavedSignature(trainset.RawEntry);

    private static readonly Dictionary<string, string> SavedSignatures = new();

    private static string SavedSignature(string raw)
    {
        lock (SavedSignatures)
        {
            if (!SavedSignatures.TryGetValue(raw, out string? signature))
                SavedSignatures[raw] = signature = Signature(new Trainset(raw));
            return signature;
        }
    }

    private static string Signature(Trainset trainset) =>
        trainset.Velocity.ToString(CultureInfo.InvariantCulture) + "\n" +
        Dynamic.ToTrainsetNodes(trainset.Vehicles);

    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
