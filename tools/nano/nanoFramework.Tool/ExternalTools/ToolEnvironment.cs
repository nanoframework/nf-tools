// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace NanoFramework.Tool.ExternalTools;

/// <summary>
/// The host-environment seams the resolver depends on, behind an interface so the
/// resolution order (bundled → installed/PATH → cache) can be unit-tested without a
/// real filesystem, PATH, or network. <see cref="SystemToolEnvironment"/> is the
/// production implementation.
/// </summary>
public interface IToolEnvironment
{
    /// <summary>The directory the running tool package lives in (root of <c>tools/</c>).</summary>
    string BaseDirectory { get; }

    /// <summary>The per-user cache root downloaded releases are unpacked into.</summary>
    string CacheDirectory { get; }

    /// <summary>The directories on PATH, in order.</summary>
    IReadOnlyList<string> PathDirectories { get; }

    /// <summary>The executable file-name candidates for a bare tool name (e.g. nanoff, nanoff.exe).</summary>
    IReadOnlyList<string> ExecutableNamesFor(string toolName);

    /// <summary>True if a file exists at the given path.</summary>
    bool FileExists(string path);
}

/// <summary>The production <see cref="IToolEnvironment"/> over the real machine.</summary>
public sealed class SystemToolEnvironment : IToolEnvironment
{
    public string BaseDirectory { get; }
    public string CacheDirectory { get; }

    public SystemToolEnvironment(string? baseDirectory = null, string? cacheDirectory = null)
    {
        BaseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        CacheDirectory = cacheDirectory ?? DefaultCacheDirectory();
    }

    private static string DefaultCacheDirectory()
    {
        // Per-user, OS-appropriate cache: ~/.nanoframework/tools (or %LOCALAPPDATA% on Windows).
        var root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, ".nanoframework", "tools");
    }

    public IReadOnlyList<string> PathDirectories
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return Array.Empty<string>();
            return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public IReadOnlyList<string> ExecutableNamesFor(string toolName)
    {
        // On Windows a bare name resolves through PATHEXT; we only probe the common
        // launcher extensions for an external CLI here.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new[] { toolName + ".exe", toolName + ".cmd", toolName + ".bat", toolName };
        return new[] { toolName };
    }

    public bool FileExists(string path) => File.Exists(path);
}
