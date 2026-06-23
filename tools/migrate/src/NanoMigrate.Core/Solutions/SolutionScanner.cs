// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Solutions;

/// <summary>
/// Discovers Visual Studio solution files (<c>.sln</c> and <c>.slnx</c>) beneath a
/// directory. Pure file-system discovery — no console, no side effects.
/// </summary>
public static class SolutionScanner
{
    /// <summary>
    /// Finds every <c>.sln</c> and <c>.slnx</c> under <paramref name="dir"/>
    /// (recursively), returning absolute paths, sorted. A non-existent directory
    /// yields an empty list.
    /// </summary>
    public static List<string> Find(string dir)
    {
        if (!Directory.Exists(dir)) return new List<string>();

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in new[] { "*.sln", "*.slnx" })
            foreach (var sln in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                found.Add(Path.GetFullPath(sln));

        return found.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Loads every solution under <paramref name="dir"/>. Files that fail to parse
    /// (malformed XML, unreadable) are skipped rather than aborting discovery.
    /// </summary>
    public static List<SolutionFile> Load(string dir)
    {
        var result = new List<SolutionFile>();
        foreach (var path in Find(dir))
        {
            try { result.Add(SolutionFile.Load(path)); }
            catch { /* skip a solution we cannot parse */ }
        }
        return result;
    }
}
