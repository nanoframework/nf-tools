// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Projects;

/// <summary>How a single project ended up after a (dry-run or real) conversion.</summary>
public enum ConvertStatus
{
    /// <summary>Converted cleanly.</summary>
    Converted,

    /// <summary>Already SDK-style; nothing to do.</summary>
    Skipped,

    /// <summary>Converted, but flagged items need a human.</summary>
    Review,

    /// <summary>Threw while converting.</summary>
    Error,
}

/// <summary>
/// Outcome of converting a single project, expressed as DATA. The engine never
/// writes to the console — the CLI consumes this to render its presentation.
/// </summary>
public sealed class ConvertResult
{
    /// <summary>The path the conversion emits (or, in dry-run, would emit).</summary>
    public required string OutputPath { get; init; }

    /// <summary>Items the tool could not confidently resolve; each needs a human.</summary>
    public List<string> Review { get; } = new();

    /// <summary>True when the project was already SDK-style and was left untouched.</summary>
    public bool AlreadySdk { get; set; }

    /// <summary>Resolved PackageReferences (id -> version) the emitted project will carry.</summary>
    public List<KeyValuePair<string, string>> Packages { get; } = new();

    /// <summary>Files this conversion deletes (or, in dry-run, would delete).</summary>
    public List<string> DeletedFiles { get; } = new();

    /// <summary>.sln files this conversion retargets (or, in dry-run, would retarget).</summary>
    public List<string> UpdatedSolutions { get; } = new();

    /// <summary>
    /// Under Central Package Management, the <c>Directory.Packages.props</c> this
    /// conversion adds <c>PackageVersion</c> entries to (or, in dry-run, would add).
    /// Null when CPM is inactive or no entries are missing.
    /// </summary>
    public string? UpdatedPackagesProps { get; set; }

    /// <summary>
    /// The <c>PackageVersion</c> entries (id -> version) added to the central
    /// <c>Directory.Packages.props</c> under CPM (or, in dry-run, that would be added).
    /// </summary>
    public List<KeyValuePair<string, string>> AddedPackageVersions { get; } = new();

    /// <summary>Set when the conversion threw; used to render a red Error row.</summary>
    public string? Error { get; set; }

    /// <summary>Derived status, computed from the data above.</summary>
    public ConvertStatus Status =>
        Error is not null ? ConvertStatus.Error
        : AlreadySdk       ? ConvertStatus.Skipped
        : Review.Count > 0 ? ConvertStatus.Review
        :                    ConvertStatus.Converted;
}
