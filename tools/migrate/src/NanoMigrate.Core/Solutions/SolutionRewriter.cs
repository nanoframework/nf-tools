// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace NanoFramework.Migrate.Core.Solutions;

/// <summary>
/// Retargets a solution's converted projects from <c>.nfproj</c> to <c>.csproj</c>.
/// Handles both the classic line-based <c>.sln</c> and the XML <c>.slnx</c> by
/// mutating the .NET Foundation <see cref="SolutionModel"/> and re-serialising with
/// the matching serializer — the library writes the canonical format for each (a
/// <c>.sln</c> stays a <c>.sln</c>, a <c>.slnx</c> stays a <c>.slnx</c>). For a
/// classic <c>.sln</c> the retargeted entry's project type is set to the SDK-style C#
/// GUID; for a path-based <c>.slnx</c> the now-redundant nanoFramework type is cleared.
///
/// Idempotent / reentrant: the retarget is decided at the model level — only projects
/// still pointing at a <c>.nfproj</c> that is in the converted set are touched. When
/// nothing matched, the original file text is returned/left untouched (no write, no
/// reformat).
/// </summary>
public static class SolutionRewriter
{
    // Project-type GUIDs: legacy nanoFramework flavor -> SDK-style C#. Retained for
    // back-compat (tests/consumers reference these); the actual GUID swap on a classic
    // .sln is performed by the serializer when the project type is cleared and the
    // path becomes a .csproj.
    public const string NfprojTypeGuid = "{11A8DD76-328B-46DF-9F39-F559912D0360}";
    public const string CsprojTypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";

    /// <summary>
    /// Returns the rewritten text of <paramref name="solutionText"/>, retargeting any
    /// entry whose referenced <c>.nfproj</c> (resolved against <paramref name="solutionDir"/>)
    /// is in <paramref name="convertedNfprojPaths"/> (absolute paths). The returned
    /// string equals the input when nothing matched (no-op).
    /// </summary>
    public static string Rewrite(
        string solutionText,
        string solutionDir,
        SolutionFormat format,
        IEnumerable<string> convertedNfprojPaths)
    {
        var converted = new HashSet<string>(
            convertedNfprojPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        if (converted.Count == 0) return solutionText;

        var isXml = format == SolutionFormat.Xml;

        SolutionModel model;
        try
        {
            // .slnx parsing applies the bare-.nfproj Type compatibility shim; .sln
            // carries its type GUIDs per entry and needs none.
            model = isXml ? SolutionFile.LoadSlnxModel(solutionText) : SolutionFile.OpenFromText(isXml: false, solutionText);
        }
        catch
        {
            // If the library cannot parse it even after the shim, leave it untouched.
            return solutionText;
        }

        var changed = RetargetModel(model, solutionDir, converted, isXml);
        if (!changed) return solutionText;

        return SaveToText(isXml, model);
    }

    /// <summary>
    /// Rewrites the solution file in place when at least one entry was retargeted.
    /// Returns true if the file changed. A no-op leaves the file untouched (no write).
    /// </summary>
    public static bool RewriteFile(SolutionFile solution, IEnumerable<string> convertedNfprojPaths)
    {
        string text;
        try { text = File.ReadAllText(solution.Path); }
        catch { return false; }

        var dir = Path.GetDirectoryName(solution.Path)!;
        var updated = Rewrite(text, dir, solution.Format, convertedNfprojPaths);
        if (string.Equals(updated, text, StringComparison.Ordinal)) return false;

        File.WriteAllText(solution.Path, updated, new UTF8Encoding(false));
        return true;
    }

    // Mutates the model: every project whose resolved .nfproj path is in the converted
    // set is pointed at the sibling .csproj. For a classic .sln the project type is
    // set to the SDK-style C# type GUID (so the serializer writes it on that entry,
    // line-scoped — other still-unconverted nanoFramework projects keep their legacy
    // GUID). For a .slnx (path-based) the now-redundant nanoFramework Type attribute is
    // cleared so the converted entry carries no Type, exactly as before. Returns true
    // if any project changed.
    private static bool RetargetModel(SolutionModel model, string solutionDir, HashSet<string> converted, bool isXml)
    {
        var changed = false;
        foreach (var project in model.SolutionProjects)
        {
            var rel = project.FilePath;
            if (!rel.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase)) continue;

            var abs = Path.GetFullPath(Path.Combine(solutionDir, rel));
            if (!converted.Contains(abs)) continue;

            project.FilePath = Path.ChangeExtension(rel, ".csproj");
            try { project.Type = isXml ? string.Empty : CsprojTypeGuid; }
            catch (SolutionException) { /* leave the type as-is */ }
            changed = true;
        }
        return changed;
    }

    private static string SaveToText(bool isXml, SolutionModel model)
    {
        using var ms = new MemoryStream();
        if (isXml)
            SolutionSerializers.SlnXml.SaveAsync(ms, model, CancellationToken.None).GetAwaiter().GetResult();
        else
            SolutionSerializers.SlnFileV12.SaveAsync(ms, model, CancellationToken.None).GetAwaiter().GetResult();
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }
}
