// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using nanoFramework.Tool.ExternalTools;
using Xunit;

namespace nanoFramework.Tool.Tests;

public class ExternalToolResolverTests
{
    private static readonly ToolManifestEntry Nanoff = new()
    {
        Name = "nanoff",
        Version = "2.5.78",
        Source = "https://example/nanoff/2.5.78",
    };

    // A pure in-memory IToolEnvironment: the set of "existing" files is explicit, so
    // the resolution order can be exercised without a real filesystem, PATH, or network.
    private sealed class FakeEnv : IToolEnvironment
    {
        private readonly HashSet<string> _files;

        public FakeEnv(params string[] existingFiles) =>
            _files = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);

        public string BaseDirectory => "/pkg";
        public string CacheDirectory => "/cache";
        public IReadOnlyList<string> PathDirectories { get; init; } = new[] { "/usr/bin", "/usr/local/bin" };

        // Mirror the non-Windows single-name behaviour so paths in tests are stable
        // across the OS the suite runs on.
        public IReadOnlyList<string> ExecutableNamesFor(string toolName) => new[] { toolName };

        public bool FileExists(string path) => _files.Contains(Norm(path));

        private static string Norm(string p) => p.Replace('\\', '/');
    }

    // A spy downloader: records whether it was asked to fetch, and can "materialise"
    // a cached binary to simulate a successful download.
    private sealed class SpyDownloader(FakeEnvMutable? cache = null) : IToolDownloader
    {
        public int Calls { get; private set; }
        private readonly FakeEnvMutable? _cache = cache;

        public bool Download(ToolManifestEntry entry, string targetDir)
        {
            Calls++;
            if (_cache is null) return false;
            _cache.Add($"{targetDir}/{entry.Name}");
            return true;
        }
    }

    // A mutable variant so the spy downloader can add the fetched file mid-resolve.
    private sealed class FakeEnvMutable : IToolEnvironment
    {
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        public string BaseDirectory => "/pkg";
        public string CacheDirectory => "/cache";
        public IReadOnlyList<string> PathDirectories => Array.Empty<string>();
        public IReadOnlyList<string> ExecutableNamesFor(string toolName) => new[] { toolName };
        public void Add(string path) => _files.Add(path.Replace('\\', '/'));
        public bool FileExists(string path) => _files.Contains(path.Replace('\\', '/'));
    }

    [Fact]
    public void Bundled_wins_over_path_and_cache()
    {
        // Bundled, PATH, and cache all have the binary; bundled must win.
        var env = new FakeEnv(
            "/pkg/tools/nanoff/nanoff",
            "/usr/bin/nanoff",
            "/cache/nanoff/2.5.78/nanoff");
        var resolver = new ExternalToolResolver(env);

        var result = resolver.Resolve(Nanoff);

        Assert.NotNull(result);
        Assert.Equal(ToolSource.Bundled, result!.Value.Source);
        Assert.Equal("/pkg/tools/nanoff/nanoff", result.Value.ExecutablePath.Replace('\\', '/'));
    }

    [Fact]
    public void Path_wins_over_cache_when_not_bundled()
    {
        // No bundled binary: PATH must be preferred over the cache.
        var env = new FakeEnv(
            "/usr/local/bin/nanoff",
            "/cache/nanoff/2.5.78/nanoff");
        var resolver = new ExternalToolResolver(env);

        var result = resolver.Resolve(Nanoff);

        Assert.NotNull(result);
        Assert.Equal(ToolSource.InstalledOrPath, result!.Value.Source);
        Assert.Equal("/usr/local/bin/nanoff", result.Value.ExecutablePath.Replace('\\', '/'));
    }

    [Fact]
    public void Cache_used_when_neither_bundled_nor_on_path()
    {
        // Only the pinned cache slot has the binary.
        var env = new FakeEnv("/cache/nanoff/2.5.78/nanoff");
        var resolver = new ExternalToolResolver(env);

        var result = resolver.Resolve(Nanoff);

        Assert.NotNull(result);
        Assert.Equal(ToolSource.Cache, result!.Value.Source);
    }

    [Fact]
    public void Unresolvable_returns_null_and_does_not_download_without_a_downloader()
    {
        // Nothing anywhere and no downloader: resolution fails cleanly (null).
        var env = new FakeEnv(/* no files */);
        var resolver = new ExternalToolResolver(env);

        Assert.Null(resolver.Resolve(Nanoff));
    }

    [Fact]
    public void Download_is_attempted_only_after_the_first_three_steps_miss()
    {
        // Bundled present: the downloader must never be consulted.
        var env = new FakeEnv("/pkg/tools/nanoff/nanoff");
        var spy = new SpyDownloader();
        var resolver = new ExternalToolResolver(env, spy);

        var result = resolver.Resolve(Nanoff);

        Assert.Equal(ToolSource.Bundled, result!.Value.Source);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Download_materialises_into_cache_and_resolves_when_nothing_else_found()
    {
        // Nothing on disk; the downloader fetches into the cache and resolution then
        // finds the freshly-materialised cache binary.
        var env = new FakeEnvMutable();
        var spy = new SpyDownloader(env);
        var resolver = new ExternalToolResolver(env, spy);

        var result = resolver.Resolve(Nanoff);

        Assert.Equal(1, spy.Calls);
        Assert.NotNull(result);
        Assert.Equal(ToolSource.Cache, result!.Value.Source);
        Assert.Equal("/cache/nanoff/2.5.78/nanoff", result.Value.ExecutablePath.Replace('\\', '/'));
    }

    [Fact]
    public void Embedded_manifest_pins_nanoff()
    {
        // The shipped manifest is loadable from the tool assembly and pins nanoff.
        var manifest = ToolManifest.LoadEmbedded(typeof(NanoffTool).Assembly);

        var entry = manifest.Find("nanoff");
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrWhiteSpace(entry!.Version));
    }

    [Fact]
    public void NotImplemented_downloader_throws_a_clear_message()
    {
        var downloader = new NotImplementedToolDownloader();
        var ex = Assert.Throws<NotImplementedException>(() => downloader.Download(Nanoff, "/cache/nanoff/2.5.78"));
        Assert.Contains("nanoff", ex.Message);
    }
}
