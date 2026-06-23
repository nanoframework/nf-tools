// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

public class HintPathTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    private static string NfprojWithHintPath(string hintPath, string referenceInclude) => $"""
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <AssemblyName>Sample</AssemblyName>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="{referenceInclude}">
              <HintPath>{hintPath}</HintPath>
            </Reference>
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void HintPath_splits_id_and_version_at_first_numeric_segment()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", NfprojWithHintPath(
            @"..\packages\nanoFramework.System.Device.Gpio.1.1.57\lib\System.Device.Gpio.dll",
            "System.Device.Gpio"));

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Contains(result.Packages,
            p => p.Key == "nanoFramework.System.Device.Gpio" && p.Value == "1.1.57");
    }

    [Fact]
    public void HintPath_keeps_prerelease_suffix_attached_to_version()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", NfprojWithHintPath(
            @"..\packages\nanoFramework.CoreLibrary.2.0.0-preview.52\lib\mscorlib.dll",
            "mscorlib"));

        var result = Converter.Convert(nfproj, new ConversionOptions { DryRun = true });

        Assert.Contains(result.Packages,
            p => p.Key == "nanoFramework.CoreLibrary" && p.Value == "2.0.0-preview.52");
    }
}
