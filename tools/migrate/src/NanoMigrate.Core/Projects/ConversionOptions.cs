// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Migrate.Core.Projects;

/// <summary>
/// The knobs that drive a conversion. This is the engine-facing options type — it
/// is intentionally NOT the CLI settings type, so the engine carries no console or
/// command-line dependency. The CLI maps its parsed settings onto this record.
/// </summary>
public sealed record ConversionOptions
{
    /// <summary>
    /// Output extension for the emitted project. Either <c>.csproj</c> or
    /// <c>.nfproj</c>. Default <c>.csproj</c>: a normal run produces Foo.csproj and
    /// retires Foo.nfproj.
    /// </summary>
    public string Ext { get; init; } = ".csproj";

    /// <summary>Target framework moniker written into the emitted project.</summary>
    public string Tfm { get; init; } = "netnano1.0";

    /// <summary>
    /// Accepted for back-compat but no longer emitted: the SDK reference is
    /// versionless (the version is pinned via global.json <c>msbuild-sdks</c>).
    /// </summary>
    public string Sdk { get; init; } = "2.0.0";

    /// <summary>Analyse and preview only; write nothing to disk.</summary>
    public bool DryRun { get; init; }

    /// <summary>Don't write a <c>.nfproj.bak</c> alongside the converted project.</summary>
    public bool NoBackup { get; init; }

    /// <summary>
    /// Glob filter (relative to the input directory) selecting which <c>.nfproj</c>
    /// to convert. Null means "all <c>.nfproj</c> recursively" (the default).
    /// Supports <c>*</c>, <c>**</c> and <c>?</c>.
    /// </summary>
    public string? Glob { get; init; }

    /// <summary>
    /// Suppress the converter's built-in solution retargeting (its walk-up
    /// discovery + rewrite). Set when the host drives a <em>solution-scoped</em>
    /// migration and rewrites the chosen solutions itself via
    /// <see cref="SolutionRewriter"/>, so the converter never touches a solution the
    /// user did not select. The <see cref="ConvertResult.UpdatedSolutions"/> preview
    /// list is still populated. Default false preserves the historical behavior.
    /// </summary>
    public bool SkipSolutionRewrite { get; init; }

    /// <summary>
    /// After a real (non-dry-run) migration, verify the result by building the
    /// affected solution(s)/project(s) with the <c>dotnet</c> CLI. <c>null</c> (the
    /// default) means "verify on for real runs, off for dry-run"; set
    /// <c>true</c>/<c>false</c> to force it on/off. A failed verification is what
    /// triggers the rollback prompt.
    /// </summary>
    public bool? Verify { get; init; }

    /// <summary>
    /// Resolves <see cref="Verify"/> against <see cref="DryRun"/>: dry-run never
    /// verifies; otherwise the default is on.
    /// </summary>
    public bool VerifyEffective => !DryRun && (Verify ?? true);

    /// <summary>
    /// Central Package Management (CPM) override. <c>null</c> (the default) means
    /// auto-detect: the converter walks up from the project directory for a
    /// <c>Directory.Packages.props</c> with
    /// <c>&lt;ManagePackageVersionsCentrally&gt;true&lt;/ManagePackageVersionsCentrally&gt;</c>.
    /// Set <c>true</c>/<c>false</c> to force CPM on/off regardless of what is on disk.
    /// When CPM is active the emitted <c>PackageReference</c> elements are versionless
    /// and each referenced id is ensured to have a <c>PackageVersion</c> entry in the
    /// nearest <c>Directory.Packages.props</c>.
    /// </summary>
    public bool? CentralPackageManagement { get; init; }
}
