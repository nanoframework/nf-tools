// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

public class ProjectScannerTests
{
    private const string Stub = "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\" />";

    [Fact]
    public void ResolveProjects_returns_single_file_when_path_is_an_nfproj()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Solo.nfproj", Stub);

        var targets = ProjectScanner.ResolveProjects(nfproj, glob: null);

        Assert.Single(targets);
        Assert.Equal(Path.GetFullPath(nfproj), targets[0]);
    }

    [Fact]
    public void ResolveProjects_enumerates_directory_recursively_sorted()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("b", "Two.nfproj"), Stub);
        dir.File(Path.Combine("a", "One.nfproj"), Stub);
        dir.File("ReadMe.txt", "not a project");

        var targets = ProjectScanner.ResolveProjects(dir.Path, glob: null);

        Assert.Equal(2, targets.Count);
        Assert.EndsWith("One.nfproj", targets[0]);   // sorted: a/ before b/
        Assert.EndsWith("Two.nfproj", targets[1]);
    }

    [Fact]
    public void ResolveProjects_applies_glob_filter()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("Beginner", "Blink.nfproj"), Stub);
        dir.File(Path.Combine("Advanced", "Threads.nfproj"), Stub);

        var targets = ProjectScanner.ResolveProjects(dir.Path, glob: "Beginner/**");

        Assert.Single(targets);
        Assert.EndsWith("Blink.nfproj", targets[0]);
    }

    [Fact]
    public void ResolveProjects_returns_empty_for_missing_path()
    {
        var targets = ProjectScanner.ResolveProjects(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()), glob: null);
        Assert.Empty(targets);
    }
}
