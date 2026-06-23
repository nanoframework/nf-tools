// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using nanoFramework.Migrate.Core.Projects;

namespace nanoFramework.Migrate.Core.Backup;

/// <summary>
/// Bridges a (dry-run) <see cref="ConvertResult"/> preview into a
/// <see cref="RollbackJournal"/>: backs up every file the conversion will modify or
/// delete and records the files it will create. This must run BEFORE the real
/// conversion touches disk, so the backups hold the true originals.
///
/// The converter already computes, in dry-run, exactly what a real run acts on:
/// <see cref="ConvertResult.DeletedFiles"/> (the .nfproj, packages.config and any
/// hand-written AssemblyInfo.cs it removes), <see cref="ConvertResult.OutputPath"/>
/// (the .csproj it creates) and <see cref="ConvertResult.UpdatedSolutions"/> /
/// <see cref="ConvertResult.UpdatedPackagesProps"/> (the files it rewrites). We
/// journal all of those.
///
/// The journal is SELF-CONTAINED: every original it must restore (the .nfproj,
/// packages.config, Properties/AssemblyInfo.cs, and any touched .sln/.slnx/
/// Directory.Packages.props) is copied INTO <c>.nanomigrate/rollback-&lt;id&gt;/</c>,
/// and rollback restores from there only. It does NOT depend on the converter's loose
/// next-to-project <c>*.nfproj.bak</c>, so a <c>--no-backup</c> migration (which writes
/// no loose .bak at all) is still fully reversible.
/// </summary>
public static class MigrationJournaling
{
    /// <summary>
    /// Records, into <paramref name="journal"/>, the rollback entries implied by a
    /// dry-run <paramref name="preview"/> of converting one project. Optionally also
    /// backs up the host-driven solutions in <paramref name="extraSolutionsToBackup"/>
    /// (the solutions a solution-scoped run rewrites itself).
    /// </summary>
    public static void Record(RollbackJournal journal, ConvertResult preview,
        IEnumerable<string>? extraSolutionsToBackup = null)
    {
        // Files removed by the conversion (original .nfproj, packages.config,
        // AssemblyInfo.cs): back up so they can be restored.
        foreach (var deleted in preview.DeletedFiles)
            journal.BackupBeforeChange(deleted);

        // Solutions the conversion rewrites: back up so the original references and
        // GUIDs can be restored.
        foreach (var sln in preview.UpdatedSolutions)
            journal.BackupBeforeChange(sln);

        // A central Directory.Packages.props the conversion appends to: back up.
        if (preview.UpdatedPackagesProps is not null)
            journal.BackupBeforeChange(preview.UpdatedPackagesProps);

        // The .csproj the conversion creates (only when it differs from the source,
        // i.e. it is a genuinely new file): record for deletion on rollback.
        var output = Path.GetFullPath(preview.OutputPath);
        var createsNewFile = !preview.DeletedFiles.Any(d =>
            string.Equals(Path.GetFullPath(d), output, StringComparison.OrdinalIgnoreCase))
            ? true
            // When OutputPath equals a deleted source (same extension run) nothing new is created.
            : false;
        // The .csproj is "new" whenever it is not already the original being deleted.
        if (createsNewFile && !File.Exists(output))
            journal.RecordCreated(output);
        else if (createsNewFile)
            // Output already exists on disk (e.g. a prior partial run): treat as modified.
            journal.BackupBeforeChange(output);

        if (extraSolutionsToBackup is not null)
            foreach (var sln in extraSolutionsToBackup)
                journal.BackupBeforeChange(sln);
    }
}
