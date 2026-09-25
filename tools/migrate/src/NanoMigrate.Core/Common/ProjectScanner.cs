// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Common;

/// <summary>
/// Enumerates <c>.nfproj</c> files under a path, with an optional glob filter.
/// Pure file-system discovery — no console, no side effects.
/// </summary>
public static class ProjectScanner
{
    /// <summary>
    /// Resolves the set of <c>.nfproj</c> targets for a path. If <paramref name="path"/>
    /// is a single <c>.nfproj</c> file it is returned alone; if it is a directory,
    /// every <c>.nfproj</c> beneath it (optionally filtered by <paramref name="glob"/>,
    /// matched against the path relative to the directory) is returned, sorted.
    /// A non-existent path yields an empty list.
    /// </summary>
    public static List<string> ResolveProjects(string path, string? glob)
    {
        if (File.Exists(path) && path.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase))
            return new List<string> { Path.GetFullPath(path) };
        if (!Directory.Exists(path)) return new List<string>();

        var baseDir = Path.GetFullPath(path);
        return Directory.EnumerateFiles(path, "*.nfproj", SearchOption.AllDirectories)
                        .Select(Path.GetFullPath)
                        .Where(p => glob is null || Glob.IsMatch(Path.GetRelativePath(baseDir, p), glob))
                        .OrderBy(p => p)
                        .ToList();
    }

    /// <summary>
    /// Enumerates the <c>.nfproj</c> under a repo directory that survive the glob
    /// filter (matched against the path relative to the repo directory). Returns
    /// absolute, full paths.
    /// </summary>
    public static IEnumerable<string> NfprojUnder(string repoDir, string? glob)
    {
        var baseDir = Path.GetFullPath(repoDir);
        foreach (var nf in Directory.EnumerateFiles(repoDir, "*.nfproj", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(nf);
            if (glob is null || Glob.IsMatch(Path.GetRelativePath(baseDir, full), glob))
                yield return full;
        }
    }
}
