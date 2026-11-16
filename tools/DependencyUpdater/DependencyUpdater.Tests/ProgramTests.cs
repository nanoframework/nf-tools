//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace DependencyUpdater.Tests;

[TestClass]
public class ProgramTests
{
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
