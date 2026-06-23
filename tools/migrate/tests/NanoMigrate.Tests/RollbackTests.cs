// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Xunit;

namespace NanoFramework.Migrate.Tests;

public class RollbackTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    private const string Nfproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <ProjectGuid>{AAAAAAAA-0000-0000-0000-000000000001}</ProjectGuid>
            <AssemblyName>Sample</AssemblyName>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="nanoFramework.System.Text" />
          </ItemGroup>
        </Project>
        """;

    private const string PackagesConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          <package id="nanoFramework.System.Text" version="1.2.54" targetFramework="netnano1.0" />
        </packages>
        """;

    private const string AssemblyInfo = """
        using System.Reflection;
        [assembly: AssemblyTitle("Sample")]
        """;

    private static string SolutionText() => """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{11A8DD76-328B-46DF-9F39-F559912D0360}") = "Sample", "Sample.nfproj", "{AAAAAAAA-0000-0000-0000-000000000001}"
        EndProject
        Global
        EndGlobal
        """;

    // Mirrors what MigrateCommand does on a real run: dry-run preview FIRST (to know
    // what changes), back up / record into the journal, then the real conversion.
    private static RollbackJournal MigrateWithJournal(string root, string nfproj, ConversionOptions o)
    {
        var journal = RollbackJournal.Start(root);
        var preview = Converter.Convert(nfproj, o with { DryRun = true });
        MigrationJournaling.Record(journal, preview);
        Converter.Convert(nfproj, o);
        journal.Save();
        return journal;
    }

    [Fact]
    public void Rollback_restores_nfproj_packages_assemblyinfo_and_sln_byte_for_byte_and_deletes_csproj()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        var pc = dir.File("packages.config", PackagesConfig);
        var ai = dir.File(Path.Combine("Properties", "AssemblyInfo.cs"), AssemblyInfo);
        var sln = dir.File("Sample.sln", SolutionText());
        var csproj = dir.Combine("Sample.csproj");

        // Capture the exact original bytes so the round-trip can be proven byte-equal.
        var nfprojBytes = File.ReadAllBytes(nfproj);
        var pcBytes = File.ReadAllBytes(pc);
        var aiBytes = File.ReadAllBytes(ai);
        var slnBytes = File.ReadAllBytes(sln);

        var journal = MigrateWithJournal(dir.Path, nfproj, new ConversionOptions());

        // The migration happened: .csproj created, originals gone, .sln retargeted.
        Assert.True(File.Exists(csproj));
        Assert.False(File.Exists(nfproj));
        Assert.False(File.Exists(pc));
        Assert.False(File.Exists(ai));
        Assert.Contains("Sample.csproj", File.ReadAllText(sln));

        // Roll the run back from its saved manifest.
        var result = RollbackJournal.ApplyAndCleanup(journal.ManifestPath);
        Assert.NotNull(result);
        Assert.Empty(result!.Problems);

        // The created .csproj is gone.
        Assert.False(File.Exists(csproj));

        // Every original is restored byte-for-byte.
        Assert.True(File.Exists(nfproj));
        Assert.Equal(nfprojBytes, File.ReadAllBytes(nfproj));
        Assert.True(File.Exists(pc));
        Assert.Equal(pcBytes, File.ReadAllBytes(pc));
        Assert.True(File.Exists(ai));
        Assert.Equal(aiBytes, File.ReadAllBytes(ai));
        Assert.True(File.Exists(sln));
        Assert.Equal(slnBytes, File.ReadAllBytes(sln));
    }

    [Fact]
    public void Real_migrate_with_no_backup_writes_zero_loose_bak_but_journal_rolls_back_byte_for_byte()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        var pc = dir.File("packages.config", PackagesConfig);
        var ai = dir.File(Path.Combine("Properties", "AssemblyInfo.cs"), AssemblyInfo);
        var sln = dir.File("Sample.sln", SolutionText());
        var csproj = dir.Combine("Sample.csproj");

        var nfprojBytes = File.ReadAllBytes(nfproj);
        var pcBytes = File.ReadAllBytes(pc);
        var aiBytes = File.ReadAllBytes(ai);
        var slnBytes = File.ReadAllBytes(sln);

        // A real migration with --no-backup: the loose *.nfproj.bak must NOT be written.
        var journal = MigrateWithJournal(dir.Path, nfproj, new ConversionOptions { NoBackup = true });

        // The migration happened.
        Assert.True(File.Exists(csproj));
        Assert.False(File.Exists(nfproj));

        // ZERO loose, next-to-project .nfproj.bak anywhere outside the journal folder.
        var nanomigrate = Path.Combine(dir.Path, RollbackJournal.FolderName);
        var looseBaks = Directory.EnumerateFiles(dir.Path, "*.nfproj.bak", SearchOption.AllDirectories)
            .Where(p => !Path.GetFullPath(p).StartsWith(Path.GetFullPath(nanomigrate), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(looseBaks);

        // …but the journal IS present and self-contained.
        Assert.True(Directory.Exists(nanomigrate));
        Assert.True(File.Exists(journal.ManifestPath));

        // Rolling back from the journal restores every original byte-for-byte and
        // removes the created .csproj — proving the journal does NOT depend on the
        // loose .bak that --no-backup suppressed.
        var result = RollbackJournal.ApplyAndCleanup(journal.ManifestPath);
        Assert.NotNull(result);
        Assert.Empty(result!.Problems);

        Assert.False(File.Exists(csproj));
        Assert.Equal(nfprojBytes, File.ReadAllBytes(nfproj));
        Assert.Equal(pcBytes, File.ReadAllBytes(pc));
        Assert.Equal(aiBytes, File.ReadAllBytes(ai));
        Assert.Equal(slnBytes, File.ReadAllBytes(sln));
    }

    [Fact]
    public void Real_migrate_without_no_backup_still_writes_the_loose_bak()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        dir.File("packages.config", PackagesConfig);

        // Default options (backups enabled) keep the historical loose .bak alongside.
        MigrateWithJournal(dir.Path, nfproj, new ConversionOptions());

        Assert.True(File.Exists(nfproj + ".bak"));
        Assert.Equal(Nfproj, File.ReadAllText(nfproj + ".bak"));
    }

    [Fact]
    public void Rollback_is_safe_with_no_journal()
    {
        using var dir = new TempDir();

        // No .nanomigrate folder exists → nothing to roll back.
        Assert.Empty(RollbackJournal.ManifestPaths(dir.Path));
        Assert.Null(RollbackJournal.FindLatest(dir.Path));
    }

    [Fact]
    public void Apply_is_idempotent_second_apply_is_a_noop()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        dir.File("packages.config", PackagesConfig);
        var nfprojBytes = File.ReadAllBytes(nfproj);

        var journal = MigrateWithJournal(dir.Path, nfproj, new ConversionOptions());
        var manifest = RollbackJournal.FindLatest(dir.Path)!;

        var first = RollbackJournal.Apply(manifest);
        Assert.Empty(first.Problems);
        Assert.Equal(nfprojBytes, File.ReadAllBytes(nfproj));   // restored

        // A second apply over the already-restored tree is a clean no-op (the created
        // file is already gone; the restore just rewrites identical bytes).
        var second = RollbackJournal.Apply(manifest);
        Assert.Empty(second.Problems);
        Assert.Equal(nfprojBytes, File.ReadAllBytes(nfproj));
    }

    [Fact]
    public void Empty_journal_writes_nothing()
    {
        using var dir = new TempDir();
        var journal = RollbackJournal.Start(dir.Path);
        Assert.True(journal.IsEmpty);
        journal.Save();
        Assert.False(Directory.Exists(Path.Combine(dir.Path, RollbackJournal.FolderName)));
    }

    [Fact]
    public void BackupBeforeChange_keeps_the_first_original_when_recorded_twice()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "original");
        var journal = RollbackJournal.Start(dir.Path);

        journal.BackupBeforeChange(file);
        File.WriteAllText(file, "changed");
        journal.BackupBeforeChange(file);   // must NOT overwrite the first backup

        journal.Save();
        var manifest = RollbackJournal.FindLatest(dir.Path)!;
        RollbackJournal.Apply(manifest);

        // The very first backup (the true original) is what gets restored.
        Assert.Equal("original", File.ReadAllText(file));
    }
}
