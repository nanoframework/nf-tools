// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace NanoFramework.Migrate.Core.Solutions;

/// <summary>The on-disk format of a Visual Studio solution.</summary>
public enum SolutionFormat
{
    /// <summary>Classic line-based <c>.sln</c> (each project has a type GUID).</summary>
    Classic,

    /// <summary>XML <c>.slnx</c> (path-based).</summary>
    Xml,
}

/// <summary>
/// Parsed model of a Visual Studio solution — both the classic <c>.sln</c> line
/// format and the newer XML <c>.slnx</c> format. Backed by the .NET Foundation
/// <c>Microsoft.VisualStudio.SolutionPersistence</c> serializers: the library does
/// all of the parsing and (on retarget) re-serialization; this type only exposes the
/// referenced project paths (absolute) and the format, and drives the nfproj→csproj
/// retarget through the model.
/// </summary>
public sealed class SolutionFile
{
    // The legacy nanoFramework project-type GUID. A flavored .nfproj carries this in a
    // classic .sln, and VS writes it as the Type attribute in a .slnx. The serializer
    // does not know the .nfproj extension, so a .slnx that references a bare .nfproj
    // (no Type attribute) cannot be parsed as-is; we inject this Type for those
    // entries before handing the text to the library (see LoadSlnxModel).
    internal const string NfprojTypeGuid = "{11A8DD76-328B-46DF-9F39-F559912D0360}";

    /// <summary>Absolute path to the solution file.</summary>
    public string Path { get; }

    /// <summary>Whether this is a classic <c>.sln</c> or an XML <c>.slnx</c>.</summary>
    public SolutionFormat Format { get; }

    /// <summary>
    /// Every project the solution references, as an absolute path. Solution folders
    /// are excluded (the library models them separately from projects).
    /// </summary>
    public IReadOnlyList<string> ProjectPaths { get; }

    private SolutionFile(string path, SolutionFormat format, IReadOnlyList<string> projectPaths)
    {
        Path = path;
        Format = format;
        ProjectPaths = projectPaths;
    }

    /// <summary>True if the file extension denotes a solution (<c>.sln</c> or <c>.slnx</c>).</summary>
    public static bool IsSolutionPath(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    /// <summary>Loads and parses the solution at <paramref name="solutionPath"/>.</summary>
    public static SolutionFile Load(string solutionPath)
    {
        var full = System.IO.Path.GetFullPath(solutionPath);
        var dir = System.IO.Path.GetDirectoryName(full)!;
        var isXml = full.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        var model = LoadModel(full, isXml);
        var paths = model.SolutionProjects
            .Select(p => System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, p.FilePath)))
            .ToList();

        return new SolutionFile(full, isXml ? SolutionFormat.Xml : SolutionFormat.Classic, paths);
    }

    /// <summary>
    /// The <c>.nfproj</c> projects referenced by the solution (absolute paths).
    /// </summary>
    public IReadOnlyList<string> NanoProjects() =>
        ProjectPaths.Where(p => p.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase)).ToList();

    // ---- Library bridge ------------------------------------------------------

    /// <summary>
    /// Reads a solution into the library's <see cref="SolutionModel"/>, picking the
    /// serializer by extension. For <c>.slnx</c> the bare-<c>.nfproj</c> compatibility
    /// shim is applied so a solution that references a flavored project without an
    /// explicit <c>Type</c> attribute still parses (the serializer does not know the
    /// <c>.nfproj</c> extension on its own).
    /// </summary>
    internal static SolutionModel LoadModel(string fullPath, bool isXml) =>
        isXml ? LoadSlnxModel(File.ReadAllText(fullPath)) : OpenFromText(isXml: false, File.ReadAllText(fullPath));

    // Classic .sln: the serializer carries the project-type GUID per entry, so no shim
    // is needed. .slnx: try a straight parse; if the serializer rejects an unknown
    // .nfproj project type, inject the nanoFramework Type attribute on bare .nfproj
    // <Project> elements and retry. This is the only place we touch the raw text, and
    // only to make an otherwise-unparseable solution ingestible by the library.
    internal static SolutionModel LoadSlnxModel(string text)
    {
        try
        {
            return OpenFromText(isXml: true, text);
        }
        catch (SolutionException)
        {
            var patched = InjectNfprojTypeAttribute(text);
            if (patched is null) throw;
            return OpenFromText(isXml: true, patched);
        }
    }

    // Opens solution text into the model via the matching typed serializer (chosen by
    // format). The library's stream-based open avoids any temp file.
    internal static SolutionModel OpenFromText(bool isXml, string text)
    {
        using var ms = new MemoryStream(new UTF8Encoding(false).GetBytes(text));
        return isXml
            ? SolutionSerializers.SlnXml.OpenAsync(ms, CancellationToken.None).GetAwaiter().GetResult()
            : SolutionSerializers.SlnFileV12.OpenAsync(ms, CancellationToken.None).GetAwaiter().GetResult();
    }

    // Adds Type="{nfproj GUID}" to every <Project Path="...nfproj"> element that lacks
    // a Type attribute, so the SolutionPersistence .slnx serializer can resolve the
    // otherwise-unknown extension. Returns null when nothing needed patching (so the
    // original error is preserved). Preserves all other formatting via XDocument.
    internal static string? InjectNfprojTypeAttribute(string slnxText)
    {
        XDocument doc;
        try { doc = XDocument.Parse(slnxText, LoadOptions.PreserveWhitespace); }
        catch { return null; }

        var changed = false;
        foreach (var proj in doc.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var path = (string?)proj.Attribute("Path");
            if (path is null || !path.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase)) continue;
            var hasType = proj.Attributes().Any(a => a.Name.LocalName == "Type");
            if (hasType) continue;
            proj.SetAttributeValue("Type", NfprojTypeGuid);
            changed = true;
        }
        return changed ? doc.ToString(SaveOptions.DisableFormatting) : null;
    }
}
