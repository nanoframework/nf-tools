// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace NanoFramework.Migrate.Tests;

/// <summary>
/// Tests for the packages.config-first resolution strategy and Central Package
/// Management (CPM) support added to <see cref="ProjectConverter"/>.
/// </summary>
public class PackageResolutionTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    // A WiFiAP-like project whose <Reference> elements are bare assembly names that
    // routinely differ from the NuGet package id, while packages.config carries the
    // real nano package ids+versions.
    private const string WiFiApLikeNfproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <AssemblyName>WifiAP</AssemblyName>
            <RootNamespace>WifiAP</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="System.Device.Gpio, Version=1.1.57.0, Culture=neutral, PublicKeyToken=c07d481e9758c731" />
            <Reference Include="System.Net.Http, Version=1.5.207.0, Culture=neutral, PublicKeyToken=c07d481e9758c731" />
            <Reference Include="System.Threading, Version=1.1.52.34401, Culture=neutral, PublicKeyToken=c07d481e9758c731" />
            <Reference Include="mscorlib, Version=1.17.11.0, Culture=neutral, PublicKeyToken=c07d481e9758c731" />
          </ItemGroup>
        </Project>
        """;

    private const string WiFiApLikePackagesConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          <package id="nanoFramework.System.Device.Gpio" version="1.1.57" targetFramework="netnano1.0" />
          <package id="nanoFramework.System.Net.Http.Server" version="1.5.207" targetFramework="netnano1.0" />
          <package id="nanoFramework.System.Threading" version="1.1.52" targetFramework="netnano1.0" />
          <package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
        </packages>
        """;

    [Fact]
    public void PackagesConfig_is_authoritative_emits_exactly_its_ids_and_versions()
    {
        using var dir = new TempDir();
        dir.File("packages.config", WiFiApLikePackagesConfig);
        var nfproj = dir.File("WifiAP.nfproj", WiFiApLikeNfproj);

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Equal(ConvertStatus.Converted, result.Status);
        Assert.Empty(result.Review);                                   // no assembly/id mismatch flags

        // The emitted package set is exactly the packages.config entries, verbatim.
        var expected = new Dictionary<string, string>
        {
            ["nanoFramework.System.Device.Gpio"]      = "1.1.57",
            ["nanoFramework.System.Net.Http.Server"]  = "1.5.207",
            ["nanoFramework.System.Threading"]        = "1.1.52",
            ["nanoFramework.CoreLibrary"]             = "1.17.11",
        };
        Assert.Equal(expected.Count, result.Packages.Count);
        foreach (var kv in expected)
            Assert.Contains(result.Packages, p => p.Key == kv.Key && p.Value == kv.Value);

        // The bare assembly names from <Reference> never become packages.
        Assert.DoesNotContain(result.Packages, p => p.Key == "System.Device.Gpio");
        Assert.DoesNotContain(result.Packages, p => p.Key == "System.Net.Http");
        Assert.DoesNotContain(result.Packages, p => p.Key == "System.Threading");
        Assert.DoesNotContain(result.Packages, p => p.Key == "mscorlib");
    }

    [Fact]
    public void PackagesConfig_first_emits_no_Reference_elements()
    {
        using var dir = new TempDir();
        dir.File("packages.config", WiFiApLikePackagesConfig);
        var nfproj = dir.File("WifiAP.nfproj", WiFiApLikeNfproj);

        Converter.Convert(nfproj, new ConversionOptions());  // real run

        var emitted = File.ReadAllText(dir.Combine("WifiAP.csproj"));
        Assert.DoesNotContain("<Reference ", emitted);                 // no legacy references left
        Assert.Contains("PackageReference Include=\"nanoFramework.System.Net.Http.Server\" Version=\"1.5.207\"", emitted);
        Assert.DoesNotContain("Version=\"\"", emitted);
    }

    [Fact]
    public void No_packages_config_falls_back_to_hintpath()
    {
        using var dir = new TempDir();
        // No packages.config — resolution must come from the HintPath folder.
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Reference Include="System.Device.Gpio">
                  <HintPath>..\packages\nanoFramework.System.Device.Gpio.1.1.57\lib\System.Device.Gpio.dll</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Empty(result.Review);
        Assert.Contains(result.Packages,
            p => p.Key == "nanoFramework.System.Device.Gpio" && p.Value == "1.1.57");
    }

    [Fact]
    public void No_packages_config_unresolved_reference_is_flagged()
    {
        using var dir = new TempDir();
        // No packages.config and no HintPath: unresolvable → review, never blank version.
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Reference Include="MysteryLib" />
              </ItemGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions());

        Assert.Equal(ConvertStatus.Review, result.Status);
        Assert.Contains(result.Review, r => r.Contains("MysteryLib"));
        var emitted = File.ReadAllText(dir.Combine("Sample.csproj"));
        Assert.DoesNotContain("Version=\"\"", emitted);
    }

    [Fact]
    public void ProjectCapability_TestContainer_is_preserved_not_flagged()
    {
        using var dir = new TempDir();
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
              <package id="nanoFramework.TestFramework" version="3.0.80" targetFramework="netnano1.0" />
            </packages>
            """);
        var nfproj = dir.File("NFUnitTest.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <ProjectCapability Include="TestContainer" />
              </ItemGroup>
              <PropertyGroup>
                <AssemblyName>NFUnitTest</AssemblyName>
                <IsTestProject>true</IsTestProject>
                <TestProjectType>UnitTest</TestProjectType>
              </PropertyGroup>
            </Project>
            """);

        Converter.Convert(nfproj, new ConversionOptions());

        var emitted = File.ReadAllText(dir.Combine("NFUnitTest.csproj"));
        Assert.Contains("<ProjectCapability Include=\"TestContainer\" />", emitted);
        Assert.Contains("<IsTestProject>true</IsTestProject>", emitted);
        Assert.Contains("<TestProjectType>UnitTest</TestProjectType>", emitted);
    }
}
