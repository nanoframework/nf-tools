// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

/// <summary>
/// Tests for the converter hardening that keeps migrated samples building: the SDK
/// globs Compile/EmbeddedResource/Content/None by default, so an explicit
/// <c>Include=</c> of a default-located file duplicates the globbed item (NETSDK1022)
/// or loses an intended exclusion. These cover the Include→Update / drop / keep
/// decisions, the Compile-subset <c>Remove</c>, a kept nano AssemblyInfo, and the
/// Shared Project import.
/// </summary>
public class ItemGlobTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    // Convert a project (real run so we can read the emitted .csproj) and return the
    // emitted text. Seeds a packages.config so packages.config-authoritative mode is
    // exercised, matching real samples.
    private static string ConvertAndRead(TempDir dir, string nfprojBody, string? csName = null)
    {
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
            </packages>
            """);
        var nfproj = dir.File((csName ?? "Sample") + ".nfproj", nfprojBody);
        var result = Converter.Convert(nfproj, new ConversionOptions());
        Assert.Null(result.Error);
        return File.ReadAllText(dir.Combine((csName ?? "Sample") + ".csproj"));
    }

    [Fact]
    public void EmbeddedResource_with_metadata_becomes_Update_not_Include()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <EmbeddedResource Include="Resources.resx">
                  <Generator>nFResXFileCodeGenerator</Generator>
                  <LastGenOutput>Resources.Designer.cs</LastGenOutput>
                </EmbeddedResource>
              </ItemGroup>
            </Project>
            """);

        // Update (not Include) so the metadata attaches to the SDK-globbed resx
        // without re-including it — re-including would trip NETSDK1022.
        Assert.Contains("<EmbeddedResource Update=\"Resources.resx\">", emitted);
        Assert.DoesNotContain("<EmbeddedResource Include=\"Resources.resx\"", emitted);
        // Child metadata must be preserved (the emitter used to drop it).
        Assert.Contains("<Generator>nFResXFileCodeGenerator</Generator>", emitted);
        Assert.Contains("<LastGenOutput>Resources.Designer.cs</LastGenOutput>", emitted);
    }

    [Fact]
    public void EmbeddedResource_without_metadata_is_dropped()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <EmbeddedResource Include="Plain.resx" />
              </ItemGroup>
            </Project>
            """);

        // No metadata → the SDK globs it plainly, so the explicit item is dropped
        // entirely (keeping it would duplicate the globbed item).
        Assert.DoesNotContain("Plain.resx", emitted);
    }

    [Fact]
    public void None_with_metadata_becomes_Update()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <None Include="appsettings.json">
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                </None>
              </ItemGroup>
            </Project>
            """);

        Assert.Contains("<None Update=\"appsettings.json\">", emitted);
        Assert.Contains("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", emitted);
    }

    [Fact]
    public void None_without_metadata_is_dropped()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <None Include="Resources\Images\favicon.png" />
              </ItemGroup>
            </Project>
            """);

        Assert.DoesNotContain("favicon.png", emitted);
    }

    [Fact]
    public void Item_with_Link_is_kept_as_Include()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <None Include="..\Shared\shared.txt">
                  <Link>shared.txt</Link>
                </None>
              </ItemGroup>
            </Project>
            """);

        // A linked file is NOT default-globbed, so it must stay an Include with its Link.
        Assert.Contains("Include=\"..\\Shared\\shared.txt\"", emitted);
        Assert.Contains("<Link>shared.txt</Link>", emitted);
        Assert.DoesNotContain("Update=", emitted);
    }

    [Fact]
    public void External_rooted_path_is_kept_as_Include()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <EmbeddedResource Include="..\Outside\external.resx" />
              </ItemGroup>
            </Project>
            """);

        // Outside the project dir → SDK won't glob it → keep verbatim (no dup, no drop).
        Assert.Contains("Include=\"..\\Outside\\external.resx\"", emitted);
    }

    [Fact]
    public void Content_with_metadata_becomes_Update()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Content Include="Resources\Html\main.html">
                  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
                </Content>
              </ItemGroup>
            </Project>
            """);

        Assert.Contains("<Content Update=\"Resources\\Html\\main.html\">", emitted);
        Assert.Contains("<CopyToOutputDirectory>Always</CopyToOutputDirectory>", emitted);
    }

    [Fact]
    public void Compile_subset_emits_Remove_for_unlisted_on_disk_cs()
    {
        using var dir = new TempDir();
        // On disk there are TWO .cs files, but the legacy project compiles only one.
        // The SDK globs **/*.cs, so the unlisted file must be Removed or it gets
        // compiled (duplicate definitions in real samples).
        dir.File("Test.cs", "// compiled\n");
        dir.File("Alternate.cs", "// NOT compiled by the legacy project\n");
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Compile Include="Test.cs" />
              </ItemGroup>
            </Project>
            """);

        Assert.Contains("<Compile Remove=\"Alternate.cs\" />", emitted);
        Assert.DoesNotContain("Remove=\"Test.cs\"", emitted);   // the listed file is not removed
    }

    [Fact]
    public void Compile_listing_all_on_disk_cs_emits_no_Remove()
    {
        using var dir = new TempDir();
        dir.File("Program.cs", "// compiled\n");
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.cs" />
              </ItemGroup>
            </Project>
            """);

        // The explicit set equals the on-disk set → nothing to exclude.
        Assert.DoesNotContain("Compile Remove", emitted);
    }

    [Fact]
    public void AssemblyInfo_with_native_version_is_kept_and_generation_disabled()
    {
        using var dir = new TempDir();
        var aiPath = dir.File(Path.Combine("Properties", "AssemblyInfo.cs"),
            "using System.Reflection;\n[assembly: AssemblyNativeVersion(\"1.0.0.0\")]\n");
        var nfproj = dir.File("Lib.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <AssemblyName>Lib</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Properties\AssemblyInfo.cs" />
              </ItemGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions());
        var emitted = File.ReadAllText(dir.Combine("Lib.csproj"));

        // The MetadataProcessor needs AssemblyNativeVersion when generating stubs, and
        // the SDK never generates it — so the file is KEPT (not deleted) and generation
        // is turned off so the standard attributes don't collide with generated ones.
        Assert.True(File.Exists(aiPath));                                      // not deleted
        Assert.Contains("<GenerateAssemblyInfo>false</GenerateAssemblyInfo>", emitted);
        Assert.DoesNotContain(aiPath, result.DeletedFiles);                   // not previewed as a deletion
    }

    [Fact]
    public void Ordinary_AssemblyInfo_is_still_deleted_and_generation_left_on()
    {
        using var dir = new TempDir();
        var aiPath = dir.File(Path.Combine("Properties", "AssemblyInfo.cs"),
            "using System.Reflection;\n[assembly: AssemblyTitle(\"Sample\")]\n");
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <ItemGroup>
                <Compile Include="Properties\AssemblyInfo.cs" />
              </ItemGroup>
            </Project>
            """);

        Converter.Convert(nfproj, new ConversionOptions());
        var emitted = File.ReadAllText(dir.Combine("Sample.csproj"));

        // No nano-specific attribute → original behavior: delete the file, let the SDK
        // generate the assembly info.
        Assert.False(File.Exists(aiPath));
        Assert.DoesNotContain("GenerateAssemblyInfo", emitted);
    }

    [Fact]
    public void Shared_project_import_is_carried_through()
    {
        using var dir = new TempDir();
        var emitted = ConvertAndRead(dir, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup Label="Globals">
                <NanoFrameworkProjectSystemPath>$(MSBuildExtensionsPath)\nanoFramework\v1.0\</NanoFrameworkProjectSystemPath>
              </PropertyGroup>
              <Import Project="$(NanoFrameworkProjectSystemPath)NFProjectSystem.Default.props" Condition="Exists('$(NanoFrameworkProjectSystemPath)NFProjectSystem.Default.props')" />
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <Import Project="..\Shared\Shared.projitems" Label="Shared" />
              <Import Project="$(NanoFrameworkProjectSystemPath)NFProjectSystem.CSharp.targets" Condition="Exists('$(NanoFrameworkProjectSystemPath)NFProjectSystem.CSharp.targets')" />
            </Project>
            """);

        // The Shared Project import carries source the SDK would otherwise never see —
        // it must survive. The NFProjectSystem.* imports are SDK boilerplate, dropped.
        Assert.Contains("<Import Project=\"..\\Shared\\Shared.projitems\" Label=\"Shared\" />", emitted);
        Assert.DoesNotContain("NFProjectSystem", emitted);
    }

    [Fact]
    public void Unknown_top_level_import_is_flagged_for_review()
    {
        using var dir = new TempDir();
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
            </packages>
            """);
        var nfproj = dir.File("Sample.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
              <Import Project="..\Custom\Custom.targets" />
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Contains(result.Review, r => r.Contains("Custom.targets"));
    }
}
