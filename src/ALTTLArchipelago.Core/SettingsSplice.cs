using System;

namespace ALTTLArchipelago.Core;

/// <summary>
/// Replace ONE object inside a JSON document, leaving every other byte alone.
///
/// WHY NOT JUST PARSE AND RE-SERIALISE. The document this operates on is the
/// player's campaign save, and the only thing the mod has any business
/// changing in it is the settings object. Round-tripping the whole file
/// through a JSON library to move one key puts every OTHER value through a
/// parse and a re-serialise as well - so a number the game wrote as "1.0"
/// can come back as "1", key order can shift, and precision can move. None
/// of that is likely to matter and all of it is the mod rewriting bytes it
/// was never asked to touch, in a file that holds someone's progress.
///
/// So: find the span of the named object, and splice. Everything outside the
/// span is copied through unexamined and comes out identical.
///
/// IT REFUSES RATHER THAN GUESSES. A document it cannot read confidently
/// returns null, and the caller falls back to the parse-and-rewrite it was
/// doing before. Being unable to make the careful edit is not a reason to
/// make no edit; it is a reason to say so.
/// </summary>
public static class SettingsSplice
{
    /// <summary>
    /// The document with <paramref name="key"/>'s object replaced by
    /// <paramref name="replacement"/>, or null if the span could not be
    /// located unambiguously.
    /// </summary>
    /// <param name="document">Decoded JSON. Not modified.</param>
    /// <param name="key">The property name, without quotes.</param>
    /// <param name="replacement">
    /// The new value, already serialised - an object literal including its
    /// braces.
    /// </param>
    public static string? Replace(string document, string key, string replacement)
    {
        if (string.IsNullOrEmpty(document)) return null;
        if (string.IsNullOrEmpty(key)) return null;
        if (string.IsNullOrEmpty(replacement)) return null;

        var span = Find(document, key);
        if (span == null) return null;

        var (start, end) = span.Value;
        return document.Substring(0, start) + replacement + document.Substring(end);
    }

    /// <summary>
    /// Where the value of "key" starts and ends, as [start, end).
    ///
    /// Returns null unless the key occurs EXACTLY ONCE as a property name and
    /// its value is an object. Two matches means the document has the same
    /// property in more than one place and there is no way to tell from here
    /// which one was meant.
    /// </summary>
    internal static (int Start, int End)? Find(string document, string key)
    {
        var needle = "\"" + key + "\"";
        var found = -1;

        for (int at = document.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = document.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            // Only count it as a property name if a colon follows. The same
            // text inside a string VALUE would otherwise match.
            var after = SkipSpace(document, at + needle.Length);
            if (after >= document.Length || document[after] != ':') continue;

            // And only if the quote that opens it is not itself escaped or
            // part of a larger token - a property name is preceded by '{' or
            // ',' once whitespace is discounted.
            var before = BackSkipSpace(document, at - 1);
            if (before >= 0 && document[before] != '{' && document[before] != ',')
            {
                continue;
            }

            if (found >= 0) return null;              // ambiguous; refuse
            found = after + 1;
        }

        if (found < 0) return null;

        var start = SkipSpace(document, found);
        if (start >= document.Length || document[start] != '{') return null;

        var end = EndOfObject(document, start);
        return end < 0 ? null : (start, end);
    }

    /// <summary>
    /// One past the closing brace of the object starting at
    /// <paramref name="start"/>, or -1 if it never closes.
    ///
    /// Counts braces, and knows that braces inside a STRING are not braces.
    /// Without that a settings object holding a path or a label with a brace
    /// in it would end the scan early and the splice would cut the file in
    /// half - which is precisely the failure this class exists to avoid.
    /// </summary>
    internal static int EndOfObject(string document, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (int i = start; i < document.Length; i++)
        {
            var c = document[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i + 1;
            }
        }

        return -1;
    }

    private static int SkipSpace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    private static int BackSkipSpace(string s, int i)
    {
        while (i >= 0 && char.IsWhiteSpace(s[i])) i--;
        return i;
    }
}
