using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StarterNG.Domain.Sceneries;

/// <summary>
/// The scenario's own files - included after FirstInit - put in place of their
/// include line when they hold trainsets, as the old starter did. Decorations
/// and road traffic live there; inlined, their consists are listed, can be
/// started from, and the copy written for a start carries them instead of the
/// include, so they are not loaded twice.
/// </summary>
/// <remarks>
/// Only the plain <c>include path end</c> form: an include with parameters needs
/// them put in, and one whose <c>end</c> is not on its line is read by the
/// simulator in ways not worth guessing. One marked <c>$optional</c> stays too:
/// whether it is loaded is the player's to choose. Each inlined file is wrapped in
/// marker comments, which is how a trainset is told to belong to it.
/// </remarks>
public static class SceneryIncludes
{
    public const string BeginMarker = "//$starter-include ";
    public const string EndMarker = "//$starter-endinclude";

    private static readonly Regex FirstInit = new(@"\bFirstInit\b", RegexOptions.IgnoreCase);

    private static readonly Regex Include = new(
        @"^[ \t]*include[ \t]+(\S+)[ \t]+end\b[^\r\n]*", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex HasTrainset = new(@"^[ \t]*trainset\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex BlockComment = new(@"/\*[\s\S]*?\*/");

    private static readonly Regex Marked = new(
        Regex.Escape(BeginMarker) + @"(\S+)[\s\S]*?" + Regex.Escape(EndMarker));

    /// <param name="read">
    /// A file named as an include names it: its name as it is on disk, which the
    /// marker carries, and its text - or null when there is none.
    /// </param>
    public static string Inline(string text, Func<string, (string File, string Text)?> read)
    {
        var firstInit = FirstInit.Match(text);
        if (!firstInit.Success)
            return text;

        string head = text[..firstInit.Index];
        string tail = text[firstInit.Index..];
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var comments = BlockComment.Matches(tail).Select(m => (m.Index, End: m.Index + m.Length)).ToList();

        return head + Include.Replace(tail, m =>
        {
            if (comments.Any(c => m.Index >= c.Index && m.Index < c.End))
                return m.Value;

            if (m.Value.Contains("$optional", StringComparison.OrdinalIgnoreCase))
                return m.Value;

            if (read(m.Groups[1].Value) is not var (file, content) || !HasTrainset.IsMatch(content))
                return m.Value;

            return BeginMarker + file + newline + content.TrimEnd() + newline + EndMarker;
        });
    }

    /// <summary>The text without the inlined files: what the scenery itself says.</summary>
    public static string WithoutInlined(string text) => Marked.Replace(text, "");

    /// <summary>Where each inlined file sits in the text, by the name its include gave.</summary>
    public static IReadOnlyList<(int Start, int End, string File)> Ranges(string text) =>
        Marked.Matches(text).Select(m => (m.Index, m.Index + m.Length, m.Groups[1].Value)).ToList();

    public static string? FileAt(IReadOnlyList<(int Start, int End, string File)> ranges, int index) =>
        ranges.FirstOrDefault(r => index >= r.Start && index < r.End).File;
}
