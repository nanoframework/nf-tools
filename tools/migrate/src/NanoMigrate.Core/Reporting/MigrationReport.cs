// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using nanoFramework.Migrate.Core.Projects;
using nanoFramework.Migrate.Core.Verification;

namespace nanoFramework.Migrate.Core.Reporting;

/// <summary>
/// The result of a single project within a <see cref="MigrationReport"/>, expressed
/// as plain DATA so the report writers stay pure and console-free. Mirrors the data
/// the on-screen summary table renders, but decoupled from any presentation layer.
/// </summary>
public sealed class ReportEntry
{
    /// <summary>The project path, relative to the report's root, with forward slashes.</summary>
    public required string RelativePath { get; init; }

    /// <summary>The conversion status of the project.</summary>
    public required ConvertStatus Status { get; init; }

    /// <summary>The resolved <c>PackageReference</c> ids and versions the project carries.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Packages { get; init; } =
        Array.Empty<KeyValuePair<string, string>>();

    /// <summary>The items flagged for manual review (empty when none).</summary>
    public IReadOnlyList<string> Review { get; init; } = Array.Empty<string>();

    /// <summary>An optional error message (set only when <see cref="Status"/> is Error).</summary>
    public string? Error { get; init; }
}

/// <summary>
/// One verification build result inside a <see cref="MigrationReport"/>. A flat,
/// presentation-free projection of <see cref="BuildOutcome"/> keyed by a relative
/// target path so the writers don't depend on the verification slice's shape.
/// </summary>
public sealed class ReportVerifyEntry
{
    /// <summary>The built target, relative to the report's root where possible.</summary>
    public required string Target { get; init; }

    /// <summary>True when the build succeeded.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>True when the build was skipped (e.g. no <c>dotnet</c> on PATH).</summary>
    public required bool Skipped { get; init; }

    /// <summary>The build process exit code.</summary>
    public int ExitCode { get; init; }

    /// <summary>A short note: the skip reason or the error tail on failure.</summary>
    public string? Details { get; init; }
}

/// <summary>
/// A migration run captured as DATA, ready to be rendered by the
/// <see cref="MarkdownReportWriter"/> or <see cref="HtmlReportWriter"/>. The engine
/// never calls <c>DateTime.Now</c>: the timestamp is supplied by the caller so the
/// writers stay pure and deterministically testable.
/// </summary>
public sealed class MigrationReport
{
    /// <summary>The root directory the migration ran against (paths are relative to it).</summary>
    public required string RootPath { get; init; }

    /// <summary>A UTC timestamp for the run, supplied by the caller.</summary>
    public required DateTime GeneratedUtc { get; init; }

    /// <summary>True when this report describes a dry-run (what WOULD change).</summary>
    public bool DryRun { get; init; }

    /// <summary>The per-project results, in run order.</summary>
    public IReadOnlyList<ReportEntry> Projects { get; init; } = Array.Empty<ReportEntry>();

    /// <summary>The solutions retargeted (or, in dry-run, that would be retargeted).</summary>
    public IReadOnlyList<string> AffectedSolutions { get; init; } = Array.Empty<string>();

    /// <summary>The verification build results, when a verify pass ran (otherwise empty).</summary>
    public IReadOnlyList<ReportVerifyEntry> Verify { get; init; } = Array.Empty<ReportVerifyEntry>();

    /// <summary>Projects that converted cleanly.</summary>
    public int Converted => Projects.Count(p => p.Status == ConvertStatus.Converted);

    /// <summary>Projects already SDK-style (skipped).</summary>
    public int Skipped => Projects.Count(p => p.Status == ConvertStatus.Skipped);

    /// <summary>Projects converted but flagged for manual review.</summary>
    public int Flagged => Projects.Count(p => p.Status == ConvertStatus.Review);

    /// <summary>Projects that errored.</summary>
    public int Errors => Projects.Count(p => p.Status == ConvertStatus.Error);

    /// <summary>The total number of projects in the report.</summary>
    public int Total => Projects.Count;
}
