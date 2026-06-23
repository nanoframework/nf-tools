// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;

namespace nanoFramework.Migrate.Core.Common;

/// <summary>
/// Small hand-rolled glob matcher (no external dependency). Matches a path that
/// is relative to the input directory against a pattern supporting:
///   *   any run of characters except a path separator
///   **  any run of characters including path separators (spanning directories)
///   ?   exactly one character except a path separator
/// Matching is case-insensitive and separator-insensitive ('/' and '\' are
/// treated as equivalent), and is anchored to the whole relative path.
/// </summary>
public static class Glob
{
    public static bool IsMatch(string relativePath, string pattern)
    {
        var input = Normalize(relativePath);
        var regex = ToRegex(Normalize(pattern));
        return regex.IsMatch(input);
    }

    private static string Normalize(string s) => s.Replace('\\', '/').Trim('/');

    // Translates a normalized ('/'-separated) glob into an anchored, case-insensitive
    // regex. Literal characters are escaped; only *, ** and ? carry meaning.
    private static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                // "/**" spans directories AND the leading separator is optional, so
                // "Beginner/**" also matches "Beginner" itself. Emit "(/.*)?" and
                // consume the slash, both stars, and any trailing slash.
                case '/' when i + 2 < pattern.Length && pattern[i + 1] == '*' && pattern[i + 2] == '*':
                    i += 2;                        // consume '/' is current; skip both '*'
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++;
                    sb.Append("(/.*)?");
                    break;
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        i++;                       // consume the second '*'
                        // "**/" (or trailing "**") spans directories; collapse any
                        // following slash so "Beginner/**" also matches "Beginner".
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++;
                        sb.Append(".*");
                    }
                    else
                    {
                        sb.Append("[^/]*");        // single star: stay within a segment
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
