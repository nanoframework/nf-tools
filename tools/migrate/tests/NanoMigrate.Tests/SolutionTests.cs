// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace NanoFramework.Migrate.Tests;

public class SolutionTests
{
    private const string LegacyGuid = "{11A8DD76-328B-46DF-9F39-F559912D0360}";
    private const string SdkGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";
    private const string CsharpGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
    private const string FolderGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

    private const string Nfproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
        </Project>
        """;

    // A classic .sln referencing one .nfproj and one ordinary .csproj, plus a
    // solution-folder entry (which must not be treated as a project).
    private static string ClassicSln(string nfprojRel = "Foo.nfproj") => $$"""
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{{FolderGuid}}") = "src", "src", "{AAAAAAAA-0000-0000-0000-0000000000FF}"
        EndProject
        Project("{{LegacyGuid}}") = "Foo", "{{nfprojRel}}", "{AAAAAAAA-0000-0000-0000-000000000001}"
        EndProject
        Project("{{CsharpGuid}}") = "Bar", "Bar\Bar.csproj", "{BBBBBBBB-0000-0000-0000-000000000002}"
        EndProject
        Global
        EndGlobal
        """;

    // An .slnx referencing two .nfproj (one nested under a folder) and one .csproj.
    private static string Slnx() => """
        <Solution>
          <Folder Name="/src/">
            <Project Path="Foo/Foo.nfproj" />
          </Folder>
          <Project Path="Bar/Bar.nfproj" />
          <Project Path="Baz/Baz.csproj" />
        </Solution>
        """;

    // ---- SolutionFile parsing -------------------------------------------------

    [Fact]
    public void Parse_classic_sln_yields_project_paths_and_format()
    {
        using var dir = new TempDir();
        var sln = dir.File("App.sln", ClassicSln());

        var parsed = SolutionFile.Load(sln);

        Assert.Equal(SolutionFormat.Classic, parsed.Format);
        // The solution folder ("src") is excluded; the two real projects remain.
        Assert.Equal(2, parsed.ProjectPaths.Count);
        Assert.Contains(parsed.ProjectPaths, p => p.EndsWith("Foo.nfproj"));
        Assert.Contains(parsed.ProjectPaths, p => p.EndsWith("Bar.csproj"));
        Assert.All(parsed.ProjectPaths, p => Assert.True(Path.IsPathFullyQualified(p)));
    }

    [Fact]
    public void Parse_slnx_yields_project_paths_and_format()
    {
        using var dir = new TempDir();
        var slnx = dir.File("App.slnx", Slnx());

        var parsed = SolutionFile.Load(slnx);

        Assert.Equal(SolutionFormat.Xml, parsed.Format);
        Assert.Equal(3, parsed.ProjectPaths.Count);
        Assert.Contains(parsed.ProjectPaths, p => p.EndsWith("Foo.nfproj"));   // nested under <Folder>
        Assert.Contains(parsed.ProjectPaths, p => p.EndsWith("Bar.nfproj"));
        Assert.Contains(parsed.ProjectPaths, p => p.EndsWith("Baz.csproj"));
    }

    [Fact]
    public void NanoProjects_returns_only_nfproj()
    {
        using var dir = new TempDir();
        var classic = SolutionFile.Load(dir.File("A.sln", ClassicSln()));
        var slnx = SolutionFile.Load(dir.File("B.slnx", Slnx()));

        Assert.Single(classic.NanoProjects());
        Assert.All(classic.NanoProjects(), p => Assert.EndsWith(".nfproj", p));

        Assert.Equal(2, slnx.NanoProjects().Count);
        Assert.All(slnx.NanoProjects(), p => Assert.EndsWith(".nfproj", p));
    }

    // ---- SolutionScanner ------------------------------------------------------

    [Fact]
    public void Scanner_finds_both_sln_and_slnx_recursively()
    {
        using var dir = new TempDir();
        dir.File("Top.sln", ClassicSln());
        dir.File(Path.Combine("nested", "Inner.slnx"), Slnx());
        dir.File("readme.txt", "not a solution");

        var found = SolutionScanner.Find(dir.Path);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, p => p.EndsWith("Top.sln"));
        Assert.Contains(found, p => p.EndsWith("Inner.slnx"));
    }

    // ---- SolutionRewriter: classic .sln --------------------------------------

    [Fact]
    public void Rewriter_classic_retargets_path_and_guid_for_converted_only()
    {
        using var dir = new TempDir();
        var sln = dir.File("App.sln", ClassicSln());
        var converted = new[] { dir.Combine("Foo.nfproj") };

        var changed = SolutionRewriter.RewriteFile(SolutionFile.Load(sln), converted);
        var updated = File.ReadAllText(sln);

        Assert.True(changed);
        Assert.Contains("\"Foo.csproj\"", updated);
        Assert.DoesNotContain("Foo.nfproj", updated);
        Assert.Contains(SdkGuid, updated);
        Assert.DoesNotContain(LegacyGuid, updated);
        // The unrelated Bar.csproj entry is untouched.
        Assert.Contains("Bar\\Bar.csproj", updated);
    }

    [Fact]
    public void Rewriter_classic_is_a_noop_on_rerun()
    {
        using var dir = new TempDir();
        var sln = dir.File("App.sln", ClassicSln());
        var converted = new[] { dir.Combine("Foo.nfproj") };

        SolutionRewriter.RewriteFile(SolutionFile.Load(sln), converted);
        var afterFirst = File.ReadAllText(sln);

        // Second pass: the solution already points at Foo.csproj, so no change.
        var changed = SolutionRewriter.RewriteFile(SolutionFile.Load(sln), converted);

        Assert.False(changed);
        Assert.Equal(afterFirst, File.ReadAllText(sln));
    }

    [Fact]
    public void Rewriter_classic_leaves_unconverted_nfproj_alone()
    {
        using var dir = new TempDir();
        // Two .nfproj in the same solution; only one is in the converted set.
        var text = $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{{LegacyGuid}}") = "Foo", "Foo.nfproj", "{AAAAAAAA-0000-0000-0000-000000000001}"
            EndProject
            Project("{{LegacyGuid}}") = "Keep", "Keep.nfproj", "{AAAAAAAA-0000-0000-0000-000000000003}"
            EndProject
            Global
            EndGlobal
            """;
        var sln = dir.File("App.sln", text);

        SolutionRewriter.RewriteFile(SolutionFile.Load(sln), new[] { dir.Combine("Foo.nfproj") });
        var updated = File.ReadAllText(sln);

        Assert.Contains("\"Foo.csproj\"", updated);
        Assert.Contains("\"Keep.nfproj\"", updated);     // untouched
        Assert.Contains(SdkGuid, updated);               // Foo flipped
        Assert.Contains(LegacyGuid, updated);            // Keep's legacy GUID survives
    }

    // ---- SolutionRewriter: .slnx ---------------------------------------------

    [Fact]
    public void Rewriter_slnx_retargets_path_only_for_converted_only()
    {
        using var dir = new TempDir();
        var slnx = dir.File("App.slnx", Slnx());
        // Convert only the nested Foo.nfproj; leave Bar.nfproj as-is.
        var converted = new[] { dir.Combine(Path.Combine("Foo", "Foo.nfproj")) };

        var changed = SolutionRewriter.RewriteFile(SolutionFile.Load(slnx), converted);
        var updated = File.ReadAllText(slnx);

        Assert.True(changed);
        Assert.Contains("Foo/Foo.csproj", updated);
        Assert.DoesNotContain("Foo/Foo.nfproj", updated);
        Assert.Contains("Bar/Bar.nfproj", updated);      // not in the converted set
        // .slnx is path-based: no project-type GUID is ever introduced.
        Assert.DoesNotContain(SdkGuid, updated);
        Assert.DoesNotContain(LegacyGuid, updated);
    }

    [Fact]
    public void Rewriter_slnx_is_a_noop_on_rerun()
    {
        using var dir = new TempDir();
        var slnx = dir.File("App.slnx", Slnx());
        var converted = new[]
        {
            dir.Combine(Path.Combine("Foo", "Foo.nfproj")),
            dir.Combine(Path.Combine("Bar", "Bar.nfproj")),
        };

        SolutionRewriter.RewriteFile(SolutionFile.Load(slnx), converted);
        var afterFirst = File.ReadAllText(slnx);

        var changed = SolutionRewriter.RewriteFile(SolutionFile.Load(slnx), converted);

        Assert.False(changed);
        Assert.Equal(afterFirst, File.ReadAllText(slnx));
    }

    // ---- Round-trip via the SolutionPersistence library ----------------------

    [Fact]
    public void Roundtrip_classic_sln_retarget_then_reparse_sees_csproj()
    {
        using var dir = new TempDir();
        var sln = dir.File("App.sln", ClassicSln("Foo\\Foo.nfproj"));
        var converted = new[] { dir.Combine(Path.Combine("Foo", "Foo.nfproj")) };

        // Retarget the converted project, then re-read the solution through the
        // library: the entry now resolves to the .csproj (not the .nfproj).
        Assert.True(SolutionRewriter.RewriteFile(SolutionFile.Load(sln), converted));

        var reparsed = SolutionFile.Load(sln);
        Assert.Contains(reparsed.ProjectPaths, p => p.EndsWith("Foo.csproj"));
        Assert.DoesNotContain(reparsed.ProjectPaths, p => p.EndsWith("Foo.nfproj"));
        Assert.Empty(reparsed.NanoProjects());
    }

    [Fact]
    public void Roundtrip_slnx_retarget_then_reparse_sees_csproj()
    {
        using var dir = new TempDir();
        var slnx = dir.File("App.slnx", Slnx());
        var converted = new[]
        {
            dir.Combine(Path.Combine("Foo", "Foo.nfproj")),
            dir.Combine(Path.Combine("Bar", "Bar.nfproj")),
        };

        Assert.True(SolutionRewriter.RewriteFile(SolutionFile.Load(slnx), converted));

        var reparsed = SolutionFile.Load(slnx);
        // Both converted .nfproj now resolve to .csproj; the unrelated Baz.csproj stays.
        Assert.Contains(reparsed.ProjectPaths, p => p.EndsWith("Foo.csproj"));
        Assert.Contains(reparsed.ProjectPaths, p => p.EndsWith("Bar.csproj"));
        Assert.Contains(reparsed.ProjectPaths, p => p.EndsWith("Baz.csproj"));
        Assert.Empty(reparsed.NanoProjects());
    }

    [Fact]
    public void Slnx_with_explicit_nfproj_type_attribute_parses()
    {
        using var dir = new TempDir();
        // A .slnx as Visual Studio writes it for a flavored project: the .nfproj
        // carries the nanoFramework project-type GUID as the Type attribute.
        var slnx = dir.File("App.slnx", $$"""
            <Solution>
              <Project Path="Foo/Foo.nfproj" Type="{{LegacyGuid}}" />
              <Project Path="Bar/Bar.csproj" />
            </Solution>
            """);

        var parsed = SolutionFile.Load(slnx);

        Assert.Equal(SolutionFormat.Xml, parsed.Format);
        Assert.Equal(2, parsed.ProjectPaths.Count);
        Assert.Single(parsed.NanoProjects());
        Assert.EndsWith("Foo.nfproj", parsed.NanoProjects()[0]);
    }

    // ---- SolutionDiscovery ----------------------------------------------------

    [Fact]
    public void Discovery_returns_solutions_referencing_target_and_excludes_unrelated()
    {
        using var dir = new TempDir();
        // Solution A references Foo.nfproj; solution B references an unrelated project.
        var aText = $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{{LegacyGuid}}") = "Foo", "Foo\Foo.nfproj", "{AAAAAAAA-0000-0000-0000-000000000001}"
            EndProject
            Global
            EndGlobal
            """;
        var bText = $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{{LegacyGuid}}") = "Other", "Other\Other.nfproj", "{AAAAAAAA-0000-0000-0000-000000000002}"
            EndProject
            Global
            EndGlobal
            """;
        dir.File("A.sln", aText);
        dir.File("B.sln", bText);
        dir.File(Path.Combine("Foo", "Foo.nfproj"), Nfproj);

        var target = dir.Combine(Path.Combine("Foo", "Foo.nfproj"));
        var matches = SolutionDiscovery.FindReferencing(dir.Path, new[] { target });

        Assert.Single(matches);
        Assert.EndsWith("A.sln", matches[0].Path);
    }

    // ---- MigrationPlanner -----------------------------------------------------

    [Fact]
    public void Planner_solution_input_yields_that_solutions_projects()
    {
        using var dir = new TempDir();
        var slnx = dir.File("App.slnx", Slnx());
        dir.File(Path.Combine("Foo", "Foo.nfproj"), Nfproj);
        dir.File(Path.Combine("Bar", "Bar.nfproj"), Nfproj);

        var plan = MigrationPlanner.Plan(slnx, explicitSolution: null, glob: null);

        Assert.Equal(PlanKind.ExplicitSolution, plan.Kind);
        Assert.False(plan.RequiresSelection);
        var projects = MigrationPlan.ProjectsOf(plan.Candidates);
        Assert.Equal(2, projects.Count);
        Assert.All(projects, p => Assert.EndsWith(".nfproj", p));
    }

    [Fact]
    public void Planner_explicit_solution_option_overrides_directory()
    {
        using var dir = new TempDir();
        var sln = dir.File("App.sln", ClassicSln("Foo\\Foo.nfproj"));
        dir.File(Path.Combine("Foo", "Foo.nfproj"), Nfproj);

        var plan = MigrationPlanner.Plan(dir.Path, explicitSolution: sln, glob: null);

        Assert.Equal(PlanKind.ExplicitSolution, plan.Kind);
        Assert.Single(plan.Candidates);
        Assert.EndsWith("App.sln", plan.Candidates[0].Solution.Path);
    }

    [Fact]
    public void Planner_directory_with_solutions_lists_candidates()
    {
        using var dir = new TempDir();
        dir.File("One.sln", ClassicSln("Foo\\Foo.nfproj"));
        dir.File(Path.Combine("sub", "Two.slnx"), """
            <Solution><Project Path="Quux/Quux.nfproj" /></Solution>
            """);
        dir.File(Path.Combine("Foo", "Foo.nfproj"), Nfproj);
        dir.File(Path.Combine("sub", "Quux", "Quux.nfproj"), Nfproj);

        var plan = MigrationPlanner.Plan(dir.Path, explicitSolution: null, glob: null);

        Assert.Equal(PlanKind.DirectoryWithSolutions, plan.Kind);
        Assert.True(plan.RequiresSelection);
        Assert.Equal(2, plan.Candidates.Count);
    }

    [Fact]
    public void Planner_directory_without_solutions_falls_back_to_loose()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("a", "One.nfproj"), Nfproj);
        dir.File(Path.Combine("b", "Two.nfproj"), Nfproj);

        var plan = MigrationPlanner.Plan(dir.Path, explicitSolution: null, glob: null);

        Assert.Equal(PlanKind.LooseDirectory, plan.Kind);
        Assert.False(plan.RequiresSelection);
        Assert.Equal(2, plan.LooseProjects.Count);
        Assert.Empty(plan.Candidates);
    }

    [Fact]
    public void Planner_glob_yields_only_affected_solutions()
    {
        using var dir = new TempDir();
        // A.sln references Foo.nfproj (matched); B.sln references Bar.nfproj (not).
        dir.File("A.sln", $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{{LegacyGuid}}") = "Foo", "Foo\Foo.nfproj", "{AAAAAAAA-0000-0000-0000-000000000001}"
            EndProject
            Global
            EndGlobal
            """);
        dir.File("B.sln", $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{{LegacyGuid}}") = "Bar", "Bar\Bar.nfproj", "{AAAAAAAA-0000-0000-0000-000000000002}"
            EndProject
            Global
            EndGlobal
            """);
        dir.File(Path.Combine("Foo", "Foo.nfproj"), Nfproj);
        dir.File(Path.Combine("Bar", "Bar.nfproj"), Nfproj);

        var plan = MigrationPlanner.Plan(dir.Path, explicitSolution: null, glob: "Foo/**");

        Assert.Equal(PlanKind.GlobScoped, plan.Kind);
        Assert.True(plan.RequiresSelection);
        Assert.Single(plan.Candidates);
        Assert.EndsWith("A.sln", plan.Candidates[0].Solution.Path);
        Assert.Single(plan.Candidates[0].NanoProjects);
        Assert.EndsWith("Foo.nfproj", plan.Candidates[0].NanoProjects[0]);
    }

    [Fact]
    public void Planner_single_nfproj_is_not_solution_scoped()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Solo.nfproj", Nfproj);

        var plan = MigrationPlanner.Plan(nfproj, explicitSolution: null, glob: null);

        Assert.Equal(PlanKind.SingleProject, plan.Kind);
        Assert.False(plan.RequiresSelection);
        Assert.Single(plan.LooseProjects);
    }
}
