// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

public class CleanTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    private const string Nfproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
        </Project>
        """;

    [Fact]
    public void Clean_removes_all_nfproj_bak_and_nanomigrate_folders_and_reports_count()
    {
        using var dir = new TempDir();

        // A real conversion leaves a Sample.nfproj.bak behind.
        var nfproj = dir.File("a/Sample.nfproj", Nfproj);
        Converter.Convert(nfproj, new ConversionOptions());
        Assert.True(File.Exists(nfproj + ".bak"));

        // A second, nested project's backup, plus a rollback journal folder.
        var nfproj2 = dir.File("b/Other.nfproj", Nfproj);
        Converter.Convert(nfproj2, new ConversionOptions());
        var journal = RollbackJournal.Start(dir.Path);
        journal.BackupBeforeChange(dir.File("c/some.txt", "x"));
        journal.Save();
        Assert.True(Directory.Exists(Path.Combine(dir.Path, RollbackJournal.FolderName)));

        var plan = BackupCleaner.Plan(dir.Path);
        Assert.Equal(2, plan.BackupFiles.Count);
        Assert.Single(plan.RollbackFolders);
        Assert.Equal(3, plan.Total);

        var result = BackupCleaner.Remove(plan);
        Assert.Empty(result.Problems);
        Assert.Equal(3, result.Total);

        // Everything is gone; a re-plan finds nothing (idempotent).
        Assert.False(File.Exists(nfproj + ".bak"));
        Assert.False(File.Exists(nfproj2 + ".bak"));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, RollbackJournal.FolderName)));
        Assert.True(BackupCleaner.Plan(dir.Path).IsEmpty);
    }

    [Fact]
    public void Clean_does_not_count_journal_internal_nfproj_backups_as_loose_backups()
    {
        using var dir = new TempDir();

        // A --no-backup real migration: zero loose .bak, but the journal keeps its own
        // "<seq>-Sample.nfproj.bak" copy of the original .nfproj inside .nanomigrate/.
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        var journal = RollbackJournal.Start(dir.Path);
        journal.BackupBeforeChange(nfproj);     // -> .nanomigrate/rollback-<id>/0000-Sample.nfproj.bak
        journal.Save();

        var plan = BackupCleaner.Plan(dir.Path);

        // The journal-internal .nfproj.bak must NOT be listed as a loose backup; the
        // whole .nanomigrate folder is the single rollback-folder leftover instead.
        Assert.Empty(plan.BackupFiles);
        Assert.Single(plan.RollbackFolders);

        // Clean still removes the .nanomigrate folder wholesale.
        BackupCleaner.Remove(plan);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, RollbackJournal.FolderName)));
    }

    [Fact]
    public void Clean_on_a_tree_with_no_leftovers_is_an_empty_noop()
    {
        using var dir = new TempDir();
        dir.File("Sample.csproj", "<Project/>");

        var plan = BackupCleaner.Plan(dir.Path);
        Assert.True(plan.IsEmpty);

        var result = BackupCleaner.Remove(plan);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Clean_on_nonexistent_path_yields_empty_plan()
    {
        var plan = BackupCleaner.Plan(Path.Combine(Path.GetTempPath(), "nanomig-does-not-exist-" + Guid.NewGuid()));
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Clean_does_not_touch_unrelated_files()
    {
        using var dir = new TempDir();
        var keep = dir.File("Keep.nfproj", Nfproj);          // a live project, NOT a .bak
        var keepCs = dir.File("Sample.csproj", "<Project/>");
        dir.File("Sample.nfproj.bak", "backup");             // a leftover to remove

        var plan = BackupCleaner.Plan(dir.Path);
        Assert.Single(plan.BackupFiles);
        BackupCleaner.Remove(plan);

        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(keepCs));
        Assert.False(File.Exists(dir.Combine("Sample.nfproj.bak")));
    }
}
