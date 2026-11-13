// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Solutions;

/// <summary>
/// Given a set of target <c>.nfproj</c> paths, finds which discovered solutions
/// reference any of them — by parsing each solution's project list and intersecting
/// it with the targets. Pure analysis over already-loaded solution models.
/// </summary>
public static class SolutionDiscovery
{
    /// <summary>
    /// Returns the solutions (from <paramref name="solutions"/>) that reference at
    /// least one of <paramref name="targetNfprojPaths"/>. Comparison is by absolute
    /// path, case-insensitive. Unrelated solutions are excluded.
    /// </summary>
    public static List<SolutionFile> Referencing(
        IEnumerable<SolutionFile> solutions,
        IEnumerable<string> targetNfprojPaths)
    {
        var targets = new HashSet<string>(
            targetNfprojPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0) return new List<SolutionFile>();

        return solutions
            .Where(s => s.ProjectPaths.Any(p => targets.Contains(Path.GetFullPath(p))))
            .ToList();
    }

    /// <summary>
    /// Loads every solution under <paramref name="dir"/> and returns those that
    /// reference at least one of <paramref name="targetNfprojPaths"/>.
    /// </summary>
    public static List<SolutionFile> FindReferencing(string dir, IEnumerable<string> targetNfprojPaths) =>
        Referencing(SolutionScanner.Load(dir), targetNfprojPaths);
}
