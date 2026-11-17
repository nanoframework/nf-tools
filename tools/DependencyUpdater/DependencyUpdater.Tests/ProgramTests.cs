//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace DependencyUpdater.Tests;

[TestClass]
public class ProgramTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanodu-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_testDirectory, true);
    }

    [TestMethod]
    [DataRow("Library.sln")]
    [DataRow("Library.SLN")]
    [DataRow("Library.slnx")]
    [DataRow("Library.SLNX")]
    public void ResolveSolutionTargets_Should_SelectOnlyTopLevelSolution(string solutionName)
    {
        string solutionPath = Path.Combine(_testDirectory, solutionName);
        File.WriteAllText(solutionPath, "");
        File.WriteAllText(Path.Combine(_testDirectory, "Ignored.slnx.bak"), "");
        string nestedDirectory = Directory.CreateDirectory(Path.Combine(_testDirectory, "nested")).FullName;
        File.WriteAllText(Path.Combine(nestedDirectory, solutionName), "");
        File.WriteAllText(Path.Combine(nestedDirectory, "Other.sln"), "");

        var result = nanoFramework.Tools.DependencyUpdater.Program.ResolveSolutionTargets(
            _testDirectory, false, false, null!);

        Assert.IsTrue(result.SolutionsToCheck);
        CollectionAssert.AreEqual(new[] { solutionPath }, result.Targets);
        CollectionAssert.AreEqual(new[] { solutionPath },
            nanoFramework.Tools.DependencyUpdater.Program.CollectSolutions(_testDirectory, result.Targets));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResolveSolutionTargets_Should_FailWhenNoTopLevelSolutionsExist(bool hasNestedSolution)
    {
        if (hasNestedSolution)
        {
            string nestedDirectory = Directory.CreateDirectory(Path.Combine(_testDirectory, "nested")).FullName;
            File.WriteAllText(Path.Combine(nestedDirectory, "Library.sln"), "");
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            nanoFramework.Tools.DependencyUpdater.Program.ResolveSolutionTargets(
                _testDirectory, false, false, Array.Empty<string>()));

        StringAssert.Contains(exception.Message, "No solution found");
        StringAssert.Contains(exception.Message, "--solutions-to-check");
        StringAssert.Contains(exception.Message, "--repos-to-update");
    }

    [TestMethod]
    [DataRow("First.sln", "Second.sln")]
    [DataRow("First.slnx", "Second.slnx")]
    [DataRow("Library.sln", "Library.slnx")]
    public void ResolveSolutionTargets_Should_FailWhenMultipleSolutionsExist(string first, string second)
    {
        File.WriteAllText(Path.Combine(_testDirectory, first), "");
        File.WriteAllText(Path.Combine(_testDirectory, second), "");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            nanoFramework.Tools.DependencyUpdater.Program.ResolveSolutionTargets(
                _testDirectory, false, false, Array.Empty<string>()));

        StringAssert.Contains(exception.Message, "Multiple solution files");
        StringAssert.Contains(exception.Message, first);
        StringAssert.Contains(exception.Message, second);
        StringAssert.Contains(exception.Message, "--solutions-to-check");
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void ResolveSolutionTargets_Should_PreserveExplicitWorkflow(bool solutionsToCheck, bool reposToUpdate)
    {
        var result = nanoFramework.Tools.DependencyUpdater.Program.ResolveSolutionTargets(
            Path.Combine(_testDirectory, "DoesNotExist"), solutionsToCheck, reposToUpdate,
            new[] { "ExplicitTarget", "", null! });

        Assert.AreEqual(solutionsToCheck, result.SolutionsToCheck);
        CollectionAssert.AreEqual(new[] { "ExplicitTarget" }, result.Targets);
    }

    [TestMethod]
    [DataRow("Library.sln")]
    [DataRow("Library.slnx")]
    [DataRow("*.sln")]
    [DataRow("*.slnx")]
    public void CollectSolutions_Should_PreserveExplicitRecursiveSearch(string target)
    {
        string nestedDirectory = Directory.CreateDirectory(Path.Combine(_testDirectory, "nested")).FullName;
        string solutionPath = Path.Combine(nestedDirectory, $"Library{Path.GetExtension(target)}");
        File.WriteAllText(solutionPath, "");
        File.WriteAllText(Path.Combine(nestedDirectory, "Ignored.txt"), "");

        CollectionAssert.AreEqual(new[] { solutionPath },
            nanoFramework.Tools.DependencyUpdater.Program.CollectSolutions(_testDirectory, new[] { target }));
    }

    [TestMethod]
    [DataRow("Library.nfproj", "")]
    [DataRow("src/Library.nfproj", "src")]
    [DataRow(@"src\Library.nfproj", "src")]
    [DataRow("src/../Library.nfproj", "")]
    public void FindProjectForPackageConfig_Should_ResolveSlnxProjectPaths(string projectPath, string projectDirectory)
    {
        string solutionContent = $"""
            <Solution>
              <Folder Name="/Libraries/">
                <Project Path="{projectPath}" />
              </Folder>
              <Project Path="Other/Other.csproj" />
            </Solution>
            """;

        var result = nanoFramework.Tools.DependencyUpdater.Program.FindProjectForPackageConfig(
            Path.Combine(_testDirectory, "Library.slnx"), solutionContent,
            Path.Combine(_testDirectory, projectDirectory, "packages.config"));

        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(Path.Combine(_testDirectory, projectDirectory, "Library.nfproj"), result.Value.Path);
        Assert.AreEqual("Library", result.Value.Name);
    }

    [TestMethod]
    [DataRow("Unrelated")]
    [DataRow("Other")]
    public void FindProjectForPackageConfig_Should_IgnoreProjectsNotInSlnx(string projectDirectory)
    {
        const string solutionContent = """
            <Solution>
              <Project Path="src/Library.nfproj" />
              <Project Path="Other/Other.csproj" />
            </Solution>
            """;

        var result = nanoFramework.Tools.DependencyUpdater.Program.FindProjectForPackageConfig(
            Path.Combine(_testDirectory, "Library.slnx"), solutionContent,
            Path.Combine(_testDirectory, projectDirectory, "packages.config"));

        Assert.IsNull(result);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("src.test")]
    public void FindProjectForPackageConfig_Should_PreserveLegacySlnMatching(string projectDirectory)
    {
        string projectPath = Path.Combine(projectDirectory, "Library.nfproj");
        string fullProjectPath = Path.Combine(_testDirectory, projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullProjectPath)!);
        File.WriteAllText(fullProjectPath, "");
        string solutionContent = $"Project(\"{{GUID}}\") = \"DisplayName\", \"{projectPath}\", \"{{GUID}}\"";

        var result = nanoFramework.Tools.DependencyUpdater.Program.FindProjectForPackageConfig(
            Path.Combine(_testDirectory, "Library.sln"), solutionContent,
            Path.Combine(_testDirectory, projectDirectory, "packages.config"));

        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(fullProjectPath, result.Value.Path);
        Assert.AreEqual("DisplayName", result.Value.Name);
    }

    [TestMethod]
    [DataRow("Microsoft.Build.Tasks.Git", true)]
    [DataRow("Microsoft.SourceLink.Common", true)]
    [DataRow("Microsoft.SourceLink.GitHub", true)]
    [DataRow("microsoft.sourcelink.github", true)]
    [DataRow("Nerdbank.GitVersioning", false)]
    [DataRow("Microsoft.SourceLink.GitLab", false)]
    [DataRow("Microsoft.SourceLink.GitHub.Custom", false)]
    [DataRow("nanoFramework.CoreLibrary", false)]
    public void IsSourceLinkPackage_Should_OnlyMatchCappedPackages(string packageName, bool expectedResult)
    {
        Assert.AreEqual(expectedResult,
            nanoFramework.Tools.DependencyUpdater.Program.IsSourceLinkPackage(packageName));
    }

    [TestMethod]
    [DataRow("Microsoft.Build.Tasks.Git")]
    [DataRow("Microsoft.SourceLink.Common")]
    [DataRow("Microsoft.SourceLink.GitHub")]
    public void GetLatestSourceLinkVersion_Should_SelectLatestStable10Release(string packageName)
    {
        var versions = new[] { "11.0.0", "10.2.0", "10.12.0-preview.1", "9.99.0", "10.10.0", "10.1.0" };

        Assert.AreEqual("10.10.0",
            nanoFramework.Tools.DependencyUpdater.Program.GetLatestSourceLinkVersion(packageName, versions));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("11.0.0;9.0.0")]
    [DataRow("10.0.0-preview.1")]
    public void GetLatestSourceLinkVersion_Should_FailWhenNoStable10ReleaseExists(string versionList)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            nanoFramework.Tools.DependencyUpdater.Program.GetLatestSourceLinkVersion(
                "Microsoft.SourceLink.GitHub", versionList.Split(';', StringSplitOptions.RemoveEmptyEntries)));

        StringAssert.Contains(exception.Message, "No stable 10.x version of Microsoft.SourceLink.GitHub");
    }

    [TestMethod]
    [DataRow("Microsoft.Build.Tasks.Git", "11.0.0", "10.2.0", false, true)]
    [DataRow("Microsoft.SourceLink.Common", "11.0.0", "10.2.0", true, true)]
    [DataRow("Microsoft.SourceLink.GitHub", "11.0.0", "10.2.0", false, true)]
    [DataRow("Microsoft.SourceLink.GitHub", "10.1.0", "10.2.0", false, true)]
    [DataRow("Microsoft.SourceLink.GitHub", "10.2.0", "10.2.0", false, false)]
    [DataRow("Nerdbank.GitVersioning", "3.5.0", "3.6.0", false, true)]
    [DataRow("Nerdbank.GitVersioning", "3.5.0", "3.6.0", true, true)]
    [DataRow("Nerdbank.GitVersioning", "3.6.0", "3.5.0", false, false)]
    public void BuildPackageUpdateCache_Should_ApplyCapBeforeSkippingSolution(
        string packageName, string currentVersion, string latestVersion, bool stablePackages, bool updateExpected)
    {
        string configPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configPath,
                $"<packages><package id=\"{packageName}\" version=\"{currentVersion}\" targetFramework=\"netnano1.0\" /></packages>");

            var cache = nanoFramework.Tools.DependencyUpdater.Program.BuildPackageUpdateCache(
                new[] { configPath }, stablePackages, (id, includePrerelease) =>
                {
                    Assert.AreEqual(packageName, id);
                    Assert.AreEqual(!stablePackages
                        && !nanoFramework.Tools.DependencyUpdater.Program.IsSourceLinkPackage(packageName), includePrerelease);
                    return NuGet.Versioning.NuGetVersion.Parse(latestVersion);
                });

            Assert.AreEqual(updateExpected ? latestVersion : null, cache[(packageName, currentVersion)]);
            Assert.AreEqual(updateExpected, cache.Any(entry => entry.Value != null));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [TestMethod]
    [DataRow("--depth 1", "TestOwner", "TestLib", false, "", "clone --depth 1 https://github.com/TestOwner/TestLib TestLib")]
    [DataRow("--depth 1", "TestOwner", "TestLib", true, "TestPat", "clone --depth 1 https://TestPat@github.com/TestOwner/TestLib TestLib")]
    public void CreateCloneCommand_Should_ReturnValidData(string cloneDepth, string repoOwner, string library,
        bool usePatForClone, string gitHubAuth, string expectedResult)
    {
        var result =
            nanoFramework.Tools.DependencyUpdater.Program.CreateCloneCommand(cloneDepth, repoOwner, library,
                usePatForClone, gitHubAuth);
    }
}
