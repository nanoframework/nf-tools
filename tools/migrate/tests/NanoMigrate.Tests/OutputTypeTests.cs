// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace NanoFramework.Migrate.Tests;

public class OutputTypeTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    [Fact]
    public void OutputType_Exe_is_preserved_so_apps_stay_executables()
    {
        using var dir = new TempDir();
        dir.File("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
            </packages>
            """);
        var nfproj = dir.File("App.nfproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <AssemblyName>App</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var result = Converter.Convert(nfproj, new ConversionOptions());

        Assert.Equal(ConvertStatus.Converted, result.Status);
        var csproj = File.ReadAllText(dir.Combine("App.csproj"));
        // Regression: OutputType used to be silently dropped, turning apps into libraries
        // (CS8805 for top-level-statement apps). It must survive conversion.
        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
    }
}
