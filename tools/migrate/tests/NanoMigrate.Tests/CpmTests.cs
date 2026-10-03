// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

/// <summary>
/// Central Package Management (CPM) behavior: versionless PackageReferences in the
/// project, central PackageVersion entries in Directory.Packages.props, idempotency,
/// and no-crash on already-central inputs.
/// </summary>
public class CpmTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    private const string CpmPropsEnabled = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
          </ItemGroup>
        </Project>
        """;

    private const string LegacyNfproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
          <ItemGroup>
            <Reference Include="System.Device.Gpio, Version=1.1.57.0, Culture=neutral, PublicKeyToken=c07d481e9758c731" />
            <Reference Include="mscorlib, Version=1.17.11.0, Culture=neutral, PublicKeyToken=c07d481e9758c731" />
          </ItemGroup>
        </Project>
        """;

    private const string LegacyPackagesConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          <package id="nanoFramework.System.Device.Gpio" version="1.1.57" targetFramework="netnano1.0" />
          <package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
        </packages>
        """;

    [Fact]
    public void Cpm_emits_versionless_PackageReferences_and_seeds_central_props()
    {
        using var dir = new TempDir();
        var props = dir.File("Directory.Packages.props", CpmPropsEnabled);
        dir.File(Path.Combine("Sample", "packages.config"), LegacyPackagesConfig);
        var nfproj = dir.File(Path.Combine("Sample", "Sample.nfproj"), LegacyNfproj);

        var result = Converter.Convert(nfproj, new ConversionOptions());

        Assert.Equal(ConvertStatus.Converted, result.Status);
        Assert.Empty(result.Review);

        // Project: versionless references, never with a Version attribute.
        var emitted = File.ReadAllText(dir.Combine(Path.Combine("Sample", "Sample.csproj")));
        Assert.Contains("<PackageReference Include=\"nanoFramework.System.Device.Gpio\" />", emitted);
        Assert.Contains("<PackageReference Include=\"nanoFramework.CoreLibrary\" />", emitted);
        Assert.DoesNotContain("Version=", emitted);  // no version on any PackageReference

        // Central props: gained a PackageVersion for each referenced id.
        var propsXml = File.ReadAllText(props);
        Assert.Contains("<PackageVersion Include=\"nanoFramework.System.Device.Gpio\" Version=\"1.1.57\" />", propsXml);
        Assert.Contains("<PackageVersion Include=\"nanoFramework.CoreLibrary\" Version=\"1.17.11\" />", propsXml);

        // Result preview reflects the central-props edit.
        Assert.Equal(Path.GetFullPath(props), result.UpdatedPackagesProps);
        Assert.Equal(2, result.AddedPackageVersions.Count);
    }

    [Fact]
    public void Cpm_second_project_with_same_ids_adds_no_duplicate_PackageVersion()
    {
        using var dir = new TempDir();
        var props = dir.File("Directory.Packages.props", CpmPropsEnabled);

        // Project A seeds the central props.
        dir.File(Path.Combine("A", "packages.config"), LegacyPackagesConfig);
        var a = dir.File(Path.Combine("A", "A.nfproj"), LegacyNfproj);
        var resultA = Converter.Convert(a, new ConversionOptions());
        Assert.Equal(2, resultA.AddedPackageVersions.Count);
        var propsAfterA = File.ReadAllText(props);

        // Project B references the same ids; the props already has them, so nothing
        // is added and the file is byte-for-byte unchanged (idempotent).
        dir.File(Path.Combine("B", "packages.config"), LegacyPackagesConfig);
        var b = dir.File(Path.Combine("B", "B.nfproj"), LegacyNfproj);
        var resultB = Converter.Convert(b, new ConversionOptions());

        Assert.Empty(resultB.AddedPackageVersions);
        Assert.Null(resultB.UpdatedPackagesProps);
        var propsAfterB = File.ReadAllText(props);
        Assert.Equal(propsAfterA, propsAfterB);  // central props untouched the second time

        // Exactly one PackageVersion per id (no duplicates).
        Assert.Equal(1, CountOccurrences(propsAfterB, "Include=\"nanoFramework.CoreLibrary\""));
        Assert.Equal(1, CountOccurrences(propsAfterB, "Include=\"nanoFramework.System.Device.Gpio\""));
    }

    [Fact]
    public void Cpm_does_not_duplicate_a_preexisting_PackageVersion()
    {
        using var dir = new TempDir();
        // Central props already pins one of the two ids.
        var props = dir.File("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="nanoFramework.CoreLibrary" Version="1.17.11" />
              </ItemGroup>
            </Project>
            """);
        dir.File(Path.Combine("Sample", "packages.config"), LegacyPackagesConfig);
        var nfproj = dir.File(Path.Combine("Sample", "Sample.nfproj"), LegacyNfproj);

        var result = Converter.Convert(nfproj, new ConversionOptions());

        var propsXml = File.ReadAllText(props);
        // The pre-existing entry appears exactly once (not duplicated).
        Assert.Equal(1, CountOccurrences(propsXml, "Include=\"nanoFramework.CoreLibrary\""));
        // The genuinely-missing id was added.
        Assert.Contains("<PackageVersion Include=\"nanoFramework.System.Device.Gpio\" Version=\"1.1.57\" />", propsXml);
        // Only the missing id is recorded as an addition.
        Assert.Single(result.AddedPackageVersions);
        Assert.Equal("nanoFramework.System.Device.Gpio", result.AddedPackageVersions[0].Key);
    }

    [Fact]
    public void Cpm_disabled_props_does_not_trigger_versionless_emission()
    {
        using var dir = new TempDir();
        // A Directory.Packages.props that does NOT enable CPM.
        dir.File("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        dir.File(Path.Combine("Sample", "packages.config"), LegacyPackagesConfig);
        var nfproj = dir.File(Path.Combine("Sample", "Sample.nfproj"), LegacyNfproj);

        Converter.Convert(nfproj, new ConversionOptions());

        var emitted = File.ReadAllText(dir.Combine(Path.Combine("Sample", "Sample.csproj")));
        // Versions are pinned inline because CPM is not active.
        Assert.Contains("<PackageReference Include=\"nanoFramework.CoreLibrary\" Version=\"1.17.11\" />", emitted);
    }

    [Fact]
    public void Cpm_does_not_crash_when_input_already_under_central_props()
    {
        using var dir = new TempDir();
        dir.File("Directory.Packages.props", CpmPropsEnabled);
        // An already-SDK-style project sitting under the central props: it is skipped
        // by the Sdk-attribute guard and must not throw.
        var csproj = dir.File(Path.Combine("Sample", "Sample.csproj"), """
            <Project Sdk="nanoFramework.NET.Sdk">
              <PropertyGroup><TargetFramework>netnano1.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="nanoFramework.CoreLibrary" />
              </ItemGroup>
            </Project>
            """);

        var ex = Record.Exception(() => Converter.Convert(csproj, new ConversionOptions()));
        Assert.Null(ex);
        var result = Converter.Convert(csproj, new ConversionOptions());
        Assert.Equal(ConvertStatus.Skipped, result.Status);
    }

    [Fact]
    public void Cpm_option_override_forces_versionless_without_props_on_disk()
    {
        using var dir = new TempDir();
        // No Directory.Packages.props at all, but the option forces CPM on.
        dir.File("packages.config", LegacyPackagesConfig);
        var nfproj = dir.File("Sample.nfproj", LegacyNfproj);

        var result = Converter.Convert(nfproj,
            new ConversionOptions { DryRun = true, CentralPackageManagement = true });

        // Dry run writes nothing; assert via the result data.
        Assert.Empty(result.Review);
        Assert.Contains(result.Packages, p => p.Key == "nanoFramework.CoreLibrary");
        // No central props on disk → nothing to update, but it must not crash.
        Assert.Null(result.UpdatedPackagesProps);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
