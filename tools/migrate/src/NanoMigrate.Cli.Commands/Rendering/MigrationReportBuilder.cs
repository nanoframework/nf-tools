// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace NanoFramework.Migrate.Cli.Rendering;

/// <summary>The output format chosen for a written migration report.</summary>
public enum ReportFormat
{
    Markdown,
    Html,
}

/// <summary>
/// Maps the command-layer migration data (<see cref="ProjectOutcome"/>, rewritten
/// solutions and verification <see cref="BuildOutcome"/>s) onto the console-free
/// Core <see cref="MigrationReport"/> the report writers consume. Lives in the
/// presentation layer because <see cref="ProjectOutcome"/> is a command-layer type;
/// the resulting report and the writers stay pure Core.
/// </summary>
public static class MigrationReportBuilder
{
    /// <summary>
    /// Builds a <see cref="MigrationReport"/> from the same outcome data the summary
    /// table renders. <paramref name="generatedUtc"/> is supplied by the caller so
    /// Core never reads the clock.
    /// </summary>
    public static MigrationReport Build(
        IReadOnlyList<ProjectOutcome> results,
        string baseDir,
        bool dryRun,
        IReadOnlyList<string> affectedSolutions,
        IReadOnlyList<BuildOutcome>? verify,
        DateTime generatedUtc)
    {
        var projects = results
            .Select(o => new ReportEntry
            {
                RelativePath = Rel(baseDir, o.Nfproj),
                Status = o.Result.Status,
                Packages = o.Result.Packages.ToList(),
                Review = o.Result.Review.ToList(),
                Error = o.Result.Error,
            })
            .ToList();

        var solutions = affectedSolutions.Select(s => Rel(baseDir, s)).ToList();

        var verifyEntries = (verify ?? Array.Empty<BuildOutcome>())
            .Select(v => new ReportVerifyEntry
            {
                Target = Rel(baseDir, v.Target),
                Succeeded = v.Succeeded,
                Skipped = v.Skipped,
                ExitCode = v.ExitCode,
                Details = v.Skipped ? v.Message
                    : v.Succeeded ? null
                    : v.ErrorTail,
            })
            .ToList();

        return new MigrationReport
        {
            RootPath = baseDir,
            GeneratedUtc = generatedUtc,
            DryRun = dryRun,
            Projects = projects,
            AffectedSolutions = solutions,
            Verify = verifyEntries,
        };
    }

    /// <summary>
    /// Picks the report format from the file extension: <c>.md</c>/<c>.markdown</c> →
    /// Markdown, <c>.html</c>/<c>.htm</c> → HTML, anything else → Markdown.
    /// </summary>
    public static ReportFormat FormatFor(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".html" or ".htm" => ReportFormat.Html,
            _ => ReportFormat.Markdown, // .md, .markdown, and everything else
        };
    }

    /// <summary>Renders <paramref name="report"/> to <paramref name="path"/> in the format keyed by the extension.</summary>
    public static ReportFormat Write(MigrationReport report, string path)
    {
        var format = FormatFor(path);
        if (format == ReportFormat.Html)
            HtmlReportWriter.Write(report, path);
        else
            MarkdownReportWriter.Write(report, path);
        return format;
    }

    // A forward-slash path relative to baseDir when the file sits beneath it;
    // otherwise the bare file name. Keeps the report stable across machines.
    private static string Rel(string baseDir, string path)
    {
        var rel = Path.GetRelativePath(baseDir, path);
        if (rel.StartsWith("..", StringComparison.Ordinal))
            rel = Path.GetFileName(path);
        return rel.Replace('\\', '/');
    }
}
