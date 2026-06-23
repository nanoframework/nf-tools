// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace NanoFramework.Migrate.Tests;

public class SolutionRewriteTests
{
    private static readonly IProjectConverter Converter = new ProjectConverter();

    private const string LegacyGuid = "{11A8DD76-328B-46DF-9F39-F559912D0360}";
    private const string SdkGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";

    private const string Nfproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup><AssemblyName>Sample</AssemblyName></PropertyGroup>
        </Project>
        """;

    private static string SolutionText(string projectGuid) => $$"""
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{{projectGuid}}") = "Sample", "Sample.nfproj", "{AAAAAAAA-0000-0000-0000-000000000001}"
        EndProject
        Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Other", "Other.csproj", "{BBBBBBBB-0000-0000-0000-000000000002}"
        EndProject
        Global
        EndGlobal
        """;

    [Fact]
    public void Convert_rewrites_solution_guid_and_extension_line_scoped()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        var sln = dir.File("Sample.sln", SolutionText(LegacyGuid));

        Converter.Convert(nfproj, new ConversionOptions());

        var updated = File.ReadAllText(sln);

        // .nfproj retargeted to .csproj and the legacy GUID swapped for the SDK one.
        Assert.Contains("\"Sample.csproj\"", updated);
        Assert.DoesNotContain("Sample.nfproj", updated);
        Assert.Contains(SdkGuid, updated);

        // Line-scoped: the unrelated Other.csproj entry (which already used the SDK
        // GUID) is untouched and there is exactly one SDK-GUID Sample line.
        Assert.Contains("\"Other.csproj\"", updated);
        Assert.DoesNotContain(LegacyGuid, updated);
    }

    [Fact]
    public void Rerun_over_already_converted_solution_is_a_noop()
    {
        using var dir = new TempDir();
        var nfproj = dir.File("Sample.nfproj", Nfproj);
        var sln = dir.File("Sample.sln", SolutionText(LegacyGuid));

        // First conversion rewrites the .sln and retires the .nfproj.
        Converter.Convert(nfproj, new ConversionOptions());
        var afterFirst = File.ReadAllText(sln);

        // Re-running the converter on the emitted .csproj must be a true no-op for
        // the solution (idempotent / reentrant).
        var csproj = dir.Combine("Sample.csproj");
        var result = Converter.Convert(csproj, new ConversionOptions());

        // The csproj is now SDK-style, so convert skips it; the .sln stays put.
        Assert.Equal(ConvertStatus.Skipped, result.Status);
        Assert.Equal(afterFirst, File.ReadAllText(sln));
    }
}
