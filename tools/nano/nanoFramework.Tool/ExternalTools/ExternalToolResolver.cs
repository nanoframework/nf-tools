// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tool.ExternalTools;

/// <summary>
/// Implements the documented external-tool resolution order, purely against an
/// <see cref="IToolEnvironment"/> so it is fully unit-testable:
/// <list type="number">
///   <item>a binary <b>bundled</b> in the tool package under <c>tools/&lt;name&gt;/</c>,</item>
///   <item>a globally/locally <b>installed</b> tool or one on <b>PATH</b>,</item>
///   <item>a pinned release <b>downloaded</b> to the per-user cache.</item>
/// </list>
/// The first step that yields an existing executable wins. Download (step 3) is
/// resolution-only here: if a previously-fetched binary already sits in the cache it
/// is returned; performing the actual network fetch is delegated to <see cref="IToolDownloader"/>.
/// </summary>
public sealed class ExternalToolResolver(IToolEnvironment environment, IToolDownloader? downloader = null)
{
    private readonly IToolEnvironment _env = environment;
    private readonly IToolDownloader? _downloader = downloader;

    /// <summary>
    /// Resolve <paramref name="entry"/> to a runnable executable, or null if no step
    /// (and no fetch) produces one.
    /// </summary>
    public ToolResolution? Resolve(ToolManifestEntry entry)
    {
        // (1) Bundled: tools/<name>/<exe>
        if (TryBundled(entry.Name, out var bundled))
            return new ToolResolution(bundled!, ToolSource.Bundled);

        // (2) Installed / on PATH.
        if (TryOnPath(entry.Name, out var onPath))
            return new ToolResolution(onPath!, ToolSource.InstalledOrPath);

        // (3) Cache: an already-downloaded pinned release.
        if (TryCache(entry, out var cached))
            return new ToolResolution(cached!, ToolSource.Cache);

        // (3b) Nothing on disk yet — attempt a pinned download into the cache, then
        //      re-probe the cache. The downloader is a stub by default.
        if (_downloader is not null && _downloader.Download(entry, CacheDirFor(entry)))
        {
            if (TryCache(entry, out var fetched))
                return new ToolResolution(fetched!, ToolSource.Cache);
        }

        return null;
    }

    private bool TryBundled(string name, out string? path)
    {
        var dir = Path.Combine(_env.BaseDirectory, "tools", name);
        return TryProbeDir(dir, name, out path);
    }

    private bool TryOnPath(string name, out string? path)
    {
        foreach (var dir in _env.PathDirectories)
            if (TryProbeDir(dir, name, out path))
                return true;
        path = null;
        return false;
    }

    private bool TryCache(ToolManifestEntry entry, out string? path) =>
        TryProbeDir(CacheDirFor(entry), entry.Name, out path);

    // The cache slot for a pinned tool: <cache>/<name>/<version>.
    private string CacheDirFor(ToolManifestEntry entry) =>
        Path.Combine(_env.CacheDirectory, entry.Name, string.IsNullOrEmpty(entry.Version) ? "latest" : entry.Version);

    // Probe a directory for any of the executable name candidates for the tool.
    private bool TryProbeDir(string dir, string name, out string? path)
    {
        foreach (var exe in _env.ExecutableNamesFor(name))
        {
            var candidate = Path.Combine(dir, exe);
            if (_env.FileExists(candidate))
            {
                path = candidate;
                return true;
            }
        }
        path = null;
        return false;
    }
}

/// <summary>Fetches a pinned release into a cache directory. Implementations may hit the network.</summary>
public interface IToolDownloader
{
    /// <summary>
    /// Download the pinned release described by <paramref name="entry"/> into
    /// <paramref name="targetDir"/>. Returns true if, afterwards, an executable is
    /// present in <paramref name="targetDir"/>.
    /// </summary>
    bool Download(ToolManifestEntry entry, string targetDir);
}

/// <summary>
/// The default downloader. The actual pinned-release fetch is not yet implemented;
/// it throws a clear <see cref="NotImplementedException"/> so the gap is explicit
/// rather than a silent failure. Resolution steps 1 and 2 are fully functional.
/// </summary>
public sealed class NotImplementedToolDownloader : IToolDownloader
{
    public bool Download(ToolManifestEntry entry, string targetDir) =>
        // TODO: fetch entry.Source (NuGet/GitHub release) pinned to entry.Version,
        // verify by version/hash, unpack into targetDir, then return true.
        throw new NotImplementedException(
            $"Downloading '{entry.Name}' {entry.Version} from '{entry.Source}' to the user cache is not yet implemented. "
          + "Install the tool (dotnet tool / PATH) or bundle it under tools/ for now.");
}
