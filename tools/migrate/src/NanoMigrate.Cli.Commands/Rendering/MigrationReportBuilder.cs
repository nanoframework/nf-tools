// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Ardalis.SmartEnum;

namespace nanoFramework.Migrate.Cli.Rendering;

/// <summary>
/// The output format for a written migration report. A SmartEnum so each value owns its
/// file extensions and its writer; adding a format (e.g. JSON) is a new value rather than
/// another switch arm. <see cref="FromPath"/> keys the format off the path's extension.
/// </summary>
public sealed class ReportFormat : SmartEnum<ReportFormat>
{
    public static readonly ReportFormat Markdown =
        new("Markdown", 0, new[] { ".md", ".markdown" }, MarkdownReportWriter.Write);

    public static readonly ReportFormat Html =
        new("Html", 1, new[] { ".html", ".htm" }, HtmlReportWriter.Write);

    private readonly string[] _extensions;
    private readonly Action<MigrationReport, string> _write;

    private ReportFormat(string name, int value, string[] extensions, Action<MigrationReport, string> write)
        : base(name, value)
    {
        _extensions = extensions;
        _write = write;
    }

    /// <summary>The format keyed by a path's extension; unknown or none falls back to Markdown.</summary>
    public static ReportFormat FromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return List.FirstOrDefault(f => f._extensions.Contains(ext)) ?? Markdown;
    }

    /// <summary>Renders <paramref name="report"/> to <paramref name="path"/> in this format.</summary>
    public void Write(MigrationReport report, string path) => _write(report, path);
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

    /// <summary>Picks the report format from the file extension (unknown or none → Markdown).</summary>
    public static ReportFormat FormatFor(string path) => ReportFormat.FromPath(path);

    /// <summary>Renders <paramref name="report"/> to <paramref name="path"/> in the format keyed by the extension.</summary>
    public static ReportFormat Write(MigrationReport report, string path)
    {
        var format = ReportFormat.FromPath(path);
        format.Write(report, path);
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
