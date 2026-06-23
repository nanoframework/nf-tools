// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tool.ExternalTools;

/// <summary>How an external tool's executable was located.</summary>
public enum ToolSource
{
    /// <summary>A binary bundled inside the tool package under <c>tools/&lt;name&gt;/</c>.</summary>
    Bundled,

    /// <summary>A globally/locally installed tool or an executable found on PATH.</summary>
    InstalledOrPath,

    /// <summary>A pinned release downloaded into the per-user cache.</summary>
    Cache,
}

/// <summary>The outcome of resolving an external tool to a runnable executable.</summary>
/// <param name="ExecutablePath">The full path to the resolved executable.</param>
/// <param name="Source">Which resolution step produced the path.</param>
public readonly record struct ToolResolution(string ExecutablePath, ToolSource Source);

/// <summary>
/// An external, prebuilt tool wrapped under <c>dotnet nano</c> (e.g. <c>nanoff</c>).
/// We do not rebuild these — we resolve a pinned binary, map args, run it, and render
/// its output through the shared Spectre reporter.
/// </summary>
public interface IExternalTool
{
    /// <summary>The tool name (e.g. <c>nanoff</c>); also the bundled sub-folder name.</summary>
    string Name { get; }

    /// <summary>The version this tool is pinned to (from the manifest).</summary>
    string Version { get; }

    /// <summary>
    /// Resolve the tool to a runnable executable using the documented order:
    /// (1) bundled, (2) installed/PATH, (3) downloaded to the user cache.
    /// Returns <c>null</c> when the tool cannot be resolved (caller renders an error).
    /// </summary>
    ToolResolution? ResolvePath();

    /// <summary>
    /// Run the resolved tool with the given arguments. Throws
    /// <see cref="ExternalToolNotFoundException"/> when the tool cannot be resolved.
    /// Returns the process exit code.
    /// </summary>
    int Run(IReadOnlyList<string> args);
}

/// <summary>Raised when an external tool cannot be resolved through any step.</summary>
public sealed class ExternalToolNotFoundException(string toolName, string message)
    : Exception(message)
{
    public string ToolName { get; } = toolName;
}
