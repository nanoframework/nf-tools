// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using nanoFramework.Migrate.Core.Common;

namespace nanoFramework.Migrate.Core.Solutions;

/// <summary>
/// How the migration target was specified, which decides whether the CLI must
/// prompt the user to choose among candidate solutions.
/// </summary>
public enum PlanKind
{
    /// <summary>Input was a single <c>.nfproj</c> file (no solution awareness).</summary>
    SingleProject,

    /// <summary>
    /// A directory with no solutions found: fall back to converting every
    /// <c>.nfproj</c> loosely (the historical directory behavior).
    /// </summary>
    LooseDirectory,

    /// <summary>
    /// Input was an explicit solution (positional <c>.sln</c>/<c>.slnx</c> or
    /// <c>--solution</c>): convert only that solution's projects, update only it.
    /// No prompt.
    /// </summary>
    ExplicitSolution,

    /// <summary>
    /// A directory where solutions were discovered: the user must choose which
    /// solution(s) to migrate (multi-select), unless non-interactive/--yes.
    /// </summary>
    DirectoryWithSolutions,

    /// <summary>
    /// A glob narrowed the projects; the affected solutions (those referencing a
    /// matched project) are presented for confirmation / multi-select.
    /// </summary>
    GlobScoped,
}

/// <summary>
/// A candidate solution surfaced by the planner, paired with the matched
/// <c>.nfproj</c> projects it references (the projects that would be converted and
/// for which this solution would be retargeted).
/// </summary>
public sealed class SolutionCandidate
{
    public required SolutionFile Solution { get; init; }

    /// <summary>The matched <c>.nfproj</c> (absolute paths) this solution references.</summary>
    public required IReadOnlyList<string> NanoProjects { get; init; }
}

/// <summary>
/// The pure result of planning a migration. Carries enough data for the CLI to
/// render a preview and (when <see cref="RequiresSelection"/>) prompt the user —
/// the planner itself does no console I/O and no prompting.
/// </summary>
public sealed class MigrationPlan
{
    /// <summary>How the target was specified.</summary>
    public required PlanKind Kind { get; init; }

    /// <summary>
    /// Candidate solutions the user may choose among (empty for
    /// <see cref="PlanKind.SingleProject"/> / <see cref="PlanKind.LooseDirectory"/>).
    /// </summary>
    public IReadOnlyList<SolutionCandidate> Candidates { get; init; } = Array.Empty<SolutionCandidate>();

    /// <summary>
    /// For project/loose-directory plans: the <c>.nfproj</c> to convert when no
    /// solution is involved. Empty for solution-scoped plans (those projects come
    /// from the chosen candidates).
    /// </summary>
    public IReadOnlyList<string> LooseProjects { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when the CLI must let the user pick among <see cref="Candidates"/>
    /// (more than one candidate, or a glob scoping that warrants confirmation). For
    /// an explicit single solution this is false (no choice to make).
    /// </summary>
    public bool RequiresSelection => Kind is PlanKind.DirectoryWithSolutions or PlanKind.GlobScoped;

    /// <summary>
    /// Flattens a chosen set of candidates into the distinct <c>.nfproj</c> projects
    /// to convert (sorted, absolute). For non-solution plans the caller uses
    /// <see cref="LooseProjects"/> instead.
    /// </summary>
    public static List<string> ProjectsOf(IEnumerable<SolutionCandidate> chosen) =>
        chosen.SelectMany(c => c.NanoProjects)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
              .ToList();
}

/// <summary>
/// Builds a <see cref="MigrationPlan"/> from the command inputs and the file system.
/// All branching about solutions vs loose conversion lives here, so the CLI only
/// renders the plan and (when required) prompts. No console, no prompting.
/// </summary>
public static class MigrationPlanner
{
    /// <summary>
    /// Plans a migration.
    /// <list type="bullet">
    /// <item><paramref name="path"/> — the positional argument: a <c>.nfproj</c>, a
    /// solution (<c>.sln</c>/<c>.slnx</c>), or a directory.</item>
    /// <item><paramref name="explicitSolution"/> — the <c>--solution</c> override;
    /// when set, only that solution is touched.</item>
    /// <item><paramref name="glob"/> — narrows the <c>.nfproj</c> within a directory
    /// and triggers solution discovery over the matched projects.</item>
    /// </list>
    /// </summary>
    public static MigrationPlan Plan(string path, string? explicitSolution, string? glob)
    {
        // 1. An explicit --solution always wins: convert only its .nfproj, touch
        //    only it. (Honoured even if the positional path is a directory.)
        if (!string.IsNullOrEmpty(explicitSolution))
            return ExplicitSolutionPlan(explicitSolution);

        // 2. Positional path is itself a solution file.
        if (File.Exists(path) && SolutionFile.IsSolutionPath(path))
            return ExplicitSolutionPlan(path);

        // 3. Positional path is a single .nfproj file: no solution awareness.
        if (File.Exists(path) && path.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase))
            return new MigrationPlan
            {
                Kind = PlanKind.SingleProject,
                LooseProjects = new[] { Path.GetFullPath(path) },
            };

        // 4. Directory.
        if (Directory.Exists(path))
            return DirectoryPlan(path, glob);

        // Non-existent path: nothing to do (empty loose plan).
        return new MigrationPlan { Kind = PlanKind.LooseDirectory };
    }

    private static MigrationPlan ExplicitSolutionPlan(string solutionPath)
    {
        var sln = SolutionFile.Load(solutionPath);
        var nano = sln.NanoProjects();
        return new MigrationPlan
        {
            Kind = PlanKind.ExplicitSolution,
            Candidates = new[]
            {
                new SolutionCandidate { Solution = sln, NanoProjects = nano },
            },
        };
    }

    private static MigrationPlan DirectoryPlan(string dir, string? glob)
    {
        var solutions = SolutionScanner.Load(dir);

        // GLOB MODE: scope to the matched projects, then discover which solutions
        // reference any of them. Even with no solutions found we honour the glob via
        // a loose conversion of the matched projects.
        if (glob is not null)
        {
            var matched = ProjectScanner.NfprojUnder(dir, glob)
                                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                        .ToList();
            var affected = SolutionDiscovery.Referencing(solutions, matched);
            if (affected.Count == 0)
                return new MigrationPlan { Kind = PlanKind.LooseDirectory, LooseProjects = matched };

            var matchedSet = new HashSet<string>(matched, StringComparer.OrdinalIgnoreCase);
            return new MigrationPlan
            {
                Kind = PlanKind.GlobScoped,
                Candidates = affected.Select(s => new SolutionCandidate
                {
                    Solution = s,
                    NanoProjects = s.NanoProjects()
                                    .Where(p => matchedSet.Contains(Path.GetFullPath(p)))
                                    .ToList(),
                }).ToList(),
            };
        }

        // NO GLOB: 0 solutions => loose directory; >=1 => the user chooses.
        if (solutions.Count == 0)
        {
            var all = ProjectScanner.ResolveProjects(dir, glob: null);
            return new MigrationPlan { Kind = PlanKind.LooseDirectory, LooseProjects = all };
        }

        return new MigrationPlan
        {
            Kind = PlanKind.DirectoryWithSolutions,
            Candidates = solutions
                .Where(s => s.NanoProjects().Count > 0)   // only solutions with .nfproj to convert
                .Select(s => new SolutionCandidate { Solution = s, NanoProjects = s.NanoProjects() })
                .ToList(),
        };
    }
}
