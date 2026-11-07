using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StarterNG.Domain.Sceneries;

/// <summary>
/// A scenery's text read the way the simulator's parser reads it: comments -
/// <c>//</c> to the end of the line, <c>/* */</c> anywhere, even glued to a word -
/// left out, and the rest split into words at spaces, tabs, line ends and
/// semicolons. A trainset commented out is no trainset.
/// </summary>
public static class SceneryCode
{
    public readonly record struct Token(string Text, int Index)
    {
        public int End => Index + Text.Length;

        public bool Is(string word) => string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex Word = new(@"[^\s;]+");

    /// <summary>
    /// The text with every comment blanked out, line ends kept, so a place in one
    /// is the same place in the other.
    /// </summary>
    /// <param name="unclosed">Where a <c>/*</c> that never ends starts, or -1.</param>
    public static string Mask(string text, out int unclosed)
    {
        unclosed = -1;
        char[]? masked = null;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                // Quotes glue what they enclose into one word, comment marks and all.
                int close = text.IndexOf('"', i + 1);
                i = close < 0 ? text.Length : close;
                continue;
            }

            if (c != '/' || i + 1 >= text.Length)
                continue;

            int end;
            if (text[i + 1] == '/')
            {
                end = text.IndexOf('\n', i + 2);
                if (end < 0)
                    end = text.Length;
            }
            else if (text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                    unclosed = i;
                end = close < 0 ? text.Length : close + 2;
            }
            else
            {
                continue;
            }

            masked ??= text.ToCharArray();
            for (int j = i; j < end; j++)
                if (masked[j] != '\n' && masked[j] != '\r')
                    masked[j] = ' ';
            i = end - 1;
        }

        return masked is null ? text : new string(masked);
    }

    /// <param name="code">Text with its comments blanked out by <see cref="Mask"/>.</param>
    public static List<Token> Tokens(string code)
    {
        var tokens = new List<Token>();
        for (var m = Word.Match(code); m.Success; m = m.NextMatch())
            tokens.Add(new Token(m.Value, m.Index));
        return tokens;
    }
}
