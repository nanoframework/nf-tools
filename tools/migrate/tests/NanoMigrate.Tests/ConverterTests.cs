// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

public class ConverterTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    [Fact]
    public void Already_sdk_style_project_is_skipped_and_not_written()
    {
        using var dir = new TempDir();
        const string sdkStyle = """
            <Project Sdk="nanoFramework.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netnano1.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        var nfproj = dir.File("Already.nfproj", sdkStyle);
        var before = File.ReadAllText(nfproj);

        // A non-dry-run so we can prove nothing on disk changed.
        var result = Converter.Convert(nfproj, new ConversionOptions());

        Assert.Equal(ConvertStatus.Skipped, result.Status);
        Assert.True(result.AlreadySdk);
        Assert.True(File.Exists(nfproj));                          // not deleted/renamed
        Assert.Equal(before, File.ReadAllText(nfproj));            // not rewritten
        Assert.False(File.Exists(nfproj + ".bak"));               // no backup written
        Assert.False(File.Exists(dir.Combine("Already.csproj")));  // nothing emitted
    }

    [Fact]
    public void PackageReference_version_comes_from_packages_config_when_absent()
    {
        using var dir = new TempDir();
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.System.Text" version="1.2.54" targetFramework="netnano1.0" />
            </packages>
            """);
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="nanoFramework.System.Text" />
              </ItemGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Contains(result.Packages,
            p => p.Key == "nanoFramework.System.Text" && p.Value == "1.2.54");
    }

    [Fact]
    public void Legacy_alias_mscorlib_maps_to_corelibrary_when_no_hintpath()
    {
        using var dir = new TempDir();
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.CoreLibrary" version="1.15.5" targetFramework="netnano1.0" />
            </packages>
            """);
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Reference Include="mscorlib" />
              </ItemGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Contains(result.Packages,
            p => p.Key == "nanoFramework.CoreLibrary" && p.Value == "1.15.5");
        Assert.Empty(result.Review);
    }

    [Fact]
    public void Unresolved_reference_is_flagged_for_review_and_no_blank_version_emitted()
    {
        using var dir = new TempDir();
        // No HintPath, no packages.config — the version cannot be resolved.
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Reference Include="SomeRandomLib" />
              </ItemGroup>
            </Project>
            """);

        var outPath = dir.Combine("Sample.csproj");
        var result = Converter.Convert(nfproj, new ConversionOptions());  // real run so we can read the emitted file

        Assert.Equal(ConvertStatus.Review, result.Status);
        Assert.Contains(result.Review, r => r.Contains("SomeRandomLib"));

        var emitted = File.ReadAllText(outPath);
        Assert.DoesNotContain("Version=\"\"", emitted);       // never emit a blank version
        Assert.DoesNotContain("SomeRandomLib", emitted);      // the unresolved ref is not carried over
    }

    [Fact]
    public void Full_convert_emits_sdk_project_deletes_inputs_and_writes_backup()
    {
        using var dir = new TempDir();
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.CoreLibrary" version="1.15.5" targetFramework="netnano1.0" />
            </packages>
            """);
        dir.File(Path.Combine("Properties", "AssemblyInfo.cs"),
            "using System.Reflection;\n[assembly: AssemblyTitle(\"Sample\")]\n");
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <ProjectGuid>{00000000-0000-0000-0000-000000000000}</ProjectGuid>
                <AssemblyName>Sample</AssemblyName>
                <RootNamespace>Sample</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="mscorlib" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="Program.cs" />
                <Compile Include="Properties\AssemblyInfo.cs" />
              </ItemGroup>
              <ItemGroup>
                <None Include="packages.config" />
              </ItemGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions());

        var csproj = dir.Combine("Sample.csproj");
        Assert.Equal(ConvertStatus.Converted, result.Status);

        // Emitted SDK-style project.
        Assert.True(File.Exists(csproj));
        var emitted = File.ReadAllText(csproj);
        Assert.Contains("<Project Sdk=\"nanoFramework.NET.Sdk\">", emitted);
        Assert.Contains("<TargetFramework>netnano1.0</TargetFramework>", emitted);
        Assert.Contains("<RootNamespace>Sample</RootNamespace>", emitted);
        Assert.Contains("nanoFramework.CoreLibrary", emitted);
        Assert.DoesNotContain("ProjectGuid", emitted);            // boilerplate dropped

        // Inputs removed.
        Assert.False(File.Exists(nfproj));                        // .nfproj retired
        Assert.False(File.Exists(dir.Combine("packages.config")));
        Assert.False(File.Exists(dir.Combine(Path.Combine("Properties", "AssemblyInfo.cs"))));

        // Backup of the original .nfproj written.
        Assert.True(File.Exists(nfproj + ".bak"));
    }

    [Fact]
    public void Dry_run_writes_nothing()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Equal(ConvertStatus.Converted, result.Status);
        Assert.True(File.Exists(nfproj));                          // original untouched
        Assert.False(File.Exists(dir.Combine("Sample.csproj")));   // nothing emitted
        Assert.False(File.Exists(nfproj + ".bak"));               // no backup
    }
}
