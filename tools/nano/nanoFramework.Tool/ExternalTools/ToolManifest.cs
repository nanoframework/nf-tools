// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoFramework.Tool.ExternalTools;

/// <summary>One external tool entry from <c>nano-tools.json</c>.</summary>
public sealed class ToolManifestEntry
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The pinned version resolution/fetch is locked to.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The NuGet/dotnet-tool package id, when the tool ships as a tool package.</summary>
    [JsonPropertyName("packageId")]
    public string? PackageId { get; init; }

    /// <summary>A download source (URL) for the pinned release; used by cache fetch.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>The parsed <c>nano-tools.json</c>: the list of external tools + pins.</summary>
public sealed class ToolManifest
{
    [JsonPropertyName("tools")]
    public IReadOnlyList<ToolManifestEntry> Tools { get; init; } = Array.Empty<ToolManifestEntry>();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Find the pinned entry for a tool by name (case-insensitive), or null.</summary>
    public ToolManifestEntry? Find(string name) =>
        Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parse a manifest from JSON text.</summary>
    public static ToolManifest Parse(string json) =>
        JsonSerializer.Deserialize<ToolManifest>(json, Options) ?? new ToolManifest();

    /// <summary>
    /// Load the manifest embedded in the tool package (<c>ExternalTools/nano-tools.json</c>).
    /// Falls back to an empty manifest if, for any reason, the resource is missing.
    /// </summary>
    public static ToolManifest LoadEmbedded(Assembly? assembly = null)
    {
        assembly ??= typeof(ToolManifest).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("nano-tools.json", StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return new ToolManifest();

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return new ToolManifest();
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
